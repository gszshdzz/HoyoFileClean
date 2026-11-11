using HoyoFileClean;
using HoyoFileClean.Cli;

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
            if (args.Contains("--network", StringComparer.Ordinal))
            {
                if (!NetworkConvertOptions.TryParse(args, out var options, out string? argumentError))
                {
                    if (argumentError is not null) CliOutput.ArgumentError(argumentError);
                    PrintUsage();
                    return 2;
                }
                using HttpClient http = NetworkManifestClient.CreateHttpClient();
                CliOutput.Conversion(await new NetworkManifestClient(http).ConvertAsync(options!.Game, options.Region,
                    options.Version, options.OutputPath, options.Package is { } package ? [package] : null));
                CliOutput.Saved(options.OutputPath);
                return 0;
            }
            if (args.Length != 3)
            {
                PrintUsage();
                return 2;
            }
            ConversionSummary conversion = ManifestConverter.Convert(
                args[1], args[2]);
            CliOutput.Conversion(conversion);
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
                CliOutput.SkippedLink(link);
            }
            CliOutput.Scan(scan, computeHashes);
            break;
        case "compare":
            if (args.Length < 6)
            {
                PrintUsage();
                return 2;
            }
            var aInputs = new List<string>();
            var bInputs = new List<string>();
            for (int index = 2; index < args.Length;)
            {
                List<string>? inputs = args[index++] switch
                {
                    "--a" => aInputs,
                    "--b" => bInputs,
                    _ => null
                };
                if (inputs is null)
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
                    inputs.Add(args[index++]);
                }
                if (index == start)
                {
                    PrintUsage();
                    return 2;
                }
            }
            if (aInputs.Count == 0 || bInputs.Count == 0)
            {
                PrintUsage();
                return 2;
            }
            ComparisonSummary comparison = FileComparer.Compare(aInputs, bInputs, args[1]);
            CliOutput.Comparison(comparison);
            CliOutput.Saved(args[1]);
            return 0;
        default:
            PrintUsage();
            return 2;
    }
    CliOutput.Saved(args[2]);
    return 0;
}
catch (Exception error) when (error is CoreException or IOException or InvalidDataException
    or HttpRequestException or OperationCanceledException
    or UnauthorizedAccessException or ArgumentException or NotSupportedException
    or System.Security.SecurityException or System.Security.Cryptography.CryptographicException)
{
    CliOutput.Error(error);
    return 1;
}

static void PrintUsage()
{
    CliOutput.Usage();
}
