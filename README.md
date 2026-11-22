# HoyoFileClean

米哈游游戏残留文件清理工具，通过对照目标版本清单，清理过往增量更新留下的不再需要的文件。

> [!CAUTION]
>
> 本程序100％由AI生成，使用者应当自行评估风险，我（gszshdzz）不对任何由于使用者操作或程序本身bug导致的包括但不仅限于文件损坏/无法启动/其他非游戏文件丢失的后果负任何责任。
> 使用者应当自行对使用本程序负责，在进行操作前核对操作的目标文件，清单外文件不等于无用文件。建议在操作后在官方启动器进行一次文件完整性扫描。
> 此项目与[**miHoYo (miHoYo Co., Ltd.)**](https://www.mihoyo.com/) 或[**HoYoverse (COGNOSPHERE PTE. LTD.)**](https://www.hoyoverse.com/en-us)**无关联**

## 支持情况

### 适配

- [x] 原神-国服/B服/国际服
- [x] 星铁-国服/国际服
- [ ] 绝区零（manifest自动选择规则不完善，无法探测当前已清理的任务资源，需要手动进行选择）
- [ ] 崩坏三（没有安装，无法测试）
- [ ] 崩坏因缘精灵（未发布）
- [ ] 星布谷地（未发布）

### 功能

- [x] 游戏自动识别（适配情况因游戏而异）
- [ ] 游戏校验（已完成md5计算，未添加下载补全功能）
- [x] 自动根据游戏以及渠道服添加排除规则
- [x] 自动获取manifest（适配情况因游戏而异）
- [x] 网络获取manifest
- [x] 读取本地manifest
- [x] 文件差异比较
- [x] 文件操作（删除/移动）

## 使用方法

### GUI（推荐）

1. 从[Release](https://github.com/gszshdzz/HoyoFileClean/releases)下载最新版本，解压到你知道的位置（例如桌面）。双击打开`HoyoFileClean.Desktop.exe`运行

   > [!NOTE]
   >
   > 如果你没有.NET Desktop Runtime 10，下载带有后缀“-with-dotnet”的文件，它包含了运行时可以直接运行；如果有，可以下载体积较小的版本

   > [!IMPORTANT]
   >
   > 仅从此仓库Release下载文件，程序不在其他位置发布。

2. 选择你要扫描游戏的目录。选择的目录应当为游戏本体的根目录，包含游戏可执行文件exe（例如`YuanShen.exe`），注意不是启动器目录

3. 点击`读取游戏并加载预置排除`按钮，程序会读取该游戏的信息，并加载对应的排除规则

4. 自行添加应当排除的目录及文件

5. 点击`扫描`（不建议勾选`MD5重扫`，会消耗大量时间，且对精简文件来讲通常不必要），扫描完成后点击下一步

6. 选择游戏参考的manifest文件。推荐自动获取，如果你清楚manifest是什么，才选择后三种方式。只选择你的游戏本身会覆盖的包，不要选择未安装的语音包或已卸载的任务资源包。选择并解析需要的manifest文件后点击下一步

7. 核对显示的信息，进行比较，点击下一步

8. 选择要操作的文件。不建议勾选`显示隐藏项`，隐藏的文件发生变化通常属于正常情况，应当保护，勾选前确认用途。选择需要操作的文件后点击下一步

9. 再次核对需要操作的文件，确认无误后进行对应操作。注意移动到回收站各个文件会作为独立条目，不便于读取和恢复。推荐选择`移动`

### CLI

命令、参数、示例及退出码见 [命令行说明](docs/cli.md)。

> [!WARNING]
>
> 不推荐使用CLI，开发重点在GUI部分，目前相关说明尚未完善，功能可能不齐全，参数后续可能有较大调整。

## 构建

1. 需要Windows，下载并安装[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

2. 克隆此仓库

   ```shell
   git clone https://github.com/gszshdzz/HoyoFileClean.git
   ```

3. 在根目录打开命令行

4. 构建GUI：

   ```powershell
   dotnet build desktop\HoyoFileClean.Desktop.csproj -c Release
   ```

   构建CLI：

   ```
   dotnet build HoyoFileClean.csproj -c Release
   ```

5. 产物路径：

   CLI：`bin\Release\net10.0\`

   GUI: `desktop\bin\Release\net10.0-windows\`

## 更多说明

- [文档导航](docs/index.md)：使用、格式、开发维护与研究资料。
- [第三方声明](THIRD-PARTY-NOTICES.md)：Sophon 协议来源与许可。

## 许可证

本项目采用 [MIT License](LICENSE)。第三方代码、协议、依赖和开发技能保留各自原有许可；参见 [第三方声明](THIRD-PARTY-NOTICES.md) 及 [.opencode/third-party-licenses/](.opencode/third-party-licenses/)。

## 鸣谢

- [Collapse启动器](https://github.com/CollapseLauncher/Collapse) 预置文件排除规则参考
- [HoyoFiles](https://files.hk4e.com/) 对manifest获取及解析提供参考
