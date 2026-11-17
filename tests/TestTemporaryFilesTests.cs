using Xunit;

namespace HoyoFileClean.Tests;

public sealed class TestTemporaryFilesTests
{
    [Fact]
    public void RemovesNestedAndReadOnlyFiles()
    {
        string directory = Path.Combine(TestTemporaryFiles.Root, Guid.NewGuid().ToString("N"));
        string nested = Path.Combine(directory, "cache");
        Directory.CreateDirectory(nested);
        string file = Path.Combine(nested, "sample.json");
        File.WriteAllText(file, "{}");
        File.SetAttributes(file, FileAttributes.ReadOnly);

        TestTemporaryFiles.DeleteDirectory(directory);

        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void RejectsSharedTemporaryParent()
    {
        string parent = Path.GetDirectoryName(TestTemporaryFiles.Root)!;
        Assert.Throws<InvalidOperationException>(() => TestTemporaryFiles.DeleteDirectory(parent));
        Assert.True(Directory.Exists(TestTemporaryFiles.Root));
    }

    [Fact]
    public void DirectoryLinkDoesNotDeleteItsTarget()
    {
        string directory = Path.Combine(TestTemporaryFiles.Root, Guid.NewGuid().ToString("N"));
        string target = Path.Combine(TestTemporaryFiles.Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(target);
        string file = Path.Combine(target, "keep.txt");
        File.WriteAllText(file, "keep");
        // Junctions do not require the symbolic-link privilege on Windows.
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add("mklink");
        start.ArgumentList.Add("/J");
        start.ArgumentList.Add(Path.Combine(directory, "link"));
        start.ArgumentList.Add(target);
        using var process = System.Diagnostics.Process.Start(start)!;
        Assert.True(process.WaitForExit(5000));
        Assert.Equal(0, process.ExitCode);

        TestTemporaryFiles.DeleteDirectory(directory);

        Assert.Equal("keep", File.ReadAllText(file));
    }
}
