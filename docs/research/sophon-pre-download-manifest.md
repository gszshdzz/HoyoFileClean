# Sophon 预下载 manifest：两个真实样本的格式研究

研究日期：2026-11-16。范围：只读检查 `refs/` 两个样本，对照 Hi3Helper.Sophon 和 Collapse Launcher 源码，提出解析建议；不修改正式解析代码、不下载游戏文件、不实现补丁应用。

## 结论

两个样本都是**未压缩的 `SophonPatchProto` protobuf 补丁清单**，不是本项目现有的 `SophonManifestProto` 构建清单。依据不是 `hk4epre` 文件名，而是完整输入的字段分布与公开 schema 一致：目标文件大小位于字段 2，MD5 位于字段 3，字段 4 是按旧版本组织的补丁引用；大样本还具有顶层字段 2 `UnusedAssets`。[实测][P]

它们能够直接提取**当前消息所列目标文件**的路径、大小、MD5，不需要下载补丁、解压游戏资源或者应用 HDiff。不能只提取有 `AssetInfos` 的条目：小样本有 140 条、大样本有 1,482 条目标记录没有补丁引用。[实测]

但这不证明它们就是整个目标版本的完整文件清单。Hi3Helper.Sophon 的更新枚举以目标构建 manifest 为遍历基准，再匹配补丁信息；它没有将 `PatchAssets` 的覆盖范围当作目标构建清单的替代契约。应以对应目标构建清单核对路径、大小、MD5，才能确认所选资源包范围内是否完整。[ENUM]

**“预下载”是获取未来版本资源的阶段，不是唯一的二进制格式名称。** Collapse 同时支持构建清单之间的 chunk 差异下载，以及 patch 清单加目标构建清单的补丁下载。不能把所有预下载 manifest 都按 patch 解析，也不能全部按 build 解析。[C-BUILD][C-PATCH]

## 1. 样本实测

下文 A、B 是完整文件名的缩写：

- A：`refs/333-hk4epre-802e8aa24e4335c9f7e88a018345593d`
- B：`refs/333-hk4epre-ab3b0aa890ee764aa13207343c7b154e`

| 项目 | A | B |
| --- | ---: | ---: |
| 输入字节数 | 35,089 | 875,281 |
| 前 16 字节 | `0A630A3A5975616E5368656E5F446174` | `0AC4040A415975616E5368656E5F4461` |
| 顶层 PatchAssets 数量 | 179 | 2,795 |
| 目标路径去重数（忽略大小写） | 179 | 2,795 |
| 没有 AssetInfos 的目标记录 | 140 | 1,482 |
| 有一个 AssetInfo 的目标记录 | 4 | 137 |
| 有两个 AssetInfos 的目标记录 | 35 | 1,176 |
| AssetInfos 总数 | 74 | 2,489 |
| 目标大小之和（字节） | 14,535,287,384 | 135,833,870,380 |
| 非 32 位十六进制目标 MD5 | 0 | 0 |
| 顶层 UnusedAssets 分组 | 0 | 2 |

输入 MD5 恰好与各文件名最后一段相同：

```text
A MD5    802E8AA24E4335C9F7E88A018345593D
A SHA256 AD49346C3F67AC9997B64959E6C984BFDA1A577CCFA3F8E33D906A6F03508B77
B MD5    AB3B0AA890EE764AA13207343C7B154E
B SHA256 21098B40A3285CFDFE69DC0768927F52C41E8D17BA54FAB3A2930142037FCA09
```

这是样本自身的一致性，不是官方来源认证，也不证明网络响应的压缩标记。两个本地输入均可从第 0 字节直接走完 protobuf 顶层，未做解压或剥离私有头部；没有产生单独的“解压后文件”，解析载荷就是原输入。[实测]

A 包含 178 条 `AudioAssets` 路径和 `Audio_Chinese_pkg_version`；首条是 `YuanShen_Data/StreamingAssets/AudioAssets/Chinese/2078.pck`。B 包含资源块、音频、`YuanShen.exe`、`pkg_version` 等。路径支持“中文语音相关”和“游戏本体相关”的人工判断，但没有配套 `matching_field` / category 元数据，不能将这些判断当作官方资源包身份。[实测][META]

### 旧版本引用

| 样本 | AssetInfo.VersionTag | 引用数 | 含 OriginalFileName | 不同 PatchName 数 |
| --- | --- | ---: | ---: | ---: |
| A | 7.1.0 | 35 | 33 | 6 |
| A | 7.0.0 | 39 | 35 | 14 |
| B | 7.1.0 | 1,176 | 1,096 | 95 |
| B | 7.0.0 | 1,313 | 1,179 | 140 |

