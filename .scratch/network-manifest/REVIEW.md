# 网络 manifest 最终审查与验证

日期：2026-11-11。用户指定固定基准 `3a6d34752195e1803c0fe187c56bf16afb1f0d27`，审查其后的全部工作区改动及新增未跟踪文件，授权确认后提交，不推送。范围包括 spec 和 issues/01..04，最新 CLI 单包规则优先于早期多包记录。

## Standards

独立规范审查未发现硬性违约或值得新增抽象的设计问题。使用项目既有解析／校验／JSON 写出、原生 WinForms、.NET 网络接口；未新增包依赖、缓存、游戏资源下载、文件移动或删除。代码提示由前端拥有，核心只提供错误代码与数据。

## Spec

独立需求审查发现一项 P2：CLI 字符串入口先请求国服，国服失败或挂起时，即使国际服可用也无法取得结果。第一轮修复后成功路径正常，复核又发现目标版本错误仍可能被无关服务挂起拖成取消超时。

最终修复：两侧目录独立查询；尚未匹配目标的查询错误只作失败记录，不阻断另一侧；已匹配目标后的构建错误直接保留并终止，绝不退回其他版本或区服。所有结束路径都会取消并等待无关请求收尾。最终需求复核通过，无剩余阻止提交项。

回归新增：对侧立即失败、对侧挂起而目标成功、目标版本不符而对侧挂起、两侧均失败、用户取消后双方收尾。先观察失败（网络错误或版本错误被转成取消），再修复后运行完整测试。

## 最终验证

- `dotnet run --project tests\HoyoFileClean.Tests.csproj --no-restore`：Total 225、Errors 0、Failed 0、Skipped 0、Not Run 4；即 221 项常规核心／CLI 通过。四项是两项显式清单回归及两种连接方式冷／热诊断。
- `dotnet run --project desktop.tests\HoyoFileClean.Desktop.Tests.csproj --no-restore`：Total 75、Errors 0、Failed 0、Skipped 0、Not Run 1；即 74 项通过。未运行项是既有真实样本诊断。
- `dotnet build desktop\HoyoFileClean.Desktop.csproj --no-restore`：0 警告、0 错误。
- `dotnet run --project tests\HoyoFileClean.Tests.csproj --no-build --no-restore -- -explicit only -class HoyoFileClean.Tests.NetworkManifestTests -showLiveOutput`：两项显式清单回归通过，约 15.6 秒。两服 `7.2.0` 本体各 2,795 文件、四种语音各 179 文件；两服 `7.1.0` 本体各 2,744 文件，十二份长度／MD5／解析及同侧合并均通过。运行器 Total 40、Not Run 38 是 only 模式排除已由常规回归执行的非显式项，并非失败或漏测。
- 真实 CLI 字符串入口：`--game hk4e --region global --version 7.1.0 --package zh-cn`，177 文件、14,095,405,662 字节，退出 0；输出在批准的临时目录，未写游戏目录。
- 真实联网仅官方 API/CDN。两项冷／热诊断本轮未重跑，其根因及前后测量见 issues/03；本轮未运行桌面真实样本诊断，未将离屏截图视为完整视觉验收。

## 结论

Standards：0 项阻塞。Spec：1 项 P2 及其失败子场景均已修复、复核通过。现有证据支持提交本功能；不承诺所有历史版本永久可用、不宣称清单覆盖整个游戏、不宣称主线合并后自动通过同样测试。主线交接见 HANDOFF.md。
