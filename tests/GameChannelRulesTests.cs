using Xunit;

namespace HoyoFileClean.Tests;

public sealed class GameChannelRulesTests
{
    [Theory]
    [InlineData("hk4e_cn", "1", "1", "official")]
    [InlineData("hk4e_cn", "14", "0", "bilibili")]
    [InlineData("hk4e_global", "1", "0", "official")]
    [InlineData("hk4e_global", "1", "6", "google_play")]
    [InlineData("hkrpg_cn", "1", "1", "official")]
    [InlineData("hkrpg_cn", "14", "0", "bilibili")]
    [InlineData("hkrpg_global", "1", "1", "official")]
    [InlineData("nap_cn", "1", "1", "official")]
    [InlineData("nap_cn", "14", "0", "bilibili")]
    [InlineData("nap_global", "1", "0", "official")]
    [InlineData("bh3_cn", "1", "1", "official")]
    [InlineData("bh3_global", "1", "1", "official")]
    public void KnownChannelsAreRawIdsScopedToTheGame(string biz, string channel, string subChannel, string expected)
    {
        GameIdentification result = Recorded(biz, channel, subChannel);
        IdentifiedValue value = Assert.Single(result.Information["channel"].Values);
        Assert.Equal(expected, value.RawValue);
        Assert.Null(value.TextId);
        Assert.Null(value.DisplayFormatTextId);
        Assert.Equal(biz, Assert.Single(result.Information["biz"].Values).RawValue);
        Assert.Equal(channel, result.Detections["ini_channel"]);
        Assert.Equal(subChannel, result.Detections["ini_sub_channel"]);
    }

    [Theory]
    [InlineData("hk4e_cn", "14", "1")]
    [InlineData("hk4e_cn", "14", null)]
    [InlineData("hk4e_cn", null, "0")]
    [InlineData("hk4e_cn", "unknown", "future")]
    [InlineData("hk4e_cn", "1", "6")]
    [InlineData("hk4e_global", "14", "0")]
    [InlineData("hkrpg_global", "14", "0")]
    public void UnknownOrIncompleteChannelKeepsOnlyBaseRules(string biz, string? channel, string? subChannel)
    {
        GameIdentification result = Recorded(biz, channel, subChannel);
        Assert.False(result.Information.ContainsKey("channel"));
        Assert.Equal(biz, Assert.Single(result.Information["biz"].Values).RawValue);
        GameFilePreset preset = GameIdentifier.GetFilePreset(result)!;
        Assert.NotNull(preset);
        Assert.Contains("ldiff", preset.ScanDirectories);
        Assert.DoesNotContain("YuanShen_Data/Plugins/BLPlatform64", preset.ScanDirectories);
        Assert.DoesNotContain("YuanShen_Data/Plugins/PCGameSDK.dll", preset.ScanFiles);
        Assert.DoesNotContain("sdk_pkg_version", preset.ScanFiles);
    }

    [Theory]
    [InlineData("hkrpg_cn")]
    [InlineData("nap_cn")]
    public void OtherBilibiliGamesDoNotInheritGenshinSdkPaths(string biz)
    {
        GameIdentification result = Recorded(biz, "14", "0");
        Assert.Equal("bilibili", Assert.Single(result.Information["channel"].Values).RawValue);
        GameFilePreset preset = GameIdentifier.GetFilePreset(result)!;
        Assert.DoesNotContain("YuanShen_Data/Plugins/BLPlatform64", preset.ScanDirectories);
        Assert.DoesNotContain("sdk_pkg_version", preset.ScanFiles);
        Assert.Equal(preset.ScanFiles.Count, preset.ScanFiles.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void ChannelValuesAloneNeverIdentifyAnUnknownGame()
    {
        GameIdentification result = Recorded("future_cn", "14", "0");
        Assert.False(result.Information.ContainsKey("channel"));
        Assert.Null(GameIdentifier.GetFilePreset(result));
        CoreException error = Assert.Throws<CoreException>(() => GameIdentifier.FromDetections(
            new Dictionary<string, string> { ["ini_channel"] = "14", ["ini_sub_channel"] = "0" }));
        Assert.Equal(CoreErrorCode.GameIdentificationFailed, error.Code);
    }

    [Fact]
    public void ExportedScanRestoresChannelAndRulesWithoutTheOriginalDirectory()
    {
        string parent = Path.Combine(TestTemporaryFiles.Root, "channel-" + Guid.NewGuid().ToString("N"));
        string root = Path.Combine(parent, "game");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "config.ini"), "uapc={\"hk4e_cn\":{}}\nchannel=14\nsub_channel=0");
        var identification = GameIdentifier.Identify(root, TestContext.Current.CancellationToken);
        string path = Path.Combine(parent, "scan.json");
        DirectoryScanner.Scan(root, path, cancellationToken: TestContext.Current.CancellationToken,
            identification: identification);
        Directory.Move(root, root + "-moved");
        var scan = FileComparer.ReadInputs([path]);
        GameIdentification restored = GameIdentifier.FromDetections(scan.Sources[0].Scan!.GameDetections!);
        Assert.Equal("bilibili", Assert.Single(restored.Information["channel"].Values).RawValue);
        Assert.Contains("YuanShen_Data/Plugins/BLPlatform64", GameIdentifier.GetFilePreset(restored)!.ScanDirectories);
        Assert.DoesNotContain("bilibili", File.ReadAllText(path));
        Assert.False(Directory.Exists(root));
    }

    private static GameIdentification Recorded(string biz, string? channel, string? subChannel)
    {
        var detections = new Dictionary<string, string> { ["ini_game_id"] = biz };
        if (channel is not null) detections["ini_channel"] = channel;
        if (subChannel is not null) detections["ini_sub_channel"] = subChannel;
        return GameIdentifier.FromDetections(detections);
    }
}
