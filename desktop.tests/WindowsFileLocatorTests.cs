using System.ComponentModel;
using Xunit;

namespace HoyoFileClean.Desktop.Tests;

public sealed class WindowsFileLocatorTests
{
    [Fact]
    public void ExistingFileWithSpacesUnicodeAndCommaIsPassedUnchanged() => NativeTestHost.OnSta(() =>
    {
        string root = Path.Combine(TestTemporaryFiles.Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "中文 空格,文件.txt");
        File.WriteAllText(path, "sample");
        var located = new List<string>();
        var errors = new List<string>();
        using var owner = new Form();
        WindowsFileLocator.Locate(owner, path, located.Add, errors.Add);
        Assert.Equal(new[] { path }, located);
        Assert.Empty(errors);
        Assert.Equal("sample", File.ReadAllText(path));
    });

    [Fact]
    public void MissingFileDoesNotLaunchExplorer() => NativeTestHost.OnSta(() =>
    {
        var located = new List<string>();
        var errors = new List<string>();
        using var owner = new Form();
        WindowsFileLocator.Locate(owner, Path.Combine(TestTemporaryFiles.Root, "missing-" + Guid.NewGuid()), located.Add, errors.Add);
        Assert.Empty(located);
        Assert.Equal(new[] { "文件已不存在，无法定位。请重新扫描确认文件状态。" }, errors);
    });

    [Theory]
    [InlineData("relative.txt")]
    [InlineData("C:\\folder\\..\\file.txt")]
    [InlineData("C:\\folder\\file\".txt")]
    public void InvalidPathsNeverLaunchExplorer(string path) => NativeTestHost.OnSta(() =>
    {
        var located = new List<string>();
        var errors = new List<string>();
        using var owner = new Form();
        WindowsFileLocator.Locate(owner, path, located.Add, errors.Add);
        Assert.Empty(located);
        Assert.Single(errors);
    });

    [Fact]
    public void LaunchFailureIsReportedWithoutChangingFile() => NativeTestHost.OnSta(() =>
    {
        string root = Path.Combine(TestTemporaryFiles.Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "file.txt");
        File.WriteAllText(path, "original");
        var errors = new List<string>();
        using var owner = new Form();
        WindowsFileLocator.Locate(owner, path, _ => throw new Win32Exception(5), errors.Add);
        Assert.Equal(new[] { "无法打开文件所在位置，请检查文件状态及访问权限。" }, errors);
        Assert.Equal("original", File.ReadAllText(path));
    });

    [Fact]
    public void LinkedDirectoryIsNotFollowed() => NativeTestHost.OnSta(() =>
    {
        string root = Path.Combine(TestTemporaryFiles.Root, Guid.NewGuid().ToString("N"));
        string target = Path.Combine(TestTemporaryFiles.Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "file.txt"), "outside");
        Directory.CreateSymbolicLink(Path.Combine(root, "link"), target);
        var located = new List<string>();
        var errors = new List<string>();
        using var owner = new Form();
        WindowsFileLocator.Locate(owner, Path.Combine(root, "link", "file.txt"), located.Add, errors.Add);
        Assert.Empty(located);
        Assert.Single(errors);
        Assert.Equal("outside", File.ReadAllText(Path.Combine(target, "file.txt")));
    });
}
