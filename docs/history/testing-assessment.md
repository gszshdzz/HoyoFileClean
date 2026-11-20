# 测试必要性与耗时评估

> 2026-11-18 的历史测量与审查，保留对照证据，不代表当前测试数量或性能保证。日常入口见 [测试说明](../testing.md)。

2026-11-18，针对当前两个测试项目的清点、常规全量计时及桌面宿主诊断。没有运行联网、真实缓存或真实样本显式诊断。

## 保留哪些测试

保留 `tests/HoyoFileClean.Tests.csproj` 与 `desktop.tests/HoyoFileClean.Desktop.Tests.csproj`。前者覆盖跨平台核心与进程级 CLI，后者需要 Windows Forms、STA 线程及消息循环；合并会把 Windows 限制带给核心测试，也不消除界面初始化开销。没有证据支持删除某个测试项目。

按测试类职责评估，不把名称相近视为重复覆盖：

- 核心文件语义：`FileComparerTests`、`ComparisonInterfaceTests`、`FileEntryInterfaceTests` 验证文件差异、输入合同与格式边界；`DirectoryScannerTests` 验证扫描、排除、链接保护及 CLI 入口。保留。
- 清单：`ManifestConverterTests`、`ManifestJsonTests` 覆盖转换、大小上限、格式与内存边界；`ManifestVersionTests`、`ManifestIdentifierTests`、`ManifestIdentificationConfigTests`、`ManifestDiscoveryTests`、`PreDownloadManifestTests` 分别覆盖版本、识别规则、配置、发现和预下载。保留，不用端到端成功用例替代错误边界测试。
- 游戏识别：`GameIdentifierTests`、`IdentificationEvaluatorTests`、`GameExclusionPresetTests` 分别覆盖真实输入识别、证据求值和预置排除，不能由桌面显示测试替代。保留。
- 网络：`NetworkManifestTests`、`NetworkServiceTests`、`NetworkTimingTests`、`OfficialConnectionTests` 覆盖包体选择、配置、时间参数、地址竞争与取消；普通用例使用模拟输入，显式诊断仍隔离。保留。
- CLI 与收尾：`CliPresentationTests`、`LocalizationTests`、`TestTemporaryFilesTests` 保护进程输出、错误展示、翻译合同及目录所有权。CLI 子进程有启动成本，但验证的是实际入口，不改成只调用库来换取速度。
- 桌面解析与状态：`ReferenceParserTests`、`OperationLifecycleTests` 覆盖并发上限、取消、发布和解锁；`WizardTests` 还覆盖输入失效、内存流程及真实页面操作。部分业务链路相似，但验证的发布层级不同，保留。
- 桌面清单显示：`ManifestTableTests`、`ManifestMetadataTests`、`ManifestDetailsTests` 分别覆盖表格状态、元数据显示及详情／导出；`PatchReferenceTests` 覆盖差分清单确认。保留。
- 桌面网络与识别：`NetworkReferenceTests`、`GameIdentificationTests` 验证界面操作驱动的请求阶段、自动匹配、取消／重试及识别展示，不替代核心网络／识别测试。保留。
- 比较与文件操作：`ComparisonPanelTests`、`DifferencePanelTests`、`FileOperationPanelTests` 覆盖摘要、筛选／勾选及确认请求，保护真实文件不被界面擅自改动。保留。
- 宿主与诊断：`NativeTestHostTests` 验证线程、消息循环、等待失败及新加入的布局批处理；`ParsingDiagnosticsTests` 是显式真实缓存诊断，保留但不加入常规全量。

本轮精简重复执行成本，不删除现有用例、断言、输入矩阵或大数据回归。未逐项证明所有相似断言可互相替代；没有这种证据就不删。拆分更多项目、修改网络时钟接口、引入缓存或扩大 UI 并行度，收益不足以抵消额外维护或隔离风险，本轮不做。

## 耗时证据

同一工作区、Debug、依赖已还原，使用 `dotnet run --no-restore` 和 xUnit 3.2.2 的 `-explicit off -xml`。总耗时包含构建检查、启动、发现、执行和报告；xUnit 的 Time 是测试运行时间，按类求和可能大于总时间，因为不同集合可以并行。

