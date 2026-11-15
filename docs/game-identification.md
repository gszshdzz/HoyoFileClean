# 游戏识别与开发配置

Session：8。

GUI 现在先扫描游戏，再获取 manifest。点击扫描时先识别，识别成功后继续文件枚举／MD5；识别失败不会产生扫描快照。结果暂放在扫描按钮下方，并在内存中保留原始提取键值和有意义的信息。关闭窗口释放结果，不写入游戏目录。

`config/GameIdentificationRules.cs` 与 `config/NetworkServices.cs` 都是独立的 C# 开发适配数据定义，直接随核心库编译，不使用嵌入或旁置适配 JSON；更改配置后必须重新构建，不读取用户旁置的替代文件。核心识别入口可接收配置对象，便于测试和以后复用。

## 两层规则

第一层 `Detections` 定义“在哪里取什么值”，不定义游戏含义。例如字典中的条目：

```csharp
["ini_version"] = new(
    Paths: ["./config.ini", "other/version.txt"],
    Pattern: @"^[ \t]*game_version[ \t]*=[ \t]*(?<value>[^\r\n]*)",
    CanCompare: true)
```

- `Paths` 是相对游戏根目录的候选文件列表；允许 `./` 前缀，不允许越界、绝对路径或通配符。缺失候选继续尝试其他文件，不递归找文件，不跟随链接。
- `Pattern` 是 .NET 正则表达式，使用多行模式，必须有名为 `value` 的捕获组。所有命中都参加校验，不以最后一次覆盖前值；相同文件只读一次。匹配有超时，文件有大小限制。
- `CanCompare: true`：全部值须为 .NET `Version` 可解析的 2～4 段非负整数版本，按数字段取最大，例如 `1.10.0 > 1.9.0`。只出现一次的无效版本也报错，不静默忽略；不支持带预发行后缀的语义版本。
- `CanCompare: false`：去除首尾空白后必须完全一致；重复相同值允许，大小写不同也视为不同。
- 同一检测条目在多个候选文件里的值合并校验。不同格式可定义不同检测条目，不强迫未来游戏使用 `config.ini`。
- `JsonObjectKeys: true` 表示将提取值作为 JSON 对象解析，取实际顶层键名；`IgnoredKeys` 配置忽略键。`uapc` 当前只排除启动器项 `hyp`，没有 `cn/global` 后缀筛选，也没有游戏 ID 白名单；出现多个不同游戏键仍按不可比较字段报冲突。
- 正则可额外捕获 `item`，用于动态条目，例如 `plugin_(?<item>[^\s=]+)_version`。每个实际插件 ID 独立合并／比较版本，不需要逐插件增加配置。内存和 JSON 用“检测条目 ID:实际条目 ID”记录，如 `ini_plugin_version:futurePlugin`。

第二层 `Information` 定义“哪些提取结果意味着什么”。例如当前官服映射：

```csharp
["channel"] = new("IdentificationLabelChannel", Required: true, Multiple: false,
    Rules: [new(new()
    {
        ["ini_game_id"] = "hk4e_cn", ["ini_channel"] = "1", ["ini_sub_channel"] = "1"
    }, TextId: "IdentificationChannelOfficial")])
```

- `When` 中所有“检测条目 ID → 精确值”条件同时满足时，该规则生效；空字典表示不加条件。
- `TextId` 指向现有语言文件中的实际文字，例如 `IdentificationChannelOfficial` 对应“官服”；或改用 `Source` 引用某个检测条目的原始值，两者只能指定一个。来源值可配置 `DisplayFormatTextId`：`{0}` 为提取值，`{1}` 为动态 `item`，插件统一使用 `{1}（{0}）`，不维护逐插件文案。`LabelTextId` 指定字段标题。
- 同一信息可配置多条规则，分别引用不同游戏、文件格式的检测条目。没有匹配来源的规则不提供结果，不回退猜测。
- `Required: true` 而没有结果：中止识别与扫描。`Multiple: false` 却映射出多个不同结果：报冲突；相同结果去重。包／插件等集合可用 `Multiple: true`。
- 不设置 `is_gui`，避免重复维护；第二层信息列表和规则中的文本 ID 已经定义显示内容。当前临时结果区统一列出信息。
- 适配配置维护命中条件、信息来源和文本 ID；实际中文与模板统一放在 `presentation/Languages/zh-CN.json`。核心不读取语言资源，返回原始值或文本 ID，GUI 再呈现；改动后测试校验 ID 存在及模板参数数量。