引用数量大于不同补丁对象数量，同一个补丁对象会被多个文件引用。不能逐引用简单累加 `PatchSize` 得到下载量。部分 `PatchOffset` 没有写入，proto3 数值默认值为 0；这不是字段损坏。[实测][WIRE][ENUM]

B 的 `UnusedAssets` 中，`7.1.0` 分组有 29 个旧文件，`7.0.0` 分组有 12 个旧文件；各组内没有重复名称，且各组名称均不在该样本的目标路径集合中。A 没有这些分组。不能因此断言 A 没有任何应移出的本地文件，也不能把 B 的旧文件混进目标文件列表。[实测]

## 2. 二进制 schema

protobuf 保存的是“字段编号、编码类型、值”，不保存消息名称。`0A` 是字段 1、长度分隔编码，因此两种 manifest 都可能以 `0A` 开头；单个首字节无法区分。[WIRE]

以下字段定义来自固定版本的 `SophonPatchProto.proto`，本次实测的目标条目与嵌套引用字段分布与之对应。[P]

### 顶层 SophonPatchProto

| 编号 | 名称 | 类型 | 用途 |
| --- | --- | --- | --- |
| 1 | PatchAssets | repeated SophonPatchAssetProperty | 当前消息所列目标文件及可选补丁引用 |
| 2 | UnusedAssets | repeated SophonUnusedAssetProperty | 按旧版本分组的旧文件记录 |

### SophonPatchAssetProperty

| 编号 | 名称 | 类型 | 用途 |
| --- | --- | --- | --- |
| 1 | AssetName | string | 目标文件相对路径 |
| 2 | AssetSize | int64 | 目标整文件字节数 |
| 3 | AssetHashMd5 | string | 目标整文件 MD5 |
| 4 | AssetInfos | repeated SophonPatchAssetInfo | 针对不同旧版本的补丁方案；可不存在 |

`SophonPatchAssetInfo` 的字段 1 是字符串 `VersionTag`，字段 2 是消息 `Chunk`。源码用前者与 `versionTagUpdateFrom` 匹配，因此样本里的 `7.1.0`、`7.0.0` 是**更新来源版本**，不是目标版本。[ENUM]

### SophonPatchAssetChunk

| 编号 | 名称 | 类型 | 含义 |
| --- | --- | --- | --- |
| 1 | PatchName | string | 补丁对象名，不是目标文件名 |
| 2 | VersionTag | string | 补丁对象的版本标记；样本与其外层来源版本对应 |
| 3 | BuildId | string | 补丁关联构建标识；不能当目标游戏版本 |
| 4 | PatchSize | int64 | 补丁对象大小，不是目标文件大小 |
| 5 | PatchMd5 | string | 补丁对象 MD5，不是目标文件 MD5 |
| 6 | PatchOffset | int64 | 当前文件补丁数据在补丁对象中的起始偏移 |
| 7 | PatchLength | int64 | 当前文件补丁数据长度 |
| 8 | OriginalFileName | string | 来源文件路径；可为空 |
| 9 | OriginalFileLength | int64 | 来源整文件大小 |
| 10 | OriginalFileMd5 | string | 来源整文件 MD5 |

偏移和长度的语义由 `PatchTargetProperty` 的区间流，以及 HDiff 流中的 `pos + PatchOffset` 确认。缺少来源文件引用时，上游先采用 CopyOver 策略，但还会检查数据是否其实是 HDiff；不能仅凭空 `OriginalFileName` 断言内容必定是可直接复制的目标正文。本研究没有读取补丁对象正文。[APPLY]

### UnusedAssets 的嵌套结构

```text
SophonUnusedAssetProperty
  1 string VersionTag
  2 repeated SophonUnusedAssetInfo AssetInfos
    1 repeated SophonUnusedAssetFile Assets
      1 string FileName
      2 int64 FileSize
      3 string FileMd5
```

它描述旧文件，不是新版本保留文件。执行清理还需要用户选定范围、实际文件身份和本项目的保护/移动规则；不移植启动器的直接删除行为。[P][ENUM][本项目 GLOSSARY.md]

### B 的第一条目标记录

```text
AssetName    YuanShen_Data/StreamingAssets/AssetBundles/blocks/00/09501945.blk
AssetSize    50481510
AssetHashMd5 bf1cb76052d36e82c9177d53cc54c894

AssetInfos[VersionTag=7.1.0].Chunk:
  PatchName          2776b96497772cca_a790054c5a63ce026fb8d3dd42e86d13
  VersionTag         7.1.0
  BuildId            dJsUs1m2gLGR
  PatchSize          71190360
  PatchMd5           a790054c5a63ce026fb8d3dd42e86d13
  PatchOffset        19533987
  PatchLength        62
  OriginalFileName   YuanShen_Data/StreamingAssets/AssetBundles/blocks/00/09501945.blk
  OriginalFileLength 50506212
  OriginalFileMd5    a52a3972f52c452f6cbc0d66dc73f82e
```

