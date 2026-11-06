# 模块 0：本地原神 Sophon manifest 转文件列表

研究日期：2026-11-06。范围仅为本地原始清单的解压、protobuf 解析与文件列表提取；不研究自动获取版本、CDN 下载或正式清理程序。技术决策、交付形式、是否逐份输出 JSON 由父会话与用户确定。

这里的“完整文件列表”只指**当前输入 manifest 中的全部文件条目**，不指整个游戏安装目录的完整版本清单。本项目 `GLOSSARY.md` 已明确：一份版本清单可能只覆盖一个资源包，清单外文件也不能直接认定可清理。

## 结论与证据边界

1. **优先复用兄弟项目 `E:\Files\WorkSpace\GIAudioPatch` 的现有组合：`ZstdSharp.Port` 解压 + 现有 `.proto` + `Google.Protobuf` 生成解析器。** 已有代码不自行实现压缩或 protobuf 协议，满足“不造轮子”；但当前 CLI 不是本地转换工具，不能直接运行它交付模块 0。[L1][L2][L3][L4]
2. **“zstd 压缩的 protobuf”符合已核对源码，但不是所有输入必定压缩。** 兄弟项目明确支持 `compression == 0` 的未压缩 protobuf 和 `compression == 1` 的 Zstd；上游 Sophon 库同样依据外部压缩标记选择解压流或原始流。[L5] L1162–L1194；[S-PUBEXT] L491–L495。
3. 构建清单中有文件路径、字节大小和**整文件 MD5**；另有 chunk 的名称、偏移、大小与解压后 MD5。不能把 chunk 名称、chunk 校验值或 manifest 自身校验值当成整文件哈希。[L2] L5–L22；[S-PROTO]。
4. **protobuf 主消息没有版本、游戏、语言、资源包类别或整体覆盖声明。** 能解析文件，不等于能可靠识别它属于哪个目标版本、哪个游戏或是否覆盖全部安装文件。这些信息主要在外置构建元数据中，单个二进制清单无法证明。[S-PROTO][S-META][S-IDENT]
5. **未找到真实 manifest 与对应 JSON 验收样本。** 本地测试是人工构造的消息，甚至使用 `target-md5` 等非真实 MD5 值；只能说明测试意图，不能当真实清单兼容性证据。[L6] L58–L79、L362–L388。

上游 `Hi3Helper.Sophon` 源码是该库行为的一手资料，**不是米哈游公开协议规范**。本次没有读取或解析真实游戏二进制，因此不能承诺所有原神版本、渠道、语言包均适用。

## 1. 优先参考：GIAudioPatch 实际现状

### 1.1 已读说明与许可缺口

- 目标 `E:\Files\WorkSpace\GIAudioPatch` 已用 `Test-Path` 确认为存在。已读取其 `AGENTS.md` 与要求先读的 `GLOSSARY.md`；父目录 `E:\Files\WorkSpace` 没有 `AGENTS.md`、`CLAUDE.md`、`README.md`，目标目录没有 `CLAUDE.md`、`README.md`，补充文件名检查也未发现其他根目录 README。[L0]
- 兄弟项目的固定产品范围是电脑版原神 `YuanShen_Data/StreamingAssets/AudioAssets` 第一层 `Music*.pck`，它的音乐选择和网络规则**不应移植为 HoyoFileClean 的全文件列表范围**。[L0] L13、L18–L28。
- 本次只读检查不需要执行其构建、测试或网络回归。未执行项目代码，未触发下载或清理临时文件；其网络模块及删除分支都不在复用范围。
- 本地根目录未发现 `LICENSE*`、`COPYING*`、`NOTICE*`；`Protos/Sophon.proto` 中也没有来源 URL、上游 commit 或版权声明。**只能确认字段与上游兼容，不能证明这份本地文件当初具体从哪里复制、采用了哪个上游版本。** 若复用上游 schema，后续应记录准确来源并保留上游 MIT 版权与许可声明，本次不新增许可文件。[L2][S-LICENSE]

### 1.2 语言、依赖与生成入口

本地实现为 **C#，目标框架 `net10.0`**。`GIAudioPatch.csproj` L12–L19 声明：

| 依赖 | 本地版本 | 作用 | 已核对许可 |
| --- | --- | --- | --- |
| `Google.Protobuf` | `3.36.2` | 官方 protobuf C# 运行时 | BSD-3-Clause |
| `Grpc.Tools` | `2.85.0`，`PrivateAssets="All"` | 构建时从 `.proto` 生成 C#，不是运行时网络服务 | Apache-2.0 |
| `ZstdSharp.Port` | `0.8.8` | Zstandard 的 C# 移植，解压本地字节/流 | MIT |

证据：本地 [L1] L5、L12–L19；现有恢复结果 [L7] L5、L18、L31 与这些版本一致，未重新 restore/build。发布和许可信息见 [N-PB][N-GRPC][N-ZSTD][P-LICENSE][Z-LICENSE]。

schema 的完整路径是：

`E:\Files\WorkSpace\GIAudioPatch\Protos\Sophon.proto`

项目用 `<Protobuf Include="Protos\Sophon.proto" GrpcServices="None" />` 生成消息类型，不生成 gRPC 服务。[L1] L18。

已存在的生成产物：

`E:\Files\WorkSpace\GIAudioPatch\obj\Debug\net10.0\Protos\Sophon.cs`

其中 `SophonManifestProto`、静态 `Parser`、`Assets` 都是 `public`，分别在 L66、L75、L117。**复用源 `.proto` 与生成配置，不复制 `obj` 构建产物**。[L8][N-GRPC]

### 1.3 当前入口与实际转换链

`Program.cs` L34–L55 只有 `get-list`、`get-changed`、`download-hdiff`、`download-pck`，通过 `NetworkModule` 得到结果后使用 `System.Text.Json` 输出；没有本地文件输入命令，也没有把原始 protobuf 全量转 JSON 的入口。[L3]

