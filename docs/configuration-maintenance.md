# 配置与架构维护

配置面向维护者，编译进程序，修改后重新构建；不是终端用户设置，不读取旁置适配 JSON。固定业务数据放在 `config/`，官方动态数据直接使用响应，不增加逐游戏、逐包名或逐 CDN 的逻辑分支。

## 修改入口

| 内容 | 文件 | 约定 |
| --- | --- | --- |
| 官方服务 | `config/NetworkServices.cs` | `Id` 唯一；`NameTextId` 引用语言资源；接口 URL 不带查询串，`LauncherId` 独立并统一编码。无默认游戏或包。 |
| 游戏识别 | `config/GameIdentificationRules.cs` | 候选路径、提取规则及含义，见 [游戏识别](game-identification.md)。 |
| 清单识别 | `config/ManifestIdentificationRules.cs` | 清单字段特征及含义，见 [manifest 识别](manifest-identification.md)。 |
| 游戏文件规则 | `config/GameFileRules.cs` | `ScanDirectories`／`ScanFiles` 排除扫描；`ResultHiddenFiles` 只隐藏结果，不改变比较。 |
| 自动获取限制 | `config/AutomaticManifestRules.cs` | 识别不完备的游戏禁用名单，与识别和文件规则分离。 |
| 运行策略 | `config/Maintenance.cs` | 默认目录、资源上限、超时、连接节拍、线程、刷新和缓冲参数。 |
| 展示配置 | `config/PresentationOptions.cs` | 默认语言和官方 branch 的包类型映射；只定义数据。 |
| 语言文字 | `presentation/Languages/zh-CN.json` | 使用字符串 ID，不维护手工 ID 枚举，不以文案判断状态。 |
| 官方协议／网络结果 | `core/Models/OfficialProtocol.cs`、`NetworkModels.cs` | 字段映射、协议数值及可信下载来源标记。 |
| 持久化结构 | `core/Models/FileDocuments.cs` | 清单、报告、排除规则；见 [数据格式](cli.md#数据格式)。 |
| protobuf | `Protos/Sophon.proto` | 保留字段编号及 [来源许可](../THIRD-PARTY-NOTICES.md)。 |

语言资源嵌入程序，按界面语言查找，缺少翻译回退默认语言；目前仅简体中文，无语言切换控件。官方标识、文件名和路径不翻译；Windows 自带对话框语言由系统管理。

## 分层与复用

- `core/`：转换、扫描、读取校验、比较和安全写出；不依赖控制台或 Windows Forms。主动校验使用 `CoreException` 的错误码及上下文，底层原因保留，不能直接作为用户文案。
- `cli/`：参数、退出码、终端输出与进度；`desktop/`：原生向导、页面状态和列表。两者直接调用 core，GUI 不启动或解析 CLI。
- `presentation/`：语言加载、错误解释和来源网址脱敏，单向依赖 core；CLI／GUI 保留各自展示方式，不打印调用栈或原始异常文字。
- 两类识别共用 `IdentificationEvaluator`，只保留输入读取差异；两份配置独立，不按检测 ID 关联。
- `ReferenceInput` 保留本地路径或原始网络包，不通过 URL 反查包；不同包可共用官方地址。`ReferenceParseBatch.GetSnapshot()` 统一保证整批成功才可推进。
- `FileEntryRules` 共用文件大小、MD5 和路径规范化；格式、集合冲突、总量限制及错误优先级由各入口保留。多输入只在比较模块中合并，不另建组合格式或模块。
- 桌面只传递校验后的内存快照，无中间 JSON 或磁盘缓存。成功行保留独立快照供详情导出，整批失败仍不可推进；释放引用不保证进程内存立即下降，不强制 GC。

## 必须保留的边界

网络仅使用官方接口及响应中的资源 URL，不依赖第三方维护的数据服务或固定 CDN 名单。正式客户端禁止重定向；联网对象须保留官方构建响应的来源标记，地址替换或手工构造不能下载。保留 HTTPS、默认端口、无用户信息／片段，以及长度、解压、MD5、格式和路径校验；此信任模型不提供独立域名白名单防护。

默认元数据上限 2 MiB、压缩输入 64 MiB、解码内容 128 MiB，请求整体期限两分钟。连接候选按 IPv4／IPv6 各自顺序去重，初始各一个，每 0.5 秒每族新增一个，每次最多 1.5 秒；先成功者胜出并释放其他连接，无额外并发上限，不固定 IP、不禁用地址族、不预联网。TLS、代理和连接复用交给 .NET，不承诺固定网络耗时。

不要为统一风格改变数据契约：例如排除规则允许省略一个数组，但不能两项都缺，拒绝显式 `null`；当前仅支持 MD5，改算法名称不等于支持新算法。平台与路径安全规则不是可随意配置的业务数据。

修改后按 [测试说明](testing.md) 选择相关回归，涉及共用接口时执行所需全量。历史交付保留在 `.scratch/configuration/`、`.scratch/architecture/`，测量与审查见 [文档导航](index.md#研究与历史)，不重复写入当前指南。
