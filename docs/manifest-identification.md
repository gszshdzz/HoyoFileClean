# Manifest 元信息识别

离线 manifest 没有可靠的游戏、区服或版本声明。本功能从经过共用校验的清单字段匹配特征，只输出配置明确赋予的含义，不从文件名、目录名或规则顺序猜测身份。

## 开发者配置

维护 `config/ManifestIdentificationRules.cs`，修改后重新构建。与游戏规则文件同名风格、同一 `Create()` 入口、同一 `IdentificationConfiguration<TDetection>` 结构；配置编译进核心库，不创建识别引擎、不复制到构建或发布目录，也不提供用户配置入口。

```csharp
internal static IdentificationConfiguration<ManifestDetectionDefinition[]> Create() => new(
    Detections: new()
    {
        ["exe_hk4e_cn"] = [new("files.path", "YuanShen.exe", Exact: true,
            Meanings: new()
            {
                ["game"] = new("hk4e", RetainConflicts: true),
                ["biz"] = new("hk4e_cn", RetainConflicts: true),
                ["package"] = new("game", Multiple: true)
            })]
    });
```

- `Detections` 的键为线索 ID，值为选择器数组，每项定义 `Target`、`Match` 和内嵌 `Meanings`。同一 ID 任一选择器命中即可，所有选择器必须具有相同含义。
- 不再有顶层 `Results`。每条规则用 `Meanings` 直接声明 `game`、`biz`、`package` 等结果，命中后取并集，同值去重。`game` 为游戏族，`biz` 为完整官方标识；未知区服不抹去已知游戏。
- 两种读取器共用 `IdentificationMeaning` 和 `IdentificationEvaluator` 做去重、证据归并及约束，不建立跨配置 ID 匹配关系。manifest 默认保留冲突，游戏默认严格单值约束。
- `Target` 使用本项目清单 JSON 的字段名，点号逐层访问，数组自动遍历，如 `files.path`、`files.hash`；不是磁盘路径或任意 JSONPath 表达式。目标必须是现有字符串字段；错误目标在初始化规则时拒绝。
- 默认按大小写敏感的子串匹配。`Exact: true` 改为整个字段相等，`Prefix: true` 改为字段前缀匹配，两者不能同时启用；`IgnoreCase: true` 可与任一模式组合。正式规则以根执行文件精确匹配或带目录分隔符的前缀匹配，避免备份目录和相似文件名误命中。
- 空字段、空含义、同 ID 不一致含义或同字段不一致策略都会被拒绝，不支持跨检测项条件引用。
- `FromDetection: true` 取得实际匹配字段值，收集全部不同原值；固定 `RawValue` 规则首命中即止，不保存全部路径。
- manifest 元数据仅使用 `RawValue` 或 `FromDetection`；展示用 `TextId`／`DisplayFormatTextId` 在初始化时拒绝。

正式配置保留此前两服官方样本交叉验证的 20 条检测：五种根执行文件，星铁／绝区零／崩坏 3 的三个专属目录，以及原神两种布局和星铁的四种语音目录。`game` 分别为 `hk4e/hkrpg/nap/bh3`；`biz` 独立对齐完整官方标识，原神可输出 `hk4e_cn/hk4e_global`，其他游戏区服未知时省略 `biz`。根执行文件输出 `package = game`，确定的原神／星铁语音包输出官方包名 `zh-cn/en-us/ja-jp/ko-kr`。保留 exe 与完整音频目录的证据，不恢复两条原神通用 Data 目录规则。多条同值证据不算冲突。

验证依据为两服 272 份官方清单（国服 132、国际服 140），全部通过现有网络客户端大小、摘要和格式校验；取样版本为原神 7.2.0、星铁 4.7.0、绝区零 3.3.0、崩坏 3 9.1.0。该证据不保证未来版本稳定，不将路径当来源证明。绝区零普通／mini 语音、数字资源包和崩坏 3 ASB 包没有加入包体识别；它们能识别游戏但完整区服标识保持未知。没有渠道、官方 game.id 或版本推断规则，不从来源页面或文件位置猜其他游戏区服。

## 结果和冲突

命中结果按源清单保存在 `FileListSource.Metadata`。本地 `convert` 输出有命中时增加 `metadata`；没有命中则省略，不改变文件路径、大小或哈希。

```json
"metadata": {
  "game": {
    "matches": [{ "id": "exe_hk4e_cn", "value": "hk4e" }],
    "conflict": false,
    "value": "hk4e"
  },
  "biz": {
    "matches": [{ "id": "exe_hk4e_cn", "value": "hk4e_cn" }],
    "conflict": false,
    "value": "hk4e_cn"
  },
  "package": {
    "matches": [{ "id": "exe_hk4e_cn", "value": "game" }],
    "multiple": true,
    "conflict": false,
    "value": "game"
  }
}
```

