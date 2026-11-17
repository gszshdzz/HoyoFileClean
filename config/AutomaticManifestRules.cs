namespace HoyoFileClean;

// 开发者维护，修改后重新构建；仅定义自动获取限制，不参与游戏识别或扫描排除。
internal static class AutomaticManifestRules
{
    // 规则尚未覆盖所需资源包的游戏；按 game 或完整 biz 原值匹配，完善后移除。
    internal static readonly IReadOnlySet<string> IncompleteGames = new HashSet<string>(StringComparer.Ordinal)
    {
        "nap", "nap_cn", "nap_global", "bh3", "bh3_cn", "bh3_global"
    };
}
