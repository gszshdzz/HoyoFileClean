# 硬编码数据盘点与意见

盘点日期：2026-11-12。Session：7.3。

本文件仅记录盘点和建议，不代表已批准修改。请在最后一列填写意见；填写后告知读取，再确定处理范围。游戏扫描进度条的两项调整已验收，与本表待讨论内容分开。

范围：仓库中的核心、CLI、桌面、测试、协议及构建文件。位置行号对应本次盘点时的代码，后续可能变化。业务默认值适合配置化；协议字段适合集中定义；平台、安全及格式规则不能仅因是常量就当作业务配置。

## 一、适合优先分离的数据

| 编号 | 位置 | 硬编码内容及影响 | 我的建议 | 用户意见 |
| --- | --- | --- | --- | --- |
| A1 | `desktop/MainForm.LocalReference.cs:5–7` | 默认目录 `miHoYo/HYP/1_1/modules/sophon/manifest`；启动器目录变化需要改代码。 | 移至项目配置，保留系统 AppData 路径解析和用户手动选择；不自动扫描其他目录。 | 改 |
| A2 | `core/NetworkManifestClient.cs:305` | 默认资源包 `game`；默认包策略仍在逻辑中。 | 将 CLI 默认包标识移至配置，仍只转换一个包；GUI 继续全部默认不选。 | 直接去除逻辑，不选默认 |
| A3 | `core/NetworkManifestClient.cs:7` 及其调用方 | `GenshinChannel`、`China`、`Global` 带原神及两服历史命名；不是游戏白名单，但配置身份仍耦合枚举。 | 若要让新增入口仅改配置，应使用配置中的稳定服务标识替代固定枚举；不要只改名字而保留同样限制。涉及公共接口，单独确认兼容范围。 | 似乎是通用性有，但是当时没有修改命名，只是命名遗留而非实际数据。你把命名规范一下不要这样，把区服枚举分离出来 |
| A4 | `desktop/MainForm.NetworkReference.cs:13、154`；`cli/CliOutput.cs:114` | `7.1.0`、原神、`hk4e`、`cn`、`global`、`zh-cn` 等示例；不限制实际输入。 | 示例文案集中到资源文件；不把示例变成候选名单，也不为几个示例单独增加复杂配置机制。 | 对。不只是这个，我认为所有的用户可读文字都应该抽离到单独的语言文件引用id，不仅方便统一管理和规范，也便于日后可能的多语言 |
| A5 | `desktop/ManifestTable.cs:34–41、76、158` | 列标题、列宽权重、日期格式、大小单位与控件逻辑混合。 | 标题和日期格式可集中到展示资源；单位和列宽保留具名定义即可，除非确实需要用户配置，不必全部外置。 | 似乎和上面是一样的，所有文字都应当抽离。标题可以抽离，列宽权重没有必要抽出，大小单位也没有设计为可更改。似乎就是你的建议就行 |
| A6 | `desktop/MainForm.cs:64、230`；`desktop/DifferencePanel.cs:23、196` | 步骤标题重复；筛选项与分类名称分散维护。 | 共用一份步骤／分类展示定义，避免改名时遗漏；无需另建可编辑业务配置。 | 一样，所有文案抽离 |
| A7 | `desktop/MainForm.NetworkReference.cs:172–194`；`desktop/ManifestTable.cs:185–235` | `未获取`、`未解析`、`获取并解析中` 等文字；部分逻辑通过显示文字判断状态。 | 优先把状态和文案分开：逻辑使用具名状态，显示层统一映射文字，避免改文案影响行为。 | 似乎和A4类似问题。分离文本资源并分离逻辑，同意 |

## 二、字段与格式约定

这些内容是协议的一部分，不是任意可替换的数据；改字段名不一定足以适配新协议。

| 编号 | 位置 | 固定内容及判断 | 我的建议 | 用户意见 |
| --- | --- | --- | --- | --- |
| B1 | `core/NetworkManifestClient.cs:16–17、73–83、131、208–239、352–356` | 官方 JSON 字段：`game`、`biz`、`id`、`game_branches`、`main`、`tag`、`manifests`、`manifest`、`manifest_download`、`matching_field`、`category_name`、`checksum`、压缩大小及下载地址等；散落在读取逻辑中。 | 集中到协议模型或单一映射层；保留类型、必填项、大小及来源校验。不建议直接把全部字段名变成自由配置。 | 不应当为配置文件，但是可以抽为数据模型，类似于那个proto |
| B2 | `core/NetworkManifestClient.cs:212–213` | `branch`、`password`、`package_id`、`tag` 官方查询参数名。 | 与官方响应模型一起集中到请求协议定义，避免多个位置重复字符串。这里的 `password` 是参数名称，不是本文件记录的凭据。 | 一样抽离为数据模型即可。password等参数的内容似乎是别人分析出来的，可能会改变，应当抽离方便后续配置适配 |
| B3 | `core/NetworkManifestClient.cs:86、223–236`；`core/ManifestConverter.cs:189` | `biz` 最后一个下划线拆分规则；压缩值 `0/1`、加密值 `0`、文件类型 `0`。 | 协议数值改为具名常量／枚举；拆分规则留在身份解析入口并补明确说明。协议语义改变时仍需适配逻辑，不能仅改配置值。 | 可以，但是我记得biz是游戏id，你详细解释一下 |
| B4 | `desktop/ExclusionRules.cs:13–25` | `scan`、`excludedDirectories`、`excludedFiles` 本项目 JSON 字段。 | 复用共用的数据契约定义，同时保持整批校验后发布的行为；不要让使用者随意改字段导致历史文件不兼容。 | 文件结构类抽离，先不用考虑兼容性 |
| B5 | `core/ManifestConverter.cs:10–17`；`core/ComparisonResult.cs:5–9、40–47`；`core/FileComparer.cs:201–203`；`core/DirectoryScanner.cs:79–83`；`desktop/WizardSession.cs:120–121` | 清单／报告字段及 `MD5`、`heavy`、`light`、`a`、`b`；部分由类型定义，部分字符串重复。 | 保持现有 JSON 格式；模式和两侧标记集中为具名定义。MD5 是当前实现的校验算法，不能只改名称就切换算法。 | 把json的md5字段，名称改为hash以防止歧义，我记得json文件里面有写是什么算法，读这个就行。同上，读分离的文件结构定义 |
| B6 | `Protos/Sophon.proto:9–41` | protobuf 字段名、编号和类型。 | 已独立为协议文件，保留现状；未来适配应更新协议并做真实样本回归，不再复制进另一份配置。 | 对，我b1b2就是想抽离为类似这个的数据模型 |
| B7 | `core/NetworkService.cs:9`；`core/HoyoFileClean.Core.csproj:12–13`；`cli/CliOutput.cs:91`；`desktop/MainForm.Actions.cs:256` | `config/network-services.json` 路径在加载、构建复制和提示中重复。 | 运行时代码统一引用配置路径定义；构建声明保留必要路径，校验二者一致。不值得为此额外引入生成工具。 | 统一引用 |
| B8 | `core/JsonFileOutput.cs:15–16`；`desktop/MainForm.GameReading.cs:200–203`；`desktop/MainForm.Actions.cs:152` | `.partial`、`*.json` 等后缀和文件过滤器。 | 同用途过滤器可复用；这些是通用输出／选择约定，单独外置收益很小，不值得配置化。 | 这个似乎不算是什么定义，是逻辑的一部分，不用抽 |