新增适配时，先增加检测条目，再给现有信息增加对应规则。不要为每个游戏复制扫描流程；修改后运行识别与 GUI 回归测试。

## 当前证据范围

用户提供 `refs/config.ini`：`game_version=7.2.0`，`uapc` 包含 `hk4e_cn`，`channel=1`、`sub_channel=1` 由用户确认对应原神国服官服。重复相同的 `uapc` 允许；重复不同版本按最大值处理。

样本还包含 `plugin_COa9vWaYX9_version=1.0.0`，因此临时展示该插件与版本。未提供可确认本体／语音资源包安装状态的文件，不据此声称已识别完整包列表；“未检测到”也不等于未安装。识别结果不会自动联网、选择游戏或勾选网络包。CLI 独立扫描维持通用目录扫描，不强制识别游戏。

界面只显示实际游戏 ID，不再额外显示游戏名称；游戏、版本、渠道放在同一行，其余动态包／插件信息另起一行。渠道含义仍由第二层配置映射，不把官服含义泛化为所有游戏通用规则。

## 扫描 JSON 的识别记录

`ScanScope.GameDetections` 是可选字段，写出为 `scan.gameDetections`，内容为原始检测键值，不保存中文或展示文本 ID。核心扫描接口可接收已取得的 `identification`，生成的内存快照和扫描 JSON 会保留此记录；GUI 当前扫描已传入识别结果，但本轮不新增 GUI 导出按钮或 CLI 强制识别选项。

清单读取与转换先校验记录格式并原样保留，不暗中套用默认游戏适配，保证自定义配置产生的扫描结果也可传递／导出。GUI 导入时再校验检测 ID、版本与必要信息，按当前编译配置恢复显示，完全不访问记录的游戏根目录；核心调用方也可用 `FromDetections(recorded, configuration)` 显式使用原配置。动态条目 ID 统一禁止空白、控制字符、冒号和等号。无识别记录的旧扫描 JSON 继续可导入，但不显示凭空推测的信息；GUI 拒绝未知／损坏的识别记录。当前配置变化后，旧记录未必仍能通过当前规则，不宣称历史适配规则永久兼容。

## 验收

启动 `dotnet run --project desktop\HoyoFileClean.Desktop.csproj --no-restore`。

1. 第一页应是“1 游戏扫描”，第二页是“2 参考清单”。用包含原始样本的目录扫描，按钮下方首行应显示游戏 `hk4e_cn`、版本 `7.2.0`、渠道官服，不额外显示“原神”；次行显示实际插件 ID 与版本。此目录仅证明识别规则，不代表完整游戏安装。
2. 实际游戏目录做 MD5 重扫，点击“取消扫描”：结束后不能下一步、没有旧识别结果，按钮恢复后可重新扫描。轻扫也支持取消，但小目录可能瞬间结束。
3. 不改真实游戏文件。在临时样本副本末尾增加 `game_version=7.10.0`，再次扫描应显示 `7.10.0`；改成 `game_version=bad`，应报无法比较并禁止前进。恢复后可成功。
4. 在临时副本末尾增加 `channel=2`，应报矛盾；改为 `channel=1`，允许重复并成功。不含识别文件的空目录应识别失败，而不是产生成功的空扫描。
5. 成功后下一步，手动选择 manifest 或使用既有本地／网络获取流程，解析后进入比较。网络包仍默认不选；本地目录拖放应保持可用。历史扫描 JSON 仍按原根目录校验导入，不宣称重新识别。
6. 扫描验收副本 `dynamic-plugin`，应同时显示原插件和 `futurePlugin（2.10.0）`，无需修改配置。导入 `identified-scan.json` 时游戏根目录填其记录的 `recorded-game` 路径（该目录已被测试移走），应仍显示 `hk4e_cn`、版本、渠道与插件信息，证明未读取游戏文件。

所有操作只读游戏输入；真实清理仍未实现。自动化证据、发布目录及已生成的五组验收副本路径记在 `.scratch/game-identification/issues/01-identification-and-scan.md`。使用 `valid` 应显示 `7.2.0` 和插件 `1.0.0`，`version-max` 应显示 `7.10.0`，`version-invalid`／`channel-conflict`／`unknown` 应失败且不能前进；返回 `valid` 后应恢复。
