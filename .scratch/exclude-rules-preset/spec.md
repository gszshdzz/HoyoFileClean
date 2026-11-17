# 游戏预置排除规则候选方案

状态：2026-11-16 用户批准实施并补充以下修订；session 12。原候选列表保留作核对记录，实施规则以本节和 `config/GameIdentificationRules.cs` 为准。不修改游戏目录。

## 已批准修订与实现

- 四款游戏追加根目录 `ldiff/` 扫描排除；保持精确路径、忽略大小写、目录包含子目录、不使用通配符。
- `Audio_Chinese_pkg_version` 属于语音包稳定文件，从所有排除候选中撤下；扫描和比较均保留。
- `pkg_version` 属于本体包且会热更新，仍进入扫描清单；`Audio_Chinese_pkg_version` 同样保留扫描。用户再次澄清：不能把 pkg_version 的情况扩大到其他候选文件。其余文件按原方案恢复扫描排除（原神为 config.ini、beyond_pkg_version；绝区零为 config.ini；星铁／崩三为下列原文件列表去掉 pkg_version）。比较排除是未来需求，本轮不实现配置、界面或比较逻辑。原目录列表继续扫描排除。
- 已快进同步本地 `main` 至 `5504735`，复用统一的 GameIdentification 配置与解析。
- 新按钮“读取游戏并加载预置排除”调用 `GameIdentifier.Identify`；默认仅检查配置指定文件、在指定语音目录寻找首个普通文件，不计算 MD5，不建立全目录文件清单。正式扫描通过现有 identification 参数携带预读取结果，不重复读取元信息，也不因 config.ini 被排除而丢失身份／版本；记录的是预读取时的线索。
- 识别信息显示在扫描操作下方；成功读取是正式扫描前置条件。未读取、更换根目录、读取失败或取消读取时扫描按钮置灰；读取成功后调整 MD5 选项或扫描排除规则无需重读。预读取不发布扫描快照，不能直接下一步。导入历史 JSON 不受此条件限制。
- 预置数据与 biz 到游戏族的明确映射放在现有 `config/GameIdentificationRules.cs`；未知游戏不套用预置。原神当前仅有国服路径验证，不把 YuanShen_Data 路径套到国际服。
- 仅保留两类可编辑列表：扫描排除目录、扫描排除文件。撤回此前误加的仅比较排除列表、核心比较参数与对应第三步展示。
- 重新加载或切换根目录会移除本次自动添加的旧预置，保留不由预置添加的手动规则。重复加载不累积；用户可移除或增加规则。
- 本轮不新增 CLI 自动套用行为，不改变原有比较语义。
- 提交审查补充：重复候选存在性探测已去重；候选目录链接沿用跳过策略，根目录链接仍拒绝。只有带 Pattern 的配置文本候选做必要正文解析（默认 config.ini，每个文件一次），exe、语音和 pkg 只作存在性／类型探测，不读正文、不计算 MD5；不存在“绝对零内容读取”的承诺。审查与最终回归见 REVIEW.md。

## 本次验收

1. 选择游戏根目录，扫描应置灰。点击新按钮成功后显示已知游戏、版本、插件／包体线索并填入预置，扫描才可用，但尚不能下一步；目录不会生成扫描清单或被修改。
2. 核对仅有扫描排除目录与文件两栏，`ldiff` 在扫描排除目录；原神文件栏包含 config.ini、beyond_pkg_version，两个 pkg 文件不在排除列表；星铁 Persistent/Audio 正式语音范围继续保留，仅排除原方案指定的精确文件。
3. 正式轻扫后，`pkg_version` 和 `Audio_Chinese_pkg_version` 都在本地清单，MD5 已有数为 0；排除目录和文件不在清单，预读取的游戏／版本信息仍显示。读取后若元信息变化，需要再次点击读取；正式扫描不会重新解析它。
4. 对不同内容的本体 pkg 与语音 pkg 比较：两者均保持原有差异输出，不因预置而免比较。
5. 换根目录、重复加载、识别失败、取消后重试：旧自动预置不残留、不累积，手动规则保留，未成功读取时扫描仍置灰。

## 原候选记录（已被上述修订覆盖）

以下路径相对各游戏根目录，目录包含子目录；沿用现有精确路径、忽略大小写、不使用通配符的规则。仅适用于基于安装构建 manifest 比较的保守预置，不表示这些文件无用。未来默认应用、编辑交互及 CLI 行为尚不在本方案范围内。

## 原神（当前目录样本）

目录：

```text
Screenshot/
BeyondAssets/
chunk/
YuanShen_Data/Persistent/
YuanShen_Data/SDKCaches/
YuanShen_Data/webCaches/
```

文件：

```text
config.ini
pkg_version
Audio_Chinese_pkg_version
beyond_pkg_version
```

## 崩坏：星穹铁道（当前目录样本）

目录：

```text
StarRail_Data/ScreenShots/
StarRail_Data/SDKCaches/
StarRail_Data/webCaches/
StarRail_Data/Persistent/Apm/
StarRail_Data/Persistent/Archive/
StarRail_Data/Persistent/Asb/
StarRail_Data/Persistent/DesignData/
StarRail_Data/Persistent/IFix/
StarRail_Data/Persistent/Lua/
StarRail_Data/Persistent/NativeData/
StarRail_Data/Persistent/RawRes/
StarRail_Data/Persistent/Video/
```

