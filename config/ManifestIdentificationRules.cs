namespace HoyoFileClean;

// 开发者维护，修改后重新构建；仅定义数据，不创建引擎或发布旁置文件。
// 每条检测自带可确定的含义；与游戏配置仅统一结构和语义，不建立 ID 匹配关系。
internal static class ManifestIdentificationRules
{
    // Target 使用清单 JSON 字段名，点号逐层访问，自动遍历数组。
    // 主程序匹配根文件，目录匹配带分隔符的前缀；不猜区服、版本或不确定包体。
    internal static IdentificationConfiguration<ManifestDetectionDefinition[]> Create() => new(
        Detections: new()
        {
            ["exe_hk4e_cn"] = [new("files.path", "YuanShen.exe", Exact: true, Meanings: Identity("hk4e", "hk4e_cn", "game"))],
            ["exe_hk4e_global"] = [new("files.path", "GenshinImpact.exe", Exact: true, Meanings: Identity("hk4e", "hk4e_global", "game"))],
            ["exe_hkrpg"] = [new("files.path", "StarRail.exe", Exact: true, Meanings: Identity("hkrpg", package: "game"))],
            ["exe_nap"] = [new("files.path", "ZenlessZoneZero.exe", Exact: true, Meanings: Identity("nap", package: "game"))],
            ["exe_bh3"] = [new("files.path", "BH3.exe", Exact: true, Meanings: Identity("bh3", package: "game"))],
            ["data_hkrpg"] = [new("files.path", "StarRail_Data/", Prefix: true, Meanings: Identity("hkrpg"))],
            ["data_nap"] = [new("files.path", "ZenlessZoneZero_Data/", Prefix: true, Meanings: Identity("nap"))],
            ["data_bh3"] = [new("files.path", "BH3_Data/", Prefix: true, Meanings: Identity("bh3"))],
            ["voice_hk4e_cn_zh"] = [new("files.path", "YuanShen_Data/StreamingAssets/AudioAssets/Chinese/", Prefix: true, Meanings: Identity("hk4e", "hk4e_cn", "zh-cn"))],
            ["voice_hk4e_cn_en"] = [new("files.path", "YuanShen_Data/StreamingAssets/AudioAssets/English(US)/", Prefix: true, Meanings: Identity("hk4e", "hk4e_cn", "en-us"))],
            ["voice_hk4e_cn_ja"] = [new("files.path", "YuanShen_Data/StreamingAssets/AudioAssets/Japanese/", Prefix: true, Meanings: Identity("hk4e", "hk4e_cn", "ja-jp"))],
            ["voice_hk4e_cn_ko"] = [new("files.path", "YuanShen_Data/StreamingAssets/AudioAssets/Korean/", Prefix: true, Meanings: Identity("hk4e", "hk4e_cn", "ko-kr"))],
            ["voice_hk4e_global_zh"] = [new("files.path", "GenshinImpact_Data/StreamingAssets/AudioAssets/Chinese/", Prefix: true, Meanings: Identity("hk4e", "hk4e_global", "zh-cn"))],
            ["voice_hk4e_global_en"] = [new("files.path", "GenshinImpact_Data/StreamingAssets/AudioAssets/English(US)/", Prefix: true, Meanings: Identity("hk4e", "hk4e_global", "en-us"))],
            ["voice_hk4e_global_ja"] = [new("files.path", "GenshinImpact_Data/StreamingAssets/AudioAssets/Japanese/", Prefix: true, Meanings: Identity("hk4e", "hk4e_global", "ja-jp"))],
            ["voice_hk4e_global_ko"] = [new("files.path", "GenshinImpact_Data/StreamingAssets/AudioAssets/Korean/", Prefix: true, Meanings: Identity("hk4e", "hk4e_global", "ko-kr"))],
            ["voice_hkrpg_zh"] = [new("files.path", "StarRail_Data/Persistent/Audio/AudioPackage/Windows/Chinese(PRC)/", Prefix: true, Meanings: Identity("hkrpg", package: "zh-cn"))],
            ["voice_hkrpg_en"] = [new("files.path", "StarRail_Data/Persistent/Audio/AudioPackage/Windows/English/", Prefix: true, Meanings: Identity("hkrpg", package: "en-us"))],
            ["voice_hkrpg_ja"] = [new("files.path", "StarRail_Data/Persistent/Audio/AudioPackage/Windows/Japanese/", Prefix: true, Meanings: Identity("hkrpg", package: "ja-jp"))],
            ["voice_hkrpg_ko"] = [new("files.path", "StarRail_Data/Persistent/Audio/AudioPackage/Windows/Korean/", Prefix: true, Meanings: Identity("hkrpg", package: "ko-kr"))]
        });

    private static Dictionary<string, IdentificationMeaning> Identity(string game, string? biz = null, string? package = null)
    {
        var meanings = new Dictionary<string, IdentificationMeaning> { ["game"] = new(game, RetainConflicts: true) };
        if (biz is not null) meanings["biz"] = new(biz, RetainConflicts: true);
        if (package is not null) meanings["package"] = new(package, Multiple: true);
        return meanings;
    }
}
