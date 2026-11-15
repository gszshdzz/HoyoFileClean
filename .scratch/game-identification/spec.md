# 游戏识别与扫描调整

Session：8。用户要求整项连续完成，自行验证后统一交付；未授权提交或推送。

## 范围与决定

- GUI 顺序：游戏扫描、参考清单、比较、差异。实际扫描必须先识别成功；历史扫描 JSON 仍按原契约导入，不重新识别或访问记录目录。
- 核心扫描和 GUI 识别／扫描可取消；取消不发布快照和识别信息，旧结果在重新开始前失效，线程退出后允许重试。
- 用户最终调整：`config/NetworkServices.cs` 与 `config/GameIdentificationRules.cs` 均为可编译 C# 数据定义，不使用嵌入或旁置适配 JSON，不提供运行时文件覆盖。
- 两层识别配置：`Detections` 定义候选文件、正则提取目标和可比较标记；`Information` 定义条件映射、来源、含义、必要性和单／多值约束。
- 用户后续调整：删除 `is_gui`，由信息定义及其规则中的文本 ID 表达显示内容，不重复维护标记。配置引用 `LabelTextId`、`TextId`、`DisplayFormatTextId`，实际文字统一放在现有语言资源；核心不依赖展示层。
- 重复版本按数字版本取最大；使用 .NET `Version`，支持 2～4 段非负整数。任意值无法解析即失败，不按字符串大小或忽略坏值。
- 不可比较字段重复出现时必须完全一致（去除首尾空白）；同一检测条目的多个文件合并使用同一规则。单值信息映射出多个不同结果同样失败。
- 样本来源：用户的 `refs/config.ini`，原神国服 `hk4e_cn`，版本 `7.2.0`，`channel=1` 与 `sub_channel=1` 映射官服。只声明可证明的插件 `COa9vWaYX9`，不猜完整资源／语音包列表、不自动选网络包。
- 后续验收调整：不显示游戏名称，只显示实际游戏 ID；`uapc` 按 JSON 对象键提取，只配置忽略 `hyp`，不枚举 `cn/global` 后缀。插件捕获动态 ID，每个插件独立比较版本，不维护逐插件命中参数或名称文案。
- 结果首行紧凑显示游戏／版本／渠道，插件另行；扫描范围新增可选 `GameDetections` 原始键值记录，核心导出 JSON 写入 `scan.gameDetections`，导入按记录恢复显示、不读取源游戏目录。旧 JSON 兼容，不新增 GUI 导出或 CLI 识别开关。

## 文件范围

`config/`、`core/GameIdentifier.cs` 与识别模型、`DirectoryScanner`、桌面向导、共用错误文案、测试、README 与识别说明。不新增第三方依赖，不实现自动联网匹配或移动／删除。

## 验证与交付

任务记录见 `issues/01-identification-and-scan.md`；规则和验收说明见 `docs/game-identification.md`。提交前仍须用户认可，提交编号为 8。
