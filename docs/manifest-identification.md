# Manifest 元信息识别

离线 manifest 没有可靠的游戏、区服或版本声明。本功能从经过共用校验的清单字段匹配特征，只输出配置明确赋予的含义，不从文件名、目录名或规则顺序猜测身份。

## 开发者配置

维护 `config/ManifestIdentificationRules.cs`，修改后重新构建。与游戏规则文件同名风格、同一 `Create()` 入口、同一 `IdentificationConfiguration<TDetection>` 结构；配置编译进核心库，不创建识别引擎、不复制到构建或发布目录，也不提供用户配置入口。

```csharp
internal static IdentificationConfiguration<ManifestDetectionDefinition[]> Create() => new(
    Detections: new()
    {
        ["exe"] = [new("files.path", "YuanShen.exe", Exact: true)]
    },
    Results: new()
    {
        ["game"] = new(RetainConflicts: true,
            Rules: [new(new(), AnyDetection: ["exe"], RawValue: "hk4e")]),
        ["biz"] = new(RetainConflicts: true,
            Rules: [new(new(), AnyDetection: ["exe"], RawValue: "hk4e_cn")]),
        ["package"] = new(RetainConflicts: true,
            Rules: [new(new(), AnyDetection: ["exe"], RawValue: "game")])
    });
```

- `Detections` 的键为内部线索 ID，值是字符串选择器数组，每项定义字段位置 `Target` 和特征 `Match`；同一 ID 下任一选择器命中即可。这里不维护业务结果。
- `Results` 的键是含义，规则通过 `AnyDetection` 引用任一已命中线索、通过 `When` 要求多条线索的精确值条件、通过 `RawValue` 给出结果，或用 `Source` 取得线索原始值。一次命中可供多个含义引用，多个同值线索可合成一条映射；没有第二张重复的 ID／含义对照表。`game` 为游戏族，`biz` 为官方完整标识，未知区服不抹去已知游戏。
- 共用模型位于 `core/Models/Identification.cs`。与游戏读取一样，格式读取器只提取线索，再交给 `IdentificationEvaluator` 做条件映射、值去重、证据归并、必要项及冲突约束；manifest 默认 `RetainConflicts: true`，游戏默认严格单值约束。提取格式不同不再意味着两套结果判定逻辑。
- `Target` 使用本项目清单 JSON 的字段名，点号逐层访问，数组自动遍历，如 `files.path`、`files.hash`；不是磁盘路径或任意 JSONPath 表达式。目标必须是现有字符串字段；错误目标在初始化规则时拒绝。
- 默认按大小写敏感的子串匹配。`Exact: true` 改为整个字段相等，`Prefix: true` 改为字段前缀匹配，两者不能同时启用；`IgnoreCase: true` 可与任一模式组合。正式规则以根执行文件精确匹配或带目录分隔符的前缀匹配，避免备份目录和相似文件名误命中。
- 空字段、空结果、未知线索引用，以及同一线索对同一字段的矛盾无条件映射都会被拒绝。
- `Source` 取得实际匹配字段值，不是命中占位符；同一线索有多个不同原值时都参与结果约束。只有引用 `Source` 或检查精确值的 `When` 才收集全部匹配值；纯存在性规则首命中即止，不保存所有命中路径。
- manifest 元数据持久化原始字符串，结果仅使用 `RawValue` 或 `Source`；游戏结果使用的 `TextId`／`DisplayFormatTextId` 不是原始数据，在 manifest 初始化时明确拒绝，不能写出空元数据或混入翻译文字。

正式配置保留此前两服官方样本交叉验证的 20 条检测：五种根执行文件，星铁／绝区零／崩坏 3 的三个专属目录，以及原神两种布局和星铁的四种语音目录。`game` 分别为 `hk4e/hkrpg/nap/bh3`；`biz` 独立对齐完整官方标识，原神可输出 `hk4e_cn/hk4e_global`，其他游戏区服未知时省略 `biz`。根执行文件输出 `package = game`，确定的原神／星铁语音包输出官方包名 `zh-cn/en-us/ja-jp/ko-kr`。保留 exe 与完整音频目录的证据，不恢复两条原神通用 Data 目录规则。多条同值证据不算冲突。

验证依据为两服 272 份官方清单（国服 132、国际服 140），全部通过现有网络客户端大小、摘要和格式校验；取样版本为原神 7.2.0、星铁 4.7.0、绝区零 3.3.0、崩坏 3 9.1.0。该证据不保证未来版本稳定，不将路径当来源证明。绝区零普通／mini 语音、数字资源包和崩坏 3 ASB 包没有加入包体识别；它们能识别游戏但完整区服标识保持未知。没有渠道、官方 game.id 或版本推断规则，不从来源页面或文件位置猜其他游戏区服。

## 结果和冲突

命中结果按源清单保存在 `FileListSource.Metadata`。本地 `convert` 输出有命中时增加 `metadata`；没有命中则省略，不改变文件路径、大小或哈希。

```json
"metadata": {
  "game": {
    "matches": [{ "id": "game_hk4e", "value": "hk4e" }],
    "conflict": false,
    "value": "hk4e"
  },
  "biz": {
    "matches": [{ "id": "game_hk4e", "value": "hk4e_cn" }],
    "conflict": false,
    "value": "hk4e_cn"
  },
  "package": {
    "matches": [{ "id": "game_hk4e", "value": "game" }],
    "conflict": false,
    "value": "game"
  }
}
```

一个字段命中不同结果值时，`conflict` 为 `true`，保留全部 `matches` 并省略单值 `value`。多个 ID 得出同一值表示证据相互印证，不算冲突；同一 ID 在多个文件或多条规则中重复命中只记一次。候选按 ID 稳定排序，不按顺序选择冲突结果。冲突作为识别结果保留，不使已经校验有效的文件清单失效；原有同侧文件内容矛盾仍阻止合并。

