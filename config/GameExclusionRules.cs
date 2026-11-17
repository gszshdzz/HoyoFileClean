namespace HoyoFileClean;

// 开发者维护，修改后重新构建；仅定义扫描排除预置及其适用身份，不参与游戏识别。
internal static class GameExclusionRules
{
    internal static readonly IReadOnlyDictionary<string, string> PresetGames = new Dictionary<string, string>
    {
        ["hkrpg_cn"] = "hkrpg", ["hkrpg_global"] = "hkrpg",
        ["nap_cn"] = "nap", ["nap_global"] = "nap",
        ["bh3_cn"] = "bh3", ["bh3_global"] = "bh3"
    };

    // Exact root-relative paths, case-insensitive. pkg_version and voice package metadata stay in scans.
    internal static readonly IReadOnlyDictionary<string, GameExclusionPreset> ExclusionPresets =
        new Dictionary<string, GameExclusionPreset>(StringComparer.Ordinal)
        {
            ["hk4e_cn"] = new(["Screenshot", "BeyondAssets", "chunk", "ldiff",
                "YuanShen_Data/Persistent", "YuanShen_Data/SDKCaches", "YuanShen_Data/webCaches"],
                ["config.ini", "beyond_pkg_version"], ["pkg_version"]),
            ["hkrpg"] = new(["ldiff", "StarRail_Data/ScreenShots", "StarRail_Data/SDKCaches", "StarRail_Data/webCaches",
                "StarRail_Data/Persistent/Apm", "StarRail_Data/Persistent/Archive", "StarRail_Data/Persistent/Asb",
                "StarRail_Data/Persistent/DesignData", "StarRail_Data/Persistent/IFix", "StarRail_Data/Persistent/Lua",
                "StarRail_Data/Persistent/NativeData", "StarRail_Data/Persistent/RawRes", "StarRail_Data/Persistent/Video"],
                ["config.ini", "StarRail_Data/Persistent/AppIdentity.txt",
                "StarRail_Data/Persistent/AudioLaucherRecord.txt", "StarRail_Data/Persistent/DownloadBlacklist.json",
                "StarRail_Data/Persistent/DownloadedFullAssets.txt", "StarRail_Data/Persistent/InstallVersion.bin",
                "StarRail_Data/Persistent/launcher_verify",
                "StarRail_Data/Persistent/Audio/AudioPackage/Windows/AudioLangRedord.txt",
                "StarRail_Data/Persistent/Audio/AudioPackage/Windows/Extra.pck",
                "StarRail_Data/Persistent/Audio/AudioPackage/Windows/PckPatchConfig.json",
                "StarRail_Data/Persistent/Audio/AudioPackage/Windows/M_AudioV.bytes"], ["pkg_version"]),
            ["nap"] = new(["ldiff", "ScreenShot", "ZenlessZoneZero_Data/Persistent",
                "ZenlessZoneZero_Data/SDKCaches", "ZenlessZoneZero_Data/webCaches"], ["config.ini"], ["pkg_version"]),
            ["bh3"] = new(["ldiff", "ScreenShot", "BH3_Data/SDKCaches", "BH3_Data/webCaches", "LauncherPlugins"],
                ["config.ini", "Version.txt", "@IgnoredFiles"], ["pkg_version"])
        };
}
