namespace HoyoFileClean;

// 开发者维护，修改后重新构建；仅定义数据，不创建引擎或发布旁置文件。
// 每条检测自带含义；渠道仅记录原值，不用于限制游戏身份或选择 manifest。
internal static class GameIdentificationRules
{
    internal static IdentificationConfiguration<GameDetectionDefinition> Create() => new(
        Detections: new()
        {
            ["ini_version"] = new(["config.ini"],
                @"^[ \t]*game_version[ \t]*=[ \t]*(?<value>[^\r\n]*)", CanCompare: true,
                Meanings: new() { ["version"] = new(FromDetection: true, LabelTextId: "IdentificationLabelVersion") }),
            ["ini_channel"] = new(["config.ini"],
                @"^[ \t]*channel[ \t]*=[ \t]*(?<value>[^\r\n]*)", CanCompare: false),
            ["ini_sub_channel"] = new(["config.ini"],
                @"^[ \t]*sub_channel[ \t]*=[ \t]*(?<value>[^\r\n]*)", CanCompare: false),
            ["ini_game_id"] = new(["config.ini"],
                @"^[ \t]*uapc[ \t]*=[ \t]*(?<value>[^\r\n]*)", CanCompare: false,
                JsonObjectKeys: true, IgnoredKeys: ["hyp"],
                Meanings: new() { ["biz"] = new(FromDetection: true, LabelTextId: "IdentificationLabelGame") }),
            ["ini_plugin_version"] = new(["config.ini"],
                @"^[ \t]*plugin_(?<item>[^\s=]+)_version[ \t]*=[ \t]*(?<value>[^\r\n]*)", CanCompare: true,
                Meanings: new() { ["plugins"] = new(FromDetection: true, LabelTextId: "IdentificationLabelPlugins",
                    Multiple: true, DisplayFormatTextId: "IdentificationPluginVersion") }),
            ["exe_hk4e_cn"] = new(["YuanShen.exe"], Meanings: Identity("hk4e", "hk4e_cn", "game")),
            ["exe_hk4e_global"] = new(["GenshinImpact.exe"], Meanings: Identity("hk4e", "hk4e_global", "game")),
            ["exe_hkrpg"] = new(["StarRail.exe"], Meanings: Identity("hkrpg", package: "game")),
            ["exe_nap"] = new(["ZenlessZoneZero.exe"], Meanings: Identity("nap", package: "game")),
            ["exe_bh3"] = new(["BH3.exe"], Meanings: Identity("bh3", package: "game")),
            ["voice_hk4e_cn_zh"] = new(["YuanShen_Data/StreamingAssets/AudioAssets/Chinese/"], Prefix: true, Meanings: Identity("hk4e", "hk4e_cn", "zh-cn")),
            ["voice_hk4e_cn_en"] = new(["YuanShen_Data/StreamingAssets/AudioAssets/English(US)/"], Prefix: true, Meanings: Identity("hk4e", "hk4e_cn", "en-us")),
            ["voice_hk4e_cn_ja"] = new(["YuanShen_Data/StreamingAssets/AudioAssets/Japanese/"], Prefix: true, Meanings: Identity("hk4e", "hk4e_cn", "ja-jp")),
            ["voice_hk4e_cn_ko"] = new(["YuanShen_Data/StreamingAssets/AudioAssets/Korean/"], Prefix: true, Meanings: Identity("hk4e", "hk4e_cn", "ko-kr")),
            ["voice_hk4e_global_zh"] = new(["GenshinImpact_Data/StreamingAssets/AudioAssets/Chinese/"], Prefix: true, Meanings: Identity("hk4e", "hk4e_global", "zh-cn")),
            ["voice_hk4e_global_en"] = new(["GenshinImpact_Data/StreamingAssets/AudioAssets/English(US)/"], Prefix: true, Meanings: Identity("hk4e", "hk4e_global", "en-us")),
            ["voice_hk4e_global_ja"] = new(["GenshinImpact_Data/StreamingAssets/AudioAssets/Japanese/"], Prefix: true, Meanings: Identity("hk4e", "hk4e_global", "ja-jp")),
            ["voice_hk4e_global_ko"] = new(["GenshinImpact_Data/StreamingAssets/AudioAssets/Korean/"], Prefix: true, Meanings: Identity("hk4e", "hk4e_global", "ko-kr")),
            ["voice_hkrpg_zh"] = new(["StarRail_Data/Persistent/Audio/AudioPackage/Windows/Chinese(PRC)/"], Prefix: true, Meanings: Identity("hkrpg", package: "zh-cn")),
            ["voice_hkrpg_en"] = new(["StarRail_Data/Persistent/Audio/AudioPackage/Windows/English/"], Prefix: true, Meanings: Identity("hkrpg", package: "en-us")),
            ["voice_hkrpg_ja"] = new(["StarRail_Data/Persistent/Audio/AudioPackage/Windows/Japanese/"], Prefix: true, Meanings: Identity("hkrpg", package: "ja-jp")),
            ["voice_hkrpg_ko"] = new(["StarRail_Data/Persistent/Audio/AudioPackage/Windows/Korean/"], Prefix: true, Meanings: Identity("hkrpg", package: "ko-kr"))
        });

    // 只构造适配数据；不引用另一份配置，也不执行匹配。
    private static Dictionary<string, IdentificationMeaning> Identity(string game, string? biz = null, string? package = null)
    {
        var meanings = new Dictionary<string, IdentificationMeaning>
        {
            ["game"] = new(game, LabelTextId: "IdentificationLabelGame")
        };
        if (biz is not null) meanings["biz"] = new(biz, LabelTextId: "IdentificationLabelGame");
        if (package is not null) meanings["package"] = new(package, LabelTextId: "IdentificationLabelPackages", Multiple: true);
        return meanings;
    }
}
