using System.Text.Json;
using Google.Protobuf;
using HoyoFileClean.Protos;
using Xunit;
using ZstdSharp;

namespace HoyoFileClean.Tests;

public sealed class ManifestIdentifierTests
{
    private static FileListDocument Document(params string[] paths)
        => new(paths.Length, paths.Length, paths.Select(path => new ManifestFile(path, 1)).ToArray());

    private sealed record ManifestIdentificationRule(string Target, string Match, string Id,
        IReadOnlyDictionary<string, string> Results, bool Exact = false, bool IgnoreCase = false, bool Prefix = false);

    private static ManifestIdentifier Identifier(params ManifestIdentificationRule[] detections)
    {
        // Compact fixture notation only; production accepts the common configuration directly.
        var selectors = detections.GroupBy(rule => rule.Id).ToDictionary(group => group.Key,
            group => group.Select(rule => new ManifestDetectionDefinition(rule.Target, rule.Match,
                rule.Exact, rule.IgnoreCase, rule.Prefix, rule.Results.ToDictionary(pair => pair.Key,
                    pair => new IdentificationMeaning(pair.Value, RetainConflicts: true)))).ToArray());
        return new ManifestIdentifier(new IdentificationConfiguration<ManifestDetectionDefinition[]>(selectors));
    }

    private static ManifestIdentificationRule Rule(string target, string match, string id, string result,
        bool exact = false, bool ignoreCase = false, string field = "game")
        => new(target, match, id, new Dictionary<string, string> { [field] = result }, exact, ignoreCase);