同一目标还有 `7.0.0` 引用，来源大小为 50,508,863，补丁长度为 69。目标大小、旧文件大小、整个补丁对象大小、单文件补丁片段长度是四个不同数值。[实测]

## 3. 启动器如何选择预下载清单

固定检查 Collapse commit `74cfbfb29398baf65e89b8254e143ee417d61d09`，Hi3Helper.Sophon commit `4c6001e8585ffa2fda78805db0ab960e3245c049`；不把它们称为米哈游官方协议规范。

### 构建清单差异路线

`StartPackageUpdateSophon` 选择 PreloadUrl 或 MainUrl；`AddSophonDiffAssetsToList` 分别取得来源和目标的构建清单，再调用 `SophonUpdate.EnumerateUpdateAsync`。预下载阶段只调用 `DownloadDiffChunksAsync`，不写入目标安装文件。它所使用的目标构建 manifest 仍是普通构建 schema。[C-BUILD]

### 补丁路线

`AlterStartPatchUpdateSophon`：

1. 从安装版本取得来源版本，从预下载 API 版本或当前 API 版本取得目标版本。
2. 按 `isPreloadMode` 重新关联分支信息。
3. 请求 PatchUrl，提供来源版本与所选资源包的 matching field。
4. 同时取得 PreloadUrl（预下载）或 MainUrl（普通更新）的目标构建清单。
5. `SophonPatch.EnumerateUpdateAsync` 解析 patch，按来源 VersionTag 建索引，再遍历目标构建清单；没有匹配补丁的目标文件走 DownloadOver。
6. 预下载阶段下载完后返回，不应用补丁。[C-PATCH][ENUM]

外置 JSON 中，构建与 patch 响应均有 `data.build_id`、`data.tag` 和 `manifests`；patch 另有 `patch_id`。每个资源包身份携带 `manifest`、`manifest_download`；构建资源使用 `chunk_download`，patch 使用 `diff_download`。manifest 自身的 ID、checksum、压缩前后大小，压缩/加密标记都来自这些外置元数据，不在目标文件条目里。[META]

本次没有与两个文件名对应的官方响应，因此未确认目标版本、官方 matching field/category、实际下载 URL 和网络传输时是否压缩；也未继续追踪 `EnsureReassociated` 的分支 URL 组装实现。不根据 `333`、`hk4epre` 或旧版本号猜目标版本。

## 4. 对本项目的最小接入建议（尚未实现）

现有 `Protos/Sophon.proto` 只有构建清单与构建清单的文件投影；`core/ManifestConverter.cs:159-195` 固定调用 `SophonFileManifest.Parser`。构建文件大小位于字段 4、MD5 位于字段 5；patch 的大小/MD5 分别位于 2/3，字段 4 还是嵌套消息，编码类型也不同。因此不能复用同一个消息类型解析两者。[BUILD][P][本地源码]

protobuf 允许忽略未知编号或不匹配编码类型的字段。用错 schema 可能仍然“解析成功”，随后看到的是默认大小、空 MD5 或缺失信息；不能把语法成功当格式识别。现有 `WithDiscardUnknownFields(true)` 更不会保留这些区别用于后验判断。本次未运行正式转换入口，错误表现是依据源码分析，而非声称已复现某个错误码。[WIRE][本地源码]

建议仅增加 patch 的只读目标文件投影：顶层 repeated 字段 1；目标字段 1/2/3；忽略字段 4 的补丁正文和顶层字段 2 的旧文件正文。复用现有 Google.Protobuf 生成机制、限量读取、路径与 MD5 校验、重复路径校验和规范化输出，不引入整个下载/HDiff 库，不手写正式 protobuf 解析器。

格式入口优先依据可信外置元数据指定 build/patch；本地文件缺少元数据时，必须检查候选 schema 与业务字段的一致性，拒绝混合、歧义和损坏输入。不能“build 失败就无条件换 patch 再试”，也不能靠文件名后缀决定。具体识别契约应另行设计并测试，本文不宣布已实现可靠自动识别。

只输出目标文件清单时，无需来源版本来过滤 `PatchAssets`，无需把 `UnusedAssets` 合并进来，也无需保留补丁对象。应标明这是 patch 所列目标集合，完整覆盖未确认。若要证明目标版本完整性，最直接的方法是取得同资源包的目标构建 manifest，逐路径核对三个文件身份字段。[ENUM]

## 5. 验证范围与后续验收

