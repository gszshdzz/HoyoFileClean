# 游戏识别与开发配置

GUI 正式扫描前须成功执行“读取游戏并加载预置排除”，未读取或读取失败时扫描置灰。读取仅检查配置候选并显示元信息、加载可用扫描排除预置，不生成扫描快照、不计算 MD5；随后正式扫描游戏，再获取 manifest。正式扫描通过现有 identification 参数携带预读取结果，按排除规则枚举文件并按需计算 MD5，不重新解析元信息；即使 config.ini 不在扫描清单中，也保留此前读取的身份／版本。预读取的线索不等于正式扫描覆盖范围；元信息变化后需再次点击读取。结果显示在扫描按钮下方，不写入游戏目录。预置规则与验收见 `.scratch/exclude-rules-preset/spec.md`。

`config/GameIdentificationRules.cs` 与 `config/NetworkServices.cs` 都是独立的 C# 开发适配数据定义，直接随核心库编译，不使用嵌入或旁置适配 JSON；更改配置后必须重新构建，不读取用户旁置的替代文件。核心识别入口可接收配置对象，便于测试和以后复用。

`config/GameIdentificationRules.cs` 与 `config/ManifestIdentificationRules.cs` 仅定义识别检测及含义。游戏文件规则独立维护在 `config/GameFileRules.cs`，同样随核心库编译：`ScanDirectories`／`ScanFiles` 排除扫描，`ResultHiddenFiles` 仅默认隐藏结果页文件，不改变扫描或比较。两类规则共用预置身份映射和 `GameIdentifier.GetFilePreset`，不参与身份识别。文件路径均为根目录相对路径、精确匹配、不区分大小写；未知身份不套用。当前四款预置仅隐藏 `pkg_version`，稳定语音元数据正常显示。

## 检测项内嵌含义

配置只有 `Detections`；每项同时定义检测方式及 `Meanings`，不再有顶层 `Results` 或跨项条件。例如：

```csharp
["ini_version"] = new(
    Paths: ["./config.ini", "other/version.txt"],
    Pattern: @"^[ \t]*game_version[ \t]*=[ \t]*(?<value>[^\r\n]*)",
    CanCompare: true,
    Meanings: new() { ["version"] = new(FromDetection: true,
        LabelTextId: "IdentificationLabelVersion") })
```

- `Paths` 是相对游戏根目录的候选路径；允许 `./` 前缀，不允许越界、绝对路径或通配符。调用方传入文件表时，不指定 `Pattern` 的规则精确匹配文件表；`Prefix: true` 匹配完整目录前缀，空目录不算命中。不打开资源正文。独立调用 `Identify` 未传文件表时仅检查配置候选：精确文件检查存在性，目录前缀寻找首个非链接文件即停止；文本仍使用原有大小限制、解析与共用含义校验。GUI 预读取使用这一候选模式，正式扫描携带其结果，扫描器统一处理扫描排除和链接；预读取不建立全目录文件清单，也不把预置排除应用到识别候选。
- `Pattern` 是 .NET 正则表达式，使用多行模式，必须有名为 `value` 的捕获组。所有命中都参加校验，不以最后一次覆盖前值；相同文件只读一次。匹配有超时，文件有大小限制。
- `CanCompare: true`：全部值须为 .NET `Version` 可解析的 2～4 段非负整数版本，按数字段取最大，例如 `1.10.0 > 1.9.0`。只出现一次的无效版本也报错，不静默忽略；不支持带预发行后缀的语义版本。
- `CanCompare: false`：去除首尾空白后必须完全一致；重复相同值允许，大小写不同也视为不同。
- 同一检测条目在多个候选文件里的值合并校验。不同格式可定义不同检测条目，不强迫未来游戏使用 `config.ini`。
- `JsonObjectKeys: true` 表示将提取值作为 JSON 对象解析，取实际顶层键名；`IgnoredKeys` 配置忽略键。`uapc` 当前只排除启动器项 `hyp`，没有 `cn/global` 后缀筛选，也没有游戏 ID 白名单；出现多个不同游戏键仍按不可比较字段报冲突。
- 正则可额外捕获 `item`，用于动态条目，例如 `plugin_(?<item>[^\s=]+)_version`。每个实际插件 ID 独立合并／比较版本，不需要逐插件增加配置。内存和 JSON 用“检测条目 ID:实际条目 ID”记录，如 `ini_plugin_version:futurePlugin`。

每项的 `Meanings` 定义“命中意味着什么”。例如：

```csharp
["exe_hk4e_cn"] = new(["YuanShen.exe"], Meanings: new()
    {
        ["game"] = new("hk4e", LabelTextId: "IdentificationLabelGame"),
        ["biz"] = new("hk4e_cn", LabelTextId: "IdentificationLabelGame")
    })
```

