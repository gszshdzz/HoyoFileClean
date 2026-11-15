namespace HoyoFileClean;

// 开发者维护，修改后重新构建；仅定义数据，不创建引擎或发布旁置文件。
// Detections 定义格式相关线索，Results 定义共用条件、结果与约束。
internal static class ManifestIdentificationRules
{
    // Target 使用清单 JSON 字段名，点号逐层访问，自动遍历数组。
    // 主程序匹配根文件，目录匹配带分隔符的前缀；不猜区服、版本或不确定包体。
    internal static IdentificationConfiguration<ManifestDetectionDefinition[]> Create() => new(
        Detections: new()
        {
            ["game_hk4e"] = [new("files.path", "YuanShen.exe", Exact: true)],
            ["exe_hk4e_global"] = [new("files.path", "GenshinImpact.exe", Exact: true)],
            ["exe_hkrpg"] = [new("files.path", "StarRail.exe", Exact: true)],
            ["exe_nap"] = [new("files.path", "ZenlessZoneZero.exe", Exact: true)],
            ["exe_bh3"] = [new("files.path", "BH3.exe", Exact: true)],
            ["data_hkrpg"] = [new("files.path", "StarRail_Data/", Prefix: true)],
            ["data_nap"] = [new("files.path", "ZenlessZoneZero_Data/", Prefix: true)],
            ["data_bh3"] = [new("files.path", "BH3_Data/", Prefix: true)],
            ["voice_hk4e_cn_zh"] = [new("files.path", "YuanShen_Data/StreamingAssets/AudioAssets/Chinese/", Prefix: true)],
            ["voice_hk4e_cn_en"] = [new("files.path", "YuanShen_Data/StreamingAssets/AudioAssets/English(US)/", Prefix: true)],
            ["voice_hk4e_cn_ja"] = [new("files.path", "YuanShen_Data/StreamingAssets/AudioAssets/Japanese/", Prefix: true)],
            ["voice_hk4e_cn_ko"] = [new("files.path", "YuanShen_Data/StreamingAssets/AudioAssets/Korean/", Prefix: true)],
            ["voice_hk4e_global_zh"] = [new("files.path", "GenshinImpact_Data/StreamingAssets/AudioAssets/Chinese/", Prefix: true)],
            ["voice_hk4e_global_en"] = [new("files.path", "GenshinImpact_Data/StreamingAssets/AudioAssets/English(US)/", Prefix: true)],
            ["voice_hk4e_global_ja"] = [new("files.path", "GenshinImpact_Data/StreamingAssets/AudioAssets/Japanese/", Prefix: true)],
            ["voice_hk4e_global_ko"] = [new("files.path", "GenshinImpact_Data/StreamingAssets/AudioAssets/Korean/", Prefix: true)],
            ["voice_hkrpg_zh"] = [new("files.path", "StarRail_Data/Persistent/Audio/AudioPackage/Windows/Chinese(PRC)/", Prefix: true)],
            ["voice_hkrpg_en"] = [new("files.path", "StarRail_Data/Persistent/Audio/AudioPackage/Windows/English/", Prefix: true)],
            ["voice_hkrpg_ja"] = [new("files.path", "StarRail_Data/Persistent/Audio/AudioPackage/Windows/Japanese/", Prefix: true)],
            ["voice_hkrpg_ko"] = [new("files.path", "StarRail_Data/Persistent/Audio/AudioPackage/Windows/Korean/", Prefix: true)]
        },
        Results: new()
        {
            ["game"] = new(RetainConflicts: true, Rules:
            [
                new(new(), RawValue: "hk4e", AnyDetection:
                    ["game_hk4e", "exe_hk4e_global", "voice_hk4e_cn_zh", "voice_hk4e_cn_en",
                     "voice_hk4e_cn_ja", "voice_hk4e_cn_ko", "voice_hk4e_global_zh", "voice_hk4e_global_en",
                     "voice_hk4e_global_ja", "voice_hk4e_global_ko"]),
                new(new(), RawValue: "hkrpg", AnyDetection:
                    ["exe_hkrpg", "data_hkrpg", "voice_hkrpg_zh", "voice_hkrpg_en", "voice_hkrpg_ja", "voice_hkrpg_ko"]),
                new(new(), RawValue: "nap", AnyDetection: ["exe_nap", "data_nap"]),
                new(new(), RawValue: "bh3", AnyDetection: ["exe_bh3", "data_bh3"])
            ]),
            ["biz"] = new(RetainConflicts: true, Rules:
            [
                new(new(), RawValue: "hk4e_cn", AnyDetection:
                    ["game_hk4e", "voice_hk4e_cn_zh", "voice_hk4e_cn_en", "voice_hk4e_cn_ja", "voice_hk4e_cn_ko"]),
                new(new(), RawValue: "hk4e_global", AnyDetection:
                    ["exe_hk4e_global", "voice_hk4e_global_zh", "voice_hk4e_global_en", "voice_hk4e_global_ja", "voice_hk4e_global_ko"])
            ]),
            ["package"] = new(RetainConflicts: true, Rules:
            [
                new(new(), RawValue: "game", AnyDetection:
                    ["game_hk4e", "exe_hk4e_global", "exe_hkrpg", "exe_nap", "exe_bh3"]),
                new(new(), RawValue: "zh-cn", AnyDetection: ["voice_hk4e_cn_zh", "voice_hk4e_global_zh", "voice_hkrpg_zh"]),
                new(new(), RawValue: "en-us", AnyDetection: ["voice_hk4e_cn_en", "voice_hk4e_global_en", "voice_hkrpg_en"]),
                new(new(), RawValue: "ja-jp", AnyDetection: ["voice_hk4e_cn_ja", "voice_hk4e_global_ja", "voice_hkrpg_ja"]),
                new(new(), RawValue: "ko-kr", AnyDetection: ["voice_hk4e_cn_ko", "voice_hk4e_global_ko", "voice_hkrpg_ko"])
            ])
        });
}