| 范围 | 优化前测试时间 | 优化后测试时间 | 优化前总时间 | 优化后总时间 |
| --- | ---: | ---: | ---: | ---: |
| 核心／CLI | 4.883 秒 | 未重跑：代码未变 | 7.846 秒 | 未重跑 |
| 桌面 | 104.264 秒 | 59.897 秒 | 109.557 秒 | 63.151 秒 |

桌面测试时间减少约 42.6%。这是一次前后全量测量，不是统计基准；期间同时修正了宿主的正式字体、视觉样式与 DPI 初始化，所以不能把每项测试的耗时差全归因于批量布局，也不保证其他机器得到相同秒数。

优化前核心通过 482 项，9 项显式测试未运行；桌面通过 234 项、失败 4 项、1 项显式诊断未运行。优化后桌面通过 240 项（原有 238 项加新增 2 项），显式诊断仍有 1 项未运行，错误、失败、跳过均为 0。

## 根因与修复

临时分段计时显示，宿主启动通常约 1 毫秒，重复线程创建不是主要瓶颈；简单方法构造完整 `MainForm` 约 795 毫秒，异步导入的实际等待约 158 毫秒。初始化挂载与刷新控件状态反复触发嵌套自动尺寸布局。

`LayoutBatch` 在批量改动前暂停当前控件树布局，结束时从子控件到父控件恢复。用于页面组装与 `MainForm.RefreshView`；摘要构造也批量挂载，初始隐藏页面不再先显示再隐藏。十次扫描／导入方式切换的布局事件从 402 次降至 151 次；回归上限为 300 次，不以机器耗时作为通过条件。另一个新测试验证异常退出后嵌套布局继续正常工作。

四个原有失败分别是比较摘要被裁切和最小高度扫描列表重叠。正式入口调用 `ApplicationConfiguration.Initialize()`，测试宿主此前没有调用。让两者共享 `Program.InitializeApplication()` 后，原断言全部通过；没有放宽布局要求或修改原测试尺寸。以后宿主必须复用正式初始化，不能在测试内另抄字体／DPI 配置。

临时探针已移除；保留原等待机制、测试集合隔离和显式测试选择规则。

## 验收

定向复验批量布局、原失败与最小窗口：

```powershell
.\tests\run-tests.ps1 -Suite Desktop -Class NativeTestHostTests,ComparisonPanelTests,WizardTests
```

完整阶段回归仍使用 `tests/run-tests.ps1 -Suite All -Full`，同一代码版本不重复跑已通过的全量。不要把显式联网或真实缓存诊断加进此命令。

本轮按用户授权由代理自行验收，不要求用户人工运行或确认界面。桌面常规全量覆盖真实消息循环、最小窗口布局、方式切换、摘要边界、前进／返回和异步状态；最终 240 项通过。证据限于当前测试环境，不宣称已验证所有显示器、DPI 组合或真实系统文件对话框。

## 提交前审查

2026-11-18，session 23，按 `code-review` 的两条轴自行审查 `HEAD` 到工作区的全部改动（含新文件），基准为 `86f34c1f679ca0563f41813294cdee1abc0915af`；按用户限制未调用 subagent。

- Standards：依据 `AGENTS.md`、`README.md`、`docs/testing.md` 及本次设计原则，无未解决的规范问题。业务配置未改，没有新增业务名单、适配常量、依赖或测试缓存；共用布局工具集中暂停／恢复逻辑，正式入口与宿主共享初始化。构造阶段的原生 `SuspendLayout`／`ResumeLayout` 用于一次性组装，运行时批处理用 `LayoutBatch` 的异常安全作用域，不复制配置。
- Spec：需求为评估测试必要性、适当精简执行成本、诊断并优化时长；用户批准相关界面初始化根因修复。两项目保留有职责依据，原断言和显式诊断隔离规则未减弱，无额外业务行为。耗时差及宿主初始化失败修复有运行证据。

两轴未解决发现均为 0。最后代码版本已通过桌面全量与宿主专项；审查收尾仅改文档，未重复运行相同代码全量。`git diff --check` 无补丁格式错误，临时探针已移除。临时 XML 报表经用户确认移入可还原回收目录；历史未知测试目录未清理。
