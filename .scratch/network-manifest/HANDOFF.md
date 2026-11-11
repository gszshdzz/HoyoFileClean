# 给主线 agent 的合并交接

## 来源与方式

- 来源分支：`feat/network-manifest`，基于 `3a6d34752195e1803c0fe187c56bf16afb1f0d27`（支持拖入文件到手动清单表格）。网络功能作为一个完整提交交付；具体提交号以本分支最终提交及聊天交接文本为准。
- 用户授权审查修复后提交，没有授权 push。本工作树不直接合并或覆盖 main。
- 建议将该单次提交 cherry-pick 到主线，并按语义处理冲突；合并前检查主线未提交工作，不丢弃其他 agent 改动。合并后重新跑测试。

## 新功能

1. `core/NetworkManifestClient.cs`：官方目录查询、指定版本元数据、受限下载、内存解析与 JSON 写出。已验证原神国服官方和国际服，latest／自定义三段版本，不枚举旧版列表；返回版本不符报错，不回退。
2. CLI `convert <output.json> --network --game <game> --region <region> --version <version> [--package <package>]`。网络模式游戏／区服／版本必填；游戏与区服按官方 `game.biz` 末下划线两侧身份匹配，包按 `matching_field`，不使用固定值名单。示例 `hk4e/cn`、`hk4e/global`。`--package` 最多一次，重复同值或不同值在联网／文件操作前返回 2；省略默认本体。未知标识先查询后报业务错误。仍拒绝覆盖已有输出。
3. GUI `MainForm.NetworkReference.cs`：替换网络占位页，游戏／区服／版本并列单选；游戏、区服六行可见但项目总数不限，可滚动。版本 latest／自定义输入。启动和切换不联网，点击查询仅列可用区服，获取下载本体与语音全部 manifest 并填表，默认勾选本体，解析仅消费勾选的内存数据不再联网。游戏列为“原神-官服／原神-国际服”，版本列实际值，修改时间“—”；全部有效且无同侧冲突才允许下一步。

## 共用代码与其他功能影响

- `core/ManifestConverter.cs` 新增内部 seekable 流入口，复用既有格式检测／Zstd／protobuf；网络增加期望压缩方式、解压长度、MD5 验证并拒绝 JSON 伪装。本地公开入口不变，仍规范化完整磁盘来源路径。
- `core/ManifestReadStream.cs` 可选增量 MD5／长度核对，不保存完整解压副本。原解压上限与本地取消规则保留。
- `core/FileComparer.cs` 内部 `FromFiles` 允许保留 URL 来源身份，磁盘 `ReadInputs` 仍使用原路径规范化；比较分类、同侧冲突和 JSON 结构不变。
- `core/CoreException.cs` 增加网络错误代码；CLI 与 GUI 各自映射文字，不显示原始网络异常或含查询参数的 URL。
- `desktop/ManifestTable.cs` 复用同一表格，`ReferenceInput` 可保存远端元数据和下载字节；批量添加／删除后发选择变更通知。网络／本地／手动均无勾选项时禁用解析；切换来源重置三张表解析状态并失效参考与下游，不丢独立游戏扫描。
- `desktop/MainForm.Actions.cs` 根据当前来源选择表和读取器，沿用 ReferenceParser、确定进度、取消及增量合并。没有改 ReferenceParser 的业务实现。
- `core/OfficialConnection.cs` 针对 DNS 首个坏 CDN 地址 TCP 等待约21秒：每地址最多等2秒再尝试下一个，TLS／代理／连接池仍由 .NET 管理；不固定 IP、不禁IPv6、不预联网，不重试下载。无新依赖或锁文件变化；`AssemblyInfo.cs` 仅向核心测试开放内部连接测试接口。
- 动态 CLI 两个官方目录独立查询，其他服务失败／挂起不阻断目标；已匹配服务的错误立即保留，结束时取消并等待所有请求收尾。
- 不改扫描、排除、实际比较算法；不增加磁盘缓存／中间 JSON、游戏资源正文下载、移动或删除实现。

## 主线冲突重点：务必保留主线的新设计

审查时 main 已到 `fba7a40`（合并游戏读取界面优化并统一状态与进度反馈），还包含 `f547727` 本地目录拖放、`3aa6d74` 手动拒绝目录混合批次、`3caed2b/27cb4c9` 扫描布局及链接提示修正。

本分支仍基于旧界面，不应整文件采用本分支版本：

- `desktop/MainForm.cs`：仅合入网络页构造、显示／按钮状态、客户端释放及网络来源可推进；保留主线第二步布局、扫描按钮位置、页内进度与底部状态，不恢复旧全局进度占位或扫描大结果框。
- `desktop/MainForm.Actions.cs`：合入网络解析入口与网络错误提示，保留主线扫描／排除／导入／状态逻辑。网络进度应按主线当前页面进度机制接入，测试也相应更新；不要为了保留旧测试控件名恢复全局进度设计。
- `desktop.tests/WizardTests.cs`、`desktop.tests/NetworkReferenceTests.cs`：保留主线扫描、拖放、进度测试；合入网络页／无勾选禁解析断言。新网络测试目前沿用本分支原有 `OperationProgress`，合并后按主线页内进度控件调整断言，不能跳过测试。
- `README.md`：按段落保留两边已实现功能，不用本分支旧扫描说明覆盖主线。
- 未修改 `DirectoryScanner.cs`、`MainForm.GameReading.cs`、`ExclusionRules.cs`、`MainForm.LocalReference.cs`、`ResultText.cs`；主线这些文件的新增逻辑必须保留。

## 验证与边界

- 最终核心／CLI 常规 221 项、桌面 74 项通过，构建零警告错误。
- 两项显式官方清单回归覆盖两服最新版十份清单、两服7.1.0本体，共十二份 MD5／长度／解析／合并通过；实际CLI国际服7.1.0中文包177文件通过。
- 四项显式核心联网测试平时不运行（两份清单回归、两种连接冷／热诊断）；桌面一项既有真实样本诊断未运行。不能把默认 Not Run 当失败，也不能宣称这些项目每轮都执行。
- 测试命令：`dotnet run --project tests\HoyoFileClean.Tests.csproj --no-restore`；`dotnet run --project desktop.tests\HoyoFileClean.Desktop.Tests.csproj --no-restore`。同一输出目录不要边构建边运行，避免 DLL 文件锁。
- 仅官方 API 与原神官方两 CDN，拒绝重定向／加密／未知压缩／超限／哈希不符。其他游戏协议和渠道未验证，CLI接受新标识不等于已适配其他游戏。
- 此分支通过不等于主线合并后已验证；真实桌面视觉和其他DPI仍需人工验收。审查证据在 REVIEW.md，历史取舍见 issues/01..04。
