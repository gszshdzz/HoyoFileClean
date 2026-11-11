# 网络获取 manifest：原神逻辑

最终审查、验证与交付范围见 [REVIEW.md](REVIEW.md) 和 [主线交接说明](HANDOFF.md)。用户已授权审查修复后提交；真实桌面视觉验收不等同于自动测试。

## 已确认范围

- 用户要求最小实现、低冗余、不造轮子；先逻辑，随后补充要求接入 CLI 与 GUI。
- 先交付原神，其他三款游戏暂缓。不做预下载；支持最新版及自定义版本号，不查询历史版本列表。旧版本是否保留以官方返回为准，版本不符不得退回最新版。
- 国服官方、国际服可选。其他渠道尽量核实，未确认接口则仅提供官方渠道，不冒用其他渠道身份。
- 真实联网只允许官方服务器，禁止第三方连接。获准下载 manifest 验证，不下载游戏资源、不落缓存。

## 实现

`core/NetworkManifestClient.cs` 提供目录查询、下载、内存解析及转换写出；.NET `HttpClient` 与 `System.Text.Json` 处理网络和元数据，现有 `ManifestConverter`、`ManifestReadStream`、`FileComparer` 处理解压、文件条目校验与合并。无新依赖、下载调度框架、自动重试、磁盘缓存或游戏文件访问。

CLI 使用 `convert <output> --network --game <game> --region <region> --version <version>`；网络模式必填游戏／区服／版本，默认本体，`--package <package>` 最多指定一次；重复同值或不同值均在联网／写出前返回参数错误 2。游戏／区服依据官方 `game.biz` 匹配，资源包依据官方 `matching_field`，不以固定名单提前拒绝；示例为 `hk4e/cn`，旧自定别名 `genshin/china` 不再使用。GUI 复用共用表格与 ReferenceParser，在来源下方并排游戏／区服／版本单选，列表只约束六行可见、超过可滚动，版本含最新版与自定义输入框；查询只在按钮动作请求分支目录，获取当前条件的全部 manifest 后列行并填游戏／版本，默认勾选本体，解析只处理勾选项且不再次联网。三种来源没有勾选项时均禁用解析。条件变化、重新获取及切换来源使参考与下游失效，不混用来源。

正式客户端禁用重定向，清单 CDN 限定原神官方两个域名。下载和元数据均有实际读取上限，复用解压上限并增加声明长度、压缩方式及解压后 MD5 核对。整体取消期限两分钟，接受调用者取消。来源用 URL，不伪装成本地文件路径。

接口行为与验证命令见 README“网络获取 manifest”。任务记录：[核心](issues/01-genshin-network.md)、[CLI／GUI](issues/02-network-entrypoints.md)、[紧凑布局与联网修正](issues/03-network-refinements.md)。

CLI 单包限制的最新约定与验证见 [04](issues/04-cli-single-package.md)，取代早期任务记录中的重复选包设计；不影响 GUI 多选与核心合并能力。

## 参考边界

仅参考 Collapse 的“游戏分支 → 构建元数据 → manifest”流程及公开字段，不复制整套启动器／Sophon 下载库，不新增第三方包。

- https://github.com/CollapseLauncher/Collapse/blob/main/CollapseLauncher/Classes/Helper/LauncherApiLoader/HoYoPlay/HypLauncherSophonBranchesApi.cs
- https://github.com/CollapseLauncher/Hi3Helper.Sophon/blob/main/Structs/SophonChunksBranch.cs
- https://github.com/CollapseLauncher/Hi3Helper.Sophon/blob/main/Structs/SophonInfosJson.cs

这些资料在用户明确“仅官方连接”前已读取；之后仅联网请求官方 API／CDN。未能确认其他原神渠道的可用独立接口，不宣称这些渠道必定不存在清单。
