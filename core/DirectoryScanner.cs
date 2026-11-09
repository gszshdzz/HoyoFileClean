using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace HoyoFileClean;

public sealed record ScanSummary(int FileCount, long TotalSize, IReadOnlyList<string> SkippedLinks);

public sealed record ScanProgress(int CompletedFiles, int TotalFiles, long ProcessedBytes, long TotalBytes)
{
    public double Percentage => TotalBytes > 0 ? 100.0 * ProcessedBytes / TotalBytes
        : TotalFiles > 0 ? 100.0 * CompletedFiles / TotalFiles : 100;
}

public static class DirectoryScanner
{
    public static string NormalizeRootPath(string path)
        => Path.TrimEndingDirectorySeparator(FullPath(path));

    public static ScanSummary Scan(
        string rootPath,
        string outputPath,
        IEnumerable<string>? excludedDirectories = null,
        IEnumerable<string>? excludedFiles = null,
        bool computeHashes = false,
        Action<ScanProgress>? progress = null)
    {
        string root = NormalizeRootPath(rootPath);
        string output = FullPath(outputPath);
        if (!IsOutsideRoot(RelativePath(root, output)))
        {
            throw new ArgumentException("输出必须放在扫描根目录之外，以免修改或扫描到结果文件。");
        }
        if (File.Exists(output) || Directory.Exists(output))
        {
            throw new IOException($"输出已存在，不能覆盖：{output}");
        }
        string outputParent = Path.GetDirectoryName(output)
            ?? throw new ArgumentException("输出路径必须指定文件名。");
        ValidateDirectoryPath(root);
        ValidateDirectoryPath(outputParent);
        HashSet<string> excludedDirs = NormalizeExclusions(root, excludedDirectories);
        HashSet<string> excludedFilePaths = NormalizeExclusions(root, excludedFiles);
        var skippedLinks = new List<string>();
        List<ManifestFile> files = ReadFileList(root, excludedDirs, excludedFilePaths, skippedLinks);
        files.Sort((left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
        skippedLinks.Sort(StringComparer.Ordinal);
        FilePathRules.ValidatePaths(files.Select(file => file.Path), "扫描结果");
        long totalSize;
        try
        {
            totalSize = files.Sum(file => file.Size);
        }
        catch (OverflowException error)
        {
            throw new InvalidDataException("扫描文件总大小超过可表示范围，已停止。", error);
        }
        if (computeHashes)
        {
            ComputeHashes(root, files, totalSize, progress);
        }
        IReadOnlyList<string> links = skippedLinks.AsReadOnly();
        var details = new ScanDetails(root,
            excludedDirs.OrderBy(path => path, StringComparer.Ordinal).ToArray(),
            excludedFilePaths.OrderBy(path => path, StringComparer.Ordinal).ToArray(), links,
            computeHashes ? "heavy" : "light");
        var result = new ScanFileList(computeHashes ? "MD5" : null, files.Count, totalSize, files, details);
        ValidateDirectoryPath(outputParent);
        JsonFileOutput.WriteNew(output, result);
        return new ScanSummary(files.Count, totalSize, links);
    }

    private static List<ManifestFile> ReadFileList(
        string root, HashSet<string> excludedDirs, HashSet<string> excludedFiles, List<string> skippedLinks)
    {
        var files = new List<ManifestFile>();
        var directories = new Stack<string>();
        directories.Push(root);
        var options = new EnumerationOptions
        {
            AttributesToSkip = 0,
            IgnoreInaccessible = false,
            RecurseSubdirectories = false
        };

        while (directories.Count > 0)
        {
            string directory = directories.Pop();
            FileAttributes directoryAttributes = File.GetAttributes(directory);
            if ((directoryAttributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException($"扫描目录变为链接或重解析点，已停止：{directory}");
            }
            if ((directoryAttributes & FileAttributes.Directory) == 0)
            {
                throw new IOException($"扫描目录已不再是目录，已停止：{directory}");
            }
            foreach (FileSystemInfo entry in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", options))
            {
                string relative = FilePathRules.NormalizeRelative(RelativePath(root, entry.FullName), "扫描");
                FileAttributes attributes = entry.Attributes;
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    skippedLinks.Add(relative);
                    continue;
                }
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (!excludedDirs.Contains(relative))
                    {
                        directories.Push(entry.FullName);
                    }
                    continue;
                }
                if (excludedFiles.Contains(relative))
                {
                    continue;
                }
                // 使用目录枚举缓存的大小，不打开文件正文。
                files.Add(new ManifestFile(relative, ((FileInfo)entry).Length));
            }
        }

        return files;
    }

    private static void ComputeHashes(
        string root, List<ManifestFile> files, long totalSize, Action<ScanProgress>? progress)
    {
        byte[] buffer = new byte[128 * 1024];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        var timer = Stopwatch.StartNew();
        long processedBytes = 0;
        long lastReport = 0;
        void Report(int completedFiles, bool force = false)
        {
            long now = timer.ElapsedMilliseconds;
            if (progress is null || (!force && now - lastReport < 500))
            {
                return;
            }
            progress(new ScanProgress(completedFiles, files.Count, processedBytes, totalSize));
            lastReport = now;
        }

        Report(0, force: true);
        for (int index = 0; index < files.Count; index++)
        {
            ManifestFile file = files[index];
            string path = Path.Combine(root, file.Path);
            // 列表和哈希分两个阶段，读取前重新确认路径及文件类型。
            ValidateDirectoryPath(Path.GetDirectoryName(path)!);
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            {
                throw new IOException($"扫描文件变为链接或目录，已停止：{path}");
            }
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                buffer.Length, FileOptions.SequentialScan);
            if (input.Length != file.Size)
            {
                throw new IOException($"扫描时文件大小发生变化，已停止：{path}");
            }
            int count;
            while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
            {
                hash.AppendData(buffer, 0, count);
                processedBytes += count;
                Report(index);
            }
            if (input.Length != file.Size || input.Position != file.Size)
            {
                throw new IOException($"扫描时文件大小发生变化，已停止：{path}");
            }
            files[index] = file with { Hash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() };
        }
        Report(files.Count, force: true);
    }

    private static HashSet<string> NormalizeExclusions(string root, IEnumerable<string>? paths)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths ?? [])
        {
            if (string.IsNullOrWhiteSpace(path)
                || (Path.IsPathRooted(path) && !Path.IsPathFullyQualified(path)))
            {
                throw new ArgumentException($"排除项必须是相对路径或完整绝对路径：{path}");
            }
            string fullPath = Path.TrimEndingDirectorySeparator(FullPath(path, root));
            string relative = RelativePath(root, fullPath);
            if (relative == "." || IsOutsideRoot(relative))
            {
                throw new ArgumentException($"排除项必须位于扫描根目录内，且不能排除根目录本身：{path}");
            }
            result.Add(FilePathRules.NormalizeRelative(relative, "排除项"));
        }
        return result;
    }

    private static string FullPath(string path, string? basePath = null)
    {
        path = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            string tail = path[4..];
            if (tail.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase))
            {
                path = @"\\" + tail[4..];
            }
            else if (tail.Length >= 3 && tail[1] == ':' && tail[2] == '\\')
            {
                path = tail;
            }
            else
            {
                throw new NotSupportedException("扫描不支持此设备卷路径，请使用普通盘符或 UNC 路径。");
            }
        }
        return basePath is null ? Path.GetFullPath(path) : Path.GetFullPath(path, basePath);
    }

    private static string RelativePath(string root, string path)
        => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static bool IsOutsideRoot(string relative)
        => Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith("../", StringComparison.Ordinal);

    private static void ValidateDirectoryPath(string path)
    {
        var ancestors = new Stack<string>();
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            ancestors.Push(current);
        }
        while (ancestors.Count > 0)
        {
            string current = ancestors.Pop();
            FileAttributes attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException($"目录路径包含链接或重解析点，已停止：{current}");
            }
            if ((attributes & FileAttributes.Directory) == 0)
            {
                throw new DirectoryNotFoundException($"路径不是目录：{current}");
            }
        }
    }

    private sealed record ScanFileList(
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? HashAlgorithm,
        int FileCount, long TotalSize, IReadOnlyList<ManifestFile> Files, ScanDetails Scan);

    private sealed record ScanDetails(
        string Root, IReadOnlyList<string> ExcludedDirectories, IReadOnlyList<string> ExcludedFiles,
        IReadOnlyList<string> SkippedLinks, string Mode);
}