一个字段命中不同结果值时，`conflict` 为 `true`，保留全部 `matches` 并省略单值 `value`。多个 ID 得出同一值表示证据相互印证，不算冲突；同一 ID 在多个文件或多条规则中重复命中只记一次。候选按 ID 稳定排序，不按顺序选择冲突结果。冲突作为识别结果保留，不使已经校验有效的文件清单失效；原有同侧文件内容矛盾仍阻止合并。

正式 manifest 的身份字段按单值加冲突保留处理，`package` 与游戏识别统一为 `Multiple: true` 合法集合，本体与语音同时存在不算冲突。多值输出元数据记录 `multiple: true`，保留全部证据、没有伪造的单值 `value`；未启用时不增加该字段。冲突判断复用共用策略，不在 manifest 层重新改判；比较摘要汇总不同来源的值，不把跨来源差异重新判为冲突。

压缩、未压缩 manifest 与本项目清单 JSON 使用同一识别流程。导入 JSON 时从有效条目重新计算，不信任其已保存的 `metadata`；不同来源的识别结果各自保留，不把多份清单的游戏字段混在一起判断冲突。识别不打开清单列出的游戏文件，不保留完整 JSON 副本。

桌面本地／手动表格未尝试解析时，游戏、版本、包体及条目数留空；解析后仍未取得的信息显示“—”，失败及取消的条目数同样显示“—”。冲突显示“冲突（详见提示）”，悬停查看全部 ID 和结果。网络来源继续显示已从官方响应取得的信息，不被离线推断替换，未下载时条目数仍为空。开始重试或切换来源时清空过期离线识别值。

游戏列有 `biz` 时显示原始 `biz`，没有时显示原始 `game`，不使用自定中文名称。有 `biz` 但存在冲突时显示冲突，不退回游戏族掩盖冲突。元数据仍分别保存两者及全部证据；网络表格继续显示官方响应中的原始 biz。

条目数空值样式随 GUI 状态直接更新，底层仍为数字或 null，不在绘制时跨列推断。取消位置和表格操作统一见 [使用指南](usage.md)。

## 预下载／补丁清单

本地输入支持 Sophon 构建清单与 `SophonPatchProto` 的目标文件投影。未知本地输入使用两种轻量投影，通过关键标量字段的编号、编码类型与 presence 区分布局；混合或歧义结构拒绝，不因解析异常改用另一种格式。已经明确只接受构建格式的官方网络入口只解析一次，不再为格式识别重复解压。chunk、补丁方案和 `UnusedAssets` 不进入目标文件列表。没有补丁方案的目标记录仍保留，游戏、biz 和资源包继续由上面的共用识别规则计算；旧版本 `VersionTag` 不作为目标版本。

补丁清单的来源记录和导出 JSON 增加可选 `manifestKind: "sophon-patch"`，重新导入及多输入合并保留该标记，旧 JSON 和普通构建清单不增加该字段。它仅用于提醒，不证明官方来源、目标版本或完整覆盖；JSON 保存的游戏识别 metadata 仍重新计算。仅凭补丁格式不能确定预下载阶段，因此界面提醒采用“预下载／补丁清单”，不伪造预下载身份。

用户在参考清单页点击“下一步”时，若当前有效结果包含已选中的补丁清单，弹窗列出这些文件，说明通常不应作为对比参考及完整性风险。默认按钮为“否”，确认“是”才进入下一页；拒绝保持原页和选择，不在添加或解析时弹窗。返回该页再次点击下一步仍确认；未选中的、失败的或已失效的条目不触发旧提醒。

样本身份、数量与原始依据见 [补丁研究](research/sophon-pre-download-manifest.md)。`PreDownloadManifestTests` 常规用例自包含，`refs/` 不入库，真实样本测试须显式选择；`PatchReferenceTests` 验证确认、拒绝、返回再次确认和失效，不代替原生 MessageBox 人工验收。测试选择见 [测试说明](testing.md)。

## 回归与验收

- 用单文件 JSON（`fileCount: 1`、`totalSize: 1`、文件大小 1）分别测试 `YuanShen.exe`、`GenshinImpact.exe`、`StarRail.exe`，应为 `hk4e_cn`、`hk4e_global`、`hkrpg`，包体 `game`，版本未知；换成 `Other.exe` 应清空旧身份。
- 同时含前两种执行文件时，游戏族仍为 `hk4e`、包体仍为 `game`，biz 为冲突且无单值；交换条目顺序不改变结果。JSON 导入重算 metadata，文件身份不变。
- 按 [使用指南](usage.md) 检查三来源详情／导出、重试失效和补丁提醒。详情表虚拟显示、无持久单元格选择；大小通常三位有效数字，100GB 以上 GB 值最多四位，默认焦点为关闭按钮。
- 修改后按 [测试说明](testing.md) 选择 manifest 识别、配置、补丁、桌面元数据及详情相关类；保留取消、输入边界和十万条分配量回归。原生保存对话框、所有 DPI 和真实大批快照峰值不能由这些测试代替。
