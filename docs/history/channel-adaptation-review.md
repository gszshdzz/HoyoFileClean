# 渠道补充规则与原神预置交付

日期：2026-11-21。分支 `feat/channel-adaption`，审查基线 `aac211d23668a00ac2654a1eb929c886550990a9`。本记录只说明本次交付，不构成未来功能授权。用户指定本次提交的 session 编号留白，由主线 agent 补齐。

## 要求与实现

- 渠道只作为基础身份后的补充。`config/GameChannelRules.cs` 集中维护身份条件、原始渠道条件、直接展示的渠道 ID 和附加文件预置；核心只做通用匹配和去重合并。匹配不到任何渠道时不生成渠道信息，GUI 不显示渠道字段或后缀，不默认官服，也不阻断基础识别或官方清单匹配。
- 扫描摘要、自动获取摘要、比较页本地游戏信息直接展示 ID，不新增逐渠道语言资源；不向官方参考清单添加推测渠道。扫描 JSON 继续保存原始 detections，导入时重建渠道信息，不访问原游戏目录。
- 补齐原神国际服 `GenshinImpact_Data` 预置，保留原神整体排除 `Persistent` 的策略。仅 `hk4e_cn` 且 `channel=14`、`sub_channel=0` 追加排除 `YuanShen_Data/Plugins/BLPlatform64`、`YuanShen_Data/Plugins/PCGameSDK.dll`、`sdk_pkg_version`；不排除整个 Plugins，不推广到其他游戏。
- 渠道数值仅参考 Collapse 固定快照，SDK 路径来自用户提供 ZIP 的只读条目检查。没有解压执行补丁、修改真实游戏、引入新依赖或新增注册表、卸载、转换、清理保护层及热更新匹配功能。详细来源和限制见 [游戏识别说明](../game-identification.md)。

## 双轴审查与修复

- Standards：覆盖所有未提交及新增文件；初审及修复后复核均未发现确定违规或值得重构的冗余，配置与逻辑职责分离。
- Spec：发现一项旧的自动归属缺口——用户手动添加与自动排除同路径的规则后，换渠道仍可能移除该手动规则。目录／文件、直接重复添加／删除后重加共四项 GUI 测试先失败；修复后全部通过。删除同步清理自动归属，手动添加、拖入、导入复用同一合并入口，保留大小写不敏感去重。复核原发现关闭，无剩余确定问题。
- GUI 补充未知值、条件不符及缺少渠道字段的三组数据，检查扫描、自动获取与比较页均不出现渠道。导入同路径也有独立回归，确保显式手动规则保留、纯自动 SDK 项仍会移除。

## 验证证据

- `tests/run-tests.ps1 -Suite All -Full`：核心 Total 517，执行通过 508、Not Run 9；该核心版本此后未修改，沿用结果。此前桌面全量也通过，修复手动归属后重新验证如下。
- `tests/run-tests.ps1 -Suite Desktop -Class GameChannelTests,WizardTests`：106 项通过。
- `dotnet build desktop/HoyoFileClean.Desktop.csproj --no-restore --nologo`：0 警告、0 错误。
- 最终 `tests/run-tests.ps1 -Suite Desktop -Full -NoBuild`：Total 254，执行通过 253、Not Run 1，Errors／Failed／Skipped 均为 0。显式联网与真实样本测试未启动。
- 上述桌面最终通过之前，首次全量在未修改的详情窗口测试 `SummaryWrapsAtMinimumWidthAndLeavesSpaceForContents` 出现一次 `header.Height > wideHeight` 断言失败；随后详情单类 17 项、渠道与详情组合 28 项、全量复跑均通过。原因未确定，没有跳过或放宽断言，也未修改无关详情窗口；主线如复现应单独调查。

测试使用隔离临时目录、离屏原生控件和模拟官方响应，不能替代真实渠道登录、SDK 运行、国际服文件或人工桌面视觉验收。用户没有渠道游戏，不要求其提供真实渠道目录。未推送，不自动合并主线。

## 合并注意

本轮修改共用的 `GameIdentifier.GetFilePreset`、GUI 身份摘要和排除规则手动合并入口，并在核心项目中编译链接新规则文件。其他分支若同时修改这些位置，解决冲突时须保留基础与渠道预置追加语义、未知渠道不显示及手动归属修复。合并提交的 message／session 对照随合并一并维护。
