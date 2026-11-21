using HoyoFileClean.Desktop;
using Xunit;
using System.Runtime.InteropServices;

namespace HoyoFileClean.Desktop.Tests;

public sealed class FileOperationExecutorTests
{
    private static (string Root, FileListSnapshot Game, FileOperationRequest Request) Request(
        FileOperationKind kind, string? destination = null, bool hashes = false)
    {
        string root = NewRoot();
        File.WriteAllText(Path.Combine(root, "first"), "first content");
        File.WriteAllText(Path.Combine(root, "second"), "second content");
        File.WriteAllText(Path.Combine(root, "keep"), "keep content");
        var game = DirectoryScanner.Scan(root, computeHashes: hashes, cancellationToken: TestContext.Current.CancellationToken);
        return (root, game, new(kind, root, new[] { "first", "second" }.Select(path =>
            new DifferenceRow(path, game.Files[path], null, DifferenceCategory.GameOnly)).ToArray(), destination));
    }

    private static string NewRoot()
    {
        string root = Path.Combine(TestTemporaryFiles.Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    public void MovePreservesRelativeStructureAndDoesNotTouchUnselectedFiles()
    {
        string root = NewRoot(), target = NewRoot();
        Directory.CreateDirectory(Path.Combine(root, "data"));
        File.WriteAllText(Path.Combine(root, "data", "selected"), "selected content");
        File.WriteAllText(Path.Combine(root, "data", "keep"), "keep content");
        var game = DirectoryScanner.Scan(root, cancellationToken: TestContext.Current.CancellationToken);
        var request = new FileOperationRequest(FileOperationKind.Move, root,
            [new("data/selected", game.Files["data/selected"], null, DifferenceCategory.GameOnly)], target);

        var result = FileOperationExecutor.Execute(request, game);

        Assert.Null(result.Error);
        Assert.Equal(FileOperationState.Succeeded, Assert.Single(result.Files).State);
        Assert.Equal("selected content", File.ReadAllText(Path.Combine(target, "data", "selected")));
        Assert.False(File.Exists(Path.Combine(root, "data", "selected")));
        Assert.Equal("keep content", File.ReadAllText(Path.Combine(root, "data", "keep")));
        Assert.True(Directory.Exists(Path.Combine(root, "data")));
        Assert.False(File.Exists(Path.Combine(target, "data", "keep")));
    }

    [Fact]
    public void PermanentDeleteOnlyDeletesTheExplicitFiles()
    {
        var (root, game, request) = Request(FileOperationKind.Delete);
        var result = FileOperationExecutor.Execute(request, game);
        Assert.Null(result.Error);
        Assert.Equal(2, result.SucceededCount);
        Assert.False(File.Exists(Path.Combine(root, "first")));
        Assert.False(File.Exists(Path.Combine(root, "second")));
        Assert.Equal("keep content", File.ReadAllText(Path.Combine(root, "keep")));
        Assert.True(Directory.Exists(root));
    }

    [Fact]
    public void LaterDestinationCollisionRejectsTheWholeBatchWithoutCreatingDirectories()
    {
        string target = NewRoot();
        var (root, game, request) = Request(FileOperationKind.Move, target);
        File.WriteAllText(Path.Combine(target, "second"), "existing target");
        var result = FileOperationExecutor.Execute(request, game);
        Assert.Contains("整批预检失败", result.Error);
        Assert.Equal(0, result.SucceededCount);
        Assert.Equal("first content", File.ReadAllText(Path.Combine(root, "first")));
        Assert.Equal("second content", File.ReadAllText(Path.Combine(root, "second")));
        Assert.Equal("existing target", File.ReadAllText(Path.Combine(target, "second")));
        Assert.Equal(new[] { "second" }, Directory.GetFileSystemEntries(target).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangedFileRejectsTheWholeBatch(bool hashes)
    {
        var (root, game, request) = Request(FileOperationKind.Delete, hashes: hashes);
        File.WriteAllText(Path.Combine(root, "second"), hashes ? "SECOND CONTENT" : "different size added");
        var result = FileOperationExecutor.Execute(request, game);
        Assert.Contains("整批预检失败", result.Error);
        Assert.Equal(0, result.SucceededCount);
        Assert.Equal("first content", File.ReadAllText(Path.Combine(root, "first")));
        Assert.True(File.Exists(Path.Combine(root, "second")));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("C:/outside")]
    [InlineData("first:stream")]
    [InlineData("first.")]
    [InlineData("CON")]
    [InlineData("not-scanned")]
    public void ForgedPathsNeverAuthorizeDeletion(string path)
    {
        var (root, game, request) = Request(FileOperationKind.Delete);
        request = request with { Files = [new(path, new(path, 13), null, DifferenceCategory.GameOnly)] };
        var result = FileOperationExecutor.Execute(request, game);
        Assert.NotNull(result.Error);
        Assert.Equal(0, result.SucceededCount);
        Assert.Equal("first content", File.ReadAllText(Path.Combine(root, "first")));
        Assert.Equal("second content", File.ReadAllText(Path.Combine(root, "second")));
    }

    [Fact]
    public void DuplicatePathsAreRejectedWithoutTouchingFiles()
    {
        var (root, game, request) = Request(FileOperationKind.Delete);
        request = request with { Files = [request.Files[0], request.Files[0]] };
        var result = FileOperationExecutor.Execute(request, game);
        Assert.NotNull(result.Error);
        Assert.Equal(0, result.SucceededCount);
        Assert.Equal("first content", File.ReadAllText(Path.Combine(root, "first")));
    }

    [Theory]
    [InlineData(DifferenceCategory.ReferenceOnly)]
    [InlineData(DifferenceCategory.OutsideScope)]
    public void UnselectableRowsAreRejected(DifferenceCategory category)
    {
        var (root, game, request) = Request(FileOperationKind.Delete);
        request = request with { Files = [request.Files[0] with { Category = category,
            Game = category == DifferenceCategory.ReferenceOnly ? null : request.Files[0].Game }] };
        var result = FileOperationExecutor.Execute(request, game);
        Assert.NotNull(result.Error);
        Assert.Equal("first content", File.ReadAllText(Path.Combine(root, "first")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void OverlappingRootsAreRejected(int kind)
    {
        var (root, game, request) = Request(FileOperationKind.Move);
        string target = kind == 0 ? root : kind == 1 ? Path.Combine(root, "target") : Path.GetDirectoryName(root)!;
        var result = FileOperationExecutor.Execute(request with { Destination = target }, game);
        Assert.Contains("不能相同或互相包含", result.Error);
        Assert.Equal("first content", File.ReadAllText(Path.Combine(root, "first")));
        Assert.False(Directory.Exists(Path.Combine(root, "target")));
    }

    [Fact]
    public void RootMismatchDoesNotTouchEitherRoot()
    {
        var (root, game, request) = Request(FileOperationKind.Delete);
        string outside = NewRoot();
        File.WriteAllText(Path.Combine(outside, "first"), "outside sentinel");
        var result = FileOperationExecutor.Execute(request with { Root = outside }, game);
        Assert.NotNull(result.Error);
        Assert.Equal("first content", File.ReadAllText(Path.Combine(root, "first")));
        Assert.Equal("outside sentinel", File.ReadAllText(Path.Combine(outside, "first")));
    }

    [Fact]
    public void LinkInsertedAfterScanningCannotEscapeToOutsideFile()
    {
        string root = NewRoot(), outside = NewRoot(), target = NewRoot();
        string original = Path.Combine(root, "original");
        Directory.CreateDirectory(original);
        File.WriteAllText(Path.Combine(original, "file"), "abc");
        File.WriteAllText(Path.Combine(outside, "file"), "abc");
        var game = DirectoryScanner.Scan(root, cancellationToken: TestContext.Current.CancellationToken);
        Directory.Move(original, Path.Combine(root, "saved"));
        Directory.CreateSymbolicLink(original, outside);
        var request = new FileOperationRequest(FileOperationKind.Move, root,
            [new("original/file", game.Files["original/file"], null, DifferenceCategory.GameOnly)], target);
        var result = FileOperationExecutor.Execute(request, game);
        Assert.NotNull(result.Error);
        Assert.Equal("abc", File.ReadAllText(Path.Combine(outside, "file")));
        Assert.Equal("abc", File.ReadAllText(Path.Combine(root, "saved", "file")));
        Assert.Empty(Directory.GetFileSystemEntries(target));
    }

    [Fact]
    public void DestinationLinkDoesNotWriteOutsideTarget()
    {
        string root = NewRoot(), outside = NewRoot(), target = NewRoot();
        Directory.CreateDirectory(Path.Combine(root, "data"));
        File.WriteAllText(Path.Combine(root, "data", "file"), "abc");
        Directory.CreateSymbolicLink(Path.Combine(target, "data"), outside);
        var game = DirectoryScanner.Scan(root, cancellationToken: TestContext.Current.CancellationToken);
        var result = FileOperationExecutor.Execute(new(FileOperationKind.Move, root,
            [new("data/file", game.Files["data/file"], null, DifferenceCategory.GameOnly)], target), game);
        Assert.NotNull(result.Error);
        Assert.Equal("abc", File.ReadAllText(Path.Combine(root, "data", "file")));
        Assert.Empty(Directory.GetFileSystemEntries(outside));
    }

    [Fact]
    public void FailureAfterFirstMovePreservesCompletedFilesAndStopsRemainingFiles()
    {
        string target = NewRoot();
        var (root, game, request) = Request(FileOperationKind.Move, target);
        var selected = request.Files.ToList();
        selected.Add(new("keep", game.Files["keep"], null, DifferenceCategory.GameOnly));
        request = request with { Files = selected };
        var result = FileOperationExecutor.Execute(request, game, _ =>
            File.SetAttributes(Path.Combine(root, "second"), FileAttributes.ReadOnly));
        Assert.Contains("执行失败", result.Error);
        Assert.Equal(new[] { FileOperationState.Succeeded, FileOperationState.Failed, FileOperationState.NotProcessed },
            result.Files.Select(file => file.State));
        Assert.Equal("first content", File.ReadAllText(Path.Combine(target, "first")));
        Assert.False(File.Exists(Path.Combine(root, "first")));
        Assert.Equal("second content", File.ReadAllText(Path.Combine(root, "second")));
        Assert.Equal("keep content", File.ReadAllText(Path.Combine(root, "keep")));
        Assert.False(File.Exists(Path.Combine(target, "second")));
        Assert.False(File.Exists(Path.Combine(target, "keep")));
    }

    [Fact]
    public void SameSizeReplacementBetweenPreflightAndExecutionIsRejected()
    {
        string target = NewRoot();
        var (root, game, request) = Request(FileOperationKind.Move, target);
        var result = FileOperationExecutor.Execute(request, game, _ =>
        {
            File.Move(Path.Combine(root, "second"), Path.Combine(root, "old-second"));
            File.WriteAllText(Path.Combine(root, "second"), "SECOND CONTENT");
        });
        Assert.Contains("执行失败", result.Error);
        Assert.Equal(1, result.SucceededCount);
        Assert.Equal("SECOND CONTENT", File.ReadAllText(Path.Combine(root, "second")));
        Assert.Equal("second content", File.ReadAllText(Path.Combine(root, "old-second")));
        Assert.False(File.Exists(Path.Combine(target, "second")));
    }

    [Fact]
    public void CopyDeleteEnginePreservesContentBeforeRemovingSource()
    {
        var (root, game, _) = Request(FileOperationKind.Move);
        string target = NewRoot(), destination = Path.Combine(target, "copied");
        using (var directories = new WindowsDirectoryLease())
        {
            directories.Pin(root);
            directories.Pin(target);
            using var input = WindowsSourceFile.Open(Path.Combine(root, "first"));
            input.Validate(game.Files["first"]);
            input.CopyThenDelete(destination);
        }
        Assert.Equal("first content", File.ReadAllText(destination));
        Assert.False(File.Exists(Path.Combine(root, "first")));
        Assert.Equal("second content", File.ReadAllText(Path.Combine(root, "second")));
    }

    [Fact]
    public void CopyDeleteEngineDoesNotOverwriteOrDeleteSourceOnCollision()
    {
        var (root, game, _) = Request(FileOperationKind.Move);
        string target = NewRoot(), destination = Path.Combine(target, "copied");
        File.WriteAllText(destination, "existing target");
        using (var directories = new WindowsDirectoryLease())
        {
            directories.Pin(root);
            directories.Pin(target);
            using var input = WindowsSourceFile.Open(Path.Combine(root, "first"));
            input.Validate(game.Files["first"]);
            Assert.Throws<System.ComponentModel.Win32Exception>(() => input.CopyThenDelete(destination));
        }
        Assert.Equal("existing target", File.ReadAllText(destination));
        Assert.Equal("first content", File.ReadAllText(Path.Combine(root, "first")));
    }

    [Fact]
    public void PinnedDirectoriesDenyRenameHandlesUntilTheBatchFinishes()
    {
        string root = NewRoot();
        using (var directories = new WindowsDirectoryLease())
        {
            directories.Pin(root);
            var error = Assert.Throws<System.ComponentModel.Win32Exception>(() =>
            {
                using var rename = WindowsNative.Open(root, 0x00010000, FileShare.ReadWrite | FileShare.Delete);
            });
            Assert.Equal(32, error.NativeErrorCode); // ERROR_SHARING_VIOLATION
            Assert.Empty(Directory.GetFileSystemEntries(root));
        }
        using var available = WindowsNative.Open(root, 0x00010000, FileShare.ReadWrite | FileShare.Delete);
        Assert.False(available.IsInvalid);
    }

    [Fact]
    public void FileBeingEditedRejectsTheWholeBatchAndPromptsToCloseOtherPrograms()
    {
        var (root, game, request) = Request(FileOperationKind.Delete);
        using (var editor = new FileStream(Path.Combine(root, "second"), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            var result = FileOperationExecutor.Execute(request, game);
            Assert.Contains("整批预检失败", result.Error);
            Assert.Contains("请关闭游戏、更新器或编辑器后重试", result.Error);
            Assert.Equal(0, result.SucceededCount);
            Assert.Equal(FileOperationState.Failed, result.Files[1].State);
        }
        Assert.Equal("first content", File.ReadAllText(Path.Combine(root, "first")));
        Assert.Equal("second content", File.ReadAllText(Path.Combine(root, "second")));
        Assert.Equal("keep content", File.ReadAllText(Path.Combine(root, "keep")));
    }

    [Fact]
    public void LockedSourceRejectsNewEditorsUntilTheOperationFinishes()
    {
        var (root, _, _) = Request(FileOperationKind.Delete);
        string path = Path.Combine(root, "first");
        using (var input = WindowsSourceFile.Open(path))
        {
            Assert.Throws<IOException>(() =>
            {
                using var editor = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
            });
        }
        Assert.Equal("first content", File.ReadAllText(path));
        using var available = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        Assert.Equal(13, available.Length);
    }

    [Fact]
    public void NativeRecycleFailureDoesNotFallBackToPermanentDelete()
    {
        var (root, game, _) = Request(FileOperationKind.Recycle);
        string path = Path.Combine(root, "first");
        using var directories = new WindowsDirectoryLease();
        directories.Pin(root);
        using var locked = WindowsSourceFile.Open(path);
        var error = Record.Exception(() => WindowsRecycleBin.RunOnSta(() =>
        {
            using var bin = new WindowsRecycleBin();
            return bin.Recycle(path, game.Files["first"], locked.Identity);
        }));
        Assert.NotNull(error);
        Assert.True(error is IOException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException,
            error.ToString());
        locked.Dispose();
        Assert.Equal("first content", File.ReadAllText(path));
        Assert.Equal("second content", File.ReadAllText(Path.Combine(root, "second")));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(0x2u)]
    [InlineData(0x400u)]
    public void ShellPermanentDeleteRequestIsAbortedBeforeAccessingAnyItem(uint flags)
    {
        var (root, game, _) = Request(FileOperationKind.Recycle);
        var guard = new WindowsRecycleBin.RecycleGuard(Path.Combine(root, "first"), game.Files["first"], default);
        Assert.Equal(unchecked((int)0x80004004), guard.PreDeleteItem(flags, null!));
        Assert.IsType<IOException>(guard.Failure);
        Assert.False(guard.Completed);
        Assert.Equal("first content", File.ReadAllText(Path.Combine(root, "first")));
        Assert.Equal("second content", File.ReadAllText(Path.Combine(root, "second")));
    }

    [Fact(Explicit = true)]
    public void NativeRecycleMovesOnlyTheSampleAndShellCanRestoreIt() => NativeTestHost.OnSta(() =>
    {
        var (root, game, _) = Request(FileOperationKind.Recycle);
        var recycled = new Dictionary<string, string>();
        try
        {
            using var directories = new WindowsDirectoryLease();
            using var bin = new WindowsRecycleBin();
            directories.Pin(root);
            foreach (string name in new[] { "first", "second" })
            {
                string path = Path.Combine(root, name);
                using var input = WindowsSourceFile.Open(path, allowDelete: true);
                recycled.Add(name, bin.Recycle(path, game.Files[name], input.Identity));
            }
            Assert.Equal(2, recycled.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.False(File.Exists(Path.Combine(root, "first")));
            Assert.False(File.Exists(Path.Combine(root, "second")));
            Assert.Equal("first content", File.ReadAllText(recycled["first"]));
            Assert.Equal("second content", File.ReadAllText(recycled["second"]));
            Assert.Equal("keep content", File.ReadAllText(Path.Combine(root, "keep")));
        }
        finally
        {
            foreach (var item in recycled) RestoreSample(item.Value, root, item.Key);
        }
        Assert.Equal("first content", File.ReadAllText(Path.Combine(root, "first")));
        Assert.Equal("second content", File.ReadAllText(Path.Combine(root, "second")));
        foreach (string path in recycled.Values)
        {
            Assert.False(File.Exists(path));
            string metadata = Path.Combine(Path.GetDirectoryName(path)!, "$I" + Path.GetFileName(path)[2..]);
            Assert.False(File.Exists(metadata), $"本轮样本的回收元数据未移除：{metadata}");
        }
    });

    private static void RestoreSample(string recycled, string root, string name)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application", throwOnError: true)!)!;
        dynamic bucket = shell.NameSpace(10);
        dynamic items = bucket.Items();
        try
        {
            for (int index = 0; index < (int)items.Count; index++)
            {
                dynamic item = items.Item(index);
                try
                {
                    if (!string.Equals((string)item.Path, recycled, StringComparison.OrdinalIgnoreCase)) continue;
                    Assert.False(File.Exists(Path.Combine(root, name)));
                    dynamic verbs = item.Verbs();
                    try
                    {
                        for (int verbIndex = 0; verbIndex < (int)verbs.Count; verbIndex++)
                        {
                            dynamic verb = verbs.Item(verbIndex);
                            try
                            {
                                string label = (string)verb.Name;
                                string plain = label.Replace("&", "");
                                if (!plain.StartsWith("还原", StringComparison.Ordinal)
                                    && !plain.StartsWith("Restore", StringComparison.OrdinalIgnoreCase)) continue;
                                verb.DoIt();
                                NativeTestHost.WaitUntil(() => File.Exists(Path.Combine(root, name)) && !File.Exists(recycled),
                                    conditionText: $"还原本轮样本 {recycled} -> {Path.Combine(root, name)}");
                                return;
                            }
                            finally { Marshal.FinalReleaseComObject(verb); }
                        }
                    }
                    finally { Marshal.FinalReleaseComObject(verbs); }
                    Assert.Fail("本轮系统回收站诊断未找到中文或英文的还原命令。");
                    return;
                }
                finally { Marshal.FinalReleaseComObject(item); }
            }
            Assert.Fail($"系统回收站中找不到本轮样本：{recycled}");
        }
        finally
        {
            Marshal.FinalReleaseComObject(items);
            Marshal.FinalReleaseComObject(bucket);
            Marshal.FinalReleaseComObject(shell);
        }
    }
}
