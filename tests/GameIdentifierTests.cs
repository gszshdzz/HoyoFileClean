using Xunit;

namespace HoyoFileClean.Tests;

public sealed class GameIdentifierTests
{
    private readonly string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Temp", "opencode", "identification-" + Guid.NewGuid().ToString("N"));

    private const string Sample = """
        [General]
        game_version=7.2.0
        channel=1
        sub_channel=1
        uapc={"hk4e_cn":{"uapc":""},"hyp":{"uapc":""}}
        plugin_COa9vWaYX9_version=1.0.0
        [General]
        uapc={"hk4e_cn":{"uapc":""},"hyp":{"uapc":""}}
        """;

    private string Write(string text, string name = "config.ini")
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, name);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void SampleIdentifiesGameVersionChannelAndPluginWithoutChangingFile()
    {
        string path = Write(Sample);
        DateTime modified = File.GetLastWriteTimeUtc(path);
        GameIdentification result = GameIdentifier.Identify(root, TestContext.Current.CancellationToken);
        Assert.Equal("7.2.0", result.Detections["ini_version"]);
        Assert.False(result.Information.ContainsKey("game"));
        Assert.Equal("hk4e_cn", Assert.Single(result.Information["biz"].Values).RawValue);
        Assert.Equal("IdentificationChannelOfficial", Assert.Single(result.Information["channel"].Values).TextId);
        Assert.Equal("1.0.0", Assert.Single(result.Information["plugins"].Values).RawValue);
        Assert.Equal("COa9vWaYX9", Assert.Single(result.Information["plugins"].Values).Item);
        Assert.Equal("IdentificationPluginVersion", Assert.Single(result.Information["plugins"].Values).DisplayFormatTextId);
        Assert.Equal(Sample, File.ReadAllText(path));
        Assert.Equal(modified, File.GetLastWriteTimeUtc(path));
        Assert.Single(Directory.GetFiles(root));
    }

    [Theory]
    [InlineData("game_version=7.10.0", "7.10.0")]
    [InlineData("game_version=7.1.0", "7.2.0")]
    [InlineData("game_version=7.2.0", "7.2.0")]
    [InlineData("channel=1", "7.2.0")]
    public void DuplicateValuesUseNumericMaximumOrRequireEquality(string extra, string expected)
    {
        Write(Sample + "\n" + extra);
        Assert.Equal(expected, Assert.Single(GameIdentifier.Identify(root, TestContext.Current.CancellationToken).Information["version"].Values).RawValue);
    }

    [Theory]
    [InlineData("game_version=invalid", CoreErrorCode.GameDetectionInvalid)]
    [InlineData("game_version=", CoreErrorCode.GameDetectionInvalid)]
    [InlineData("channel=2", CoreErrorCode.GameDetectionConflict)]
    [InlineData("sub_channel=2", CoreErrorCode.GameDetectionConflict)]
    [InlineData("uapc={\"hk4e_global\":{}}", CoreErrorCode.GameDetectionConflict)]
    public void InvalidOrConflictingDuplicatesFail(string extra, CoreErrorCode code)
    {
        Write(Sample + "\n" + extra);
        Assert.Equal(code, Assert.Throws<CoreException>(() => GameIdentifier.Identify(root, TestContext.Current.CancellationToken)).Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("game_version=7.2.0")]
    [InlineData("game_version=7.2.0\nchannel=9\nsub_channel=1\nuapc={\"hk4e_cn\":{}}")]
    public void MissingIdentityOrUnknownMappingFails(string text)
    {
        Write(text);
        Assert.Equal(CoreErrorCode.GameIdentificationFailed,
            Assert.Throws<CoreException>(() => GameIdentifier.Identify(root, TestContext.Current.CancellationToken)).Code);
    }

    [Fact]
    public void FutureGameUsesDifferentFilesRulesAndMappingsWithoutLogicChanges()
    {
        Write("release=1.9.0\nrelease=1.10.0", "future.txt");
        Write("identity=future\nchannel=42", "identity.txt");
        var configuration = new GameIdentificationConfiguration(new()
        {
            ["release"] = new(["./missing.ini", "./future.txt"], @"^release=(?<value>[^\r\n]+)", true),
            ["id"] = new(["identity.txt"], @"^identity=(?<value>[^\r\n]+)", false),
            ["channel"] = new(["identity.txt"], @"^channel=(?<value>[^\r\n]+)", false)
        }, new()
        {
            ["game"] = new("GameLabel", true, false,
                [new(new() { ["id"] = "future", ["channel"] = "42" }, TextId: "FutureGame")]),
            ["version"] = new("VersionLabel", true, false,
                [new(new() { ["id"] = "future" }, Source: "release")])
        });
        GameIdentification result = GameIdentifier.Identify(root, configuration, TestContext.Current.CancellationToken);
        Assert.Equal("FutureGame", Assert.Single(result.Information["game"].Values).TextId);
        Assert.Equal("1.10.0", Assert.Single(result.Information["version"].Values).RawValue);
    }

    [Theory]
    [InlineData("empty-detections")]
    [InlineData("empty-information")]
    [InlineData("null-detections")]
    public void InvalidConfigurationFailsExplicitly(string kind)
    {
        var original = GameIdentifier.LoadConfiguration();
        var configuration = kind switch
        {
            "empty-detections" => original with { Detections = [] },
            "empty-information" => original with { Information = [] },
            _ => original with { Detections = null! }
        };
        Assert.Equal(CoreErrorCode.GameIdentificationConfigurationInvalid,
            Assert.Throws<CoreException>(() => GameIdentifier.Identify(root, configuration, TestContext.Current.CancellationToken)).Code);
    }

    [Fact]
    public void CandidatesCannotEscapeOrFollowLinksAndHaveSizeLimits()
    {
        string path = Write(Sample);
        var config = GameIdentifier.LoadConfiguration();
        config.Detections["ini_version"] = config.Detections["ini_version"] with { Paths = ["../outside.ini"] };
        Assert.Equal(CoreErrorCode.GameIdentificationConfigurationInvalid,
            Assert.Throws<CoreException>(() => GameIdentifier.Identify(root, config, TestContext.Current.CancellationToken)).Code);
        File.WriteAllText(path, new string('x', Maintenance.MetadataLimit + 1));
        Assert.Equal(CoreErrorCode.GameDetectionInvalid, Assert.Throws<CoreException>(() => GameIdentifier.Identify(root, TestContext.Current.CancellationToken)).Code);
        string linkRoot = Path.Combine(root, "linked");
        Directory.CreateSymbolicLink(linkRoot, root);
        Assert.Equal(CoreErrorCode.DirectoryIsLink, Assert.Throws<CoreException>(() => GameIdentifier.Identify(linkRoot, TestContext.Current.CancellationToken)).Code);
    }

    [Fact]
    public void CancellationPreventsIdentificationAndBothScanModes()
    {
        Write(Sample);
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => GameIdentifier.Identify(root, source.Token));
        foreach (bool hashes in new[] { false, true })
            Assert.ThrowsAny<OperationCanceledException>(() => DirectoryScanner.Scan(root,
                computeHashes: hashes, cancellationToken: source.Token));
    }

    [Fact]
    public void HashCancellationDoesNotPublishOutputAndAllowsRetry()
    {
        Write(Sample);
        using var source = new CancellationTokenSource();
        string output = Path.Combine(Path.GetDirectoryName(root)!, Guid.NewGuid() + ".json");
        Assert.ThrowsAny<OperationCanceledException>(() => DirectoryScanner.Scan(root, output,
            computeHashes: true, progress: _ => source.Cancel(), cancellationToken: source.Token));
        Assert.False(File.Exists(output));
        Assert.Equal(1, DirectoryScanner.Scan(root, computeHashes: true, cancellationToken: TestContext.Current.CancellationToken).FileCount);
    }

    [Fact]
    public void BuildTimeConfigurationsAreCompiledAndIndependent()
    {
        string[] resources = typeof(GameIdentifier).Assembly.GetManifestResourceNames();
        Assert.DoesNotContain("HoyoFileClean.game-identification.json", resources);
        Assert.DoesNotContain("HoyoFileClean.network-services.json", resources);
        Assert.NotEmpty(NetworkManifestClient.Services);
        var first = GameIdentifier.LoadConfiguration();
        first.Detections.Clear();
        Assert.NotEmpty(GameIdentifier.LoadConfiguration().Detections);
    }

    [Fact(Explicit = true)]
    public void ProvidedRealConfigIdentifiesExpectedGame()
    {
        string repository = RepositoryRoot();
        string path = Path.Combine(repository, "refs", "config.ini");
        byte[] before = File.ReadAllBytes(path);
        var result = GameIdentifier.Identify(Path.GetDirectoryName(path)!, TestContext.Current.CancellationToken);
        Assert.Equal("hk4e_cn", Assert.Single(result.Information["biz"].Values).RawValue);
        Assert.Equal("7.2.0", Assert.Single(result.Information["version"].Values).RawValue);
        Assert.Equal("IdentificationChannelOfficial", Assert.Single(result.Information["channel"].Values).TextId);
        Assert.Equal("1.0.0", Assert.Single(result.Information["plugins"].Values).RawValue);
        Assert.Equal(before, File.ReadAllBytes(path));
        string acceptance = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "opencode", "HoyoFileClean-session8-acceptance-" + Guid.NewGuid().ToString("N"));
        foreach ((string name, string extra) in new[]
        {
            ("valid", ""), ("version-max", "\ngame_version=7.10.0"),
            ("version-invalid", "\ngame_version=bad"), ("channel-conflict", "\nchannel=2")
        })
        {
            string folder = Path.Combine(acceptance, name);
            Directory.CreateDirectory(folder);
            string copy = Path.Combine(folder, "config.ini");
            File.WriteAllBytes(copy, before);
            File.AppendAllText(copy, extra);
        }
        Directory.CreateDirectory(Path.Combine(acceptance, "unknown"));
        string dynamicRoot = Path.Combine(acceptance, "dynamic-plugin");
        Directory.CreateDirectory(dynamicRoot);
        string dynamicConfig = Path.Combine(dynamicRoot, "config.ini");
        File.WriteAllBytes(dynamicConfig, before);
        File.AppendAllText(dynamicConfig, "\nplugin_futurePlugin_version=2.9.0\nplugin_futurePlugin_version=2.10.0");
        string recordedRoot = Path.Combine(acceptance, "recorded-game");
        Directory.CreateDirectory(recordedRoot);
        File.WriteAllBytes(Path.Combine(recordedRoot, "config.ini"), before);
        DirectoryScanner.Scan(recordedRoot, Path.Combine(acceptance, "identified-scan.json"),
            cancellationToken: TestContext.Current.CancellationToken,
            identification: GameIdentifier.Identify(recordedRoot, TestContext.Current.CancellationToken));
        Directory.Move(recordedRoot, recordedRoot + "-moved");
        File.WriteAllText(Path.Combine(acceptance, "reference.json"), """{"fileCount":0,"totalSize":0,"files":[]}""");
        Console.WriteLine("ACCEPTANCE " + acceptance);
    }

    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string path = "")
        => Path.GetDirectoryName(Path.GetDirectoryName(path))!;

    [Fact]
    public void DuplicatesAcrossCandidateFilesUseTheSameResolutionPolicy()
    {
        Write(Sample);
        Write("game_version=7.10.0\nchannel=1", "other.ini");
        var config = GameIdentifier.LoadConfiguration();
        foreach (string id in new[] { "ini_version", "ini_channel" })
            config.Detections[id] = config.Detections[id] with { Paths = ["config.ini", "other.ini"] };
        var result = GameIdentifier.Identify(root, config, TestContext.Current.CancellationToken);
        Assert.Equal("7.10.0", result.Detections["ini_version"]);
        Write("channel=2", "other.ini");
        Assert.Equal(CoreErrorCode.GameDetectionConflict,
            Assert.Throws<CoreException>(() => GameIdentifier.Identify(root, config, TestContext.Current.CancellationToken)).Code);
    }

    [Fact]
    public void MeaningfulItemsAllowAlternativeSourcesAndRejectAmbiguousSingleValues()
    {
        Write(Sample);
        var config = GameIdentifier.LoadConfiguration();
        InformationDefinition original = config.Information["version"];
        config.Information["version"] = original with
        {
            Rules = [new(new() { ["ini_channel"] = "unknown" }, TextId: "unmatched"), .. original.Rules]
        };
        Assert.Equal("7.2.0", Assert.Single(GameIdentifier.Identify(root, config,
            TestContext.Current.CancellationToken).Information["version"].Values).RawValue);
        config.Information["version"] = original with
        {
            Rules = [.. original.Rules, new(new() { ["ini_channel"] = "1" }, TextId: "other-version")]
        };
        Assert.Equal(CoreErrorCode.GameDetectionConflict,
            Assert.Throws<CoreException>(() => GameIdentifier.Identify(root, config, TestContext.Current.CancellationToken)).Code);
        config.Information["version"] = config.Information["version"] with { Multiple = true };
        Assert.Equal(2, GameIdentifier.Identify(root, config, TestContext.Current.CancellationToken).Information["version"].Values.Count);
    }

    [Fact]
    public void InvalidRuleReferencesAndMissingCaptureFailBeforeReading()
    {
        Write(Sample);
        var config = GameIdentifier.LoadConfiguration();
        config.Information["biz"] = config.Information["biz"] with
        { Rules = [new(new() { ["missing"] = "anything" }, TextId: "game")] };
        Assert.Equal(CoreErrorCode.GameIdentificationConfigurationInvalid,
            Assert.Throws<CoreException>(() => GameIdentifier.Identify(root, config, TestContext.Current.CancellationToken)).Code);
        config = GameIdentifier.LoadConfiguration();
        config.Detections["ini_version"] = config.Detections["ini_version"] with { Pattern = "game_version=(.+)" };
        Assert.Equal(CoreErrorCode.GameIdentificationConfigurationInvalid,
            Assert.Throws<CoreException>(() => GameIdentifier.Identify(root, config, TestContext.Current.CancellationToken)).Code);
    }

    [Fact]
    public void CandidateFileLinksAreRejectedWithoutReadingTheTarget()
    {
        string outside = Write(Sample, "outside.ini");
        File.CreateSymbolicLink(Path.Combine(root, "config.ini"), outside);
        using var locked = new FileStream(outside, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Equal(CoreErrorCode.ScanFileTypeChanged,
            Assert.Throws<CoreException>(() => GameIdentifier.Identify(root, TestContext.Current.CancellationToken)).Code);
    }

    [Theory]
    [InlineData("future_beta")]
    [InlineData("game-without-region")]
    [InlineData("hk4e_cn")]
    public void UapcExtractsActualPropertyNameWithoutRegionSuffixFiltering(string gameId)
    {
        Write("game_version=7.2.0\nuapc={\"hyp\":{},\"" + gameId + "\":{}}");
        var configuration = GameIdentifier.LoadConfiguration();
        // Channel meanings remain game-specific adaptation data, not identity filters.
        configuration.Information.Remove("channel");
        var result = GameIdentifier.Identify(root, configuration, TestContext.Current.CancellationToken);
        Assert.Equal(gameId, result.Detections["ini_game_id"]);
        Assert.Equal(gameId, Assert.Single(result.Information["biz"].Values).RawValue);
    }

    [Theory]
    [InlineData("uapc={bad}", CoreErrorCode.GameDetectionInvalid)]
    [InlineData("uapc={\"another-game\":{}}", CoreErrorCode.GameDetectionConflict)]
    [InlineData("uapc={\"hk4e_cn\":{},\"hk4e_cn\":{}}", CoreErrorCode.GameDetectionInvalid)]
    public void MalformedOrConflictingUapcIsNotSilentlyFiltered(string extra, CoreErrorCode code)
    {
        Write(Sample + "\n" + extra);
        Assert.Equal(code, Assert.Throws<CoreException>(() => GameIdentifier.Identify(root,
            TestContext.Current.CancellationToken)).Code);
    }

    [Fact]
    public void PluginIdsAreExtractedDynamicallyAndVersionsComparedPerPlugin()
    {
        Write(Sample + "\nplugin_futurePlugin_version=2.9.0\nplugin_futurePlugin_version=2.10.0\nplugin_another_version=1.0.0");
        var result = GameIdentifier.Identify(root, TestContext.Current.CancellationToken);
        var plugins = result.Information["plugins"].Values.ToDictionary(value => value.Item!, value => value.RawValue);
        Assert.Equal(3, plugins.Count);
        Assert.Equal("1.0.0", plugins["COa9vWaYX9"]);
        Assert.Equal("2.10.0", plugins["futurePlugin"]);
        Assert.Equal("1.0.0", plugins["another"]);
        Assert.Equal("2.10.0", result.Detections["ini_plugin_version:futurePlugin"]);
    }

    [Fact]
    public void InvalidNewPluginVersionFailsWithoutAddingAnAdaptation()
    {
        Write(Sample + "\nplugin_neverConfigured_version=invalid");
        Assert.Equal(CoreErrorCode.GameDetectionInvalid, Assert.Throws<CoreException>(() =>
            GameIdentifier.Identify(root, TestContext.Current.CancellationToken)).Code);
    }

    [Fact]
    public void IdentifiedScanJsonRoundTripsWithoutAccessingGameRoot()
    {
        Write(Sample + "\nplugin_newPlugin_version=3.0.0");
        Write("game", "YuanShen.exe");
        var identification = GameIdentifier.Identify(root, TestContext.Current.CancellationToken);
        string output = Path.Combine(Path.GetDirectoryName(root)!, Guid.NewGuid() + ".json");
        DirectoryScanner.Scan(root, output, cancellationToken: TestContext.Current.CancellationToken,
            identification: identification);
        using (var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(output)))
        {
            var recorded = json.RootElement.GetProperty("scan").GetProperty("gameDetections");
            Assert.Equal("hk4e_cn", recorded.GetProperty("ini_game_id").GetString());
            Assert.Equal("3.0.0", recorded.GetProperty("ini_plugin_version:newPlugin").GetString());
            Assert.DoesNotContain("IdentificationLabel", recorded.GetRawText());
        }
        Directory.Move(root, root + "-moved");
        var imported = FileComparer.ReadInputs([output]);
        var restored = GameIdentifier.FromDetections(imported.Sources[0].Scan!.GameDetections!);
        Assert.Equal(Assert.Single(restored.Information["biz"].Values).RawValue,
            imported.Sources[0].Metadata!["biz"].Value);
        Assert.Equal("hk4e", imported.Sources[0].Metadata!["game"].Value);
        Assert.Equal("game", imported.Sources[0].Metadata!["package"].Value);
        Assert.False(restored.Information.ContainsKey("package"));
        Assert.Equal(identification.Detections, restored.Detections);
        Assert.Equal(identification.Information["plugins"].Values, restored.Information["plugins"].Values);
        string converted = output + ".converted.json";
        ManifestConverter.Convert(output, converted);
        Assert.Equal(identification.Detections, FileComparer.ReadInputs([converted]).Sources[0].Scan!.GameDetections);
        string exported = output + ".exported.json";
        ManifestConverter.Export(imported, exported);
        var reimported = FileComparer.ReadInputs([exported]);
        Assert.Equal(identification.Detections, reimported.Sources[0].Scan!.GameDetections);
        Assert.Equal("hk4e_cn", reimported.Sources[0].Metadata!["biz"].Value);
        Assert.Equal(imported.Files.Values, reimported.Files.Values);
        Assert.False(Directory.Exists(root));
    }

    [Theory]
    [InlineData("ini_version", "invalid")]
    [InlineData("unknown", "value")]
    [InlineData("ini_plugin_version", "1.0.0")]
    [InlineData("ini_plugin_version:", "1.0.0")]
    [InlineData("ini_plugin_version:bad id", "1.0.0")]
    [InlineData("ini_plugin_version:bad=id", "1.0.0")]
    [InlineData("ini_plugin_version:   ", "1.0.0")]
    [InlineData("ini_plugin_version:bad\nname", "1.0.0")]
    public void InvalidRecordedDetectionsAreRejected(string key, string value)
    {
        Write(Sample);
        var recorded = new Dictionary<string, string>(GameIdentifier.Identify(root,
            TestContext.Current.CancellationToken).Detections) { [key] = value };
        Assert.Throws<CoreException>(() => GameIdentifier.FromDetections(recorded));
    }

    [Fact]
    public void CustomConfigurationSurvivesScanningExportAndExplicitRestoration()
    {
        Write("identity=future\nrelease=1.2.0", "future.txt");
        var config = new GameIdentificationConfiguration(new()
        {
            ["future_id"] = new(["future.txt"], @"^identity=(?<value>[^\r\n]+)", false),
            ["future_version"] = new(["future.txt"], @"^release=(?<value>[^\r\n]+)", true)
        }, new()
        {
            ["biz"] = new("IdentificationLabelGame", true, false, [new(new(), Source: "future_id")]),
            ["version"] = new("IdentificationLabelVersion", true, false, [new(new(), Source: "future_version")])
        });
        var identification = GameIdentifier.Identify(root, config, TestContext.Current.CancellationToken);
        var snapshot = DirectoryScanner.Scan(root, cancellationToken: TestContext.Current.CancellationToken,
            identification: identification);
        Assert.Equal("future", snapshot.Sources[0].Scan!.GameDetections!["future_id"]);
        string output = root + ".json";
        DirectoryScanner.Scan(root, output, cancellationToken: TestContext.Current.CancellationToken,
            identification: identification);
        var recorded = FileComparer.ReadInputs([output]).Sources[0].Scan!.GameDetections!;
        Assert.Equal("1.2.0", Assert.Single(GameIdentifier.FromDetections(recorded, config)
            .Information["version"].Values).RawValue);
    }

    [Theory]
    [InlineData("future id")]
    [InlineData("future=id")]
    [InlineData("future\nid")]
    [InlineData("future:id")]
    public void UnpersistableDetectionIdsFailAtConfigurationEntry(string id)
    {
        var config = GameIdentifier.LoadConfiguration();
        config.Detections.Add(id, new(["missing.ini"], @"(?<value>.+)", false));
        Assert.Equal(CoreErrorCode.GameIdentificationConfigurationInvalid,
            Assert.Throws<CoreException>(() => GameIdentifier.Identify(root, config,
                TestContext.Current.CancellationToken)).Code);
    }
}
