using Xunit;

namespace HoyoFileClean.Tests;

public sealed class ManifestIdentificationConfigTests
{
    private static IReadOnlyDictionary<string, ManifestMetadataField> Identify(params string[] paths)
        => new ManifestIdentifier(ManifestIdentificationRules.Create()).Identify(new FileListDocument(paths.Length, paths.Length,
            paths.Select(path => new ManifestFile(path, 1)).ToArray()), TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("YuanShen.exe", "hk4e", "hk4e_cn")]
    [InlineData("GenshinImpact.exe", "hk4e", "hk4e_global")]
    [InlineData("StarRail.exe", "hkrpg", null)]
    [InlineData("ZenlessZoneZero.exe", "nap", null)]
    [InlineData("BH3.exe", "bh3", null)]
    public void RootExecutableSeparatesKnownGameFromCertainOfficialBiz(string path, string game, string? biz)
    {
        var result = Identify(path);
        Assert.Equal(game, result.GetValueOrDefault("game")?.Value);
        Assert.Equal(biz, result.GetValueOrDefault("biz")?.Value);
        Assert.Equal("game", result["package"].Value);
        Assert.Equal(path == "YuanShen.exe" ? "exe_hk4e_cn" : path == "GenshinImpact.exe"
            ? "exe_hk4e_global" : "exe_" + game, Assert.Single(result["package"].Matches).Id);
        Assert.Equal(biz is null ? 2 : 3, result.Count);
        Assert.Empty(Identify("Backup/" + path));
        Assert.Empty(Identify(path + ".bak"));
    }

    [Theory]
    [InlineData("StarRail_Data/", "hkrpg")]
    [InlineData("ZenlessZoneZero_Data/", "nap")]
    [InlineData("BH3_Data/", "bh3")]
    public void DedicatedDirectoryIdentifiesOnlyGameWithPathBoundaries(string prefix, string game)
    {
        var result = Identify(prefix + "file");
        Assert.Equal(game, result["game"].Value);
        Assert.Single(result);
        Assert.Empty(Identify("Backup/" + prefix + "file"));
        Assert.Empty(Identify(prefix.TrimEnd('/') + "Other/file"));
    }

    [Theory]
    [InlineData("YuanShen_Data/StreamingAssets/AudioAssets/", "hk4e", "hk4e_cn", "Chinese", "English(US)", "Japanese", "Korean")]
    [InlineData("GenshinImpact_Data/StreamingAssets/AudioAssets/", "hk4e", "hk4e_global", "Chinese", "English(US)", "Japanese", "Korean")]
    [InlineData("StarRail_Data/Persistent/Audio/AudioPackage/Windows/", "hkrpg", null, "Chinese(PRC)", "English", "Japanese", "Korean")]
    public void VoiceRulesIdentifyAllFourOfficialPackageNames(string prefix, string game, string? biz, params string[] directories)
    {
        string[] packages = ["zh-cn", "en-us", "ja-jp", "ko-kr"];
        for (int index = 0; index < directories.Length; index++)
        {
            var result = Identify(prefix + directories[index] + "/example.pck");
            Assert.Equal(game, result.GetValueOrDefault("game")?.Value);
            Assert.Equal(biz, result.GetValueOrDefault("biz")?.Value);
            Assert.False(result["game"].Conflict);
            Assert.Equal(biz is null ? 2 : 1, result["game"].Matches.Count);
            Assert.Equal(packages[index], result["package"].Value);
            Assert.Equal(biz is null ? 2 : 3, result.Count);
        }
    }

    [Fact]
    public void CorroboratingEvidenceIsNotAConflictButDistinctMeaningsAre()
    {
        var main = Identify("StarRail.exe", "StarRail_Data/file");
        Assert.False(main["game"].Conflict);
        Assert.Equal("hkrpg", main["game"].Value);
        Assert.Equal(2, main["game"].Matches.Count);
        var mixed = Identify("YuanShen.exe", "GenshinImpact.exe");
        Assert.False(mixed["game"].Conflict);
        Assert.Equal("hk4e", mixed["game"].Value);
        Assert.True(mixed["biz"].Conflict);
        Assert.Null(mixed["biz"].Value);
        Assert.False(mixed["package"].Conflict);
        Assert.Equal("game", mixed["package"].Value);
        var mixedPackages = Identify("YuanShen.exe", "YuanShen_Data/StreamingAssets/AudioAssets/Chinese/file.pck");
        Assert.False(mixedPackages["game"].Conflict);
        Assert.False(mixedPackages["package"].Conflict);
        Assert.True(mixedPackages["package"].Multiple);
        Assert.Equal(new[] { "game", "zh-cn" }, mixedPackages["package"].Matches.Select(match => match.Value).Order());
        Assert.Null(mixedPackages["package"].Value);
    }

    [Theory]
    [InlineData("ZenlessZoneZero_Data/StreamingAssets/Audio/Windows/Full/Cn/10100.pck", "nap")]
    [InlineData("ZenlessZoneZero_Data/StreamingAssets/Blocks/111589298.blk", "nap")]
    [InlineData("BH3_Data/StreamingAssets/Asb/pc/0026c93926a82e64.wmv", "bh3")]
    [InlineData("StarRail_Data/other", "hkrpg")]
    [InlineData("ZenlessZoneZero_Data/other", "nap")]
    [InlineData("BH3_Data/other", "bh3")]
    public void KnownGamesDoNotImplyOfficialBizOrAmbiguousPackage(string path, string game)
    {
        var result = Identify(path);
        Assert.Equal(game, result["game"].Value);
        Assert.Single(result);
    }

    [Theory]
    [InlineData("YuanShen_Data/other")]
    [InlineData("GenshinImpact_Data/other")]
    public void RemovedRedundantGenshinDirectoryRulesDoNotMatch(string path) => Assert.Empty(Identify(path));

    [Theory]
    [InlineData("Audio_Chinese_pkg_version")]
    [InlineData("pkg_version")]
    [InlineData("HoYoKProtect.sys")]
    [InlineData("GameAssembly.dll")]
    public void SharedFilenamesDoNotIdentifyAnything(string path) => Assert.Empty(Identify(path));

    [Fact]
    public void ExactAndPrefixCannotBothBeEnabled()
        => Assert.Throws<CoreException>(() => new ManifestIdentifier(
            new IdentificationConfiguration<ManifestDetectionDefinition[]>(
                new() { ["id"] = [new("files.path", "x", Exact: true, Prefix: true,
                    Meanings: new() { ["game"] = new("x") })] })));
}
