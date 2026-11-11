namespace HoyoFileClean.Cli;

internal static class CliOutput
{
    public static void Conversion(ConversionSummary value)
        => Console.WriteLine($"转换完成：{value.FileCount:N0} 个文件，{value.TotalSize:N0} 字节。");

    public static void Scan(ScanSummary value, bool hashes)
        => Console.WriteLine($"{(hashes ? "重" : "轻")}扫描完成：{value.FileCount:N0} 个文件，{value.TotalSize:N0} 字节，跳过 {value.SkippedLinks.Count:N0} 个链接或重解析点。");

    public static void SkippedLink(string path)
        => Console.Error.WriteLine($"警告：已跳过链接或重解析点，未跟随：{path}");

    public static void Comparison(ComparisonSummary value)
    {
        Console.WriteLine($"比较完成：A 独有 {value.OnlyA:N0}，B 独有 {value.OnlyB:N0}，共有但不同 {value.Different:N0}。");
        Console.WriteLine($"大小与 MD5 一致 {value.MatchedWithHash:N0}，仅大小一致（未校验哈希）{value.MatchedSizeOnly:N0}，对侧扫描范围外 {value.OutsideScope:N0}。");
    }

    public static void Saved(string path) => Console.WriteLine($"输出：{Path.GetFullPath(path)}");

    public static void ArgumentError(string message) => Console.Error.WriteLine("参数错误：" + message);

    public static void Error(Exception error)
    {
        string message = error is CoreException core ? Describe(core.Code) : error switch
        {
            FileNotFoundException => "输入文件不存在。",
            DirectoryNotFoundException => "目录不存在或路径中的某一部分不是目录。",
            UnauthorizedAccessException or System.Security.SecurityException => "没有访问文件或目录的权限。",
            ArgumentException => "路径或参数无效。",
            NotSupportedException => "路径格式或操作不受支持。",
            System.Security.Cryptography.CryptographicException => "计算文件哈希失败。",
            InvalidDataException => "输入数据无效。",
            HttpRequestException => "官方网络请求失败，请检查网络或稍后重试。",
            OperationCanceledException => "网络操作已取消或超时。",
            IOException => "文件读写或结果发布失败；请检查路径、文件占用和可用空间。",
            _ => "处理失败。"
        };
        Console.Error.WriteLine("错误：" + message);
        if (error is CoreException details)
        {
            Console.Error.WriteLine("错误代码：" + details.Code);
            if (details.Path is not null) Console.Error.WriteLine("路径：" + details.Path);
            if (details.Input is not null) Console.Error.WriteLine("输入：" + details.Input);
        }
        else
        {
            if (error is FileNotFoundException { FileName: { } path }) Console.Error.WriteLine("路径：" + path);
            Console.Error.WriteLine($"诊断代码：0x{error.HResult:X8}");
        }
    }