    [Fact]
    public void PositionMatchIdAndResultAreIndependentAndUnknownStaysUnknown()
    {
        var identifier = Identifier(Rule("files.path", "YuanShen.exe", "game_hk4e", "hk4e"));
        ManifestMetadataField game = identifier.Identify(Document("prefix/YuanShen.exe"), TestContext.Current.CancellationToken)["game"];
        Assert.Equal("hk4e", game.Value);
        Assert.False(game.Conflict);
        Assert.Equal(new ManifestMetadataMatch("game_hk4e", "hk4e"), Assert.Single(game.Matches));
        Assert.Empty(identifier.Identify(Document("Other.exe"), TestContext.Current.CancellationToken));
        Assert.Empty(identifier.Identify(Document(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void RepeatedEvidenceForOneIdIsNotAConflict()
    {
        var identifier = Identifier(Rule("files.path", "one", "same", "result"), Rule("files.path", "two", "same", "result"));
        ManifestMetadataField game = identifier.Identify(Document("one", "two", "folder/one"), TestContext.Current.CancellationToken)["game"];
        Assert.Single(game.Matches);
        Assert.False(game.Conflict);
    }

    [Fact]
    public void ConflictRetainsAllCandidatesAndIsIndependentOfRuleAndFileOrder()
    {
        ManifestIdentificationRule[] rules = [Rule("files.path", "one", "b", "first"), Rule("files.path", "two", "a", "second")];
        ManifestMetadataField first = Identifier(rules).Identify(Document("one", "two"), TestContext.Current.CancellationToken)["game"];
        ManifestMetadataField reversed = Identifier(rules.Reverse().ToArray()).Identify(Document("two", "one"), TestContext.Current.CancellationToken)["game"];
        Assert.True(first.Conflict);
        Assert.Null(first.Value);
        Assert.Equal(new[] { "a", "b" }, first.Matches.Select(match => match.Id));
        Assert.Equal(first.Matches, reversed.Matches);
        string json = JsonSerializer.Serialize(first, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        using var parsed = JsonDocument.Parse(json);
        Assert.True(parsed.RootElement.GetProperty("conflict").GetBoolean());
        Assert.False(parsed.RootElement.TryGetProperty("value", out _));
        Assert.Equal(2, parsed.RootElement.GetProperty("matches").GetArrayLength());
    }

    [Theory]
    [InlineData(false, false, "dir/YuanShen.exe", true)]
    [InlineData(true, false, "dir/YuanShen.exe", false)]
    [InlineData(true, false, "YuanShen.exe", true)]
    [InlineData(false, false, "yuanshen.exe", false)]
    [InlineData(false, true, "yuanshen.exe", true)]
    public void MatchingModeIsExplicit(bool exact, bool ignoreCase, string path, bool expected)
    {
        var result = Identifier(Rule("files.path", "YuanShen.exe", "id", "game", exact, ignoreCase))
            .Identify(Document(path), TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.ContainsKey("game"));
    }

    [Fact]
    public void NestedArraysAndOtherStringFieldsCanBeConfiguredWithoutNewLogic()
    {
        var identifier = Identifier(
            [Rule("scan.excludedDirectories", "Cache", "scope", "excluded", field: "scope"), Rule("hashAlgorithm", "MD5", "hash", "md5", field: "algorithm")]);
        var result = identifier.Identify(Document("file") with
        {
            HashAlgorithm = "MD5", Scan = new("unused", ["Cache"], [], [], "heavy")
        }, TestContext.Current.CancellationToken);
        Assert.Equal("excluded", result["scope"].Value);
        Assert.Equal("md5", result["algorithm"].Value);
        Assert.Empty(identifier.Identify(Document("file"), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("files.missing")]
    [InlineData("files.size")]
    [InlineData("files")]
    [InlineData("files..path")]
    [InlineData("")]
    public void InvalidOrNonStringTargetsAreRejected(string target)
        => Assert.Equal(CoreErrorCode.IdentificationConfigurationInvalid,
            Assert.Throws<CoreException>(() => Identifier(Rule(target, "match", "id", "result"))).Code);

    [Fact]
    public void InvalidResultsAndInconsistentDefinitionsAreRejected()
    {
        ManifestIdentificationRule rule = Rule("files.path", "one", "id", "result");
        Assert.Throws<CoreException>(() => Identifier(rule with { Results = new Dictionary<string, string>() }));
        Assert.Throws<CoreException>(() => Identifier(rule with { Results = new Dictionary<string, string> { [""] = "value" } }));
        Assert.Throws<CoreException>(() => Identifier(rule with { Results = new Dictionary<string, string> { ["game"] = "" } }));
        Assert.Throws<CoreException>(() => Identifier(rule, rule with { Results = new Dictionary<string, string> { ["game"] = "other" } }));
        Assert.Throws<CoreException>(() => Identifier(rule with { Match = "" }));
        Assert.Throws<CoreException>(() => Identifier(rule, rule with
        {
            Results = new Dictionary<string, string> { ["package"] = "game" }
        }));
    }

    [Fact]
    public void OneDetectionCanProduceDifferentMeaningsAndConflictsStayWithinTheirField()
    {
        var identifier = Identifier(
            [new("files.path", "YuanShen.exe", "exe", new Dictionary<string, string>
                { ["game"] = "hk4e", ["package"] = "game" }),
             Rule("files.path", "Other.exe", "other", "other")]);
        var single = identifier.Identify(Document("YuanShen.exe"), TestContext.Current.CancellationToken);
        Assert.Equal("hk4e", single["game"].Value);
        Assert.Equal("game", single["package"].Value);
        var conflict = identifier.Identify(Document("YuanShen.exe", "Other.exe"), TestContext.Current.CancellationToken);
        Assert.True(conflict["game"].Conflict);
        Assert.Null(conflict["game"].Value);
        Assert.False(conflict["package"].Conflict);
        Assert.Equal("game", conflict["package"].Value);
    }

    [Fact]
    public void CancellationIsObserved()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => Identifier(Rule("files.path", "one", "id", "result"))
            .Identify(Document("one"), cancelled.Token));
    }

    [Fact]
    public void MatchingDoesNotCopyOrSerializeTheWholeFileList()
    {
        var identifier = Identifier(Rule("files.path", "missing", "id", "result"));
        ManifestFile[] files = Enumerable.Range(0, 100000).Select(index => new ManifestFile($"Data/{index}.bin", 1)).ToArray();
        var document = new FileListDocument(files.Length, files.Length, files);
        _ = identifier.Identify(document, TestContext.Current.CancellationToken);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Empty(identifier.Identify(document, TestContext.Current.CancellationToken));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 64 * 1024, $"100000 files allocated={allocated}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealConversionPathRecognizesProtobufAndPreservesFilesAndOutputContract(bool compressed)
    {
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "opencode", "HoyoFileClean.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        byte[] bytes = new SophonManifestProto { Assets = { new SophonManifestAssetProperty
        {
            AssetName = "YuanShen.exe", AssetSize = 3, AssetHashMd5 = "900150983cd24fb0d6963f7d28e17f72"
        } } }.ToByteArray();
        if (compressed)
        {
            using var compressor = new Compressor();
            bytes = compressor.Wrap(bytes).ToArray();
        }
        string input = Path.Combine(directory, "input"), output = Path.Combine(directory, "output.json");
        File.WriteAllBytes(input, bytes);
        ManifestConverter.Convert(input, output);
        using var json = JsonDocument.Parse(File.ReadAllBytes(output));
        Assert.Equal("hk4e", json.RootElement.GetProperty("metadata").GetProperty("game").GetProperty("value").GetString());
        Assert.False(json.RootElement.GetProperty("metadata").GetProperty("game").TryGetProperty("multiple", out _));
        Assert.Equal("hk4e_cn", json.RootElement.GetProperty("metadata").GetProperty("biz").GetProperty("value").GetString());
        FileListSnapshot read = ManifestConverter.Read(output, TestContext.Current.CancellationToken);
        Assert.Equal("hk4e", read.Sources[0].Metadata!["game"].Value);
        Assert.Equal("hk4e_cn", read.Sources[0].Metadata!["biz"].Value);
        Assert.Equal("game", read.Sources[0].Metadata!["package"].Value);
        Assert.Equal("900150983cd24fb0d6963f7d28e17f72", read.Files["YuanShen.exe"].Hash);
        Assert.Equal(3, read.TotalSize);
        Assert.Equal(bytes, File.ReadAllBytes(input));
    }

    [Fact]
    public void SavedMetadataIsRecomputedAndPerInputMetadataSurvivesCombination()
    {
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "opencode", "HoyoFileClean.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string first = Path.Combine(directory, "first.json"), second = Path.Combine(directory, "second.json");
        var forged = new Dictionary<string, ManifestMetadataField> { ["game"] = new([new("fake", "fake")]) };
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        File.WriteAllText(first, JsonSerializer.Serialize(Document("YuanShen.exe") with { Metadata = forged }, options));
        File.WriteAllText(second, JsonSerializer.Serialize(Document("other.exe") with { Metadata = forged }, options));
        FileListSnapshot combined = FileComparer.ReadInputs([first, second]);
        Assert.Equal("hk4e", combined.Sources[0].Metadata!["game"].Value);
        Assert.Empty(combined.Sources[1].Metadata!);
        Assert.Equal(2, combined.FileCount);
    }

    [Fact]
    public void ExportUsesTheLoadedSingleSnapshotAndRefusesToOverwrite()
    {
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "opencode", "HoyoFileClean.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string input = Path.Combine(directory, "input.json"), output = Path.Combine(directory, "export.json");
        File.WriteAllText(input, JsonSerializer.Serialize(Document("YuanShen.exe"),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        FileListSnapshot snapshot = ManifestConverter.Read(input, TestContext.Current.CancellationToken);
        using (var held = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.None))
            ManifestConverter.Export(snapshot, output);
        FileListSnapshot exported = ManifestConverter.Read(output, TestContext.Current.CancellationToken);
        Assert.Equal(snapshot.Files.Values, exported.Files.Values);
        Assert.Equal("game", exported.Sources[0].Metadata!["package"].Value);
        byte[] before = File.ReadAllBytes(output);
        Assert.Equal(CoreErrorCode.OutputExists, Assert.Throws<CoreException>(() => ManifestConverter.Export(snapshot, output)).Code);
        Assert.Equal(before, File.ReadAllBytes(output));
        Assert.Throws<ArgumentException>(() => ManifestConverter.Export(FileComparer.Combine([snapshot, snapshot],
            TestContext.Current.CancellationToken), Path.Combine(directory, "merged.json")));
    }
}