相关源码链为：

1. `NetworkModule.GetVersionContextAsync` L651–L657 选择外部元数据中 `matching_field == "game"` 的清单，取得解压后字节，再调用解析器。本研究不复用这一网络入口。[L5]
2. `NetworkModule.DownloadManifestAsync` L819–L852 在取得压缩字节后调用 `DecompressAsync`，对**解压后的 manifest 字节**计算 MD5 并与外部 `checksum` 比对。这是本地代码的明确行为，尚未用真实元数据验证其覆盖口径。[L5]
3. `DecompressAsync` L1162–L1194 对未压缩输入检查长度；对 Zstd 输入使用 `MemoryStream` + `ZstdSharp.DecompressionStream`，经 `ReadAtMostAsync` 限制输出长度，最后要求与声明的解压大小一致。[L5]
4. `SophonProtocol.ParseBuildManifest(byte[] data)` L63–L117 用 `SophonManifestProto.Parser.ParseFrom(data)` 得到消息，然后遍历 `Assets`。[L4]
5. 遍历时为全部 `AssetType == 0` 文件建立大小写不敏感的 `FileIndex`：键为完整 `AssetName`，值为 `RemoteFileIdentity(Size, Md5)`；音乐子集另放 `MusicFiles`。[L4] L37–L41、L75–L114。
6. 当前 JSON 的 `Files` 只由所选音乐文件构造，版本与 BuildId 从外置 `BuildData` 取得；**不是 protobuf 内置字段，也不是全文件列表**。[L5] L1395–L1417。

`ParseBuildManifest` 虽写成 `public static`，所在的 `SophonProtocol` 类是 `internal`，因此不是可供另一个程序集直接调用的公开库接口。[L4] L59、L63。

### 1.4 可以复用与不能直接照搬的部分

**可复用的最小基础：**

- 已有 `.proto` 的构建清单三种消息及生成方式；其字段编号、类型与上游 1.7.1 发布源码和当前主分支相同，命名与 C# namespace 不同不影响这些字段的二进制兼容性。[L2][S-PROTO][S-PUBPROTO][P-ENC]
- `ZstdSharp.Port 0.8.8` 与 `Google.Protobuf 3.36.2` 的成熟公开接口，以及“有界解压 → 解析 → 建立路径/大小/MD5 索引”的小段组织逻辑。[L4][L5][Z-STREAM][P-PARSER]
- 已有大小、重复路径、protobuf 异常、解压后长度校验思路；已有人工测试可以作为另写模块 0 测试的参考，不能称其本次已通过。[L4] L70–L96；[L5] L1184–L1191；[L6]。

**不宜直接复用整个项目或当前输出：**

- 不复用音乐筛选、版本查询、网络下载、补丁下载、PCK 组装及 CLI；没有必要为本地转换引入这些职责。[L3][L4] L99–L114。
- 本地 parser 只检查 MD5 非空，并不要求 32 个十六进制字符；没有全路径越界/Windows 名称检查；非零类型直接跳过，目录和未知类型无法在 `FileIndex` 中审计。[L4] L79–L96。
- chunk 校验只对音乐子集执行 `ToRemoteChunk`；更严格的连续布局校验 `ValidateFileChunks` 属于下载流程，不是全部文件解析时统一执行。[L4] L99–L106、L170–L186；[L5] L533、L1420–L1441。
- 本地 schema 中 patch 部分有删减：主消息保留字段 1，`reserved 2`；patch chunk `reserved 2, 3`。上游这些位置是 `UnusedAssets`、`VersionTag`、`BuildId`。不要把这份音乐用途 schema 称为完整 Sophon 补丁 schema，更不能拿 patch 输入充当目标版本完整列表。[L2] L25–L51；[S-PATCH]。

## 2. 原始格式、未压缩格式与消息区别

### 2.1 zstd + protobuf 的准确含义

对已核对的非加密构建清单，流程是：**本地压缩字节 → Zstd 解压后二进制 → `SophonManifestProto` → 结构化条目，必要时再转 JSON**。JSON 是派生表示，不是原始 manifest 格式。[L2][L5][S-PUBEXT]

压缩开关、清单大小与校验值不在这个 protobuf 中。上游 `SophonManifestInfo` 的 `IsUseCompression`、`ManifestSize`、`ManifestCompressedSize`、`ManifestChecksumMd5` 来自外置元数据，注释明确指向 `manifest_download.compression`、`manifest.checksum` 等字段。[S-MANINFO] L13–L18、L35–L51。

上游读取例程使用 `ParseFrom`，而非 `ParseDelimitedFrom`；没有证据需要在 protobuf 正文前自行剥离“长度头”或游戏私有封装。用户样本若有额外头部，只能先报未知格式，不能猜偏移强行解析。[S-PUBEXT] L491–L495；[P-PARSER] L297、L339。

### 2.2 Zstandard 的一般限制，不等同于游戏的实际样本规律

- 标准 Zstd 帧魔数 `0xFD2FB528` 为小端，字节序列 `28 B5 2F FD`。魔数只能支持“看起来是 Zstd”，不证明解压内容是正确游戏清单。[Z-FORMAT] L114–L139。
- Zstd 帧的原始内容大小是可选的，不能假定从帧头一定拿得到精确解压长度。[Z-FRAME] L168–L183、L299–L304。
- 标准支持多个拼接帧与 skippable frame；后者可携带任意、会被通用解码器忽略的元数据。但本研究**没有证据原神 manifest 使用它们**。不能把单次成功解压的首帧就认定为全部输入。[Z-FORMAT] L95–L105；[Z-SKIP] L985–L1024。
- 字典 ID 是可选的，ID 为零也不从格式上保证不需要字典。现有解析流程没有加载字典的分支；遇到需要外部字典的输入应报不支持，不自动获取或猜字典。[Z-FRAME] L277–L297；[L5] L1182–L1187。
- Zstd 可选帧校验和是解压数据 xxHash64 的低 32 位，**不是 MD5**，也不是整文件条目的校验值。[Z-FORMAT] L133–L139。