正式 manifest 规则按单值加冲突保留处理。开发者显式配置 `Multiple: true` 时，多值是合法集合而非冲突，输出元数据记录 `multiple: true`，保留全部证据、没有伪造的单值 `value`；未启用时不增加该字段。冲突判断复用共用策略，不在 manifest 层重新改判。

压缩、未压缩 manifest 与本项目清单 JSON 使用同一识别流程。导入 JSON 时从有效条目重新计算，不信任其已保存的 `metadata`；不同来源的识别结果各自保留，不把多份清单的游戏字段混在一起判断冲突。识别不打开清单列出的游戏文件，不保留完整 JSON 副本。

桌面本地／手动表格未尝试解析时，游戏、版本、包体及条目数留空；解析后仍未取得的信息显示“—”，失败及取消的条目数同样显示“—”。冲突显示“冲突（详见提示）”，悬停查看全部 ID 和结果。网络来源继续显示已从官方响应取得的信息，不被离线推断替换，未下载时条目数仍为空。开始重试或切换来源时清空过期离线识别值。

游戏列有 `biz` 时显示原始 `biz`，没有时显示原始 `game`，不使用自定中文名称。有 `biz` 但存在冲突时显示冲突，不退回游戏族掩盖冲突。元数据仍分别保存两者及全部证据；网络表格继续显示官方响应中的原始 biz。

条目数使用 GUI 状态直接设置的原生空值显示样式，不在绘制时跨列判断状态。底层仍为数字或 null，失败后不需要缩放窗口才能看到横线；重试、取消和重置也同步更新显示，不影响 core／CLI 数据。

本地／手动页解析期间，“取消解析”位于该页上方操作按钮同行的最右侧；底部只保留跨页导航和关闭。共用取消按钮随来源切换移动到当前页，不改取消令牌与收尾流程，网络页仍使用其原有右侧“取消网络操作”。

## 逐条详情与导出

网络、本地获取和手动输入都支持：解析成功后双击非勾选列打开详情窗口，上方以普通文字紧凑显示游戏、版本、包体、文件数、覆盖大小及 MD5 记录数／总文件数，不再显示大块来源与扫描说明；窗口缩窄时自动换行。覆盖大小使用 1024 进位的动态单位，通常最多三位有效数字，100GB 以上的 GB 数值最多四位。中间的只读虚拟表格展示路径、大小和 MD5；底部提供关闭和导出 JSON。未成功、忙碌、表头及勾选列双击不打开详情。

详情默认焦点位于关闭按钮，上方为不可选择的普通文字；下方文件表格不保留任何单元格／全选状态，但保持可用和滚动，不通过禁用控件掩盖选择。

导出使用该条目已经载入的快照，不重读输入、不联网，也不导出整批合并结果。选择保存位置后在后台直接写出，取消选择不写文件，已有目标明确拒绝覆盖。输出沿用本项目清单 JSON，可再导入比较；网络表格的官方游戏／版本／包体显示不等于给清单 JSON 增加官方来源证明。

为支持详情与导出，成功条目的独立快照保留在内存中，包括整批失败时单独成功的条目；整批失败仍不能进入下一步。重新解析、切换来源、移除行或关闭窗口后释放相应引用；不创建磁盘缓存，不保留原始 manifest 或下载正文，也不强制垃圾回收。相较于此前只保留合并快照，这会增加托管内存占用，尚未重测真实大批清单的峰值。

## 验收

1. 在临时目录创建清单 JSON：`{"fileCount":1,"totalSize":1,"files":[{"path":"YuanShen.exe","size":1}]}`。桌面“手动输入”添加后，游戏、版本、包体和条目数均为空；解析后游戏应显示 `hk4e_cn`，包体显示 `game`，版本为“—”，条目数为 1。改成 `GenshinImpact.exe` 应显示 `hk4e_global`；改成 `StarRail.exe` 时显示 `hkrpg`，包体为 `game`，biz 未知。
2. 将路径改成 `Other.exe` 后重新解析，游戏应变成“—”，不能沿用旧结论。不要在解析过程中修改源文件。
3. CLI 使用 `convert` 转换到新的输出路径：有命中时检查上述 `metadata`，无命中时应无该字段；`files` 保持一致，已有输出仍拒绝覆盖。
4. 双击成功行的名称，上方不应默认选中文字；点击／拖选下方文件单元格不能留下选中项，滚动仍可用。导出新 JSON 并重新导入，路径、大小、哈希应一致。关闭只关闭详情窗口；取消保存不生成文件，选择已有文件明确拒绝覆盖。用本地和网络成功行重复检查。失败行不能打开详情，条目数显示“—”；切换来源后，未重新解析的离线信息为空，双击不打开旧结果。
5. 同时包含 `YuanShen.exe` 与 `GenshinImpact.exe` 的有效清单应得到 `game = hk4e`、`package = game`，但 `biz` 冲突；游戏列应显示冲突而不是回退成 `hk4e`。CLI 对 `biz` 输出 `conflict: true`、两个候选、无单值 `value`。交换条目顺序不改变结果。
6. 运行 `dotnet run --project tests\HoyoFileClean.Tests.csproj --no-restore` 和 `dotnet run --project desktop.tests\HoyoFileClean.Desktop.Tests.csproj --no-restore`。测试覆盖命中／未命中、两种 protobuf 输入、多含义及独立冲突、配置错误、输入元数据重算、取消、十万条记录分配量、桌面回填／清空、三来源详情入口、窗口内容和导出；原生保存位置对话框本身仍需人工验收，自动测试注入所选路径。显式联网和真实目录诊断不属于本次验证。
