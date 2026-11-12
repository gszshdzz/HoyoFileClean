# 02：服务配置与完整原始身份

状态：实现及自动化验证完成，纳入全量改造统一验收；尚未提交。

范围：配置、核心网络接口、CLI 参数、GUI 游戏选择以及直接受影响的测试和说明。

验收：
- 新增任意服务配置无需新增枚举、映射表或游戏特例，查询只访问选中服务。
- launcher_id 独立配置，特殊字符统一编码；无默认 biz／游戏／包体。
- 官方 biz/id 整体保留，包含多个下划线或无下划线都不拆分；重复 biz 不自动挑选分支。
- GUI 查询后未选游戏时不能查构建，用户选中／双击后才可继续；其余网络步骤、取消与布局不变。
- CLI 显式传 service、biz、version、package；缺失参数在联网前拒绝，同 biz 多分支需要 game-id。
- 现有长度、摘要、压缩、路径、HTTPS、来源和取消校验继续通过。

## 实现与验证

- 服务对象直接驱动 GUI／核心调用，删除 `GenshinChannel`、`DefaultBiz` 及跨服务猜测匹配；配置使用 Id/Name/BranchUrl/BuildUrl/LauncherId。
- CLI 改为 `--service <id> --biz <完整原值> --version <version> --package <原始包标识>`，可选 `--game-id <原始id>`；旧拆分参数不再接受。一个命令只下载一个显式指定包。
- 重复 biz 报结构化 `ManifestGameAmbiguous`，无匹配 id 不查构建，不回退。
- GUI 查询后不选游戏，双击实际游戏项目会明确选中并查询；新增服务无需维护枚举／映射表。
- 配置读取和实际查询共用服务校验；两个接口均要求 HTTPS、默认端口、无用户信息／片段／预设查询串。launcher_id 统一转义拼接。
- `dotnet run --project tests\HoyoFileClean.Tests.csproj --no-restore`：总计 278，272 通过，6 项显式联网／诊断未运行；错误、失败、跳过均为 0。
- `dotnet run --project desktop.tests\HoyoFileClean.Desktop.Tests.csproj --no-restore`：总计 113，112 通过，1 项显式真实目录诊断未运行；错误、失败、跳过均为 0。
- 离线模拟覆盖任意服务、特殊字符 launcher_id、完整不透明 biz、重复 biz 分支、缺少包不联网、GUI 自定义服务和无默认选择、取消、最小窗口。没有重跑真实官方请求，不将模拟当作人工验收。
- `git diff --check` 无空白错误。`docs/hardcoded-data-review.md` 的工作区修改为用户填写意见，未由本模块改写。

## 人工验收

启动 GUI，查询游戏列表后应未选中游戏且不能查询 manifest；手动选择后能查询，返回包列表全部未选。再次查询游戏列表后应重新变为未选择；双击实际游戏行可直接查询。

用户明确不逐模块验收，结构／参数／语言资源连续完成后统一查看。最终记录见 `docs/configuration-maintenance.md`，提交编号仍为 7.3。