外置元数据模型还描述了加密标记，而已核对的读取例程只有压缩选择，没有解密过程。**不能据此宣称所有 Sophon manifest 永远不加密；模块 0 首版遇到加密输入应拒绝为不支持。** 不需要用户提供密码或其他凭据。[S-META] L97–L106；[S-PUBEXT] L465–L499。

### 2.3 不同 protobuf 消息不能混用

上游至少提供：

- `SophonManifestProto`：构建/资源包文件与 chunks，本文模块 0 的目标。[S-PROTO]
- `SophonPatchProto`：补丁文件、旧文件引用、按 `VersionTag` 关联的差分信息和未使用文件，不能替代目标版本全列表。[S-PATCH]

普通 protobuf 不携带字段名称、消息名称或本项目需要的格式身份；依赖字段编号与指定 schema。**“没有抛 protobuf 异常”不是识别正确消息类型的充分条件**，空字节也不能被当作合法空游戏清单。[P-ENC] L104–L111；[S-PROTO] L5–L8。

protobuf 规范还允许把同类型消息的字节拼接后按合并语义解析，repeated 条目被拼在一起。因此仅凭解析成功不能可靠区分“一份完整消息”与“拼接的多份清单”；验证完整输入、期望校验值及外部范围信息仍重要。[P-ENC] L380–L402。

## 3. 文件与 chunk 字段

以下是本地 `Protos/Sophon.proto` 与上游已公开 schema 的对应，不是猜测 SDK 属性。[L2] L5–L22；[S-PROTO] L5–L26。

### 3.1 文件/目录条目

主消息字段 1：`repeated SophonManifestAssetProperty assets`，上游名称为 `Assets`。

| 编号 | 本地 proto 字段 | 上游/C# 属性 | 类型 | 已确认的语义与限制 |
| --- | --- | --- | --- | --- |
| 1 | `asset_name` | `AssetName` | `string` | 路径名称；本地用它作全文件索引键，音乐路径体现为相对安装根目录的路径。schema 本身不保证相对性或安全性。 |
| 2 | `asset_chunks` | `AssetChunks` | repeated message | 文件 chunks；与文件列表所需的整文件身份不同。 |
| 3 | `asset_type` | `AssetType` | `int32` | 本地按 0 视为文件，非零跳过。上游按非零或空 MD5 判为目录；schema 未定义枚举及各非零值的含义。 |
| 4 | `asset_size` | `AssetSize` | `int64` | 整文件字节大小，不是压缩下载大小。 |
| 5 | `asset_hash_md5` | `AssetHashMd5` | `string` | 整文件 MD5；输出必须明确算法 `MD5`，不能只写模糊的 `hash`。 |

字段语义的代码证据：本地 [L4] L75–L114、[L5] L592–L610；上游 [S-ENUM] L138–L180。

**目录处理的安全区别：** 上游把 `AssetType == 0` 但哈希为空的记录也映射为 `IsDirectory == true`；本地却拒绝这种记录。两者都是消费者策略，不是 schema 声明的目录枚举。模块 0 不应照搬“缺哈希就当目录”的宽松规则掩盖文件损坏；非零未知值也不能未经真实样本验证便宣布都是普通目录。应保留原始类型用于诊断，未确认的类型不能进入可清理判断。[S-ENUM] L145–L154；[L4] L79–L87。

proto3 普通标量没有独立的 presence：未写入的 `asset_type`、`asset_size` 会读成 0，字符串会读成空值。不能要求原始消息必须显式存储值为 0 的 `asset_type`；也不能仅因大小为 0 就判缺字段。零字节文件与目录必须依据已验证规则区分。[P-PRESENCE]；[L6] L58–L79。

### 3.2 chunk 条目

| 编号 | 本地 proto 字段 | 上游/C# 属性 | 类型 | 语义 |
| --- | --- | --- | --- | --- |
| 1 | `chunk_name` | `ChunkName` | `string` | chunk 对象名称，不是整文件路径或整文件哈希。 |
| 2 | `chunk_decompressed_hash_md5` | `ChunkDecompressedHashMd5` | `string` | 这个 chunk 解压后的 MD5。 |
| 3 | `chunk_on_file_offset` | `ChunkOnFileOffset` | `int64` | 解压后内容在最终文件内的字节偏移。 |
| 4 | `chunk_size` | `ChunkSize` | `int64` | 存储/下载 chunk 字节长度；是否使用压缩由外部信息指定，不能只靠名称推断。 |
| 5 | `chunk_size_decompressed` | `ChunkSizeDecompressed` | `int64` | 解压后字节长度。 |

证据：[L2] L17–L22；[L4] L170–L186；[S-ENUM] L161–L171。

上游还存在从名称的 16 位十六进制前缀提取 `xxHash64` 的内部辅助函数，并在 manifest 缓存校验中使用。它是**特定名称约定**，不是 protobuf 的 `AssetHashMd5`，不能把名称长度当作通用算法识别机制。[S-NAMEHASH] L338–L379；[S-CACHE] L493–L552。

本地清单输出的最小文件身份应能无歧义表达相对路径、字节大小、校验算法与校验值；是否保留 chunks、目录及原始 protobuf JSON，由父会话决定。不要将提取全列表与重新下载/组装文件绑定。

## 4. 成熟解析器与离线复用路径

### 4.1 已确认的公开离线接口

**首选基础，不需要整个 Sophon 下载库：**

