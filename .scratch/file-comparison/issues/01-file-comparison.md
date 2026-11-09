# 01：实现多输入对称清单比较

状态：实现、自动验证和独立 Standards/Spec 审查完成；用户已确认测试通过并授权提交，不推送。

方案：[模块 2](../spec.md)

- [x] `FileComparer.cs` 共用读取、校验、内存汇总及对称比较逻辑。
- [x] `Program.cs` 接入 `compare <output.json> --a <list.json> ... --b <list.json> ...`，差异成功输出，输入错误失败。
- [x] 支持轻重扫与扫描排除范围，报告区分未校验哈希及范围外条目。
- [x] 添加自动测试及 README 使用说明，不新增依赖。
- [x] 最终构建、全量测试和独立 Standards/Spec 审查通过。
- [x] 用户确认测试通过并授权提交；不推送，不进入移动或界面模块。

## 验证记录

2026-11-09，项目根目录运行 `dotnet run --project tests\HoyoFileClean.Tests.csproj --no-restore`：初次因三处测试构造简写编译失败，改为明确的 `new ManifestFile(...)` 后通过，118 项，Errors/Failed/Skipped/Not Run 均为 0。随后补充扫描范围空路径、危险路径检查，等待最终复验。

最终验证：`dotnet build HoyoFileClean.csproj --no-restore` 成功，0 警告、0 错误；`dotnet run --project tests\HoyoFileClean.Tests.csproj --no-restore` 120 项通过（原有 76 项、新增 44 项），Errors/Failed/Skipped/Not Run 均为 0。测试确认原扫描根目录移走后仍可比较 JSON，A/B 大小或 MD5 差异返回成功，同侧矛盾返回失败。

独立审查基线 `134213c1b314b4660c8f385ebbc0c8ae1db59ee2`，范围为本次未提交改动及未跟踪文件：Standards 硬性问题 0 项，Spec 0 项；Standards 有 1 项不阻塞的低优先级代码异味建议（`Program.cs` 的参数取值循环重复）。按用户最小实现要求，暂不为两段短循环引入额外抽象。未进行真实游戏扫描、移动或联网回归。
