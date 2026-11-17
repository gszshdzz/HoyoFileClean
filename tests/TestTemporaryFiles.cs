global using HoyoFileClean.Testing;

using Xunit;

[assembly: AssemblyFixture(typeof(TestTemporaryFilesCleanup))]

namespace HoyoFileClean.Testing;

// Each test assembly owns a unique run directory; never sweep shared caches.
internal static class TestTemporaryFiles
{
    private static readonly Lazy<string> root = new(() =>
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "opencode", "test-runs",
            typeof(TestTemporaryFiles).Assembly.GetName().Name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    });

    internal static string Root => root.Value;

    internal static void Cleanup()
    {
        if (root.IsValueCreated && Directory.Exists(root.Value)) DeleteDirectory(root.Value);
    }

    // Do not recurse into junctions/symlinks, including links made by scanner tests.
    internal static void DeleteDirectory(string directory)
    {
        string path = Path.GetFullPath(directory);
        string ownedRoot = Path.GetFullPath(Root);
        if (!path.Equals(ownedRoot, StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith(ownedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("只能清理本次测试拥有的临时目录。");

        DeleteTree(path);
    }

    private static void DeleteTree(string directory)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0)
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) != 0) DeleteTree(entry);
                else
                {
                    if ((attributes & FileAttributes.ReparsePoint) == 0)
                        File.SetAttributes(entry, attributes & ~FileAttributes.ReadOnly);
                    File.Delete(entry);
                }
            }
            File.SetAttributes(directory, File.GetAttributes(directory) & ~FileAttributes.ReadOnly);
        }
        Directory.Delete(directory);
    }
}

public sealed class TestTemporaryFilesCleanup : IDisposable
{
    public void Dispose() => TestTemporaryFiles.Cleanup();
}