- `FromDetection: true` 使用当前检测项原值，`RawValue` 给出固定结果，`TextId` 引用语言 ID，三者只能指定一个。`DisplayFormatTextId` 的 `{0}` 为原值、`{1}` 为动态条目 ID；`LabelTextId` 指定标题。
- 命中含义取并集，同值去重；同一字段的标题、多值及冲突策略必须一致。没有跨检测 ID 的条件或来源引用。
- 未命中的字段不提供结果。默认至少需要 `game` 或 `biz` 才算识别成功，版本、语言、插件均允许未知。
- `Required: true` 而没有结果：中止识别与扫描。`Multiple: false` 却映射出多个不同结果：报冲突；相同结果去重。插件等集合可用 `Multiple: true`。
- 中文及模板位于 `presentation/Languages/zh-CN.json`；核心返回原值或文本 ID，GUI 再呈现。

新增适配只增加自带含义的检测项，不复制扫描流程，修改后运行识别与 GUI 回归测试。

两份配置通过 `Create()` 返回 `IdentificationConfiguration<TDetection>`，共用 `IdentificationMeaning` 和 `IdentificationEvaluator`，统一去重、证据归并及约束。游戏单值冲突失败，manifest 默认保留冲突；两份配置独立，不按相同 ID 匹配。游戏侧输出供 [自动获取](automatic-manifest.md) 使用，识别本身不联网。

## 身份与证据边界

`channel`／`sub_channel` 只记录原值，不映射“官服”或限制身份识别；同一原始字段重复不同值仍报矛盾。不据登录开关、SDK 或渠道推断 manifest 不同。CLI 扫描维持通用目录扫描，不强制识别游戏。

首行有 `biz` 显示 `biz`，否则显示 `game`，并显示已知版本；不重复显示游戏族或猜渠道名称。插件及“发现的包体”另行显示，包含规则确定的本体与语音等所有包体。

`game` 是游戏族，`biz` 是完整官方标识，`package` 是资源包，`plugins` 是插件 ID 与版本。游戏侧 `package` 是允许多值的本地发现包体集合：根执行文件命中本体 `game`，原神两种布局及星铁语音路径命中对应包名。多包正常共存，不证明完整安装或当前使用语言。以后新增小包只需增加有证据的规则，本轮不猜测绝区零小包。不拆分 biz 猜游戏族，不把插件当资源包；原始检测键 `ini_game_id` 等不变。

## 扫描 JSON 的识别记录

识别配置异常遵循原有文案分离方式：核心只抛 `CoreException` 错误码及上下文，`presentation/DiagnosticText.cs` 映射到 `presentation/Languages/zh-CN.json`，不在识别逻辑中写异常描述。

`ScanScope.GameDetections` 是可选字段，写出为 `scan.gameDetections`，内容为原始检测键值，不保存中文或展示文本 ID。核心扫描接口可接收已取得的 `identification`，生成的内存快照和扫描 JSON 会保留此记录；GUI 当前扫描已传入识别结果，但本轮不新增 GUI 导出按钮或 CLI 强制识别选项。

清单读取与转换先校验记录格式并原样保留，不暗中套用默认游戏适配，保证自定义配置产生的扫描结果也可传递／导出。GUI 导入时再校验检测 ID、版本与必要信息，按当前编译配置恢复显示，完全不访问记录的游戏根目录；核心调用方也可用 `FromDetections(recorded, configuration)` 显式使用原配置。动态条目 ID 统一禁止空白、控制字符、冒号和等号。无识别记录的旧扫描 JSON 继续可导入，但不显示凭空推测的信息；GUI 拒绝未知／损坏的识别记录。当前配置变化后，旧记录未必仍能通过当前规则，不宣称历史适配规则永久兼容。

## 回归与验收

只用隔离副本制造异常，不修改真实游戏文件；操作入口见 [使用指南](usage.md)。修改规则后按 [测试说明](testing.md) 选择识别与桌面回归，核对：

1. `biz` 优先、`game` 回退、版本及插件独立显示；未识别信息不猜测。
2. 重复版本按数字取最大，单个坏版本失败；渠道重复不同值失败、相同值允许，单个未知渠道不阻断身份。
3. 语音路径有普通文件才命中，空目录、备份及相似前缀不命中；多包正常共存，不读资源正文，链接和排除边界不退化。
4. 预读取后即使排除元信息文件，正式扫描仍保留身份；换目录、读取失败、扫描取消及重试的推进状态正确。
5. 旧 JSON 无记录仍能导入，合法记录在原目录移走后仍恢复，未知／损坏记录拒绝。

历史证据与样本记录在 `.scratch/game-identification/`；临时发布包和路径不作为当前运行入口。
