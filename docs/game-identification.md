# 游戏识别与开发配置

Session：8。

GUI 正式扫描前须成功执行“读取游戏并加载预置排除”，未读取或读取失败时扫描置灰。读取仅检查配置候选并显示元信息、加载可用扫描排除预置，不生成扫描快照、不计算 MD5；随后正式扫描游戏，再获取 manifest。正式扫描通过现有 identification 参数携带预读取结果，按排除规则枚举文件并按需计算 MD5，不重新解析元信息；即使 config.ini 不在扫描清单中，也保留此前读取的身份／版本。预读取的线索不等于正式扫描覆盖范围；元信息变化后需再次点击读取。结果显示在扫描按钮下方，不写入游戏目录。预置规则与验收见 `.scratch/exclude-rules-preset/spec.md`。

`config/GameIdentificationRules.cs` 与 `config/NetworkServices.cs` 都是独立的 C# 开发适配数据定义，直接随核心库编译，不使用嵌入或旁置适配 JSON；更改配置后必须重新构建，不读取用户旁置的替代文件。核心识别入口可接收配置对象，便于测试和以后复用。

`config/GameIdentificationRules.cs` 与 `config/ManifestIdentificationRules.cs` 仅定义识别检测及含义。扫描排除预置和选择预置所需的 biz 到游戏族映射独立维护在 `config/GameExclusionRules.cs`，同样随核心库编译；排除规则不参与身份识别。此拆分仅调整配置归属，不改变预置内容或选择行为（session 21）。

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

两份配置通过 `Create()` 返回 `IdentificationConfiguration<TDetection>`，共用 `IdentificationMeaning` 和 `IdentificationEvaluator`，统一去重、证据归并及约束。游戏单值冲突失败，manifest 默认保留冲突；两份配置独立，不按相同 ID 匹配。游戏参数为未来网络自动获取作准备，本轮不自动联网或选包。

## 当前证据范围

原始样本中 `game_version=7.2.0`、`uapc` 包含 `hk4e_cn`。重复相同的 `uapc` 允许，重复不同版本取最大值。渠道对应关系尚未完整确认：`channel`／`sub_channel` 仅记录原值，不映射“官服”，不限制身份识别。同一原始字段重复不同值仍报矛盾。登录开关、附加 SDK 等影响待有证据后单独处理，不据渠道推断 manifest 不同。

样本还包含 `plugin_COa9vWaYX9_version=1.0.0`，因此临时展示该插件与版本。未提供可确认本体／语音资源包安装状态的文件，不据此声称已识别完整包列表；“未检测到”也不等于未安装。识别结果不会自动联网、选择游戏或勾选网络包。CLI 独立扫描维持通用目录扫描，不强制识别游戏。

首行有 `biz` 显示 `biz`，否则显示 `game`，并显示已知版本；不重复显示游戏族或猜渠道名称。插件及“发现的包体”另行显示，包含规则确定的本体与语音等所有包体。

`game` 是游戏族，`biz` 是完整官方标识，`package` 是资源包，`plugins` 是插件 ID 与版本。游戏侧 `package` 是允许多值的本地发现包体集合：根执行文件命中本体 `game`，原神两种布局及星铁语音路径命中对应包名。多包正常共存，不证明完整安装或当前使用语言。以后新增小包只需增加有证据的规则，本轮不猜测绝区零小包。不拆分 biz 猜游戏族，不把插件当资源包；原始检测键 `ini_game_id` 等不变。

## 扫描 JSON 的识别记录

识别配置异常遵循原有文案分离方式：核心只抛 `CoreException` 错误码及上下文，`presentation/DiagnosticText.cs` 映射到 `presentation/Languages/zh-CN.json`，不在识别逻辑中写异常描述。

`ScanScope.GameDetections` 是可选字段，写出为 `scan.gameDetections`，内容为原始检测键值，不保存中文或展示文本 ID。核心扫描接口可接收已取得的 `identification`，生成的内存快照和扫描 JSON 会保留此记录；GUI 当前扫描已传入识别结果，但本轮不新增 GUI 导出按钮或 CLI 强制识别选项。

清单读取与转换先校验记录格式并原样保留，不暗中套用默认游戏适配，保证自定义配置产生的扫描结果也可传递／导出。GUI 导入时再校验检测 ID、版本与必要信息，按当前编译配置恢复显示，完全不访问记录的游戏根目录；核心调用方也可用 `FromDetections(recorded, configuration)` 显式使用原配置。动态条目 ID 统一禁止空白、控制字符、冒号和等号。无识别记录的旧扫描 JSON 继续可导入，但不显示凭空推测的信息；GUI 拒绝未知／损坏的识别记录。当前配置变化后，旧记录未必仍能通过当前规则，不宣称历史适配规则永久兼容。

## 验收

本轮内嵌含义及扫描复用回归：核心／CLI `Total: 452, Errors: 0, Failed: 0, Skipped: 0, Not Run: 7`；桌面 `Total: 162, Errors: 0, Failed: 0, Skipped: 0, Not Run: 1`。未运行项为显式测试，未扫描真实游戏或访问官方网络。覆盖未知渠道、路径识别、多包共存、空目录和备份路径、资源正文不打开、链接安全、历史恢复、GUI biz 优先／game 回退；新增本体、语音、配置提供的非语音包体集合及“发现的包体”标签回归。扫描排除回归在修复前轻扫／重扫各失败一项，复用文件表后均通过。轻扫后移走资源目录、仍从原文件表识别的测试证明路径识别不再另行读目录。

启动 `dotnet run --project desktop\HoyoFileClean.Desktop.csproj --no-restore`。

1. 样本首行显示 `hk4e_cn`、`7.2.0`，不显示“官服”；已知插件另行显示。只有 `StarRail.exe` 时显示 `hkrpg`，版本未知。
2. 实际游戏目录做 MD5 重扫，点击“取消扫描”：结束后不能下一步、没有旧识别结果，按钮恢复后可重新扫描。轻扫也支持取消，但小目录可能瞬间结束。
3. 不改真实游戏文件。在临时样本副本末尾增加 `game_version=7.10.0`，再次扫描应显示 `7.10.0`；改成 `game_version=bad`，应报无法比较并禁止前进。恢复后可成功。
4. 在临时副本末尾增加 `channel=2`，应报矛盾；改为 `channel=1`，允许重复并成功。不含识别文件的空目录应识别失败，而不是产生成功的空扫描。
5. 成功后下一步，手动选择 manifest 或使用既有本地／网络获取流程，解析后进入比较。网络包仍默认不选；本地目录拖放应保持可用。历史扫描 JSON 仍按原根目录校验导入，不宣称重新识别。
6. 临时原神／星铁中、日语音路径中各放一个文件，应显示 `zh-cn`、`ja-jp`；空目录、备份及相似路径不命中。渠道单个值换为未知值仍可识别。带检测记录的 JSON 在根目录移走后仍能恢复结果。

所有操作只读游戏输入；真实清理仍未实现。自动化证据、发布目录及已生成的五组验收副本路径记在 `.scratch/game-identification/issues/01-identification-and-scan.md`。使用 `valid` 应显示 `7.2.0` 和插件 `1.0.0`，`version-max` 应显示 `7.10.0`，`version-invalid`／`channel-conflict`／`unknown` 应失败且不能前进；返回 `valid` 后应恢复。
