# 识别、可取消扫描与向导调整

Session: 8
Status: 实现与自动验证完成，待用户验收
Blocked by: 无

## 实现

- [x] 网络服务与识别配置改为可编译 C# 数据定义，不使用旁置或嵌入 JSON。
- [x] 通用文件提取、跨文件重复值校验和语义映射。
- [x] 按用户追加要求移除 `is_gui`，匹配规则直接引用语言文本 ID，并检查 ID 和模板完整性。
- [x] 原神国服真实样本规则与插件版本显示。
- [x] GUI 扫描前识别、临时显示结果和取消按钮。
- [x] 向导扫描在 manifest 之前，包括下一步门禁和拖放条件。
- [x] 既有测试更新与新增映射、失败恢复、取消测试。
- [x] 最终核心、桌面、真实样本和发布目录验证。
- [ ] 用户人工验收（识别显示位置暂定）。

## 已运行证据

### 提交前双轴 code review（2026-11-15，session 8）

用户批准以 HEAD `c60b8a849428e10d7e8b885206978d91d38be8fc` 为基线，审查全部未提交与新增文件，并授权无阻断后提交。规范轴未发现硬违规，发现默认适配隐式耦合与动态 ID 校验不一致；需求轴还发现扫描最终快照整理漏传取消令牌。均已修复：

- 清单传输只做与适配无关的记录格式校验，不再默认解释自定义识别结果；`FromDetections` 可显式传入原配置。新增自定义配置识别→扫描→JSON→恢复回归。
- 捕获动态 ID、记录校验和配置入口使用一致的 ID 约束，拒绝空白／控制字符／冒号／等号；新增坏动态 ID 与坏配置 ID 测试。
- `DirectoryScanner` 最终 `FileComparer.FromFiles` 传递原取消令牌，覆盖整理快照阶段。

修复后最终核心／CLI **356 项**（Errors 0、Failed 0、Skipped 0、Not Run 7），桌面 **137 项**（Errors 0、Failed 0、Skipped 0、Not Run 1），无构建警告。下面的 v2 发布包是审查前版本，最终提交包含上述额外修复；合并后以源码重新构建，不复制临时发布包。提交信息与编号已登记 `docs/commit-sessions.md`，不推送。

### 用户追加四项调整后的最终证据（2026-11-15）

- 移除重复游戏名称，只显示实际游戏 ID；uapc 使用 JSON 顶层键提取，配置忽略 `hyp`，无 `cn/global` 后缀筛选。
- 插件正则捕获实际 `item`，按每个插件 ID 分别合并版本；无逐插件命中参数或显示文案。游戏／版本／渠道同一行，插件另行。
- 扫描范围增加可选 `scan.gameDetections` 原始记录；GUI 导入从记录恢复显示，不访问源游戏目录。旧 JSON 保持兼容，不新增导出按钮。
- 核心／CLI 最新 347 项，Errors 0、Failed 0、Skipped 0、Not Run 7；桌面最新 137 项，Errors 0、Failed 0、Skipped 0、Not Run 1；均无构建警告。真实样本专项 1 项通过。
- 新增不同后缀／无后缀游戏 ID、任意新插件、各插件独立版本比较、损坏 uapc、识别 JSON 往返／转换保留、根目录移走后 GUI 导入等测试。渠道仍按独立适配映射，不泛化官服含义。
- 新版 Release 发布成功且无适配 JSON，差异空白检查通过；查看了新版最小窗口截图，无重叠。没有重新实测官方网络或真实完整游戏扫描。
- **最新程序**：`C:\Users\yob14\AppData\Local\Temp\opencode\HoyoFileClean-session8-publish-v2\HoyoFileClean.Desktop.exe`。
- **最新验收副本**：`C:\Users\yob14\AppData\Local\Temp\opencode\HoyoFileClean-session8-acceptance-29b2184f8aa043929bdbf063e3508073`。`dynamic-plugin` 应显示 `futurePlugin（2.10.0）`；导入 `identified-scan.json` 时根目录填该目录下的 `recorded-game`（已移走），仍可恢复识别。其余成功／冲突／无效版本样本保持。
- 新版截图：`C:\Users\yob14\AppData\Local\Temp\opencode\game-gui-c139a0eba5cc44e98db16e102a785b4a\identified-minimum-window.png`。

### 以下为上一轮证据，程序与验收路径以最新记录为准

最终要求已改为可编译 C# 配置，不再使用 JSON；README、配置维护说明和识别说明已同步。

2026-11-15 最终验证：

- 核心／CLI：`dotnet run --project tests\HoyoFileClean.Tests.csproj --no-restore`，334 项，Errors 0、Failed 0、Skipped 0、Not Run 7（显式测试），无构建警告。
- 桌面：`dotnet run --project desktop.tests\HoyoFileClean.Desktop.Tests.csproj --no-restore`，136 项，Errors 0、Failed 0、Skipped 0、Not Run 1（真实目录诊断），无构建警告。
- 真实样本：`dotnet run --project tests\HoyoFileClean.Tests.csproj --no-build -- -explicit only -method HoyoFileClean.Tests.GameIdentifierTests.ProvidedRealConfigIdentifiesExpectedGame`，1 项通过，原文件字节未改变。
- `dotnet publish desktop\HoyoFileClean.Desktop.csproj --no-restore -c Release -o <下述发布目录>` 成功；目录共 11 个文件，无旁置适配配置或 config 目录，仅保留必需的 `.deps.json`／`.runtimeconfig.json` 运行信息。未额外手动启动发布 exe，GUI 行为证据来自原生控件测试。
- `git -c core.safecrlf=false diff --check` 成功。查看了最小窗口识别结果截图，步骤编号、结果及排除列表无重叠。未重新请求真实官方网络或扫描真实游戏安装目录；未提交／推送。

发布目录：`C:\Users\yob14\AppData\Local\Temp\opencode\HoyoFileClean-session8-publish-20261115`。

验收副本根目录：`C:\Users\yob14\AppData\Local\Temp\opencode\HoyoFileClean-session8-acceptance-eb70e4915c654caba4128d769141de79`，包含 `valid`、`version-max`、`version-invalid`、`channel-conflict`、`unknown` 目录，以及供手动解析的 `reference.json`。只用副本验证异常，不编辑真实游戏文件。

截图：`C:\Users\yob14\AppData\Local\Temp\opencode\game-gui-7fac60d6a4cc4870be24d078382b9e60\identified-minimum-window.png`。