文件：

```text
config.ini
pkg_version
StarRail_Data/Persistent/AppIdentity.txt
StarRail_Data/Persistent/AudioLaucherRecord.txt
StarRail_Data/Persistent/DownloadBlacklist.json
StarRail_Data/Persistent/DownloadedFullAssets.txt
StarRail_Data/Persistent/InstallVersion.bin
StarRail_Data/Persistent/launcher_verify
StarRail_Data/Persistent/Audio/AudioPackage/Windows/AudioLangRedord.txt
StarRail_Data/Persistent/Audio/AudioPackage/Windows/Extra.pck
StarRail_Data/Persistent/Audio/AudioPackage/Windows/PckPatchConfig.json
StarRail_Data/Persistent/Audio/AudioPackage/Windows/M_AudioV.bytes
```

## 绝区零（当前目录样本）

目录：

```text
ScreenShot/
ZenlessZoneZero_Data/Persistent/
ZenlessZoneZero_Data/SDKCaches/
ZenlessZoneZero_Data/webCaches/
```

文件：

```text
config.ini
pkg_version
Audio_Chinese_pkg_version
```

## 崩坏三（候选，缺少本地目录树）

目录：

```text
ScreenShot/
BH3_Data/SDKCaches/
BH3_Data/webCaches/
LauncherPlugins/
```

文件：

```text
config.ini
pkg_version
Version.txt
@IgnoredFiles
```

## 核对记录与限制

- 日期：2026-11-15；项目基线：ebe7e1738cf8dc2115fcbbc62fd12258b6202c4b。
- 用户提供 gi.txt、hkrpg.txt、zzz.txt 目录树；没有直接扫描游戏目录或读取个人数据。
- 通过现有 CLI 官方获取、长度/MD5/格式校验并转换：hk4e_cn 7.2.0 game 2795 条、zh-cn 179 条；hkrpg_cn 4.7.0 game 11580 条、zh-cn 350 条；nap_cn 3.3.0 game 10825 条、zh-cn 80 条；bh3_cn 9.1.0 game 267 条、asb 807 条。产物均在 C:\Users\yob14\AppData\Local\Temp\opencode，文件名 exclude-<gi|hkrpg|zzz|bh3>-<game|zh|asb>.json。
- 本轮不是完整包覆盖证明，尤其绝区零有大量未获取的分项资源包；其他语言、渠道、插件和国际服没有验证，不擅自泛化 exe/data 目录名。
- 原神 BeyondAssets 在目录树存在但不在两份构建清单中，按独立内容保守保护，不声称已核对其插件专属清单。
- 星铁中文语音 350 条都位于 StarRail_Data/Persistent/Audio，必须保留这一范围的正式资源与 .hash 校验；不能整片排除 Persistent，也不预置 *.hash。
- 星铁 Audio 下带摘要的动态文件、AudioV_<摘要>.bytes 等不应固化为逐版本文件名；以上稳定路径列表没有解决其全部保留问题，后续清理功能不能仅凭未列入构建清单就移动它们。
- 此处是原始候选核对，已被顶部批准修订覆盖：`pkg_version` 会热更新，仍扫描与比较，在 session 20 结果选择页默认隐藏；`Audio_Chinese_pkg_version` 已明确为稳定文件，扫描、比较与结果显示均保留。结果隐藏预置不重复本来就不扫描的文件。file_category_launcher、version_info、ctable_streaming.dat、StreamingAssets/Persistent/plugindelayed.config 保持正常比较。
- 不复制 Collapse 的宽泛 .sys/.ini/.log、d3d/@ 前缀和名称包含匹配，不预置 *.dll、*.exe、*.pck、*.blk、*.block 等扩展名规则。不因文件名含 useless、del、DoNotShip 或 test 而排除。
- Collapse 固定源码版本：1eabea865cdff9b324f84f6fb62203fe903d3958。
  - https://github.com/CollapseLauncher/Collapse/blob/1eabea865cdff9b324f84f6fb62203fe903d3958/CollapseLauncher/Classes/RepairManagement/Genshin/Fetch.cs
  - https://github.com/CollapseLauncher/Collapse/blob/1eabea865cdff9b324f84f6fb62203fe903d3958/CollapseLauncher/Classes/RepairManagement/Genshin/Check.cs
  - https://github.com/CollapseLauncher/Collapse/blob/1eabea865cdff9b324f84f6fb62203fe903d3958/CollapseLauncher/Classes/RepairManagement/StarRailV2/StarRailRepairV2.Fetch.cs
  - https://github.com/CollapseLauncher/Collapse/blob/1eabea865cdff9b324f84f6fb62203fe903d3958/CollapseLauncher/Classes/RepairManagement/Zenless/ZenlessRepair.Fetch.cs
  - https://github.com/CollapseLauncher/Collapse/blob/1eabea865cdff9b324f84f6fb62203fe903d3958/CollapseLauncher/Classes/RepairManagement/HonkaiV2/HonkaiRepairV2.Check.Unused.cs

验收本轮：只有方案文档新增，源代码和游戏目录未修改；八份官方下载转换全部成功。此方案不作为清理/移动授权，也不是完整清理安全保证。