## 三、运行策略、展示文案及其他固定内容

| 编号 | 位置／类别 | 固定内容及判断 | 我的建议 | 用户意见 |
| --- | --- | --- | --- | --- |
| C1 | `core/ManifestConverter.cs:21–22`；`core/NetworkManifestClient.cs:43–45`；`cli/CliOutput.cs:64–65` | 重复的 64／128 MiB 上限，另有 2 MiB 元数据上限；提示文字也重复上限值。 | 共用安全限制定义，提示由实际限制值生成；不默认允许配置绕过安全边界。 | 抽离。这是我的配置不是用户的配置，只是方便程序管理，不是设置项目 |
| C2 | `core/NetworkManifestClient.cs`；`core/OfficialConnection.cs:13、33、57` | 2 分钟整体期限、1.5 秒单连接期限、500ms 地址递增节拍。 | 集中为具名运行策略，先保留已验证值；确有运维调节需求再配置化，不增加额外并发上限。 | 一样，这种策略性的应当可供我集中设置 |
| C3 | `desktop/ReferenceParser.cs:9–10`；`desktop/MainForm.Actions.cs:35`；`core/DirectoryScanner.cs:142、150`；`core/NetworkManifestClient.cs:339` | 默认线程数、线程上限、刷新间隔、缓冲大小。 | 保留必要具名常量，重复策略可统一；实现细节不必全部外置，不重新开放 GUI 隐藏线程配置。 | 一样 |
| C4 | `desktop/`；`cli/CliOutput.cs` | 按钮、帮助、警告、错误提示等中文文案；CLI 已部分集中，桌面仍分散。 | 若统一调整文案或支持多语言，用资源文件管理；不要把错误诊断协议和用户文字重新混在一起。 | 文案和上面的一样，全部抽离为语言文件，已经说过好几个了 |
| C5 | `core/FilePathRules.cs:5–10`；`core/ManifestConverter.cs:63–65、118–142`；`core/NetworkService.cs:28–29` | Windows 保留设备名、压缩格式识别字节、UTF-8 BOM、HTTPS 条件。 | 属于平台、文件格式和安全规则，保留在相应实现或标准定义中，不作为可编辑业务数据。 | 没必要抽离 |
| C6 | `tests/`、`desktop.tests/`，例如 `tests/NetworkManifestTests.cs:13、210–242、539–570` | 模拟 URL、游戏／包标识、版本、摘要、文件名和预期值；部分显式测试绑定真实版本／服务。 | 固定测试样本保留，保证回归能独立检测生产配置错误；规模大或多处共用的响应可迁到测试夹具文件，不把所有预期值都从生产配置读取。 | 测试用的示例信息暂时不用管 |
| C7 | 各 `.csproj`、`AssemblyInfo.cs` | 目标框架、依赖版本、项目路径、程序集名称。 | 属于构建声明，不算业务数据混入逻辑；保留项目文件管理。 | 这东西也不能动啊 |

## 四、已分离的数据

| 编号 | 位置 | 当前情况 | 我的建议 | 用户意见 |
| --- | --- | --- | --- | --- |
| D1 | `config/network-services.json` | 正式入口 URL、`launcher_id`、区服显示名称、兼容入口默认 `biz` 已外置；生产代码没有另一套 URL 回退或固定 CDN 白名单。 | 保留官方响应提供下载地址的流程；后续不要重新引入逐游戏／逐包名／逐 CDN 适配。 | 对的 |

## 本次验证记录

- GUI 两项修改已由用户明确验收，待提交；本表涉及的配置／协议／状态重构均未执行。
- 桌面测试：总计 112 项，111 项通过，1 项显式真实目录诊断未运行；错误、失败、跳过均为 0。
- 核心／CLI 测试：总计 258 项，252 项通过，6 项显式联网／诊断未运行；错误、失败、跳过均为 0。
- `git diff --check` 未报告空白错误；Git 提示现有工作副本 LF 在后续操作时可能转为 CRLF。
