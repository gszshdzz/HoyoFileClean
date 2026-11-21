namespace HoyoFileClean;

// 渠道 ID 直接用于展示，不引用语言资源。此表不是完整渠道名单；未知值不阻断基础识别。
// 数值参考 Collapse metadata/v3/stable @ c8243daa（非官方完整名单）；原神 B 服文件路径来自提供的 SDK 补丁。
internal static class GameChannelRules
{
    internal sealed record Rule(string Id, IReadOnlyDictionary<string, string> Information,
        IReadOnlyDictionary<string, string> Detections, GameFilePreset? Files = null);

    internal static readonly IReadOnlyList<Rule> Rules =
    [
        ForBiz("hk4e_cn", "1", "1", "official"),
        ForBiz("hk4e_cn", "14", "0", "bilibili", new(
            ["YuanShen_Data/Plugins/BLPlatform64"],
            ["YuanShen_Data/Plugins/PCGameSDK.dll", "sdk_pkg_version"])),
        ForBiz("hk4e_global", "1", "0", "official"),
        ForBiz("hk4e_global", "1", "6", "google_play"),
        ForBiz("hkrpg_cn", "1", "1", "official"),
        ForBiz("hkrpg_cn", "14", "0", "bilibili"),
        ForBiz("hkrpg_global", "1", "1", "official"),
        ForBiz("nap_cn", "1", "1", "official"),
        ForBiz("nap_cn", "14", "0", "bilibili"),
        ForBiz("nap_global", "1", "0", "official"),
        ForBiz("bh3_cn", "1", "1", "official"),
        ForBiz("bh3_global", "1", "1", "official")
    ];

    private static Rule ForBiz(string biz, string channel, string subChannel, string id, GameFilePreset? files = null)
        => new(id, new Dictionary<string, string> { ["biz"] = biz },
            new Dictionary<string, string> { ["ini_channel"] = channel, ["ini_sub_channel"] = subChannel }, files);
}
