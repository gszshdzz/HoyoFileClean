namespace HoyoFileClean;

// 开发者维护，修改后重新构建；仅定义数据，不创建引擎或发布旁置文件。
// Detections 定义格式相关线索，Results 定义共用条件、结果与约束。
internal static class GameIdentificationRules
{
    internal static IdentificationConfiguration<GameDetectionDefinition> Create() => new(
        Detections: new()
        {
            ["ini_version"] = new(["config.ini"],
                @"^[ \t]*game_version[ \t]*=[ \t]*(?<value>[^\r\n]*)", CanCompare: true),
            ["ini_channel"] = new(["config.ini"],
                @"^[ \t]*channel[ \t]*=[ \t]*(?<value>[^\r\n]*)", CanCompare: false),
            ["ini_sub_channel"] = new(["config.ini"],
                @"^[ \t]*sub_channel[ \t]*=[ \t]*(?<value>[^\r\n]*)", CanCompare: false),
            ["ini_game_id"] = new(["config.ini"],
                @"^[ \t]*uapc[ \t]*=[ \t]*(?<value>[^\r\n]*)", CanCompare: false,
                JsonObjectKeys: true, IgnoredKeys: ["hyp"]),
            ["ini_plugin_version"] = new(["config.ini"],
                @"^[ \t]*plugin_(?<item>[^\s=]+)_version[ \t]*=[ \t]*(?<value>[^\r\n]*)", CanCompare: true)
        },
        Results: new()
        {
            ["biz"] = new("IdentificationLabelGame", Required: true, Multiple: false,
                Rules: [new(new(), Source: "ini_game_id")]),
            ["version"] = new("IdentificationLabelVersion", Required: true, Multiple: false,
                Rules: [new(new(), Source: "ini_version")]),
            ["channel"] = new("IdentificationLabelChannel", Required: true, Multiple: false,
                Rules: [new(new()
                {
                    ["ini_game_id"] = "hk4e_cn", ["ini_channel"] = "1", ["ini_sub_channel"] = "1"
                }, TextId: "IdentificationChannelOfficial")]),
            ["plugins"] = new("IdentificationLabelPlugins", Required: false, Multiple: true,
                Rules: [new(new(), Source: "ini_plugin_version",
                    DisplayFormatTextId: "IdentificationPluginVersion")])
        });
}
