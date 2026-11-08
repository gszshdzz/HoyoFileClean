using HoyoFileClean;

if (args.Length == 0 || args is ["--help"] or ["-h"])
{
    PrintUsage();
    return 0;
}

try
{
    switch (args[0])
    {
        case "convert":
            if (args.Length is not (3 or 4) || (args.Length == 4 && args[3] != "--uncompressed"))
            {
                PrintUsage();
                return 2;
            }
            ConversionSummary conversion = ManifestConverter.Convert(
                args[1], args[2], uncompressed: args.Length == 4);
            Console.WriteLine($"转换完成：{conversion.FileCount:N0} 个文件，{conversion.TotalSize:N0} 字节。");
            break;
        case "scan":
            if (args.Length < 3)
            {
                PrintUsage();
                return 2;
            }
            var excludedDirectories = new List<string>();
            var excludedFiles = new List<string>();
            bool computeHashes = false;
            for (int index = 3; index < args.Length;)
            {
                string option = args[index++];
                if (option == "--hash")
                {
                    computeHashes = true;
                    continue;
                }
                List<string>? paths = option switch
                {
                    "--exclude-dir" => excludedDirectories,
                    "--exclude-file" => excludedFiles,
                    _ => null
                };
                if (paths is null)
                {
                    PrintUsage();
                    return 2;
                }
                int start = index;
                while (index < args.Length && !args[index].StartsWith("--", StringComparison.Ordinal))
                {
                    if (string.IsNullOrWhiteSpace(args[index]))
                    {
                        PrintUsage();
                        return 2;
                    }
                    paths.Add(args[index++]);
                }
                if (index == start)
                {
                    PrintUsage();
                    return 2;
                }
            }
            int progressWidth = 0;
            void ShowProgress(ScanProgress value)
            {
                string line = ProgressDisplay.Format(value, progressWidth, Console.WindowWidth);
                Console.Write(line);
                progressWidth = line.Length - 1;
            }
            Action<ScanProgress>? progress = computeHashes && !Console.IsOutputRedirected ? ShowProgress : null;
            ScanSummary scan;
            try
            {
                scan = DirectoryScanner.Scan(args[1], args[2], excludedDirectories, excludedFiles, computeHashes, progress);
            }
            finally
            {
                if (progressWidth > 0)
                {
                    Console.WriteLine();
                }
            }
            foreach (string link in scan.SkippedLinks)
            {
                Console.Error.WriteLine($"警告：已跳过链接或重解析点，未跟随：{link}");
            }
            Console.WriteLine($"{(computeHashes ? "重" : "轻")}扫描完成：{scan.FileCount:N0} 个文件，{scan.TotalSize:N0} 字节，跳过 {scan.SkippedLinks.Count:N0} 个链接或重解析点。");
            break;
        default:
            PrintUsage();
            return 2;
    }
    Console.WriteLine($"输出：{Path.GetFullPath(args[2])}");
    return 0;
}
catch (Exception error) when (error is IOException or InvalidDataException
    or UnauthorizedAccessException or ArgumentException or NotSupportedException
    or System.Security.SecurityException or System.Security.Cryptography.CryptographicException)
{
    Console.Error.WriteLine(error.ToString());
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine("用法：HoyoFileClean convert <manifest> <output.json> [--uncompressed]");
    Console.WriteLine("      HoyoFileClean scan <root> <output.json> [--hash] [--exclude-dir <path> ...] [--exclude-file <path> ...]");
    Console.WriteLine("默认输入为 zstd 压缩的 Sophon 构建清单；解压后的 protobuf 加 --uncompressed。");
    Console.WriteLine("默认轻扫只读路径和大小；--hash 复用列表计算 MD5，交互终端低频刷新进度。");
    Console.WriteLine("排除选项可跟多个路径，也可重复；路径相对扫描根目录，或为根目录内的绝对路径，不支持通配符。");
    Console.WriteLine("scan 只读、不跟随链接，输出必须在扫描根目录之外；两个命令均不覆盖已有输出。");
    Console.WriteLine("convert 只转换当前清单，不访问游戏目录、不联网。");
}