本次通过 PowerShell 中的临时 C# 字段检查器只读遍历输入：检查 varint、字段编号、长度边界和完整顶层结束；统计全部目标、AssetInfos、PatchChunk 及 UnusedAssets。脚本位于批准的临时目录 `C:\Users\yob14\AppData\Local\Temp\opencode\inspect-pre-manifest.ps1`，没有加入正式代码。它是研究用字段观察器，不是成熟生成 parser 的替代品，也没有执行完整路径安全校验或下载补丁验证哈希。

两次运行均成功，完整统计见上文。研究未安装依赖、未执行项目构建或正式测试。后续正式实现的验收至少应包含：

- A 输出恰为 179 条、大小总和 14,535,287,384；B 输出恰为 2,795 条、大小总和 135,833,870,380。
- A 首条路径/大小/MD5 与实测一致；B 首条目标大小是 50,481,510，不是来源大小、71,190,360 或 62。
- 没有 AssetInfos 的目标记录仍然输出；B 的 29/12 个旧文件不进入目标列表。
- 原有构建 manifest 回归不变；新增截断、混合字段、空/非法 MD5、负数大小、危险路径、重复冲突及歧义识别测试。
- 对照对应目标构建清单后，再决定能否用于该资源包完整目标版本比对；现在不宣称完整。

网络搜索尝试了 Sophon、Collapse、preload/manifest 等组合，但当前搜索集成均未返回结果；GitHub API 的树请求返回 403，改用仓库页面和固定 commit 的 raw 源码核实。**没有获得独立第三方实现的有效交叉证据**，不把“搜索无结果”写成“不存在其他实现”。通用 protobuf 规则另通过 Context7 检索其官方项目源码。[WIRE]

## 同版本正式包与预下载包对照

用户补充 `refs/manifest.json`（正式包）和 `refs/pre.json`（预下载包），并确认它们对应同一版本。本次未将 JSON 正文读入会话，只由程序检查结构并执行比较。

本次通过临时脚本调用项目已有的 `FileComparer`，按规范化路径匹配整文件大小与 MD5，不依赖数组顺序。实测结果：A 独有 0、B 独有 0、共有但不同 0；大小和 MD5 一致 2,795、仅大小一致 0、扫描范围外 0。因此**这对样本所列的目标文件集合完全相同**；这补充了之前缺少同版本构建对照的证据，但不证明所有版本、所有资源包的 patch 都具有完整覆盖，也不证明这些清单涵盖整个游戏安装目录。

临时脚本已移出项目，不作为项目交付内容。对比过程输入只读，类型标记及识别 metadata 不作为文件身份差异，比较器仍校验文件数量／大小汇总、路径及哈希。

## 来源

- [实测]：本仓库 `refs/` 两个原始输入；完整文件名、输入摘要、统计及方法见本文。
- [P]：https://github.com/CollapseLauncher/Hi3Helper.Sophon/blob/4c6001e8585ffa2fda78805db0ab960e3245c049/Protos/SophonPatchProto.proto
- [BUILD]：https://github.com/CollapseLauncher/Hi3Helper.Sophon/blob/4c6001e8585ffa2fda78805db0ab960e3245c049/Protos/SophonManifestProto.proto
- [ENUM]：https://github.com/CollapseLauncher/Hi3Helper.Sophon/blob/4c6001e8585ffa2fda78805db0ab960e3245c049/SophonPatch.cs
- [APPLY]：https://github.com/CollapseLauncher/Hi3Helper.Sophon/blob/4c6001e8585ffa2fda78805db0ab960e3245c049/SophonPatchAsset.Update.cs
- [META]：https://github.com/CollapseLauncher/Hi3Helper.Sophon/blob/4c6001e8585ffa2fda78805db0ab960e3245c049/Structs/SophonInfosJson.cs
- [C-PATCH]：https://github.com/CollapseLauncher/Collapse/blob/74cfbfb29398baf65e89b8254e143ee417d61d09/CollapseLauncher/Classes/InstallManagement/Base/InstallManagerBase.SophonPatch.cs （尤其 L60–128、L395–404、L546–550）
- [C-BUILD]：https://github.com/CollapseLauncher/Collapse/blob/74cfbfb29398baf65e89b8254e143ee417d61d09/CollapseLauncher/Classes/InstallManagement/Base/InstallManagerBase.Sophon.cs （尤其 L691–734、L880–888、L1121–1187）
- [WIRE]：Context7 `/protocolbuffers/protobuf` 返回的官方源码：https://github.com/protocolbuffers/protobuf/blob/main/src/google/protobuf/wire_format_lite.h 、https://github.com/protocolbuffers/protobuf/blob/main/src/google/protobuf/wire_format.cc 、https://github.com/protocolbuffers/protobuf/blob/main/docs/field_presence.md
- 本地源码：`Protos/Sophon.proto`、`core/ManifestConverter.cs`；已有背景笔记：`docs/research/sophon-manifest.md`。
