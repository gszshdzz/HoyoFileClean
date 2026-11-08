# 01：实现本地目录只读扫描

状态：完成；自动验证与提交前 code-review 已通过。

方案：[模块 1](../spec.md)

## 交付

- [x] `DirectoryScanner.cs` 默认轻扫路径与大小，重扫复用同一列表计算哈希；两种模式均可自定义排除目录与文件。
- [x] 使用 .NET 接口扫描与计算 MD5；`FilePathRules.cs`、`JsonFileOutput.cs` 复用现有路径校验和安全写出逻辑。
- [x] `Program.cs` 接入 `scan` 和 `--hash`，支持分组及重复排除选项；重扫低频单行进度，轻扫和重定向输出无动态进度，保留 `convert`。
- [x] 自动测试、真实 manifest 转换回归与独立 CLI 演示通过；使用说明和当前模块边界已更新。
- [x] Standards/Spec 双轴 code-review 无未解决问题。用户已授权自动验证通过后提交，无需再人工运行验收。

## 验证记录

2026-11-08，在项目根目录执行：

```powershell
dotnet build HoyoFileClean.csproj --no-restore
dotnet run --project tests\HoyoFileClean.Tests.csproj --no-restore
```

构建 0 警告、0 错误；76 项测试通过（原有 18 项、扫描 58 项），错误、失败、跳过均为 0。回归包含轻扫不打开正文、不触发进度，重扫复用固定列表及字节进度，阶段间大小或链接变化，分组参数及重定向输出，10 项设备前缀别名检查，以及 6 项列宽检查。

首次 code-review：Standards 0 项；Spec 发现窄终端进度可能折行刷屏。已用实际显示格式建立 6 项失败回归，再修复为 ASCII 文本和列宽减一限制，所有回归通过。复审 Standards 0 项、Spec 0 项，问题已关闭。

临时 CLI 轻/重扫均输出 2 个文件、10 字节，明确警告并记录 1 个断链；路径、大小一致，轻扫省略哈希字段、重扫 MD5 有效，重定向无动态进度。未扫描真实游戏目录。

重新转换 `refs/manifest_804dfe6da2a256f9_cc8e7b6a39595ac0229515f8313894f4` 和 `refs/cc8e7b6a39595ac0229515f8313894f4`，均为 2,831 个文件、135,765,032,023 字节；与 `refs/main.json` 逐条比对路径、大小、MD5，差异为 0，两种输入输出字节一致，可选哈希字段改动后也与修改前转换输出逐字节一致。临时产物位于 `C:\Users\yob14\AppData\Local\Temp\opencode\HoyoFileClean.Module1-aaa27e4306b94fb4aab028113dbbad55`，不加入项目。
