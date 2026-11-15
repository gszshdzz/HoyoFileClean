namespace HoyoFileClean;

// 开发者维护，修改后重新构建；不向输出／发布目录复制配置文件。
internal static class ManifestIdentification
{
    // Target 使用清单 JSON 字段名，点号逐层访问，自动遍历数组。
    // 主程序匹配根文件，目录匹配带分隔符的前缀；不猜区服、版本或不确定包体。
    internal static readonly ManifestIdentifier Identifier = new(
        detections:
        [
            new(Target: "files.path", Match: "YuanShen.exe", Id: "game_hk4e",
                Results: new Dictionary<string, string>
                {
                    ["game"] = "hk4e",
                    ["biz"] = "hk4e_cn",
                    ["package"] = "game"
                }, Exact: true),
            new("files.path", "GenshinImpact.exe", "exe_hk4e_global",
                new Dictionary<string, string> { ["game"] = "hk4e", ["biz"] = "hk4e_global", ["package"] = "game" }, Exact: true),
            new("files.path", "StarRail.exe", "exe_hkrpg",
                new Dictionary<string, string> { ["game"] = "hkrpg", ["package"] = "game" }, Exact: true),
            new("files.path", "ZenlessZoneZero.exe", "exe_nap",
                new Dictionary<string, string> { ["game"] = "nap", ["package"] = "game" }, Exact: true),
            new("files.path", "BH3.exe", "exe_bh3",
                new Dictionary<string, string> { ["game"] = "bh3", ["package"] = "game" }, Exact: true),

            // 这些共用目录只识别游戏，不推断区服。原神不重复配置目录判断。
            new("files.path", "StarRail_Data/", "data_hkrpg",
                new Dictionary<string, string> { ["game"] = "hkrpg" }, Prefix: true),
            new("files.path", "ZenlessZoneZero_Data/", "data_nap",
                new Dictionary<string, string> { ["game"] = "nap" }, Prefix: true),
            new("files.path", "BH3_Data/", "data_bh3",
                new Dictionary<string, string> { ["game"] = "bh3" }, Prefix: true),

            // 原神与星铁：完整语音目录在两服样本中只命中对应语言包。
            new("files.path", "YuanShen_Data/StreamingAssets/AudioAssets/Chinese/", "voice_hk4e_cn_zh",
                new Dictionary<string, string> { ["game"] = "hk4e", ["biz"] = "hk4e_cn", ["package"] = "zh-cn" }, Prefix: true),
            new("files.path", "YuanShen_Data/StreamingAssets/AudioAssets/English(US)/", "voice_hk4e_cn_en",
                new Dictionary<string, string> { ["game"] = "hk4e", ["biz"] = "hk4e_cn", ["package"] = "en-us" }, Prefix: true),
            new("files.path", "YuanShen_Data/StreamingAssets/AudioAssets/Japanese/", "voice_hk4e_cn_ja",
                new Dictionary<string, string> { ["game"] = "hk4e", ["biz"] = "hk4e_cn", ["package"] = "ja-jp" }, Prefix: true),
            new("files.path", "YuanShen_Data/StreamingAssets/AudioAssets/Korean/", "voice_hk4e_cn_ko",
                new Dictionary<string, string> { ["game"] = "hk4e", ["biz"] = "hk4e_cn", ["package"] = "ko-kr" }, Prefix: true),
            new("files.path", "GenshinImpact_Data/StreamingAssets/AudioAssets/Chinese/", "voice_hk4e_global_zh",
                new Dictionary<string, string> { ["game"] = "hk4e", ["biz"] = "hk4e_global", ["package"] = "zh-cn" }, Prefix: true),
            new("files.path", "GenshinImpact_Data/StreamingAssets/AudioAssets/English(US)/", "voice_hk4e_global_en",
                new Dictionary<string, string> { ["game"] = "hk4e", ["biz"] = "hk4e_global", ["package"] = "en-us" }, Prefix: true),
            new("files.path", "GenshinImpact_Data/StreamingAssets/AudioAssets/Japanese/", "voice_hk4e_global_ja",
                new Dictionary<string, string> { ["game"] = "hk4e", ["biz"] = "hk4e_global", ["package"] = "ja-jp" }, Prefix: true),
            new("files.path", "GenshinImpact_Data/StreamingAssets/AudioAssets/Korean/", "voice_hk4e_global_ko",
                new Dictionary<string, string> { ["game"] = "hk4e", ["biz"] = "hk4e_global", ["package"] = "ko-kr" }, Prefix: true),
            new("files.path", "StarRail_Data/Persistent/Audio/AudioPackage/Windows/Chinese(PRC)/", "voice_hkrpg_zh",
                new Dictionary<string, string> { ["game"] = "hkrpg", ["package"] = "zh-cn" }, Prefix: true),
            new("files.path", "StarRail_Data/Persistent/Audio/AudioPackage/Windows/English/", "voice_hkrpg_en",
                new Dictionary<string, string> { ["game"] = "hkrpg", ["package"] = "en-us" }, Prefix: true),
            new("files.path", "StarRail_Data/Persistent/Audio/AudioPackage/Windows/Japanese/", "voice_hkrpg_ja",
                new Dictionary<string, string> { ["game"] = "hkrpg", ["package"] = "ja-jp" }, Prefix: true),
            new("files.path", "StarRail_Data/Persistent/Audio/AudioPackage/Windows/Korean/", "voice_hkrpg_ko",
                new Dictionary<string, string> { ["game"] = "hkrpg", ["package"] = "ko-kr" }, Prefix: true)
        ]);
}
