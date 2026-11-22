# 命令行说明

命令在项目根目录执行，需要 .NET 10 SDK。可先运行 `dotnet build HoyoFileClean.csproj`，此后在未修改代码时使用 `dotnet run --no-build -- ...`；首次构建会还原声明的依赖。

标准输出和标准错误使用无 BOM 的 UTF-8，不依赖 Windows 默认代码页。其他程序重定向读取 CLI 输出时也应使用 UTF-8，避免中文提示和 Unicode 路径乱码。

## 本地转换

```powershell
dotnet run -- convert "manifest路径" "输出.json"
```

接受压缩／未压缩 Sophon 构建清单、本地补丁目标清单及本项目 JSON，按内容识别，不接受任意 JSON 或旧脚本格式。每份独立转换，不合并、不联网、不访问游戏目录；无效输入不发布最终 JSON。补丁标记不证明完整覆盖，见 [数据格式](#数据格式)。

## 官方网络转换

```powershell
dotnet run -- convert "参考清单.json" --network --service china --biz hk4e_cn --version latest --package game
```

`--service`、`--biz`、`--version`、`--package` 全部必填且不可重复；输出路径之外不再提供本地输入。服务使用配置 `Id`，`biz` 和包体使用官方原值，不拆分或猜测。版本为 `latest` 或三段版本号，版本不符不回退；相同 biz 有多个分支时须加 `--game-id <原始game.id>`。每次只转换一个包，多包分别输出后交给比较模块。

示例不是允许值名单，新标识是否可用取决于配置、官方响应和支持的格式。只请求指定服务，下载官方返回的所选 manifest，不下载游戏正文或补丁、不建立磁盘缓存。缺少／重复参数、旧 `--game`／`--region` 参数或坏版本格式在联网前报参数错误；包不存在、请求或校验失败是处理失败。

## 扫描目录

```powershell
dotnet run -- scan "扫描根目录" "扫描目录之外的输出.json" `
  --exclude-dir "Cache" "Data/Optional" `
  --exclude-file "settings.ini" "Data/private.bin"
```

默认轻扫路径与大小；加入 `--hash` 才计算整文件 MD5。重扫复用轻扫列表，不重复枚举；交互终端按字节显示低频单行进度，重定向无动态进度。轻扫没有过程百分比。

- CLI 不自动识别游戏或套用桌面预置；未指定排除时包含普通隐藏、系统、零字节文件，空目录合法。
- 排除选项可跟多个路径，也可重复。接受根内相对／绝对路径，统一分隔符、不区分大小写、精确匹配；目录包含后代但不影响相似前缀目录，不支持通配符。排除路径可尚不存在，不能排除根目录或根外路径。
- 跳过软链接、目录联接和其他重解析点，逐项警告并记录；已明确排除的路径不重复计入链接警告。根目录、输出目录及其祖先含链接则拒绝。普通盘符和 UNC 的设备前缀别名归一化，未知设备卷路径不支持。
- 元数据无法访问即停止；轻扫不读正文。重扫前复核类型、大小和路径，正文不可读、写占用或文件变化即失败，不发布部分哈希结果；读取时不允许普通文件句柄写入或删除。
- 输出必须在扫描根目录之外，父目录须存在。相对输出路径以当前工作目录为准，不是扫描根目录。

扫描结果只是枚举／读取时的状态，不是整目录原子快照；路径检查也不构成恶意并发路径替换的文件系统隔离。

## 清单比较

```powershell
dotnet run -- compare "比较结果.json" `
  --a "扫描.json" `
  --b "本体.json" "语音.json"
```

两侧仅接受本项目清单 JSON，不直接接受 protobuf。`--a`、`--b` 均可多值或重复，全部路径以当前工作目录为准。仅在内存合并，不生成组合文件，不访问 JSON 记录的原扫描目录，不修改输入。

A、B 对称，交换后独有项及差异详情也交换。路径统一分隔符、忽略大小写，保留原大小写并稳定排序；同侧重叠条目大小和 MD5（或均未计算）一致才去重，矛盾、重复路径或文件／父目录冲突报错。**两侧差异正常输出，不是运行失败。** 双方都有 MD5 才比较哈希，否则只比较大小。

对侧声明的排除或跳过链接范围列为 `outsideScope`；多输入范围保守取并集，但确实提供该路径时仍正常比较。独有项只表示本次输入集合差异，不证明文件无用、安装缺失或清单完整。报告字段见 [比较报告](#比较报告)。

## 写出与退出码

所有命令拒绝覆盖已有输出，输出父目录须存在。先写同目录唯一 `.partial`，关闭成功后发布为最终 JSON；写入、关闭或发布失败可能留下 `.partial`，已有目标不变，半成品不算成功结果。

- `0`：成功；包括比较发现差异、扫描跳过链接但其余成功。
- `1`：处理失败，如清单损坏、同侧矛盾、网络或写出失败。
- `2`：命令参数格式错误，如缺少、重复必选项或未知选项。

## 数据格式

JSON 数据定义在 `core/Models/FileDocuments.cs`，不是原始 protobuf 的 JSON 映射，也不兼容旧脚本格式。所有入口共用路径、大小、哈希与集合校验。

### 文件清单

```json
{
  "hashAlgorithm": "MD5",
  "fileCount": 1,
  "totalSize": 3,
  "files": [
    { "path": "Data/example.bin", "size": 3, "hash": "900150983cd24fb0d6963f7d28e17f72" }
  ]
}
```

`path` 是相对路径，分隔符统一 `/`，保留大小写；大小单位为字节。`hash` 是小写整文件 MD5，不是分块或 manifest 自身摘要，当前不支持其他算法。轻扫省略算法及哈希字段，不表示内容一致。

转换不输出分块、目录、补丁计划或 `UnusedAssets`；空 manifest 集合被拒绝，空扫描目录合法。危险路径、Windows 保留名、重复路径、文件／父目录冲突、非法大小／MD5 和总大小溢出均拒绝，不静默修正冲突。

扫描清单额外包含范围：

```json
{
  "scan": {
    "root": "D:\\Game",
    "excludedDirectories": ["Cache"],
    "excludedFiles": ["settings.ini"],
    "skippedLinks": ["Data/link"],
    "mode": "light"
  }
}
```

`mode` 为 `light`／`heavy`，排除和跳过项相对根目录。可选 `scan.gameDetections` 保存原始识别记录，不保存中文；导入不访问源目录，兼容限制见 [游戏识别](game-identification.md)。

识别命中可增加 `metadata`，再导入时根据有效文件条目重算，不信任保存的身份，证据及冲突结构见 [manifest 识别](manifest-identification.md)。补丁来源可带 `manifestKind: "sophon-patch"`，导出、再导入及合并保留；旧 JSON 和普通构建不增加。两类元数据均不证明官方来源、目标版本或完整覆盖。

### 排除规则

```json
{
  "excludedDirectories": ["Cache", "Data/Optional"],
  "excludedFiles": ["settings.ini"]
}
```

桌面也可从扫描 JSON 的 `scan` 读取两个数组。整份成功才合并，规范化后去重；至少提供一个数组，另一项可省略，显式 `null` 拒绝。路径规则见 [扫描目录](#扫描目录)。

### 比较报告

- `aInputs`／`bInputs` 保存两侧输入身份；`onlyA`／`onlyB` 保存独有条目。
- `different` 包含双方 `a`／`b` 条目、`sizeDifferent`、`hashDifferent`、`hashCompared`。
- `outsideScope` 保存 `presentIn`（`a`／`b`）及 `file`。
- `summary` 统计各类数量、`matchedWithHash`（大小和 MD5 一致）、`matchedSizeOnly`（仅大小一致）；相同项不逐条输出。

报告不是操作指令；核心保留导入及身份／内容复核能力，桌面不提供报告导入。
