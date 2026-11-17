using Xunit;

namespace HoyoFileClean.Tests;

public sealed class GameExclusionPresetTests
{
    private static string Root()
    {
        string root = Path.Combine(TestTemporaryFiles.Root, "preset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    [Theory]
    [InlineData("hk4e_cn", 2)]
    [InlineData("hkrpg_cn", 11)]
    [InlineData("nap_cn", 1)]
    [InlineData("bh3_cn", 3)]
    public void MetadataRemainsScannedAndComparedWhileLdiffIsExcluded(string biz, int excludedFileCount)
    {
        string root = Root();
        File.WriteAllText(Path.Combine(root, "config.ini"), "uapc={\"" + biz + "\":{}}");
        File.WriteAllText(Path.Combine(root, "pkg_version"), "local");
        File.WriteAllText(Path.Combine(root, "Audio_Chinese_pkg_version"), "audio");
        Directory.CreateDirectory(Path.Combine(root, "ldiff"));
        File.WriteAllText(Path.Combine(root, "ldiff", "patch"), "patch");
        GameFilePreset preset = Assert.IsType<GameFilePreset>(
            GameIdentifier.GetFilePreset(GameIdentifier.Identify(root, TestContext.Current.CancellationToken)));
        FileListSnapshot scan = DirectoryScanner.Scan(root, preset.ScanDirectories, preset.ScanFiles,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains("pkg_version", scan.Files.Keys);
        Assert.Contains("Audio_Chinese_pkg_version", scan.Files.Keys);
        Assert.Contains("config.ini", preset.ScanFiles);
        Assert.Equal(excludedFileCount, preset.ScanFiles.Count);
        Assert.DoesNotContain("config.ini", scan.Files.Keys);
        Assert.DoesNotContain("pkg_version", preset.ScanFiles);
        Assert.DoesNotContain("Audio_Chinese_pkg_version", preset.ScanFiles);
        Assert.DoesNotContain("ldiff/patch", scan.Files.Keys);
        Assert.Equal(0, scan.HashedFileCount);
        string other = Root();
        File.WriteAllText(Path.Combine(other, "PKG_VERSION"), "changed and longer");
        File.WriteAllText(Path.Combine(other, "Audio_Chinese_pkg_version"), "changed audio");
        ComparisonResult result = FileComparer.Compare(scan,
            DirectoryScanner.Scan(other, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(2, result.Different.Count);
        Assert.Contains(result.Different, item => item.A.Path == "pkg_version");
        Assert.Contains(result.Different, item => item.A.Path == "Audio_Chinese_pkg_version");
    }

    [Fact]
    public void LightweightIdentificationDoesNotOpenUnrelatedLockedFiles()
    {
        string root = Root();
        File.WriteAllText(Path.Combine(root, "config.ini"), "uapc={\"hk4e_cn\":{}}");
        using var locked = new FileStream(Path.Combine(root, "unrelated.bin"), FileMode.Create,
            FileAccess.ReadWrite, FileShare.None);
        GameIdentification identification = GameIdentifier.Identify(root, TestContext.Current.CancellationToken);
        Assert.NotNull(GameIdentifier.GetFilePreset(identification));
        Assert.Throws<OperationCanceledException>(() => GameIdentifier.Identify(root, new CancellationToken(true)));
    }

    [Fact]
    public void UnknownGameHasNoPreset()
    {
        string root = Root();
        File.WriteAllText(Path.Combine(root, "config.ini"), "uapc={\"unknown_cn\":{}}");
        Assert.Null(GameIdentifier.GetFilePreset(GameIdentifier.Identify(root, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public void HiddenSystemVoiceFilesRemainIdentificationCandidates()
    {
        string root = Root();
        string voice = Path.Combine(root, "YuanShen_Data", "StreamingAssets", "AudioAssets", "Chinese");
        Directory.CreateDirectory(voice);
        string resource = Path.Combine(voice, "voice.pck");
        File.WriteAllText(resource, "resource");
        File.SetAttributes(voice, FileAttributes.Directory | FileAttributes.Hidden | FileAttributes.System);
        File.SetAttributes(resource, FileAttributes.Hidden | FileAttributes.System);
        GameIdentification result = GameIdentifier.Identify(root, TestContext.Current.CancellationToken);
        Assert.Equal("zh-cn", Assert.Single(result.Information["package"].Values).RawValue);
    }

    [Theory]
    [InlineData("YuanShen_Data/StreamingAssets/AudioAssets/Chinese")]
    [InlineData("YuanShen_Data")]
    public void FileAtCandidateDirectoryDoesNotInvalidateOtherIdentification(string relative)
    {
        string root = Root();
        File.WriteAllText(Path.Combine(root, "config.ini"), "uapc={\"hk4e_cn\":{}}");
        string candidate = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(candidate)!);
        File.WriteAllText(candidate, "not a directory");
        GameIdentification result = GameIdentifier.Identify(root, TestContext.Current.CancellationToken);
        Assert.Equal("hk4e_cn", Assert.Single(result.Information["biz"].Values).RawValue);
        Assert.False(result.Information.ContainsKey("package"));
    }

    [Fact]
    public void IdentificationDoesNotReadOrHashExecutableVoiceOrPackageFiles()
    {
        string root = Root();
        File.WriteAllText(Path.Combine(root, "config.ini"), "uapc={\"hk4e_cn\":{}}\ngame_version=7.2.0");
        string voice = Path.Combine(root, "YuanShen_Data", "StreamingAssets", "AudioAssets", "Chinese", "nested");
        Directory.CreateDirectory(voice);
        using var executable = new FileStream(Path.Combine(root, "YuanShen.exe"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var resource = new FileStream(Path.Combine(voice, "voice.pck"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var bodyPackage = new FileStream(Path.Combine(root, "pkg_version"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var voicePackage = new FileStream(Path.Combine(root, "Audio_Chinese_pkg_version"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        GameIdentification result = GameIdentifier.Identify(root, TestContext.Current.CancellationToken);
        Assert.Equal("7.2.0", Assert.Single(result.Information["version"].Values).RawValue);
        Assert.Contains(result.Information["package"].Values, value => value.RawValue == "zh-cn");
        Assert.Contains(result.Information["package"].Values, value => value.RawValue == "game");
        Assert.Equal("hk4e_cn", Assert.Single(result.Information["biz"].Values).RawValue);
    }

    [Fact]
    public void FileAtExactCandidateParentDoesNotInvalidateOtherIdentification()
    {
        string root = Root();
        File.WriteAllText(Path.Combine(root, "config.ini"), "uapc={\"hk4e_cn\":{}}");
        File.WriteAllText(Path.Combine(root, "metadata"), "not a directory");
        var configuration = GameIdentifier.LoadConfiguration();
        configuration.Detections["ini_version"] = configuration.Detections["ini_version"] with
        {
            Paths = ["metadata/config.ini"]
        };
        GameIdentification result = GameIdentifier.Identify(root, configuration, TestContext.Current.CancellationToken);
        Assert.Equal("hk4e_cn", Assert.Single(result.Information["biz"].Values).RawValue);
        Assert.False(result.Information.ContainsKey("version"));
    }

    [Fact]
    public void OnlyConfiguredTextMetadataIsRead()
    {
        string root = Root();
        File.WriteAllText(Path.Combine(root, "metadata.ini"), "uapc={\"hk4e_cn\":{}}\ngame_version=7.2.0");
        using var unrelatedConfig = new FileStream(Path.Combine(root, "config.ini"), FileMode.Create,
            FileAccess.ReadWrite, FileShare.None);
        var configuration = GameIdentifier.LoadConfiguration();
        foreach (string id in configuration.Detections.Keys.ToArray())
            if (configuration.Detections[id].Pattern is not null)
                configuration.Detections[id] = configuration.Detections[id] with { Paths = ["metadata.ini"] };
        GameIdentification result = GameIdentifier.Identify(root, configuration, TestContext.Current.CancellationToken);
        Assert.Equal("7.2.0", Assert.Single(result.Information["version"].Values).RawValue);
        Assert.Equal("hk4e_cn", Assert.Single(result.Information["biz"].Values).RawValue);
    }

    [Fact]
    public void ConfiguredDirectoryLinksAreSkippedWithoutReadingTheirTargets()
    {
        string root = Root(), outside = Root();
        File.WriteAllText(Path.Combine(root, "config.ini"), "uapc={\"hk4e_cn\":{}}");
        using var resource = new FileStream(Path.Combine(outside, "voice.pck"), FileMode.Create,
            FileAccess.ReadWrite, FileShare.None);
        string parent = Path.Combine(root, "YuanShen_Data", "StreamingAssets", "AudioAssets");
        Directory.CreateDirectory(parent);
        Directory.CreateSymbolicLink(Path.Combine(parent, "Chinese"), outside);
        Directory.CreateSymbolicLink(Path.Combine(root, "Linked"), outside);
        var configuration = GameIdentifier.LoadConfiguration();
        configuration.Detections["ini_version"] = configuration.Detections["ini_version"] with { Paths = ["Linked/metadata.ini"] };
        GameIdentification result = GameIdentifier.Identify(root, configuration, TestContext.Current.CancellationToken);
        Assert.Equal("hk4e_cn", Assert.Single(result.Information["biz"].Values).RawValue);
        Assert.False(result.Information.ContainsKey("package"));
        Assert.False(result.Information.ContainsKey("version"));
    }
}
