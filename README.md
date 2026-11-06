# HoyoFileClean

对照游戏版本清单检查本地文件，并将多余文件移走而非删除。目前只实现模块 0：把本地 Sophon 构建 manifest 转成 JSON 文件列表。

扫描、比对、移动、界面与自动获取尚未实现。模块 0 不联网、不访问游戏目录，也不修改输入文件。

## 运行

需要 .NET 10 SDK。在项目根目录执行：

```powershell
dotnet build HoyoFileClean.csproj
```

转换 zstd 压缩的 manifest：

```powershell
dotnet run --no-build -- convert "manifest路径" "输出路径.json"
```

转换已经解压的 protobuf：

```powershell
dotnet run --no-build -- convert "解压后的文件路径" "输出路径.json" --uncompressed
```

输出的父目录必须存在；已有输出文件会被拒绝，不覆盖。每份 manifest 独立转换，不合并。转换成功返回退出码 `0`，转换错误返回 `1`，命令参数错误返回 `2`。

结果先写入同目录的唯一 `.partial` 文件，写入并关闭成功后才改名为目标 JSON。写入、关闭或发布失败可能保留 `.partial`，但不会把它作为成功结果发布；已有目标不被覆盖。

## 输出格式

```json
{
  "hashAlgorithm": "MD5",
  "fileCount": 1,
  "totalSize": 3,
  "files": [
    {
      "path": "Data/example.bin",
      "size": 3,
      "hash": "900150983cd24fb0d6963f7d28e17f72"
    }
  ]
}
```

- `path` 是文件相对路径，分隔符统一为 `/`，保留路径大小写。
- `size` 和 `totalSize` 的单位是字节。
- `hash` 是清单记录的整文件 MD5，统一为小写，不是分块哈希。
- 只输出 `asset_type == 0` 的文件条目，不导出分块数据。

空文件列表、损坏数据、无效文件大小或 MD5、危险相对路径、Windows 保留设备名、重复路径、文件与父目录路径冲突及总大小溢出会在创建输出前被拒绝。路径冲突按 Windows 的大小写不敏感规则判断。

本工具限制压缩输入最多 64 MiB、解压后最多 128 MiB；这是工具的资源保护上限，不是游戏协议的限制。

## 验证

运行自动测试：

```powershell
dotnet run --project tests\HoyoFileClean.Tests.csproj
```

模块 0 已验证：

- 18 个自动测试全部通过，包含目录过滤、丢弃分块数据并使用整文件 MD5、两种输入、损坏和空清单、字段校验、危险及重复路径、设备名、文件与父目录冲突、拒绝覆盖、长目标文件名、解压大小上限及总大小溢出的 CLI 退出码。
- 另用临时故障注入构建模拟写入与关闭失败，两种情况都未生成最终 JSON；这不是实际磁盘满故障测试。
- 使用 `refs/` 中用户提供的原神压缩 manifest 和解压后的 protobuf，均得到 2,831 个文件，总大小为 `135,765,032,023` 字节。
- 全部条目的路径、大小、MD5 与同源 `main.json` 一致；两种输入产生的 JSON 字节一致。

这证明该样本的转换结果一致，不证明一份 manifest 覆盖整个游戏，也不证明本地游戏文件完整。清单中的版本、来源和覆盖范围需要另外确认。

`.proto` 的来源及许可见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。`refs/` 和构建输出均不纳入 Git。
