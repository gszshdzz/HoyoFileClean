using System.Collections.ObjectModel;

namespace HoyoFileClean;

public sealed record ScanScope(string Root, IReadOnlyList<string> ExcludedDirectories,
    IReadOnlyList<string> ExcludedFiles, IReadOnlyList<string> SkippedLinks, string Mode);

public sealed record FileListSource(string Path, int FileCount, long TotalSize,
    string? HashAlgorithm, ScanScope? Scan);

// Created only by the common validated reader. Callers cannot replace its entries.
public sealed class FileListSnapshot
{
    public IReadOnlyList<FileListSource> Sources { get; }
    public IReadOnlyDictionary<string, ManifestFile> Files { get; }
    public int FileCount => Files.Count;
    public long TotalSize { get; }
    public int HashedFileCount { get; }

    internal FileListSnapshot(List<FileListSource> sources, Dictionary<string, ManifestFile> files)
    {
        Sources = sources.AsReadOnly();
        Files = new ReadOnlyDictionary<string, ManifestFile>(files);
        try { TotalSize = files.Values.Sum(file => file.Size); }
        catch (OverflowException error)
        {
            throw new CoreException(CoreErrorCode.TotalSizeOverflow, innerException: error);
        }
        HashedFileCount = files.Values.Count(file => file.Hash is not null);
    }

    internal bool IsOutsideScope(string path)
        => Sources.Any(source => source.Scan is { } scope
            && (scope.ExcludedFiles.Contains(path, StringComparer.OrdinalIgnoreCase)
                || scope.ExcludedDirectories.Concat(scope.SkippedLinks).Any(excluded =>
                    path.Equals(excluded, StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith(excluded + "/", StringComparison.OrdinalIgnoreCase))));
}

public sealed record Difference(ManifestFile A, ManifestFile B,
    bool SizeDifferent, bool HashDifferent, bool HashCompared);

public sealed record UncomparedFile(string PresentIn, ManifestFile File);

public sealed record ComparisonResult(IReadOnlyList<string> AInputs, IReadOnlyList<string> BInputs,
    ComparisonSummary Summary, IReadOnlyList<ManifestFile> OnlyA, IReadOnlyList<ManifestFile> OnlyB,
    IReadOnlyList<Difference> Different, IReadOnlyList<UncomparedFile> OutsideScope);
