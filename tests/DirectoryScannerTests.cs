using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace HoyoFileClean.Tests;

public sealed class DirectoryScannerTests
{
    private const string ExampleMd5 = "900150983cd24fb0d6963f7d28e17f72";
    private readonly string directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Temp", "opencode", "HoyoFileClean.Tests", Guid.NewGuid().ToString("N"));
    private string Root => Path.Combine(directory, "root");
    private string Output => Path.Combine(directory, "scan.json");

    [Fact]
    public void ScanProducesManifestCompatibleFilesWithoutChangingInputs()
    {
        string normal = WriteFile("Data/example.bin", "abc");
        string empty = WriteFile("empty.bin", "");
        string hidden = WriteFile("hidden.bin", "abc");
        File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden | FileAttributes.System);
        DateTime modified = File.GetLastWriteTimeUtc(normal);

        ScanSummary summary = DirectoryScanner.Scan(Root + Path.DirectorySeparatorChar, Output, computeHashes: true);

        using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(Output));
        JsonElement result = json.RootElement;
        Assert.Equal((3, 6L, "MD5", 3, 6L),
            (summary.FileCount, summary.TotalSize, result.GetProperty("hashAlgorithm").GetString(),
                result.GetProperty("fileCount").GetInt32(), result.GetProperty("totalSize").GetInt64()));
        Assert.Equal(new[] { "Data/example.bin", "empty.bin", "hidden.bin" }, FilePaths(result));
        JsonElement file = result.GetProperty("files")[0];
        Assert.Equal(("Data/example.bin", 3L, ExampleMd5),
            (file.GetProperty("path").GetString(), file.GetProperty("size").GetInt64(),
                file.GetProperty("hash").GetString()));
        Assert.Equal(new[] { "path", "size", "hash" }, file.EnumerateObject().Select(property => property.Name));
        Assert.Equal("d41d8cd98f00b204e9800998ecf8427e", result.GetProperty("files")[1].GetProperty("hash").GetString());
        Assert.Equal(Root, result.GetProperty("scan").GetProperty("root").GetString());
        Assert.Empty(summary.SkippedLinks);
        Assert.Equal("abc", File.ReadAllText(normal));
        Assert.Equal("abc", File.ReadAllText(hidden));
        Assert.Empty(File.ReadAllBytes(empty));
        Assert.Equal(modified, File.GetLastWriteTimeUtc(normal));
        Assert.Empty(Directory.EnumerateFiles(directory, "*.partial"));
    }

    [Fact]
    public void DirectoryAndFileExclusionsAreExactNormalizedAndConfigurable()
    {
        WriteFile("Data/skip/deep/ignored.bin", "abc");
        WriteFile("Data/skip-other/keep.bin", "abc");
        WriteFile("ignored.bin", "abc");
        WriteFile("nested/ignored.bin", "abc");

        DirectoryScanner.Scan(Root, Output,
            ["Data/skip", @"data\SKIP\"], [Path.Combine(Root, "ignored.bin")]);

        using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(Output));
        Assert.Equal(new[] { "Data/skip-other/keep.bin", "nested/ignored.bin" }, FilePaths(json.RootElement));
        JsonElement scope = json.RootElement.GetProperty("scan");
        Assert.Equal("Data/skip", Assert.Single(scope.GetProperty("excludedDirectories").EnumerateArray()).GetString());
        Assert.Equal("ignored.bin", Assert.Single(scope.GetProperty("excludedFiles").EnumerateArray()).GetString());
        Assert.Equal(6L, json.RootElement.GetProperty("totalSize").GetInt64());
    }

    [Fact]
    public void EmptyDirectoryProducesAnEmptyLocalFileList()
    {
        Directory.CreateDirectory(Root);

        ScanSummary summary = DirectoryScanner.Scan(Root, Output);

        using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(Output));
        Assert.Equal((0, 0L), (summary.FileCount, summary.TotalSize));
        Assert.Empty(json.RootElement.GetProperty("files").EnumerateArray());
        Assert.Empty(summary.SkippedLinks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FileDirectoryBrokenAndCyclicLinksAreReportedWithoutReadingTargets(bool computeHashes)
    {
        WriteFile("keep.bin", "abc");
        string externalDirectory = Path.Combine(directory, "external");
        Directory.CreateDirectory(externalDirectory);
        string externalFile = Path.Combine(externalDirectory, "locked.bin");
        File.WriteAllText(externalFile, "must-not-read");
        File.CreateSymbolicLink(Path.Combine(Root, "file-link.bin"), externalFile);
        File.CreateSymbolicLink(Path.Combine(Root, "broken.bin"), Path.Combine(directory, "missing.bin"));
        Directory.CreateSymbolicLink(Path.Combine(Root, "directory-link"), externalDirectory);
        Directory.CreateSymbolicLink(Path.Combine(Root, "loop"), Root);
        using var locked = new FileStream(externalFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        ScanSummary summary = DirectoryScanner.Scan(Root, Output, computeHashes: computeHashes);

        using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(Output));
        Assert.Equal(new[] { "keep.bin" }, FilePaths(json.RootElement));
        Assert.Equal(new[] { "broken.bin", "directory-link", "file-link.bin", "loop" }, summary.SkippedLinks);
        Assert.Equal(summary.SkippedLinks,
            json.RootElement.GetProperty("scan").GetProperty("skippedLinks").EnumerateArray().Select(link => link.GetString()));
        Assert.Equal((1, 3L), (summary.FileCount, summary.TotalSize));
    }

    [Fact]
    public void HashingDoesNotAllocateAWholeFileBuffer()
    {
        byte[] content = new byte[4 * 1024 * 1024];
        Array.Fill(content, (byte)'a');
        Directory.CreateDirectory(Root);
        File.WriteAllBytes(Path.Combine(Root, "large.bin"), content);
        string expectedHash = Convert.ToHexString(MD5.HashData(content)).ToLowerInvariant();
        long before = GC.GetAllocatedBytesForCurrentThread();

        ScanSummary summary = DirectoryScanner.Scan(Root, Output, computeHashes: true);

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < content.Length, $"扫描分配了 {allocated} 字节，不应分配整文件缓冲区。");
        using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(Output));
        Assert.Equal((1, (long)content.Length), (summary.FileCount, summary.TotalSize));
        Assert.Equal(expectedHash, Assert.Single(json.RootElement.GetProperty("files").EnumerateArray()).GetProperty("hash").GetString());
    }

    [Fact]
    public void ExistingOutputIsNotOverwritten()
    {
        WriteFile("keep.bin", "abc");
        File.WriteAllText(Output, "keep-existing-content");

        Assert.Equal(CoreErrorCode.OutputExists, Assert.Throws<CoreException>(() => DirectoryScanner.Scan(Root, Output)).Code);

        Assert.Equal("keep-existing-content", File.ReadAllText(Output));
        Assert.Empty(Directory.EnumerateFiles(directory, "*.partial"));
    }

    [Theory]
    [InlineData("scan.json")]
    [InlineData("nested/scan.json")]
    [InlineData("../root/scan.json")]
    public void OutputInsideTheRootIsRejected(string relativeOutput)
    {
        WriteFile("nested/keep.bin", "abc");
        string output = Path.GetFullPath(relativeOutput, Root);

        Assert.Equal(CoreErrorCode.OutputInsideScanRoot, Assert.Throws<CoreException>(() => DirectoryScanner.Scan(Root, output)).Code);

        Assert.False(File.Exists(output));
        Assert.Empty(Directory.EnumerateFiles(Root, "*.partial", SearchOption.AllDirectories));
    }

    [Fact]
    public void SimilarlyNamedSiblingOutputDirectoryIsAllowed()
    {
        WriteFile("keep.bin", "abc");
        string sibling = Path.Combine(directory, "root-other");
        Directory.CreateDirectory(sibling);
        string output = Path.Combine(sibling, "scan.json");

        ScanSummary summary = DirectoryScanner.Scan(Root, output);

        Assert.Equal((1, 3L), (summary.FileCount, summary.TotalSize));
        Assert.True(File.Exists(output));
    }

    [Theory]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData("../outside.bin", false)]
    [InlineData("*.bin", true)]
    [InlineData("C:relative.bin", false)]
    public void InvalidOrEscapingExclusionsDoNotPublishAResult(string path, bool invalidName)
    {
        WriteFile("keep.bin", "abc");

        if (invalidName)
        {
            Assert.Equal(CoreErrorCode.RelativePathInvalid, Assert.Throws<CoreException>(() => DirectoryScanner.Scan(Root, Output, excludedFiles: [path])).Code);
        }
        else
        {
            Assert.Equal(path == "C:relative.bin" ? CoreErrorCode.ExclusionPathInvalid : CoreErrorCode.ExclusionOutsideRoot,
                Assert.Throws<CoreException>(() => DirectoryScanner.Scan(Root, Output, excludedFiles: [path])).Code);
        }

        AssertNoResult();
    }

    [Fact]
    public void AbsoluteExclusionOutsideTheRootIsRejected()
    {
        WriteFile("keep.bin", "abc");

        Assert.Equal(CoreErrorCode.ExclusionOutsideRoot, Assert.Throws<CoreException>(() => DirectoryScanner.Scan(Root, Output,
            excludedDirectories: [Path.Combine(directory, "external")])).Code);

        AssertNoResult();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrNonDirectoryRootDoesNotPublishAResult(bool useFile)
    {
        string file = WriteFile("keep.bin", "abc");
        string root = useFile ? file : Path.Combine(directory, "missing");

        if (useFile)
            Assert.Equal(CoreErrorCode.DirectoryMissing, Assert.Throws<CoreException>(() => DirectoryScanner.Scan(root, Output)).Code);
        else
            Assert.ThrowsAny<IOException>(() => DirectoryScanner.Scan(root, Output));

        AssertNoResult();
    }

    [Fact]
    public void MissingOutputParentIsNotCreated()
    {
        WriteFile("keep.bin", "abc");
        string parent = Path.Combine(directory, "missing");
        string output = Path.Combine(parent, "scan.json");

        Assert.ThrowsAny<IOException>(() => DirectoryScanner.Scan(Root, output));

        Assert.False(Directory.Exists(parent));
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void AFileOpenForWritingDoesNotProduceAnIncompleteResult()
    {
        string file = WriteFile("locked.bin", "abc");
        using (var writer = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            Assert.Throws<IOException>(() => DirectoryScanner.Scan(Root, Output, computeHashes: true));
        }

        AssertNoResult();
        Assert.Equal("abc", File.ReadAllText(file));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitlyExcludedFilesAreNotOpened(bool computeHashes)
    {
        WriteFile("keep.bin", "abc");
        string file = WriteFile("locked.bin", "must-not-read");
        using var locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        ScanSummary summary = DirectoryScanner.Scan(Root, Output,
            excludedFiles: ["locked.bin"], computeHashes: computeHashes);

        using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(Output));
        Assert.Equal(new[] { "keep.bin" }, FilePaths(json.RootElement));
        Assert.Equal((1, 3L), (summary.FileCount, summary.TotalSize));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ALinkedRootOrRootAncestorIsRejected(bool useChild)
    {
        WriteFile("child/keep.bin", "abc");
        string alias = Path.Combine(directory, "alias");
        Directory.CreateSymbolicLink(alias, Root);
        string root = useChild ? Path.Combine(alias, "child") : alias;

        Assert.Equal(CoreErrorCode.DirectoryIsLink, Assert.Throws<CoreException>(() => DirectoryScanner.Scan(root, Output)).Code);

        AssertNoResult();
    }

    [Fact]
    public void ALinkedOutputParentCannotRedirectWritesIntoTheRoot()
    {
        WriteFile("keep.bin", "abc");
        string alias = Path.Combine(directory, "output-alias");
        Directory.CreateSymbolicLink(alias, Root);

        Assert.Equal(CoreErrorCode.DirectoryIsLink, Assert.Throws<CoreException>(() => DirectoryScanner.Scan(Root, Path.Combine(alias, "scan.json"))).Code);

        Assert.False(File.Exists(Path.Combine(Root, "scan.json")));
        Assert.Empty(Directory.EnumerateFiles(Root, "*.partial"));
    }

    [Fact]
    public void FailedPublicationDoesNotExposeAFinalJson()
    {
        WriteFile("keep.bin", "abc");
        string output = Path.Combine(directory, new string('a', 256) + ".json");

        Assert.ThrowsAny<IOException>(() => DirectoryScanner.Scan(Root, output));

        Assert.False(File.Exists(output));
        Assert.Single(Directory.EnumerateFiles(directory, "*.partial"));
    }

    [Fact]
    public void CliAcceptsRepeatedExclusionsAndWarnsAboutSkippedLinks()
    {
        WriteFile("keep.bin", "abc");
        WriteFile("skip/ignored.bin", "abc");
        WriteFile("other-skip/ignored.bin", "abc");
        WriteFile("ignored.bin", "abc");
        WriteFile("also-ignored.bin", "abc");
        File.CreateSymbolicLink(Path.Combine(Root, "broken.bin"), Path.Combine(directory, "missing.bin"));

        var result = RunCli("scan", Root, Output,
            "--exclude-dir", "skip", "--exclude-dir", "other-skip",
            "--exclude-file", "ignored.bin", "--exclude-file", "also-ignored.bin");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("扫描完成", result.StandardOutput);
        Assert.Contains("警告", result.StandardError);
        Assert.Contains("未跟随", result.StandardError);
        Assert.Contains("broken.bin", result.StandardError);
        using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(Output));
        Assert.Equal(new[] { "keep.bin" }, FilePaths(json.RootElement));
    }

    [Theory]
    [InlineData("--unknown", "entry.bin")]
    [InlineData("--exclude-dir", null)]
    [InlineData("--exclude-dir", "--exclude-file")]
    [InlineData("--exclude-file", null)]
    [InlineData("--hash", "unexpected-value")]
    public void InvalidCliOptionsReturnUsageError(string option, string? value)
    {
        WriteFile("keep.bin", "abc");
        var arguments = new List<string> { "scan", Root, Output, option };
        if (value is not null)
        {
            arguments.Add(value);
        }

        var result = RunCli(arguments.ToArray());

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("用法", result.StandardOutput);
        AssertNoResult();
    }

    [Theory]
    [InlineData(@"\\?\")]
    [InlineData(@"\\.\")]
    [InlineData("//?/")]
    public void NamespaceAliasesOfOrdinaryRootsAreNormalized(string prefix)
    {
        WriteFile("keep.bin", "abc");

        ScanSummary summary = DirectoryScanner.Scan(prefix + Root, Output);

        using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(Output));
        Assert.Equal((1, 3L), (summary.FileCount, summary.TotalSize));
        Assert.Equal(Root, json.RootElement.GetProperty("scan").GetProperty("root").GetString());
        Assert.Equal(new[] { "keep.bin" }, FilePaths(json.RootElement));
    }

    [Theory]
    [InlineData(@"\\?\")]
    [InlineData(@"\\.\")]
    [InlineData("//?/")]
    public void NamespaceAliasesCannotHideOutputInsideTheRoot(string prefix)
    {
        WriteFile("keep.bin", "abc");
        string actualOutput = Path.Combine(Root, "unsafe-scan.json");

        Assert.Equal(CoreErrorCode.OutputInsideScanRoot, Assert.Throws<CoreException>(() => DirectoryScanner.Scan(Root, prefix + actualOutput)).Code);

        Assert.False(File.Exists(actualOutput));
        Assert.Empty(Directory.EnumerateFiles(Root, "*.partial"));
    }

    [Theory]
    [InlineData(@"\\?\")]
    [InlineData(@"\\.\")]
    [InlineData("//?/")]
    public void NamespaceAliasesOfExclusionsAreNormalized(string prefix)
    {
        WriteFile("keep.bin", "abc");
        string ignored = WriteFile("ignored.bin", "abc");

        DirectoryScanner.Scan(Root, Output, excludedFiles: [prefix + ignored]);

        using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(Output));
        Assert.Equal(new[] { "keep.bin" }, FilePaths(json.RootElement));
        Assert.Equal("ignored.bin",
            Assert.Single(json.RootElement.GetProperty("scan").GetProperty("excludedFiles").EnumerateArray()).GetString());
    }

    [Fact]
    public void UnsupportedDeviceVolumePathsAreRejectedBeforeReading()
    {
        WriteFile("keep.bin", "abc");

        Assert.Equal(CoreErrorCode.DevicePathUnsupported, Assert.Throws<CoreException>(() => DirectoryScanner.Scan(
            @"\\?\Volume{00000000-0000-0000-0000-000000000000}\missing", Output)).Code);

        AssertNoResult();
    }

    [Fact]
    public void DefaultLightScanDoesNotOpenContentsOrReportProgress()
    {
        string file = WriteFile("locked.bin", "abc");
        using var locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var updates = new List<ScanProgress>();

        ScanSummary summary = DirectoryScanner.Scan(Root, Output, progress: updates.Add);

        using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(Output));
        Assert.Equal((1, 3L), (summary.FileCount, summary.TotalSize));
        Assert.Equal("light", json.RootElement.GetProperty("scan").GetProperty("mode").GetString());
        Assert.False(json.RootElement.TryGetProperty("hashAlgorithm", out _));
        JsonElement item = Assert.Single(json.RootElement.GetProperty("files").EnumerateArray());
        Assert.Equal(new[] { "path", "size" }, item.EnumerateObject().Select(property => property.Name));
        Assert.Empty(updates);
    }

    [Fact]
    public void HeavyScanReusesTheCompletedListAndReportsByteTotals()
    {
        WriteFile("a.bin", "abc");
        WriteFile("b.bin", "hello");
        WriteFile("empty.bin", "");
        var updates = new List<ScanProgress>();
        void Progress(ScanProgress value)
        {
            updates.Add(value);
            if (updates.Count == 1)
            {
                WriteFile("created-after-list.bin", "must-not-be-reenumerated");
            }
        }

        ScanSummary summary = DirectoryScanner.Scan(Root, Output, computeHashes: true, progress: Progress);

        using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(Output));
        Assert.Equal((3, 8L), (summary.FileCount, summary.TotalSize));
        Assert.Equal(new[] { "a.bin", "b.bin", "empty.bin" }, FilePaths(json.RootElement));
        Assert.Equal("MD5", json.RootElement.GetProperty("hashAlgorithm").GetString());
        Assert.Equal("heavy", json.RootElement.GetProperty("scan").GetProperty("mode").GetString());
        Assert.Equal(new[] { ExampleMd5, "5d41402abc4b2a76b9719d911017c592", "d41d8cd98f00b204e9800998ecf8427e" },
            json.RootElement.GetProperty("files").EnumerateArray().Select(file => file.GetProperty("hash").GetString()));
        Assert.Equal(new ScanProgress(0, 3, 0, 8), updates[0]);
        Assert.Equal(new ScanProgress(3, 3, 8, 8), updates[^1]);
        Assert.Equal(100, updates[^1].Percentage);
        for (int index = 1; index < updates.Count; index++)
        {
            Assert.True(updates[index].ProcessedBytes >= updates[index - 1].ProcessedBytes);
            Assert.InRange(updates[index].Percentage, 0, 100);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void HeavyProgressHandlesEmptyAndZeroByteLists(int count)
    {
        Directory.CreateDirectory(Root);
        for (int index = 0; index < count; index++)
        {
            WriteFile($"empty-{index}.bin", "");
        }
        var updates = new List<ScanProgress>();

        DirectoryScanner.Scan(Root, Output, computeHashes: true, progress: updates.Add);

        Assert.Equal(new ScanProgress(count, count, 0, 0), updates[^1]);
        Assert.Equal(100, updates[^1].Percentage);
        Assert.All(updates, value => Assert.InRange(value.Percentage, 0, 100));
    }

    [Fact]
    public void ASizeChangeAfterListingDoesNotPublishHashResults()
    {
        WriteFile("keep.bin", "abc");
        bool changed = false;
        void Progress(ScanProgress _)
        {
            if (!changed)
            {
                WriteFile("keep.bin", "changed-size");
                changed = true;
            }
        }

        Assert.Equal(CoreErrorCode.ScanFileSizeChanged, Assert.Throws<CoreException>(() => DirectoryScanner.Scan(Root, Output,
            computeHashes: true, progress: Progress)).Code);

        AssertNoResult();
    }

    [Fact]
    public void AParentReplacedByALinkAfterListingIsNotFollowed()
    {
        WriteFile("Data/keep.bin", "abc");
        string original = Path.Combine(Root, "Data");
        string external = Path.Combine(directory, "external");
        Directory.CreateDirectory(external);
        File.WriteAllText(Path.Combine(external, "keep.bin"), "must-not-read");
        bool changed = false;
        void Progress(ScanProgress _)
        {
            if (!changed)
            {
                Directory.Move(original, Path.Combine(directory, "saved-data"));
                Directory.CreateSymbolicLink(original, external);
                changed = true;
            }
        }

        Assert.Equal(CoreErrorCode.DirectoryIsLink, Assert.Throws<CoreException>(() => DirectoryScanner.Scan(Root, Output,
            computeHashes: true, progress: Progress)).Code);

        AssertNoResult();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CliAcceptsGroupedExclusionsAndKeepsRedirectedOutputQuiet(bool computeHashes)
    {
        WriteFile("keep.bin", "abc");
        WriteFile("skip/ignored.bin", "abc");
        WriteFile("other-skip/ignored.bin", "abc");
        WriteFile("ignored.bin", "abc");
        WriteFile("also-ignored.bin", "abc");
        var arguments = new List<string> { "scan", Root, Output,
            "--exclude-dir", "skip", "other-skip", "--exclude-file", "ignored.bin", "also-ignored.bin" };
        if (computeHashes)
        {
            arguments.Add("--hash");
        }

        var result = RunCli(arguments.ToArray());

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(computeHashes ? "重扫描完成" : "轻扫描完成", result.StandardOutput);
        Assert.DoesNotContain("哈希进度", result.StandardOutput);
        using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(Output));
        Assert.Equal(new[] { "keep.bin" }, FilePaths(json.RootElement));
        Assert.Equal(computeHashes, json.RootElement.TryGetProperty("hashAlgorithm", out _));
        Assert.Equal(computeHashes,
            Assert.Single(json.RootElement.GetProperty("files").EnumerateArray()).TryGetProperty("hash", out _));
    }

    [Theory]
    [InlineData(32, 0)]
    [InlineData(32, 80)]
    [InlineData(80, 32)]
    [InlineData(1, 80)]
    [InlineData(0, 0)]
    [InlineData(120, 120)]
    public void ProgressDisplayFitsTheWindowIncludingClearingOldText(int columns, int previousWidth)
    {
        var value = new ScanProgress(10000, 20000, 1234567890, 2469135780);

        string line = ProgressDisplay.Format(value, previousWidth, columns);

        Assert.StartsWith("\r", line);
        Assert.DoesNotContain("\n", line);
        Assert.True(line.Length - 1 <= Math.Max(0, columns - 1), "进度与清尾填充不能占满或超过终端列宽。");
        Assert.All(line.Skip(1), character => Assert.InRange(character, ' ', '~'));
    }

    private string WriteFile(string relative, string content)
    {
        string path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(content));
        return path;
    }

    private static string[] FilePaths(JsonElement result)
        => result.GetProperty("files").EnumerateArray().Select(file => file.GetProperty("path").GetString()!).ToArray();

    private void AssertNoResult()
    {
        Assert.False(File.Exists(Output));
        Assert.Empty(Directory.EnumerateFiles(directory, "*.partial"));
    }

    private static (int ExitCode, string StandardOutput, string StandardError) RunCli(params string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "HoyoFileClean.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("无法启动扫描命令。");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }
}
