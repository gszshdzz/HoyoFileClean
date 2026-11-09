using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace HoyoFileClean.Tests;

public sealed class FileComparerTests
{
    private const string Hash = "900150983cd24fb0d6963f7d28e17f72";
    private const string OtherHash = "d41d8cd98f00b204e9800998ecf8427e";
    private readonly string directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Temp", "opencode", "HoyoFileClean.Tests", Guid.NewGuid().ToString("N"));
    private string Output => Path.Combine(directory, "comparison.json");

    [Fact]
    public void DifferencesAreNormalResultsAndSidesCanBeSwapped()
    {
        string a = WriteList("a", true, new("only-a", 3, Hash), new("size", 3, Hash),
            new("hash", 3, Hash), new("both", 3, Hash), new("same", 3, Hash));
        string b = WriteList("b", true, new("only-b", 3, Hash), new("size", 4, Hash),
            new("hash", 3, OtherHash), new("both", 0, OtherHash), new("same", 3, Hash));
        byte[] aBefore = File.ReadAllBytes(a);
        byte[] bBefore = File.ReadAllBytes(b);
        DateTime modified = File.GetLastWriteTimeUtc(a);

        ComparisonSummary summary = FileComparer.Compare([a], [b], Output);
        string swappedOutput = Path.Combine(directory, "swapped.json");
        ComparisonSummary swapped = FileComparer.Compare([b], [a], swappedOutput);

        Assert.Equal(new ComparisonSummary(1, 1, 3, 1, 0, 0), summary);
        Assert.Equal(summary, swapped);
        JsonObject report = ReadReport();
        Assert.Equal(new[] { "only-a" }, Paths(report, "onlyA"));
        Assert.Equal(new[] { "only-b" }, Paths(report, "onlyB"));
        Assert.Equal(new[] { "both", "hash", "size" }, report["different"]!.AsArray()
            .Select(item => item!["a"]!["path"]!.GetValue<string>()));
        JsonNode both = report["different"]![0]!;
        Assert.True(both["sizeDifferent"]!.GetValue<bool>());
        Assert.True(both["hashDifferent"]!.GetValue<bool>());
        Assert.True(both["hashCompared"]!.GetValue<bool>());
        Assert.Equal(a, report["aInputs"]![0]!.GetValue<string>());
        Assert.Equal(b, report["bInputs"]![0]!.GetValue<string>());
        JsonObject reverse = JsonNode.Parse(File.ReadAllText(swappedOutput))!.AsObject();
        Assert.Equal(Paths(report, "onlyA"), Paths(reverse, "onlyB"));
        Assert.Equal(Paths(report, "onlyB"), Paths(reverse, "onlyA"));
        for (int index = 0; index < 3; index++)
        {
            Assert.True(JsonNode.DeepEquals(report["different"]![index]!["a"], reverse["different"]![index]!["b"]));
            Assert.True(JsonNode.DeepEquals(report["different"]![index]!["b"], reverse["different"]![index]!["a"]));
        }
        Assert.Equal(aBefore, File.ReadAllBytes(a));
        Assert.Equal(bBefore, File.ReadAllBytes(b));
        Assert.Equal(modified, File.GetLastWriteTimeUtc(a));
        Assert.Empty(Directory.EnumerateFiles(directory, "*.partial"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothSidesSupportMultipleInputsAndIdenticalOverlap(bool swap)
    {
        string first = WriteList("first", true, new ManifestFile("Data/file", 3, Hash.ToUpperInvariant()));
        string duplicate = WriteList("duplicate", true, new ManifestFile(@"data\FILE", 3, Hash));
        string extra = WriteList("extra", true, new ManifestFile("other", 0, OtherHash));
        string reference = WriteList("reference", true, new("DATA/File", 3, Hash), new("other", 0, OtherHash));
        string[] many = [first, duplicate, extra, first];

        ComparisonSummary summary = FileComparer.Compare(swap ? [reference] : many, swap ? many : [reference], Output);

        Assert.Equal(new ComparisonSummary(0, 0, 0, 2, 0, 0), summary);
        Assert.Equal(4, ReadReport()[swap ? "bInputs" : "aInputs"]!.AsArray().Count);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ContradictionsWithinEitherSideFailWithoutPublishing(bool swap, bool changeHash)
    {
        string first = WriteList("first", true, new ManifestFile("file", 3, Hash));
        string conflict = WriteList("conflict", true, new ManifestFile("FILE", changeHash ? 3 : 4, changeHash ? OtherHash : Hash));
        string empty = WriteList("empty", true);

        CoreException error = Assert.Throws<CoreException>(() =>
            FileComparer.Compare(swap ? [empty] : [first, conflict], swap ? [first, conflict] : [empty], Output));

        Assert.Equal(CoreErrorCode.InputConflict, error.Code);
        Assert.Equal(conflict, error.Input);
        Assert.Equal("FILE", error.Path);
        Assert.False(File.Exists(Output));
        Assert.Empty(Directory.EnumerateFiles(directory, "*.partial"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingHashesNeverBecomeHashDifferencesOrVerification(bool swap)
    {
        string light = WriteList("light", false, new("same", 3), new("size", 4));
        string heavy = WriteList("heavy", true, new("same", 3, Hash), new("size", 3, Hash));

        ComparisonSummary summary = FileComparer.Compare(swap ? [heavy] : [light], swap ? [light] : [heavy], Output);

        Assert.Equal(new ComparisonSummary(0, 0, 1, 0, 1, 0), summary);
        JsonNode difference = ReadReport()["different"]![0]!;
        Assert.True(difference["sizeDifferent"]!.GetValue<bool>());
        Assert.False(difference["hashDifferent"]!.GetValue<bool>());
        Assert.False(difference["hashCompared"]!.GetValue<bool>());
    }

    [Fact]
    public void TwoLightListsCompareSizesWithoutInventingHashes()
    {
        string a = WriteList("a", false, new ManifestFile("file", 3));
        string b = WriteList("b", false, new ManifestFile("FILE", 3));

        Assert.Equal(new ComparisonSummary(0, 0, 0, 0, 1, 0), FileComparer.Compare([a], [b], Output));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScanExclusionsAndSkippedLinksAreSymmetricAndRespectBoundaries(bool swap)
    {
        string scoped = WriteList("scoped", false);
        JsonObject data = JsonNode.Parse(File.ReadAllText(scoped))!.AsObject();
        data["scan"] = JsonSerializer.SerializeToNode(new
        {
            root = @"Z:\not-accessed",
            excludedDirectories = new[] { "Data/Skip" },
            excludedFiles = new[] { "config.ini" },
            skippedLinks = new[] { "link" },
            mode = "light"
        });
        File.WriteAllText(scoped, data.ToJsonString());
        string other = WriteList("other", false, new("data/skip/child", 1), new("CONFIG.ini", 1),
            new("link/child", 1), new("Data/Skip-other/file", 1), new("nested/config.ini", 1), new("link-other", 1));

        ComparisonSummary summary = FileComparer.Compare(swap ? [other] : [scoped], swap ? [scoped] : [other], Output);

        Assert.Equal(3, summary.OutsideScope);
        Assert.Equal(swap ? 3 : 0, summary.OnlyA);
        Assert.Equal(swap ? 0 : 3, summary.OnlyB);
        Assert.All(ReadReport()["outsideScope"]!.AsArray(), item =>
            Assert.Equal(swap ? "a" : "b", item!["presentIn"]!.GetValue<string>()));
    }

    [Fact]
    public void ActualScannerOutputsCanBeComparedWithoutReopeningRoots()
    {
        string root = Path.Combine(directory, "root");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "file"), "abc");
        string light = Path.Combine(directory, "light.json");
        string heavy = Path.Combine(directory, "heavy.json");
        DirectoryScanner.Scan(root, light);
        DirectoryScanner.Scan(root, heavy, computeHashes: true);
        Directory.Move(root, Path.Combine(directory, "moved-root"));

        Assert.Equal(new ComparisonSummary(0, 0, 0, 0, 1, 0), FileComparer.Compare([light], [heavy], Output));
    }

    [Fact]
    public void EmptyListsProduceAnEmptyReport()
    {
        string empty = WriteList("empty", false);

        Assert.Equal(new ComparisonSummary(0, 0, 0, 0, 0, 0), FileComparer.Compare([empty], [empty], Output));
    }

    [Theory]
    [InlineData("missingFiles")]
    [InlineData("missingSize")]
    [InlineData("nullFiles")]
    [InlineData("nullFile")]
    [InlineData("nullPath")]
    [InlineData("negativeSize")]
    [InlineData("badHash")]
    [InlineData("missingHash")]
    [InlineData("badAlgorithm")]
    [InlineData("badCount")]
    [InlineData("badTotal")]
    [InlineData("unsafePath")]
    [InlineData("reservedPath")]
    [InlineData("duplicatePath")]
    [InlineData("parentConflict")]
    [InlineData("overflow")]
    [InlineData("badMode")]
    [InlineData("nullScopePath")]
    [InlineData("unsafeScopePath")]
    public void InvalidListsDoNotPublishResults(string problem)
    {
        string input = WriteList("invalid", true, new ManifestFile("file", 3, Hash));
        JsonObject data = JsonNode.Parse(File.ReadAllText(input))!.AsObject();
        JsonNode file = data["files"]![0]!;
        switch (problem)
        {
            case "missingFiles": data.Remove("files"); break;
            case "missingSize": file.AsObject().Remove("size"); break;
            case "nullFiles": data["files"] = null; break;
            case "nullFile": data["files"]![0] = null; break;
            case "nullPath": file["path"] = null; break;
            case "negativeSize": file["size"] = -1; break;
            case "badHash": file["hash"] = "not-md5"; break;
            case "missingHash": file.AsObject().Remove("hash"); break;
            case "badAlgorithm": data["hashAlgorithm"] = "SHA256"; break;
            case "badCount": data["fileCount"] = 0; break;
            case "badTotal": data["totalSize"] = 0; break;
            case "unsafePath": file["path"] = "../escape"; break;
            case "reservedPath": file["path"] = "NUL.txt"; break;
            case "duplicatePath": data["files"]!.AsArray().Add(file.DeepClone()); break;
            case "parentConflict":
                JsonNode child = file.DeepClone();
                child["path"] = "file/child";
                data["files"]!.AsArray().Add(child);
                break;
            case "overflow":
                file["size"] = long.MaxValue;
                JsonNode extra = file.DeepClone();
                extra["path"] = "other";
                data["files"]!.AsArray().Add(extra);
                break;
            case "badMode":
            case "nullScopePath":
            case "unsafeScopePath":
                data["scan"] = JsonSerializer.SerializeToNode(new
                {
                    root = "unused",
                    excludedDirectories = problem == "badMode" ? Array.Empty<string?>()
                        : new[] { problem == "nullScopePath" ? null : "../escape" },
                    excludedFiles = Array.Empty<string>(), skippedLinks = Array.Empty<string>(),
                    mode = problem == "badMode" ? "light" : "heavy"
                });
                break;
        }
        File.WriteAllText(input, data.ToJsonString());

        CoreErrorCode expected = problem switch
        {
            "badHash" or "missingHash" or "negativeSize" or "nullFile" => CoreErrorCode.FileMetadataInvalid,
            "badAlgorithm" or "badMode" => CoreErrorCode.ListHashModeInvalid,
            "badCount" or "badTotal" => CoreErrorCode.ListSummaryInvalid,
            "unsafePath" or "reservedPath" or "unsafeScopePath" => CoreErrorCode.RelativePathInvalid,
            "duplicatePath" => CoreErrorCode.DuplicatePath,
            "parentConflict" => CoreErrorCode.FileDirectoryConflict,
            "overflow" => CoreErrorCode.TotalSizeOverflow,
            "nullScopePath" => CoreErrorCode.ScopePathMissing,
            _ => CoreErrorCode.ListJsonInvalid
        };
        Assert.Equal(expected, Assert.Throws<CoreException>(() => FileComparer.Compare([input], [input], Output)).Code);
        Assert.False(File.Exists(Output));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{\"fileCount\":0,\"fileCount\":1,\"totalSize\":0,\"files\":[]}")]
    public void MalformedOrAmbiguousJsonIsRejected(string content)
    {
        Directory.CreateDirectory(directory);
        string input = Path.Combine(directory, "input.json");
        File.WriteAllText(input, content);

        Assert.Equal(CoreErrorCode.ListJsonInvalid, Assert.Throws<CoreException>(() => FileComparer.Compare([input], [input], Output)).Code);
        Assert.False(File.Exists(Output));
    }

    [Fact]
    public void CrossInputParentConflictsAreRejected()
    {
        string first = WriteList("first", false, new ManifestFile("file", 3));
        string second = WriteList("second", false, new ManifestFile("file/child", 3));

        Assert.Equal(CoreErrorCode.FileDirectoryConflict, Assert.Throws<CoreException>(() => FileComparer.Compare([first, second], [first], Output)).Code);
        Assert.False(File.Exists(Output));
    }

    [Fact]
    public void ExistingOutputCannotBeOverwrittenOrUsedAsAnInputToOverwrite()
    {
        string input = WriteList("input", false);
        byte[] before = File.ReadAllBytes(input);

        Assert.Equal(CoreErrorCode.OutputExists, Assert.Throws<CoreException>(() => FileComparer.Compare([input], [input], input)).Code);
        Assert.Equal(before, File.ReadAllBytes(input));
    }

    [Fact]
    public void MissingInputsOrOutputParentDoNotPublishResults()
    {
        string input = WriteList("input", false);
        Assert.Equal(CoreErrorCode.InputsRequired, Assert.Throws<CoreException>(() => FileComparer.Compare([], [input], Output)).Code);
        Assert.Equal(CoreErrorCode.InputsRequired, Assert.Throws<CoreException>(() => FileComparer.Compare([input], [], Output)).Code);
        Assert.Throws<FileNotFoundException>(() => FileComparer.Compare([Path.Combine(directory, "missing")], [input], Output));
        Assert.Throws<DirectoryNotFoundException>(() =>
            FileComparer.Compare([input], [input], Path.Combine(directory, "missing", "output.json")));
        Assert.False(File.Exists(Output));
        Assert.False(Directory.Exists(Path.Combine(directory, "missing")));
    }

    [Fact]
    public void CliAcceptsMultipleAndRepeatedSideOptionsAndReturnsSuccessForDifferences()
    {
        string a = WriteList("a", true, new ManifestFile("file", 3, Hash));
        string b = WriteList("b", true, new ManifestFile("file", 4, OtherHash));
        string extra = WriteList("extra", false, new ManifestFile("extra", 1));

        var result = RunCli("compare", Output, "--b", b, "--a", a, extra, "--b", b);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("共有但不同 1", result.Text);
        Assert.Equal(1, ReadReport()["summary"]!["different"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("--a")]
    [InlineData("--unknown")]
    [InlineData("--b")]
    public void InvalidCliArgumentsReturnUsageError(string lastOption)
    {
        string input = WriteList("input", false);

        var result = RunCli("compare", Output, "--a", input, "--a", input, lastOption);

        Assert.Equal(2, result.ExitCode);
        Assert.False(File.Exists(Output));
    }

    [Fact]
    public void CliReportsInputConflictAsFailure()
    {
        string a = WriteList("a", true, new ManifestFile("file", 3, Hash));
        string b = WriteList("b", true, new ManifestFile("file", 4, Hash));

        var result = RunCli("compare", Output, "--a", a, b, "--b", a);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("同一侧", result.Error);
        Assert.False(File.Exists(Output));
    }

    private string WriteList(string name, bool hashes, params ManifestFile[] files)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            hashAlgorithm = hashes ? "MD5" : null,
            fileCount = files.Length,
            totalSize = files.Sum(file => file.Size),
            files
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        return path;
    }

    private JsonObject ReadReport() => JsonNode.Parse(File.ReadAllText(Output))!.AsObject();

    private static string[] Paths(JsonObject report, string property)
        => report[property]!.AsArray().Select(item => item!["path"]!.GetValue<string>()).ToArray();

    private static (int ExitCode, string Text, string Error) RunCli(params string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "HoyoFileClean.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("无法启动比较命令。");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }
}