- `ZstdSharp.DecompressionStream(Stream stream, int bufferSize = 0, bool checkEndOfStream = true, bool leaveOpen = true)`：公开构造器，可包裹本地文件流，默认检查未完成的压缩帧；另有接受 `Decompressor` 的公开重载。[Z-STREAM] L23–L55、L133–L141。
- `ZstdSharp.Decompressor.Unwrap(ReadOnlySpan<byte> src, int maxDecompressedSize = int.MaxValue)`：公开一次性解压接口，会在分配目标缓冲前检查推导出的解压大小上界。默认 `int.MaxValue` 不适合直接作为模块安全预算；帧未包含精确大小时有界流式路线更贴近现有代码。[Z-UNWRAP] L43–L67。
- `GIAudioPatch.Protos.SophonManifestProto.Parser`：由现成 schema 生成的公开解析器；公开运行时 `MessageParser<T>.ParseFrom(byte[])`、`ParseFrom(Stream)`、`ParseFrom(CodedInputStream)` 确实存在。[L8] L75；[P-PARSER] L259、L297、L351。
- 若需要 protobuf 原生 JSON，公开 `Google.Protobuf.JsonFormatter.Default.Format(IMessage)` 可用，不必自行遍历字段拼 JSON。[P-FORMATTER] L52、L112。

上述调用都可只使用本地输入，不创建 `HttpClient`，不调用 `NetworkModule`，也不下载游戏资源。仍需模块自己执行输入预算、类型/字段和路径验证；这是业务安全边界，不是重写压缩或 protobuf。

### 4.2 Sophon 专用库的真实名称、版本与公开范围

GitHub 搜索与 Collapse 自身 `.gitmodules` 确认，维护者相关仓库/包是 **`CollapseLauncher/Hi3Helper.Sophon` / NuGet `Hi3Helper.Sophon`**；不能未经确认使用 `CollapseLauncher/Sophon.Stream` 这个猜测地址。[S-MODULE][S-README]

截至研究日：

- NuGet 版本索引最高为 **1.7.1**，包页最后更新为 2025-06-06。包 `nuspec` 声明 MIT，并将来源固定到 commit `45d44b0eeed8e6c1edbb6d49317ad1fc036b1c8f`。[N-SOPHON][N-SOPHON-INDEX][N-SOPHON-PAGE]
- GitHub 当前 `main` 核对到 commit **`4c6001e8585ffa2fda78805db0ab960e3245c049`**，主项目文件声明版本 **1.8.1**、`net10.0`；Universal 项目声明 **1.8.0**，TFM 为 `netstandard2.0;net6.0;net8.0;net10.0`。这不等于 NuGet 已发布 1.8.1，README 的 TFM 列表也不能代替项目文件和包元数据。[S-HEAD][S-PROJ] L3–L18；[S-UPROJ] L3–L19。
- 1.7.1 包的 net6/net8/net9 依赖下限为 `Google.Protobuf 3.30.0`、`Hi3Helper.ZstdNet 1.6.4`、`SharpHDiffPatch.Core 2.3.3`、`System.IO.Hashing 9.0.5`。netstandard2.0 另有 AsyncInterfaces、Buffers、Http.Json、Text.Json、Tasks/Dataflow。不要混用当前主分支的 `SharpHPatchZ` 依赖解释旧发布包。[N-SOPHON] 的 `<dependencies>`。
- 当前主项目源码依赖为 `Google.Protobuf/Google.Protobuf.Tools 3.36.2`、`Hi3Helper.ZstdNet 1.7.1`、`SharpHPatchZ 3.1.1`、`System.IO.Hashing 10.0.14`；这些不是本地 GIAudioPatch 依赖。[S-PROJ] L47–L62。

1.7.1 发布 commit 与当前源码都公开 `Hi3Helper.Sophon.Protos.SophonManifestProto.Parser` 和 `Assets`。因此可以用这个现成消息解析器解析**已解压的本地字节**，再提取完整条目，不需要调用下载接口。[S-PUBGEN] L51–L60、L98–L104；[S-GEN]。

但是，高层公开接口 `SophonManifest.EnumerateAsync(HttpClient, SophonChunkManifestInfoPair, ...)` / `EnumerateAsync(HttpClient, SophonManifestInfo, SophonChunksInfo, ...)` **不是直接接受任意本地文件或流的离线 API**。[S-PUBENUM] L55–L59、L102–L107；[S-ENUM] L56–L108。

- 1.7.1 的 `ReadProtoFromManifestInfo<T>` 是 `internal`，会发 HTTP 请求；不能当公开 `ParseLocalManifest(...)` 使用。[S-PUBEXT] L465–L495。
- 当前主分支增加了固定位置的本地缓存读取，但缓存无效后仍走网络，而且会创建缓存目录、写入缓存文件；缓存 helper 仍为 `internal`。这不满足本模块“调用者传入本地文件，不自动联网”的契约。[S-CACHE] L487–L508、L593–L648。
- 两个版本的 `AssetProperty2SophonAsset` 也是 `internal`；不要凭想象调用它或用反射依赖私有实现。[S-PUBENUM] L139；[S-ENUM] L138。

**研究建议：** 已有 `.proto` + 通用成熟解析器已经能完成所需字段提取。仅为了调用一个本地 parser 而引入整个 Sophon 下载/差分依赖链不划算；schema 可取维护者完整公开版本作为有许可的格式来源，本地 codegen/解压组合作为复用依据。最终是否引入专用包仍由父会话决定。

### 4.3 .NET 与 Python 的可行性、维护负担

**.NET 路线**已有本地 C# schema、依赖声明、生成配置与最小解析组织，复用量最大；ZstdSharp 是 C# 移植，不要求用户另外安装 `libzstd`。这不代表本次已在用户环境运行成功，研究未构建或执行。[L1][L4][Z-README] L6–L8。

**Python 路线**技术上可行，不需要自行逆向 proto：

