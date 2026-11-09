using System.Text.Json;

namespace HoyoFileClean;

public sealed record ComparisonSummary(
    int OnlyA, int OnlyB, int Different, int MatchedWithHash, int MatchedSizeOnly, int OutsideScope);

public static class FileComparer
{
    public static ComparisonSummary Compare(IEnumerable<string> aPaths, IEnumerable<string> bPaths, string outputPath)
    {
        if (File.Exists(outputPath) || Directory.Exists(outputPath))
        {
            throw new IOException($"输出已存在，不能覆盖：{outputPath}");
        }
        Side a = ReadSide(aPaths);
        Side b = ReadSide(bPaths);
        var onlyA = new List<ManifestFile>();
        var onlyB = new List<ManifestFile>();
        var different = new List<Difference>();
        var outsideScope = new List<UncomparedFile>();
        int matchedWithHash = 0;
        int matchedSizeOnly = 0;
        foreach (string path in a.Files.Keys.Union(b.Files.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.Ordinal))
        {
            a.Files.TryGetValue(path, out ManifestFile? left);
            b.Files.TryGetValue(path, out ManifestFile? right);
            if (left is null || right is null)
            {
                ManifestFile file = (left ?? right)!;
                Side absentSide = left is null ? a : b;
                if (absentSide.IsOutsideScope(path))
                {
                    outsideScope.Add(new UncomparedFile(left is null ? "b" : "a", file));
                }
                else
                {
                    (left is null ? onlyB : onlyA).Add(file);
                }
                continue;
            }
            bool hashCompared = left.Hash is not null && right.Hash is not null;
            bool sizeDifferent = left.Size != right.Size;
            bool hashDifferent = hashCompared && left.Hash != right.Hash;
            if (sizeDifferent || hashDifferent)
            {
                different.Add(new Difference(left, right, sizeDifferent, hashDifferent, hashCompared));
            }
            else if (hashCompared)
            {
                matchedWithHash++;
            }
            else
            {
                matchedSizeOnly++;
            }
        }
        var summary = new ComparisonSummary(onlyA.Count, onlyB.Count, different.Count,
            matchedWithHash, matchedSizeOnly, outsideScope.Count);
        JsonFileOutput.WriteNew(outputPath, new
        {
            aInputs = a.Inputs,
            bInputs = b.Inputs,
            summary,
            onlyA,
            onlyB,
            different,
            outsideScope
        });
        return summary;
    }

    private static Side ReadSide(IEnumerable<string> paths)
    {
        string[] inputs = paths.Select(Path.GetFullPath).ToArray();
        if (inputs.Length == 0)
        {
            throw new ArgumentException("A、B 两侧都至少需要一份清单。");
        }
        var files = new Dictionary<string, ManifestFile>(StringComparer.OrdinalIgnoreCase);
        var scopes = new List<ScanScope>();
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            RespectRequiredConstructorParameters = true,
            RespectNullableAnnotations = true,
            AllowDuplicateProperties = false
        };
        foreach (string input in inputs)
        {
            FileList list;
            try
            {
                using var stream = File.OpenRead(input);
                list = JsonSerializer.Deserialize<FileList>(stream, options)
                    ?? throw new JsonException("清单不能为空。");
            }
            catch (JsonException error)
            {
                throw new InvalidDataException($"清单 JSON 格式无效：{input}", error);
            }
            bool hasHashes = list.HashAlgorithm == "MD5";
            if ((list.HashAlgorithm is not null && !hasHashes)
                || (list.Scan is not null && list.Scan.Mode != (hasHashes ? "heavy" : "light")))
            {
                throw new InvalidDataException($"清单哈希算法或扫描模式无效：{input}");
            }
            var normalized = new List<ManifestFile>();
            long totalSize = 0;
            foreach (ManifestFile? file in list.Files)
            {
                if (file is null || file.Size < 0 || (hasHashes ? !IsMd5(file.Hash) : file.Hash is not null))
                {
                    throw new InvalidDataException($"清单文件大小或 MD5 无效：{input}");
                }
                string path = FilePathRules.NormalizeRelative(file.Path, input);
                normalized.Add(file with { Path = path, Hash = file.Hash?.ToLowerInvariant() });
                try
                {
                    totalSize = checked(totalSize + file.Size);
                }
                catch (OverflowException error)
                {
                    throw new InvalidDataException($"清单文件总大小溢出：{input}", error);
                }
            }
            FilePathRules.ValidatePaths(normalized.Select(file => file.Path), input);
            if (list.FileCount != normalized.Count || list.TotalSize != totalSize)
            {
                throw new InvalidDataException($"清单文件数或总大小与条目不一致：{input}");
            }
            if (list.Scan is not null)
            {
                scopes.Add(list.Scan with
                {
                    ExcludedDirectories = NormalizeScope(list.Scan.ExcludedDirectories, input),
                    ExcludedFiles = NormalizeScope(list.Scan.ExcludedFiles, input),
                    SkippedLinks = NormalizeScope(list.Scan.SkippedLinks, input)
                });
            }
            foreach (ManifestFile file in normalized)
            {
                if (files.TryGetValue(file.Path, out ManifestFile? existing)
                    && (existing.Size != file.Size || existing.Hash != file.Hash))
                {
                    throw new InvalidDataException($"同一侧的清单存在相互矛盾的文件记录：{file.Path}（输入：{input}）");
                }
                files.TryAdd(file.Path, file);
            }
        }
        FilePathRules.ValidatePaths(files.Keys, "同一侧的清单");
        return new Side(inputs, files, scopes);
    }

    private static string[] NormalizeScope(IEnumerable<string> paths, string input)
        => paths.Select(path => FilePathRules.NormalizeRelative(
            path ?? throw new InvalidDataException($"清单扫描范围不能包含空路径：{input}"), input)).ToArray();

    private static bool IsMd5(string? value)
        => value is { Length: 32 } && value.All(character => character is >= '0' and <= '9'
            or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private sealed record FileList(int FileCount, long TotalSize, List<ManifestFile> Files,
        string? HashAlgorithm = null, ScanScope? Scan = null);

    private sealed record ScanScope(string Root, string[] ExcludedDirectories,
        string[] ExcludedFiles, string[] SkippedLinks, string Mode);

    private sealed record Side(string[] Inputs, Dictionary<string, ManifestFile> Files, List<ScanScope> Scopes)
    {
        public bool IsOutsideScope(string path)
            => Scopes.Any(scope => scope.ExcludedFiles.Contains(path, StringComparer.OrdinalIgnoreCase)
                || scope.ExcludedDirectories.Concat(scope.SkippedLinks).Any(excluded =>
                    path.Equals(excluded, StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith(excluded + "/", StringComparison.OrdinalIgnoreCase)));
    }

    private sealed record Difference(ManifestFile A, ManifestFile B,
        bool SizeDifferent, bool HashDifferent, bool HashCompared);

    private sealed record UncomparedFile(string PresentIn, ManifestFile File);
}
