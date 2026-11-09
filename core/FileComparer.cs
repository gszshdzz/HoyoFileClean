using System.Text.Json;
using System.Text.Json.Nodes;

namespace HoyoFileClean;

public sealed record ComparisonSummary(
    int OnlyA, int OnlyB, int Different, int MatchedWithHash, int MatchedSizeOnly, int OutsideScope);

public static class FileComparer
{
    public static ComparisonSummary Compare(IEnumerable<string> aPaths, IEnumerable<string> bPaths, string outputPath)
    {
        if (File.Exists(outputPath) || Directory.Exists(outputPath))
        {
            throw new CoreException(CoreErrorCode.OutputExists, outputPath);
        }
        ComparisonResult result = Compare(ReadInputs(aPaths), ReadInputs(bPaths));
        WriteReport(outputPath, result);
        return result.Summary;
    }

    public static ComparisonResult Compare(FileListSnapshot a, FileListSnapshot b)
    {
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
                FileListSnapshot absentSide = left is null ? a : b;
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
        return new ComparisonResult(a.Sources.Select(source => source.Path).ToArray(),
            b.Sources.Select(source => source.Path).ToArray(), summary,
            onlyA.AsReadOnly(), onlyB.AsReadOnly(), different.AsReadOnly(), outsideScope.AsReadOnly());
    }

    public static void WriteReport(string outputPath, ComparisonResult result)
        => JsonFileOutput.WriteNew(outputPath, result);

    // The report is data, never authority to open inputs or operate on a game directory.
    public static ComparisonResult ImportReport(string reportPath, FileListSnapshot a, FileListSnapshot b)
    {
        ComparisonResult expected = Compare(a, b);
        var options = ReaderOptions();
        ComparisonResult imported;
        try
        {
            using var stream = File.OpenRead(reportPath);
            imported = JsonSerializer.Deserialize<ComparisonResult>(stream, options)
                ?? throw new JsonException();
        }
        catch (JsonException error)
        {
            throw new CoreException(CoreErrorCode.ReportJsonInvalid, reportPath, innerException: error);
        }
        bool SameInputs(IReadOnlyList<string> actual, IReadOnlyList<string> current)
            => actual.Count == current.Count && actual.Zip(current).All(pair =>
                string.Equals(pair.First, pair.Second, StringComparison.OrdinalIgnoreCase));
        if (!SameInputs(imported.AInputs, expected.AInputs) || !SameInputs(imported.BInputs, expected.BInputs))
        {
            throw new CoreException(CoreErrorCode.ReportInputsMismatch, reportPath);
        }
        imported = imported with { AInputs = expected.AInputs, BInputs = expected.BInputs };
        if (!JsonNode.DeepEquals(JsonSerializer.SerializeToNode(imported), JsonSerializer.SerializeToNode(expected)))
        {
            throw new CoreException(CoreErrorCode.ReportContentMismatch, reportPath);
        }
        return expected;
    }

    private static JsonSerializerOptions ReaderOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        AllowDuplicateProperties = false
    };

    public static FileListSnapshot ReadInputs(IEnumerable<string> paths)
    {
        string[] inputs = paths.Select(Path.GetFullPath).ToArray();
        if (inputs.Length == 0)
        {
            throw new CoreException(CoreErrorCode.InputsRequired);
        }
        var files = new Dictionary<string, ManifestFile>(StringComparer.OrdinalIgnoreCase);
        var sources = new List<FileListSource>();
        var options = ReaderOptions();
        foreach (string input in inputs)
        {
            FileList list;
            try
            {
                using var stream = File.OpenRead(input);
                list = JsonSerializer.Deserialize<FileList>(stream, options)
                    ?? throw new JsonException();
            }
            catch (JsonException error)
            {
                throw new CoreException(CoreErrorCode.ListJsonInvalid, input, innerException: error);
            }
            bool hasHashes = list.HashAlgorithm == "MD5";
            if ((list.HashAlgorithm is not null && !hasHashes)
                || (list.Scan is not null && list.Scan.Mode != (hasHashes ? "heavy" : "light")))
            {
                throw new CoreException(CoreErrorCode.ListHashModeInvalid, input);
            }
            var normalized = new List<ManifestFile>();
            long totalSize = 0;
            foreach (ManifestFile? file in list.Files)
            {
                if (file is null || file.Size < 0 || (hasHashes ? !IsMd5(file.Hash) : file.Hash is not null))
                {
                    throw new CoreException(CoreErrorCode.FileMetadataInvalid, input: input);
                }
                string path = FilePathRules.NormalizeRelative(file.Path, input);
                normalized.Add(file with { Path = path, Hash = file.Hash?.ToLowerInvariant() });
                try
                {
                    totalSize = checked(totalSize + file.Size);
                }
                catch (OverflowException error)
                {
                    throw new CoreException(CoreErrorCode.TotalSizeOverflow, input, innerException: error);
                }
            }
            FilePathRules.ValidatePaths(normalized.Select(file => file.Path), input);
            if (list.FileCount != normalized.Count || list.TotalSize != totalSize)
            {
                throw new CoreException(CoreErrorCode.ListSummaryInvalid, input);
            }
            ScanScope? scope = list.Scan;
            if (scope is not null)
            {
                scope = scope with
                {
                    ExcludedDirectories = NormalizeScope(scope.ExcludedDirectories, input),
                    ExcludedFiles = NormalizeScope(scope.ExcludedFiles, input),
                    SkippedLinks = NormalizeScope(scope.SkippedLinks, input)
                };
            }
            sources.Add(new FileListSource(input, normalized.Count, totalSize, list.HashAlgorithm, scope));
            foreach (ManifestFile file in normalized)
            {
                if (files.TryGetValue(file.Path, out ManifestFile? existing)
                    && (existing.Size != file.Size || existing.Hash != file.Hash))
                {
                    throw new CoreException(CoreErrorCode.InputConflict, file.Path, input);
                }
                files.TryAdd(file.Path, file);
            }
        }
        FilePathRules.ValidatePaths(files.Keys);
        return new FileListSnapshot(sources, files);
    }

    private static IReadOnlyList<string> NormalizeScope(IEnumerable<string> paths, string input)
        => Array.AsReadOnly(paths.Select(path => FilePathRules.NormalizeRelative(
            path ?? throw new CoreException(CoreErrorCode.ScopePathMissing, input: input), input)).ToArray());

    private static bool IsMd5(string? value)
        => value is { Length: 32 } && value.All(character => character is >= '0' and <= '9'
            or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private sealed record FileList(int FileCount, long TotalSize, List<ManifestFile> Files,
        string? HashAlgorithm = null, ScanScope? Scan = null);

}
