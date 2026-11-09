# 01：实现原生四步桌面向导

Blocked by: none
状态：实现与自动验证完成，等待用户验收；未提交。

范围与决定见 `../spec.md`，操作与人工验收见根目录 `README.md`。

## 实现

- CLI／GUI 分别直接引用 `core/HoyoFileClean.Core.csproj`，核心不依赖界面或控制台。
- `desktop/MainForm*.cs` 接通前三步真实操作与导入；`WizardSession.cs` 集中管理依赖失效、异步忙碌、选择确认；`DifferencePanel.cs` 使用原生树和二次演示确认。
- 保留原命令及 JSON 格式、拒绝覆盖和扫描路径安全校验；未增加 NuGet 包或真实删除调用。

## 已取得证据

- 核心分离后原 120 项回归通过；补充核心接口测试后 131 项通过。
- 桌面项目首次构建 0 警告、0 错误。新增桌面测试阶段性 20 项通过。
- 最小窗口回归曾实际发现第四步右侧树越界；修复单列表格的百分比列宽后原断言通过，未删除或放宽测试。
- 离屏原生窗口截图只证明测试窗口的布局；不等同用户真实桌面的完整人工交互验收。截图与测试数据位于 `C:\Users\yob14\AppData\Local\Temp\opencode`。

## 最终验证

- `dotnet build HoyoFileClean.csproj --no-restore` 与 `dotnet build desktop\HoyoFileClean.Desktop.csproj --no-restore`：均 0 警告、0 错误。
- `dotnet run --project tests\HoyoFileClean.Tests.csproj --no-restore`：131 项通过；`dotnet run --project desktop.tests\HoyoFileClean.Desktop.Tests.csproj --no-restore`：20 项通过。合计 151 项，Errors／Failed／Skipped／Not Run 均 0。
- 用户提供的压缩与未压缩真实 manifest 均输出 2,831 个文件、135,765,032,023 字节；两份 JSON 字节一致，与 `refs/main.json` 路径／大小／MD5 逐条比较差异为 0。
- 独立 Standards／Spec review：0 项硬性规范违规、0 项需求偏离。Standards 的路径索引建议已采纳；比较结果接收入口建议未扩展为额外层级，当前入口受控且异步期间不能修改输入。
- 原生最小窗口布局检查覆盖四步，另检查警告不裁切、筛选文字有效、原生导航与选项失效。截图为离屏测试证据，不等同完整人工验收。

## 等待用户

- 人工验收文件选择／保存、二次演示弹窗及实际桌面体验；真实游戏目录未扫描，不冒称已通过。
- 验收后再由用户决定后续模块；本次不自动提交或推送。
