using System.Text.Json;
using System.Text.Json.Nodes;

namespace HoyoFileClean;

public static class FileComparer
{
    private static readonly ManifestIdentifier manifestIdentifier = new(ManifestIdentificationRules.Create());
    private static readonly JsonSerializerOptions readerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        AllowDuplicateProperties = false
    };

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
                    outsideScope.Add(new UncomparedFile(left is null ? FileValues.SideB : FileValues.SideA, file));
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
        ComparisonResult imported;
        try
        {
            using var stream = File.OpenRead(reportPath);
            imported = JsonSerializer.Deserialize<ComparisonResult>(stream, readerOptions)
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

    public static FileListSnapshot ReadInputs(IEnumerable<string> paths)
        => ReadInputs(paths, File.OpenRead);

    internal static FileListSnapshot ReadInputs(IEnumerable<string> paths, Func<string, Stream> open,
        CancellationToken cancellationToken = default)
        => ReadLists(paths, input =>
        {
            try
            {
                using var stream = open(input);
                return JsonSerializer.Deserialize<FileListDocument>(stream, readerOptions) ?? throw new JsonException();
            }
            catch (JsonException error)
            {
                throw new CoreException(CoreErrorCode.ListJsonInvalid, input, innerException: error);
            }
        }, cancellationToken);

    internal static FileListSnapshot FromFiles(string source, IReadOnlyList<ManifestFile> files,
        long totalSize, string? hashAlgorithm, ScanScope? scan = null, CancellationToken cancellationToken = default)
        => ReadLists([source], _ => new FileListDocument(files.Count, totalSize, files, hashAlgorithm, scan), cancellationToken,
            preserveSource: true);

    public static FileListSnapshot Combine(IEnumerable<FileListSnapshot> snapshots, CancellationToken cancellationToken = default)
    {
        var accumulator = new SnapshotAccumulator();
        int order = 0;
        foreach (FileListSnapshot snapshot in snapshots)
        {
            accumulator.Add(snapshot, order++, cancellationToken);
        }
        return accumulator.Build(cancellationToken);
    }

    // A batch owns this accumulator; callers synchronize Add when parsing in parallel.
    public sealed class SnapshotAccumulator
    {
        private bool built;
        private readonly Dictionary<string, ManifestFile> files = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> fileOrder = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(int Order, FileListSource Source)> sources = [];

        public void Add(FileListSnapshot snapshot, int order, CancellationToken cancellationToken)
        {
            if (built) throw new InvalidOperationException("The snapshot has already been published.");
            cancellationToken.ThrowIfCancellationRequested();
            foreach (FileListSource source in snapshot.Sources) sources.Add((order, source));
            MergeFiles(files, snapshot.Files.Values, snapshot.Sources[0].Path, cancellationToken);
            foreach (ManifestFile file in snapshot.Files.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!fileOrder.TryGetValue(file.Path, out int previous) || order < previous)
                {
                    // Parallel completion order must not change the first input's path spelling.
                    files.Remove(file.Path);
                    files.Add(file.Path, file);
                    fileOrder[file.Path] = order;
                }
            }
        }

        public FileListSnapshot Build(CancellationToken cancellationToken)
        {
            if (built) throw new InvalidOperationException("The snapshot has already been published.");
            if (sources.Count == 0) throw new CoreException(CoreErrorCode.InputsRequired);
            FilePathRules.ValidatePaths(files.Keys);
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = new FileListSnapshot(sources.OrderBy(item => item.Order).Select(item => item.Source).ToList(), files);
            built = true;
            return snapshot;
        }
    }

    private static FileListSnapshot ReadLists(IEnumerable<string> paths, Func<string, FileListDocument> read,
        CancellationToken cancellationToken, bool preserveSource = false)
    {
        string[] inputs = preserveSource ? paths.ToArray() : paths.Select(Path.GetFullPath).ToArray();
        if (inputs.Length == 0)
        {
            throw new CoreException(CoreErrorCode.InputsRequired);
        }
        var files = new Dictionary<string, ManifestFile>(StringComparer.OrdinalIgnoreCase);
        var sources = new List<FileListSource>();
        foreach (string input in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileListDocument list = read(input);
            bool hasHashes = list.HashAlgorithm == FileValues.Md5;
            if ((list.HashAlgorithm is not null && !hasHashes)
                || (list.Scan is not null && list.Scan.Mode != (hasHashes ? FileValues.HeavyScan : FileValues.LightScan)))
            {
                throw new CoreException(CoreErrorCode.ListHashModeInvalid, input);
            }
            var normalized = new List<ManifestFile>();
            long totalSize = 0;
            foreach (ManifestFile? file in list.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ManifestFile entry = FileEntryRules.Normalize(file, hasHashes, input);
                normalized.Add(entry);
                try
                {
                    totalSize = checked(totalSize + entry.Size);
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
                    SkippedLinks = NormalizeScope(scope.SkippedLinks, input),
                    GameDetections = scope.GameDetections is { } recorded
                        ? GameIdentifier.ValidateRecordedDetections(recorded) : null
                };
            }
            // Never trust metadata saved in an input JSON: derive it again from validated contents.
            var metadata = manifestIdentifier.Identify(
                new FileListDocument(normalized.Count, totalSize, normalized, list.HashAlgorithm, scope), cancellationToken);
            sources.Add(new FileListSource(input, normalized.Count, totalSize, list.HashAlgorithm, scope, metadata));
            MergeFiles(files, normalized, input, cancellationToken);
        }
        FilePathRules.ValidatePaths(files.Keys);
        cancellationToken.ThrowIfCancellationRequested();
        return new FileListSnapshot(sources, files);
    }

    private static IReadOnlyList<string> NormalizeScope(IEnumerable<string> paths, string input)
        => Array.AsReadOnly(paths.Select(path => FilePathRules.NormalizeRelative(
            path ?? throw new CoreException(CoreErrorCode.ScopePathMissing, input: input), input)).ToArray());

    private static void MergeFiles(Dictionary<string, ManifestFile> files, IEnumerable<ManifestFile> incoming, string input,
        CancellationToken cancellationToken)
    {
        foreach (ManifestFile file in incoming)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (files.TryGetValue(file.Path, out ManifestFile? existing)
                && (existing.Size != file.Size || existing.Hash != file.Hash))
                throw new CoreException(CoreErrorCode.InputConflict, file.Path, input);
            files.TryAdd(file.Path, file);
        }
    }


}
