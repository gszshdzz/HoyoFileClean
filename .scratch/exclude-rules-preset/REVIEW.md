# 游戏预置排除规则审查

Session：12；日期：2026-11-16。

基准：本地 main / HEAD `55047356460c75eb9b177ccafe9a7c67c841a5fd`，对照当前工作区（包含新增文件）。用户明确要求不调用 subagent，因此按 code-review 两轴分别自审，并非两个独立代理的审查。

规范依据：根 AGENTS.md、全局 AGENTS.md、README.md、docs/agents/issue-tracker.md、docs/game-identification.md，以及用户本轮配置分离／统一逻辑／最小实现要求；仓库未发现 CONTRIBUTING.md 或 CODING_STANDARDS.md。需求依据：spec.md 与本会话用户修订。

## Standards

- 判断性改进 1：同一文本文件供多条检测使用时，ReadCandidatePaths 原先重复探测存在性。按规范化候选路径和 Prefix 去重；文本正文沿用原 texts 缓存，每个文件仅读取一次。
- 判断性改进 2：gameReadReady 与 loadedIdentification 重复维护成功状态。已移除布尔值，以已保存识别结果判断扫描是否可用，避免状态不一致。
- 规则与适配数据集中在 config/GameIdentificationRules.cs；路径校验、文本解析、含义计算、扫描与取消均复用现有实现。没有逐游戏逻辑分支、新依赖、锁文件变化或比较排除实现。检查 Fowler smell baseline 后没有其他需要处理的问题，不为小收益新增框架或抽象。

## Spec

- 修复 1：候选目录链接导致预读取整体失败，与原扫描器跳过链接行为不一致。已在游戏根目录验证后仅跳过候选及其祖先目录中的链接；根目录本身是链接、权限错误及元信息格式错误仍报错。新增回归已先复现 DirectoryIsLink，修复后通过。
- 读取成功才启用正式扫描；换根目录／读取失败／取消读取后扫描置灰。原神文件排除为 config.ini、beyond_pkg_version；其他游戏依 spec 恢复文件预置；ldiff 排除扫描，pkg_version 和 Audio_Chinese_pkg_version 保留扫描与现有比较。
- 正式扫描传入已读取 identification，避免排除 config.ini 后身份／版本丢失。历史 JSON 导入不强制访问游戏目录。预读取的包体线索不等于正式扫描范围；元信息变化需重新读取。

## 最小必要探测核对

- 精确文件候选只做存在性／文件类型及祖先目录安全校验；不打开 exe、语音资源或 pkg 文件。
- 目录前缀只在配置指定范围惰性寻找首个普通文件，找到即返回；空目录不证明包体存在，跳过链接，不建立全安装目录清单。
- 只有带 Pattern 的文本候选会读取正文；当前默认配置是 config.ini。版本、渠道、插件等信息需要正文解析，因此不是“绝对零内容读取”。使用现有 MetadataLimit、UTF-8 校验及正则超时，文本内容共享缓存。
- 识别路径不调用全目录 Scan 或 MD5；正式扫描才按用户选择计算 MD5。独占锁住 exe、语音和两个 pkg 文件后识别仍成功；切换配置到 metadata.ini 后独占锁住 config.ini 仍成功；预读取后独占锁住 config.ini，GUI 正式扫描仍成功。
- 保留成熟的 scannedPaths 调用方式，提供文件表时不新增候选目录探测；核心 identifyGame 扫描模式未改动。

汇总：规范轴 2 项判断性改进已处理，需求轴 1 项功能问题已修复；未解决问题 0。

最终全量：核心 Total 465 / Errors 0 / Failed 0 / Skipped 0 / Not Run 7（458 项通过）；桌面 Total 182 / Errors 0 / Failed 0 / Skipped 0 / Not Run 1（181 项通过），无构建警告。未运行项为显式联网／真实样本诊断，未扫描用户游戏目录。提交标题：增加扫描前轻量游戏识别与预置排除规则；编号 12 已记入 docs/commit-sessions.md。