- 用已存在的上游 `.proto` 经官方 `protoc --python_out` 生成 `_pb2.py`，然后用生成消息的 `ParseFromString(bytes)`；这是官方通用流程。[P-PYTHON]
- 使用成熟 `zstandard` 绑定解压，而不是自写 Zstd；Context7 明确收录 0.25.0，PyPI 0.25.0 声明 Python >=3.9、BSD-3-Clause。[PY-ZSTD][PY-LICENSE]
- 官方 Python `protobuf` 发布元数据本次显示 7.36.2、Python >=3.10；版本号不是 C# NuGet 的 3.36.2。生成器与 Python runtime 的兼容需要按所选版本核实，不能直接把 C# 包版本填到 Python requirements。[PY-PB][P-LICENSE]
- Python `ZstdDecompressor.decompress` 默认 `allow_extra_data=True`，会忽略首个完整帧后的多余输入；0.25.0 CFFI 实现对帧内已声明大小会按该大小分配，不把 `max_output_size` 当该分支的全局硬上限。因此要单独限制帧声明大小/窗口、实际总输出与全部输入处理，不能“加一个 max_output_size 参数就算安全”。[PY-IMPL] L3731–L3773、L3829–L3843、L3867–L3873。

本研究未确认成熟的专用 Python Sophon 离线 SDK，也未发现兄弟项目有 Python 转换入口；这**不是断言不存在**。Python 会额外维护生成代码、编译器/runtime 版本配套、压缩绑定与打包环境；对目前已有的 Windows/.NET 基础，仅为这一步重新建一条语言链不值得。若整个项目最终选 Python，复用维护者 schema + 官方 runtime 仍比自行逆向字段低负担，不需要再经过 .NET JSON 中转。

### 4.4 JSON 不是额外的格式识别证据

官方 ProtoJSON 默认使用 lowerCamelCase 名称、将 int64 写成十进制字符串，且可能省略 proto3 默认值；它不保存未知 protobuf 字段。本地 `System.Text.Json` 序列化的业务 record 又是另一种派生结构，不能仅根据一份 JSON 中字段是否出现判断原始字段缺失或大小为零。[P-JSON][L3] L8–L11、L55。

验收应比较**路径集合、精确整数大小与显式算法/校验值**，而不是 JSON 缩进或文本逐字相同。是否交付原生 protobuf JSON、规范化文件清单、逐份 JSON 文件等尚未获得用户决定，本笔记不代替该决定。

## 5. manifest 自身能识别什么、不能识别什么

构建消息顶层只有 `Assets`，下层只有上述文件与 chunk 字段；没有内置格式版本、游戏 ID、目标游戏版本、语言、渠道、manifest ID、文件总数或所有资源包齐备声明。[S-PROTO]

外置元数据模型包含：

- `build_id`、`tag`，位于构建数据层；不是 `SophonManifestProto` 字段。[S-META] L70–L74；本地 [L5] L1641–L1647。
- `matching_field`、`category_id`、`category_name`，位于 manifest identity；不能凭一个固定名字推导所有语言包。[S-IDENT] L10–L18。
- `manifest.id/checksum/compressed_size/uncompressed_size`、下载压缩/加密标记，以及 `stats.file_count/chunk_count`；这是可供本地输入一并携带的外部描述，不要求研究其自动获取方式。[S-META] L79–L121。

所以：

- 路径中出现 `YuanShen_Data` 可以作人工线索，不能证明游戏来源或版本。
- 有 `AssetHashMd5` 可以记录文件身份，**没有文件正文时不能实测整文件 MD5 是否正确**。
- 有外部期望 checksum/大小可以校验输入是否与给定描述一致，不能仅凭该校验值证明描述来自官方、全部语言包齐备或整套版本清单覆盖完整。
- 本地 GIAudioPatch 只选 `matching_field == "game"`；不能从它的行为推出语音包都在这个 manifest 中，更不能用音乐列表证明安装完整性。[L5] L651–L657。
- 外部信息缺失时应明确写“游戏/版本/语言/覆盖范围未确认”，可以用于格式诊断，但不得宣称足以支撑清理。

## 6. 模块 0 的异常输入与安全边界建议

以下是给父会话的安全建议，不是已实现的保证；通用协议失败和 Windows 路径语义依据见所附一手资料。

### 6.1 输入、解压与 protobuf

- 拒绝不存在、非普通文件、空文件、明确为 JSON/patch/其他格式却当作构建 manifest 的输入；压缩方式不支持时明确报错。选定 Zstd 后解压失败，**不退回“当未压缩 protobuf 再试”**，否则可能把损坏输入解释成另一种消息。[L5] L1168–L1179；[P-ENC]
- 分别限定压缩输入大小、解压总大小、解码窗口内存、条目/chunk 数及名称长度，避免内存/CPU 消耗异常。兄弟项目的 64 MiB 压缩与 128 MiB 解压限制是应用自定安全预算，**不是原神协议保证**；真实样本是否都在该预算内仍未知。[L5] L315–L316、L823–L828；[Z-FRAME] L185–L203。
- 保留 Zstd 未完成帧检查，并读到输入真正结束；不接受截断、无效帧、校验失败、解压输出超限、外部声明长度或 checksum 不一致。拼接帧、skippable frame、magicless 或外部字典输入的支持策略需明确，未验收的变体报“不支持”，不要仅因通用 Zstd 允许它便自动扩大支持范围。[Z-STREAM] L133–L141；[Z-FORMAT][Z-SKIP][L5] L1188–L1217。
- 使用生成 parser，不自写字段解码；捕获 protobuf 损坏异常。需要更小解析预算时公开 `CodedInputStream.CreateWithLimits(Stream, int sizeLimit, int recursionLimit)` 存在；C# 默认大小上限为 `Int32.MaxValue`、递归 100，大小上限只针对底层 stream，不针对已提供的 byte[]，因此 byte[] 路线仍必须提前限制。[P-LIMITS] L57–L58、L152–L203。
- 正确处理 proto3 默认值；对空 `Assets`、全部未知字段/类型、明显错误消息结构，不把语法成功当业务成功。新增未知字段可以是未来合法扩展，不能一概称损坏；但不理解它们时要记录兼容性/完整性未确认，不经 JSON 静默丢弃后给出强完整性结论。[P-PRESENCE][P-ENC][P-JSON]

