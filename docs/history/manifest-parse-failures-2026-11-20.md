# manifest 缓存五个解析失败的诊断

## 范围与复现

- 日期：2026-11-20。
- 分支：`fix/manifest-5-fails`。
- 只读输入：`C:\Users\yob14\AppData\Roaming\miHoYo\HYP\1_1\modules\sophon\manifest`。
- 当前程序构建：`dotnet build HoyoFileClean.csproj --nologo`，0 个警告、0 个错误。
- 使用构建后的 `HoyoFileClean.Core.dll`，逐个调用正式入口 `ManifestConverter.Read(path, CancellationToken.None)`，递归枚举目录全部普通文件；不导出 JSON。
- 实际结果：`TOTAL=1750 SUCCESS=1745 FAILED=5`。
- 正文诊断只涉及以下五个失败文件，没有展开成功样本正文。

## 四个有效补丁清单被误判为 JSON

失败文件：

1. `92a671221f04dcf7dc587ceacd48c00f`（1,402 字节）。
2. `manifest_310936b6307ba796_92a671221f04dcf7dc587ceacd48c00f`（539 字节，压缩）。
3. `bbd6a9919997fad7f75cfa137bbefb21`（240 字节）。
4. `manifest_3c1c9ce2a9ce0360_bbd6a9919997fad7f75cfa137bbefb21`（189 字节，压缩）。

正式入口均报 `ListJsonInvalid`，内层异常为：

```text
'R' is an invalid start of a property name. Expected a '"'. Path: $ | LineNumber: 2 | BytePositionInLine: 0.
```

四份解码内容都以 `0A 7B 0A 52 5A 65 6E 6C` 开头。按 `Protos/Sophon.proto`，首个 `0A` 是外层 assets 字段的标记，`7B` 是首条嵌套消息的长度 123，随后 `0A 52` 是文件名字段标记及长度 82。

`core/ManifestConverter.cs` 的 `ReadContent` 在第 117 行起把 `0A` 当作文本空白跳过，继而在第 129–140 行把 `7B`（字符 `{`）判成 JSON，并转入 `FileComparer.ReadInputs`。二进制字节与文本空白、左花括号重合，导致格式误判，并非四份输入损坏。

验证：在内存中解压压缩版，通过反射绕过 `ReadContent`，调用原有私有 `ReadProtobuf`，保留现有补丁识别、文件元数据、路径、集合及总大小校验；未修改程序或输入。四份均成功，来源类型均为 `sophon-patch`：

- 前两份解码后均为 1,402 字节，MD5 均为 `92a671221f04dcf7dc587ceacd48c00f`；12 条文件记录，总大小 369,885,280 字节。
- 后两份解码后均为 240 字节，MD5 均为 `bbd6a9919997fad7f75cfa137bbefb21`；2 条文件记录，总大小 40,992,224 字节。

因此四个失败文件实际上是两份补丁清单各自的压缩／未压缩副本。

## 一个不完整的压缩流

失败文件：`manifest_92139b870b4fe738_a0c81c2081a2b658e8d4d96342f4ac8f_tmp`（409,438 字节）。

正式入口报 `ManifestCompressionInvalid`，内层异常：

```text
System.IO.EndOfStreamException: Premature end of stream
```

单独使用现有 `ZstdSharp.DecompressionStream` 将解码内容丢弃到 `Stream.Null`，同样报上述异常，输入位置已达 `409438/409438`。确认该文件当前的压缩流不完整；`_tmp` 后缀也符合临时文件特征，但无法仅凭此判断是下载尚未完成、写入中断或其他原因。

## 结论与边界

- 四个失败源于程序格式识别缺陷；另一个源于输入压缩流不完整。
- 本轮只诊断，没有修改代码或样本，也没有实施修复或声称回归通过。
- 后续若修复，应覆盖该二进制前缀及压缩／未压缩输入，并继续拒绝不完整压缩流；真实目录内容可能变化，以上计数仅代表本轮读取状态。
- 诊断在内存中完成，没有新增人工临时脚本、导出 JSON 或复制样本。
