# core／CLI：分离逻辑与用户呈现

Blocked by: none
基线：`dd67195`（用户要求先保存桌面阶段，不代表 UI 验收通过）。
状态：实现、验证与 review 完成；等待用户确认，本次重构尚未提交。

## 用户要求

GUI 暂缓，当前只看 CLI。core 仅负责逻辑，其输出不能直接面向用户；抽离交互文字，避免补丁堆积。CLI 和 GUI 分别调用核心，不相互调用。

## 实现决定

- core 的主动校验失败使用 `CoreException`，携带稳定 `CoreErrorCode` 与路径事实，保留底层原因；系统 IO 异常保留原类型，不在每层重复包装。
- `cli/` 负责入口、摘要、帮助、警告、错误说明与终端进度；不直接输出异常 Message／ToString 或调用栈。不引入资源框架、通用错误管理框架或依赖。
- 保留现有转换／扫描／比较算法、安全检查及机器 JSON 契约。本次不是将 JSON 序列化或安全文件写出再做一次架构拆分。
- 原测试改为核对具体错误代码，不删安全断言；新增真实 CLI 子进程测试验证呈现隔离。GUI 源码不改，仅做兼容性回归；其新错误展示留待恢复 UI 工作时处理。

## 验证记录

- 结构化错误测试曾发现测试预期误把空文件条目、空范围路径归为 JSON 错误，已精确改为 FileMetadataInvalid／ScopePathMissing。
- 新增 CLI 测试中一处提示前缀漏写“文件路径”，修正测试数据后，9 项 CLI 呈现专项测试全部通过。
- 最终 `dotnet run --project tests\HoyoFileClean.Tests.csproj --no-restore`：140 项通过；`dotnet run --project desktop.tests\HoyoFileClean.Desktop.Tests.csproj --no-restore`：20 项通过。合计 160 项，Errors／Failed／Skipped／Not Run 均为 0。
- `dotnet build HoyoFileClean.csproj --no-restore` 与 `dotnet build desktop\HoyoFileClean.Desktop.csproj --no-restore`：均 0 警告、0 错误。
- 独立 Standards／Spec review 均无发现。检查未发现 core 的中文交互字符串／Console 调用，CLI 未直接输出异常 Message／ToString。
- GUI 源码未修改，桌面测试仅为兼容性回归，不代表 UI 已完成用户验收或新错误呈现已适配。