### 6.2 条目、大小与哈希

- 对文件拒绝空/空白路径、负大小、空 MD5、非 32 位十六进制 MD5；输出显式算法 `MD5`。不能把空哈希的文件偷偷当目录，不能只凭哈希字符串长度把其解释为另一种算法。[L2][L4] L84–L96；[S-ENUM] L145–L154。
- 不因为 proto3 省略 `asset_type == 0` 而拒绝合法文件；零字节文件的有效哈希与 chunk 规则需真实样本覆盖，不能凭空添加“文件大小必须大于零”的要求。[P-PRESENCE]
- 检查重复路径（包括 Windows 大小写不敏感和统一分隔符后的冲突）、同路径文件/目录冲突、文件被当成其他条目的父目录；遇到冲突整体报告失败，不静默最后一条覆盖前一条。已有本地索引只覆盖大小写冲突的一部分。[L4] L75–L96；[W-NAME] “Naming Conventions”。
- 若保存/验证 chunks，拒绝负偏移、负大小、整数溢出、超出文件范围及冲突布局；对非零文件验证有数据的 chunk 布局不能遗漏或重叠。本地已有连续布局与 checked 累加可参考，但完整合法规则、零文件是否无 chunks、总计与外部 stats 的口径仍需样本确认。提取路径/大小/整文件哈希本身不需要下载 chunk 或合成文件哈希。[L5] L1420–L1441；[L2]。
- 目录不应伪造文件大小、整文件校验或当作“可移动文件”；未知 `AssetType` 的确切含义没有 schema 枚举证明，保留用于诊断，阻止依赖该输入的强覆盖/清理结论。[S-PROTO][S-ENUM]

### 6.3 Windows 路径必须防越界

模块 0 只输出列表也应拒绝危险路径，避免后续扫描/移动把它们当真实目标：