    private static string Describe(CoreErrorCode code) => code switch
    {
        CoreErrorCode.OutputExists => "输出已存在，不能覆盖。",
        CoreErrorCode.OutputInsideScanRoot => "输出必须放在扫描根目录之外，以免修改或扫描到结果文件。",
        CoreErrorCode.OutputFileNameRequired => "输出路径必须指定文件名。",
        CoreErrorCode.ManifestCompressedSizeLimit => "Manifest 压缩输入大小超过 64 MiB，已停止转换。",
        CoreErrorCode.ManifestDecodedSizeLimit => "Manifest 解压后大小超过 128 MiB，已停止转换。",
        CoreErrorCode.ManifestCompressionInvalid or CoreErrorCode.ManifestEncodingInvalid => "清单损坏或不是支持的 manifest。",
        CoreErrorCode.ManifestEmpty => "Manifest 没有文件条目，不能生成文件列表。",
        CoreErrorCode.FileMetadataInvalid => "文件大小或 MD5 无效。",
        CoreErrorCode.TotalSizeOverflow => "文件总大小超过可表示范围，已停止。",
        CoreErrorCode.RelativePathInvalid => "文件路径不是安全的相对路径。",
        CoreErrorCode.DuplicatePath => "清单中存在重复文件路径。",
        CoreErrorCode.FileDirectoryConflict => "清单中的文件同时被用作父目录。",
        CoreErrorCode.InputsRequired => "A、B 两侧都至少需要一份清单。",
        CoreErrorCode.ListJsonInvalid => "清单 JSON 格式无效。",
        CoreErrorCode.ListHashModeInvalid => "清单哈希算法或扫描模式无效。",
        CoreErrorCode.ListSummaryInvalid => "清单文件数或总大小与条目不一致。",
        CoreErrorCode.InputConflict => "同一侧的清单存在相互矛盾的文件记录。",
        CoreErrorCode.ScopePathMissing => "清单扫描范围不能包含空路径。",
        CoreErrorCode.ReportJsonInvalid => "比较报告 JSON 格式无效。",
        CoreErrorCode.ReportInputsMismatch => "比较报告的 A、B 输入身份与当前清单不一致。请重新比较。",
        CoreErrorCode.ReportContentMismatch => "比较报告内容与当前清单不一致或已损坏。请重新比较。",
        CoreErrorCode.ScanDirectoryIsLink => "扫描目录变为链接或重解析点，已停止。",
        CoreErrorCode.ScanDirectoryMissing => "扫描目录已不再是目录，已停止。",
        CoreErrorCode.ScanFileTypeChanged => "扫描文件变为链接或目录，已停止。",
        CoreErrorCode.ScanFileSizeChanged => "扫描时文件大小发生变化，已停止。",
        CoreErrorCode.ExclusionPathInvalid => "排除项必须是相对路径或完整绝对路径。",
        CoreErrorCode.ExclusionOutsideRoot => "排除项必须位于扫描根目录内，且不能排除根目录本身。",
        CoreErrorCode.DevicePathUnsupported => "扫描不支持此设备卷路径，请使用普通盘符或 UNC 路径。",
        CoreErrorCode.DirectoryIsLink => "目录路径包含链接或重解析点，已停止。",
        CoreErrorCode.DirectoryMissing => "路径不是目录。",
        CoreErrorCode.ManifestNetworkMetadataInvalid => "官方清单元数据无效。",
        CoreErrorCode.ManifestChannelUnavailable => "官方接口未提供所选游戏、区服或版本的清单。",
        CoreErrorCode.ManifestVersionUnavailable => "官方返回版本与指定版本不符，未改用其他版本。",
        CoreErrorCode.ManifestPackageUnavailable => "所选版本没有指定的资源包。",
        CoreErrorCode.ManifestDownloadMismatch => "清单下载长度、压缩方式或 MD5 不符。",
        CoreErrorCode.ManifestNetworkSizeLimit => "网络清单或元数据超过大小上限。",
        CoreErrorCode.ManifestNetworkUnsupported => "清单地址或格式不受支持；仅允许官方 HTTPS 清单。",
        _ => "处理失败，核心返回了未识别的错误代码。"
    };

    public static void Usage()
    {
        Console.WriteLine("用法：HoyoFileClean convert <manifest> <output.json>");
        Console.WriteLine("      HoyoFileClean convert <output.json> --network --game <game> --region <region> --version <version> [--package <package>]");
        Console.WriteLine("      HoyoFileClean scan <root> <output.json> [--hash] [--exclude-dir <path> ...] [--exclude-file <path> ...]");
        Console.WriteLine("      HoyoFileClean compare <output.json> --a <list.json> ... --b <list.json> ...");
        Console.WriteLine("支持本地 Sophon 构建清单，程序自动识别输入，无需手动处理。");
        Console.WriteLine("默认轻扫只读路径和大小；--hash 复用列表计算 MD5，交互终端低频刷新进度。");
        Console.WriteLine("排除选项可跟多个路径，也可重复；路径相对扫描根目录，或为根目录内的绝对路径，不支持通配符。");
        Console.WriteLine("scan 只读、不跟随链接，输出必须在扫描根目录之外；所有命令均不覆盖已有输出。");
        Console.WriteLine("convert 默认转换本地清单；--network 仅连接官方服务器，游戏、区服、版本必填，默认只转本体；--package 最多指定一次。不访问游戏目录。");
        Console.WriteLine("网络参数使用官方返回的游戏／区服标识与资源包 matching_field；未知值会请求官方查询，不使用固定可选值名单。");
        Console.WriteLine("提示示例（不是限制）：原神 --game hk4e --region cn；国际服 --region global；语音包 --package zh-cn。版本可用 latest 或三段版本号。");
        Console.WriteLine("compare 两侧平等，均可输入多份清单；差异正常输出，同一侧冲突报错，不移动文件。");
    }
}
