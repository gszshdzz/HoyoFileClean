using HoyoFileClean;

if (args.Length == 0 || args is ["--help"] or ["-h"])
{
    PrintUsage();
    return 0;
}

if (args.Length is not (3 or 4) || args[0] != "convert"
    || (args.Length == 4 && args[3] != "--uncompressed"))
{
    PrintUsage();
    return 2;
}

try
{
    ConversionSummary summary = ManifestConverter.Convert(
        args[1], args[2], uncompressed: args.Length == 4);
    Console.WriteLine($"转换完成：{summary.FileCount:N0} 个文件，{summary.TotalSize:N0} 字节。");
    Console.WriteLine($"输出：{Path.GetFullPath(args[2])}");
    return 0;
}
catch (Exception error) when (error is IOException or InvalidDataException
    or UnauthorizedAccessException or ArgumentException or NotSupportedException)
{
    Console.Error.WriteLine(error.ToString());
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine("用法：HoyoFileClean convert <manifest> <output.json> [--uncompressed]");
    Console.WriteLine("默认输入为 zstd 压缩的 Sophon 构建清单；解压后的 protobuf 加 --uncompressed。");
    Console.WriteLine("只转换当前清单，不访问游戏目录、不联网、不覆盖已有输出。");
}