- 拒绝绝对路径、单斜杠根路径、盘符路径和盘符相对路径（如 `C:foo`）、UNC、`\\?\` / `\\.\` 设备路径。
- 统一识别 `/` 与 `\` 分隔符后拒绝 `.`、`..`、空段、NUL/控制字符、Windows 非法字符、包含 `:` 的段（含备用数据流）、保留设备名（含 `NUL.txt`）、末尾空格或点。不要用“修正成一个看起来安全的名字”掩盖原始冲突。
- 如后续需要拼安装根目录，按显式 basePath 解析后检查位于根目录内部；比较根路径必须带目录分隔符边界，不能只作 `StartsWith("E:\\Game")` 而误接受 `E:\\GameOther`。仅调用 `Path.Combine` 或 `GetFullPath` 不自动建立安全边界。
- 字符串路径位于根目录内仍不代表真实访问不会经 junction/symlink/reparse point 指向外部。模块 0 不需访问安装目录；后续文件操作模块另行处理重解析点与实际访问边界，不能把词法检查说成物理防越界保证。

依据：[W-PATH] “Path normalization / Identify the path / Apply the current directory”；[W-NAME] “Naming Conventions / Namespaces”；[W-REPARSE]。

### 6.4 错误不能产生可用于清理的半成品

转换失败不发布“成功的完整清单”；纯诊断结果应与可用于比对的结果区别标识。不联网补缺失元数据、不访问游戏目录、不改动输入，也不为了校验哈希下载游戏文件。是否及如何落 JSON、覆盖/合并策略由用户决定，模块 0 验收不得顺势推进扫描或清理模块。

## 7. 样本状态与必须由用户补充的验收材料

### 7.1 已发现的本地文件，不是游戏样本

**真实 manifest 路径：未发现。对应真实 JSON 路径：未发现。**

只读列目录、文件名/长度与源码引用检查范围是兄弟项目根目录及 `Protos`、`tests`、`bin`、`obj`；未读取游戏目录、用户下载目录、父目录其他项目或 `refs/` 中的文件内容。实际候选 `.json` 均为 `deps.json`、`runtimeconfig.json`、NuGet dgspec/project.assets，不能作为游戏清单样本。

可供主会话定位现成逻辑/测试的完整路径：

- `E:\Files\WorkSpace\GIAudioPatch\Protos\Sophon.proto`：schema，不是清单数据。
- `E:\Files\WorkSpace\GIAudioPatch\SophonProtocol.cs`：现成解压后字节 parser 与全文件索引。
- `E:\Files\WorkSpace\GIAudioPatch\NetworkModule.cs`：Zstd 有界解压与外部大小/校验逻辑，仅参考有关函数。
- `E:\Files\WorkSpace\GIAudioPatch\tests\SophonProtocolTests.cs`：人工 protobuf 测试；L58–L79 覆盖音乐子集与全索引区分，L362–L388 使用非真实 MD5。没有据此声明真实原神 manifest 已验收。
- `E:\Files\WorkSpace\GIAudioPatch\obj\Debug\net10.0\Protos\Sophon.cs`：已生成消息类，不是验收输出，也不是应该复制维护的源文件。

### 7.2 父会话需一次性向用户收齐

1. **至少一份本地原始构建 manifest 的完整路径**，说明是原始压缩件还是解压后的 protobuf；不是 `get-list` 音乐 JSON，不是 patch，不需要游戏资源正文。
2. 若已经有原流程转换成的 JSON，提供其完整路径及与哪份原始清单对应；父会话再选作验收基准。不能编造一份示例 JSON 代替真实结果。
3. 对应的非敏感外部描述：游戏（原神）、PC、国服/国际服/渠道、目标版本、主资源/具体语言包、manifest ID、压缩方式、压缩/解压大小、checksum 和其算法/覆盖字节。缺少某项就标未确认；不要索取凭据、口令或带敏感参数的完整请求 URL。
4. 如要求判断版本清单覆盖完整，必须说明安装所选资源/语言包及应包含哪几份 manifest。单份主包清单和一次成功转换都无法证明覆盖所有安装文件。
5. 验收预期至少包括路径集合/文件数、精确字节大小与整文件 MD5，以及文件/目录/零字节文件样例；有现成完整 JSON 时比较结构化内容，不要求格式字节相同。

真实样本用于证明可解压/可解析和字段、类别口径；损坏、重复路径、越界、错误类型、异常大小等测试后续可用人工数据制作，不需要用户提供恶意输入。**本次没有执行解析、构建或测试，没有下载二进制清单/资源，所有运行结果仍待授权的模块 0 实验。**

## 8. 资料与检索记录

先通过 Context7 `resolve-library-id` 查 `Sophon.Stream`、`Sophon`、`Collapse Launcher`，并在发现真实名称后补查 `Hi3Helper.Sophon`。对应 Sophon manifest 库未收录；返回的 MCP 上下文压缩 Sophon/深度学习 SOPHONSDK 与游戏格式无关，未使用。Collapse 的准确索引 `/collapselauncher/collapse` 可用，`query-docs` 主要返回其依赖名单，所以关键格式与接口回溯维护者源码。

另经 Context7 核对 `/oleg-st/zstdsharp`、`/protocolbuffers/protobuf/v36.2`、`/protocolbuffers/protocolbuffers.github.io`、`/indygreg/python-zstandard/0.25.0`、`/meta/zstd`、`/dotnet/docs`。部分 C# protobuf 查询只返回 Python 文档，因此公开 C# API、访问级别和默认 limits 以 v36.2 精确源码为准。没有用索引摘要替代未确认字段。

通用网页搜索服务不可用（HTTP 402）；GitHub REST 标签查询曾遇到未认证配额限制。改用公开仓库页面、raw 源码和 NuGet 文本元数据完成核对，未使用令牌。没有下载包二进制、安装依赖或执行第三方程序。

### 本地一手证据

- **L0**：`E:\Files\WorkSpace\GIAudioPatch\AGENTS.md`，L3、L13、L18–L28；及按其要求读取的 `GLOSSARY.md`。
- **L1**：`E:\Files\WorkSpace\GIAudioPatch\GIAudioPatch.csproj`，L5、L12–L19。
- **L2**：`E:\Files\WorkSpace\GIAudioPatch\Protos\Sophon.proto`，L1–L52。
- **L3**：`E:\Files\WorkSpace\GIAudioPatch\Program.cs`，L34–L55、L93–L103。
- **L4**：`E:\Files\WorkSpace\GIAudioPatch\SophonProtocol.cs`，L37–L41、L59–L117、L170–L213。
- **L5**：`E:\Files\WorkSpace\GIAudioPatch\NetworkModule.cs`，本文各处标明精确行范围。
- **L6**：`E:\Files\WorkSpace\GIAudioPatch\tests\SophonProtocolTests.cs`，L58–L79、L362–L388。
- **L7**：`E:\Files\WorkSpace\GIAudioPatch\obj\project.assets.json`，仅检索相关依赖名称与版本，L5、L18、L31。
- **L8**：`E:\Files\WorkSpace\GIAudioPatch\obj\Debug\net10.0\Protos\Sophon.cs`，L66、L75、L117、L306、L329、L341、L353。

[L0]: ../../../GIAudioPatch/AGENTS.md
[L1]: ../../../GIAudioPatch/GIAudioPatch.csproj
[L2]: ../../../GIAudioPatch/Protos/Sophon.proto
[L3]: ../../../GIAudioPatch/Program.cs
[L4]: ../../../GIAudioPatch/SophonProtocol.cs
[L5]: ../../../GIAudioPatch/NetworkModule.cs
[L6]: ../../../GIAudioPatch/tests/SophonProtocolTests.cs
[L7]: ../../../GIAudioPatch/obj/project.assets.json
[L8]: ../../../GIAudioPatch/obj/Debug/net10.0/Protos/Sophon.cs

### 上游源码、发布与官方规范

以下为本文使用的准确 URL；GitHub Sophon 核心证据固定 commit，公开发布包则固定其 `nuspec` 给出的来源 commit。

[S-HEAD]: https://api.github.com/repos/CollapseLauncher/Hi3Helper.Sophon/branches/main
[S-MODULE]: https://github.com/CollapseLauncher/Collapse/blob/main/.gitmodules#L16-L18
[S-README]: https://github.com/CollapseLauncher/Hi3Helper.Sophon/blob/4c6001e8585ffa2fda78805db0ab960e3245c049/README.md#L5-L11
[S-PROTO]: https://github.com/CollapseLauncher/Hi3Helper.Sophon/blob/4c6001e8585ffa2fda78805db0ab960e3245c049/Protos/SophonManifestProto.proto#L1-L26
[S-PATCH]: https://github.com/CollapseLauncher/Hi3Helper.Sophon/blob/4c6001e8585ffa2fda78805db0ab960e3245c049/Protos/SophonPatchProto.proto#L5-L55
[S-GEN]: https://github.com/CollapseLauncher/Hi3Helper.Sophon/blob/4c6001e8585ffa2fda78805db0ab960e3245c049/Protos/SophonManifestProto.cs#L51-L104
[S-ENUM]: https://github.com/CollapseLauncher/Hi3Helper.Sophon/blob/4c6001e8585ffa2fda78805db0ab960e3245c049/SophonManifest.cs#L56-L189
[S-MANINFO]: https://github.com/CollapseLauncher/Hi3Helper.Sophon/blob/4c6001e8585ffa2fda78805db0ab960e3245c049/Structs/SophonManifestInfo.cs#L11-L71
[S-META]: https://github.com/CollapseLauncher/Hi3Helper.Sophon/blob/4c6001e8585ffa2fda78805db0ab960e3245c049/Structs/SophonInfosJson.cs#L70-L121
[S-IDENT]: https://github.com/CollapseLauncher/Hi3Helper.Sophon/blob/4c6001e8585ffa2fda78805db0ab960e3245c049/Structs/SophonIdentifiableProperty.cs#L10-L18
[S-CACHE]: https://github.com/CollapseLauncher/Hi3Helper.Sophon/blob/4c6001e8585ffa2fda78805db0ab960e3245c049/Helper/Extension.cs#L487-L660
[S-NAMEHASH]: https://github.com/CollapseLauncher/Hi3Helper.Sophon/blob/4c6001e8585ffa2fda78805db0ab960e3245c049/Helper/Extension.cs#L338-L379
[S-PROJ]: https://github.com/CollapseLauncher/Hi3Helper.Sophon/blob/4c6001e8585ffa2fda78805db0ab960e3245c049/Hi3Helper.Sophon.csproj#L3-L62
[S-UPROJ]: https://github.com/CollapseLauncher/Hi3Helper.Sophon/blob/4c6001e8585ffa2fda78805db0ab960e3245c049/Hi3Helper.Sophon.Universal.csproj#L3-L78
[S-LICENSE]: https://github.com/CollapseLauncher/Hi3Helper.Sophon/blob/4c6001e8585ffa2fda78805db0ab960e3245c049/LICENSE#L1-L13
[S-PUBPROTO]: https://github.com/CollapseLauncher/Hi3Helper.Sophon/blob/45d44b0eeed8e6c1edbb6d49317ad1fc036b1c8f/Protos/SophonManifestProto.proto#L1-L26
[S-PUBGEN]: https://github.com/CollapseLauncher/Hi3Helper.Sophon/blob/45d44b0eeed8e6c1edbb6d49317ad1fc036b1c8f/Protos/SophonManifestProto.cs#L51-L104
[S-PUBENUM]: https://github.com/CollapseLauncher/Hi3Helper.Sophon/blob/45d44b0eeed8e6c1edbb6d49317ad1fc036b1c8f/SophonManifest.cs#L55-L181
[S-PUBEXT]: https://github.com/CollapseLauncher/Hi3Helper.Sophon/blob/45d44b0eeed8e6c1edbb6d49317ad1fc036b1c8f/Helper/Extension.cs#L465-L499
[N-SOPHON]: https://api.nuget.org/v3-flatcontainer/hi3helper.sophon/1.7.1/hi3helper.sophon.nuspec
[N-SOPHON-INDEX]: https://api.nuget.org/v3-flatcontainer/hi3helper.sophon/index.json
[N-SOPHON-PAGE]: https://www.nuget.org/packages/Hi3Helper.Sophon/1.7.1
[N-PB]: https://www.nuget.org/packages/Google.Protobuf/3.36.2
[N-GRPC]: https://www.nuget.org/packages/Grpc.Tools/2.85.0
[N-ZSTD]: https://www.nuget.org/packages/ZstdSharp.Port/0.8.8
[Z-README]: https://github.com/oleg-st/ZstdSharp/blob/0.8.8/README.md#L6-L42
[Z-STREAM]: https://github.com/oleg-st/ZstdSharp/blob/0.8.8/src/ZstdSharp/DecompressionStream.cs#L23-L141
[Z-UNWRAP]: https://github.com/oleg-st/ZstdSharp/blob/0.8.8/src/ZstdSharp/Decompressor.cs#L43-L67
[Z-LICENSE]: https://github.com/oleg-st/ZstdSharp/blob/0.8.8/LICENSE#L1-L13
[P-PARSER]: https://github.com/protocolbuffers/protobuf/blob/v36.2/csharp/src/Google.Protobuf/MessageParser.cs#L254-L355
[P-LIMITS]: https://github.com/protocolbuffers/protobuf/blob/v36.2/csharp/src/Google.Protobuf/CodedInputStream.cs#L57-L215
[P-FORMATTER]: https://github.com/protocolbuffers/protobuf/blob/v36.2/csharp/src/Google.Protobuf/JsonFormatter.cs#L49-L138
[P-PRESENCE]: https://protobuf.dev/programming-guides/field_presence/
[P-JSON]: https://protobuf.dev/programming-guides/json/
[P-ENC]: https://github.com/protocolbuffers/protocolbuffers.github.io/blob/main/content/programming-guides/encoding.md#L104-L402
[P-PYTHON]: https://protobuf.dev/getting-started/pythontutorial/
[P-LICENSE]: https://github.com/protocolbuffers/protobuf/blob/v36.2/LICENSE#L1-L32
[PY-ZSTD]: https://pypi.org/pypi/zstandard/0.25.0/json
[PY-LICENSE]: https://github.com/indygreg/python-zstandard/blob/0.25.0/LICENSE#L1-L24
[PY-PB]: https://pypi.org/pypi/protobuf/json
[PY-IMPL]: https://github.com/indygreg/python-zstandard/blob/0.25.0/zstandard/backend_cffi.py#L3731-L3873
[Z-FORMAT]: https://github.com/meta/zstd/blob/v1.5.7/doc/zstd_compression_format.md#L95-L139
[Z-FRAME]: https://github.com/meta/zstd/blob/v1.5.7/doc/zstd_compression_format.md#L168-L304
[Z-SKIP]: https://github.com/meta/zstd/blob/v1.5.7/doc/zstd_compression_format.md#L985-L1024
[W-PATH]: https://learn.microsoft.com/en-us/dotnet/standard/io/file-path-formats
[W-NAME]: https://learn.microsoft.com/en-us/windows/win32/fileio/naming-a-file
[W-REPARSE]: https://learn.microsoft.com/en-us/windows/win32/fileio/reparse-points
