using Xunit;

namespace HoyoFileClean.Tests;

public sealed class ManifestDiscoveryTests
{
    private readonly string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Temp", "opencode", "HoyoFileClean.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void FindsMetadataWithoutParsingOrRecursingAndSortsNewestFirst()
    {
        Directory.CreateDirectory(directory);
        string old = Path.Combine(directory, "manifest-old"), recent = Path.Combine(directory, "opaque-cache-file");
        File.WriteAllText(old, "not a valid manifest");
        File.WriteAllText(recent, "also invalid");
        DateTime time = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(old, time);
        File.SetLastWriteTimeUtc(recent, time.AddDays(1));
        Directory.CreateDirectory(Path.Combine(directory, "child"));
        File.WriteAllText(Path.Combine(directory, "child", "not-listed"), "ignored");
        var files = ManifestDiscovery.Find(directory);
        Assert.Equal(new[] { "opaque-cache-file", "manifest-old" }, files.Select(file => file.Name));
        Assert.Equal(recent, files[0].FullPath);
        Assert.Equal(new FileInfo(recent).Length, files[0].Size);
        Assert.Equal(time.AddDays(1), files[0].LastWriteTimeUtc);
        Assert.Equal("not a valid manifest", File.ReadAllText(old));
        Assert.Equal(2, Directory.GetFiles(directory).Length);
    }

    [Fact]
    public void EmptyDirectorySucceedsButMissingDirectoryDoesNot()
    {
        Directory.CreateDirectory(directory);
        Assert.Empty(ManifestDiscovery.Find(directory));
        Assert.Throws<DirectoryNotFoundException>(() => ManifestDiscovery.Find(Path.Combine(directory, "missing")));
    }

    [Fact]
    public void LinkFilesAreNotListedAndEqualTimesHaveStableNameOrder()
    {
        Directory.CreateDirectory(directory);
        DateTime time = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (string name in new[] { "b", "a" })
        {
            File.WriteAllText(Path.Combine(directory, name), "invalid");
            File.SetLastWriteTimeUtc(Path.Combine(directory, name), time);
        }
        File.CreateSymbolicLink(Path.Combine(directory, "link"), Path.Combine(directory, "a"));
        Assert.Equal(new[] { "a", "b" }, ManifestDiscovery.Find(directory).Select(file => file.Name));
    }
}
