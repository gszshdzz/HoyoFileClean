using System.Text.Json.Serialization;

namespace HoyoFileClean;

public sealed record ManifestFile(string Path, long Size,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Hash = null);

public sealed record FileListDocument(int FileCount, long TotalSize, IReadOnlyList<ManifestFile> Files,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? HashAlgorithm = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ScanScope? Scan = null);

public sealed record ScanScope(string Root, IReadOnlyList<string> ExcludedDirectories,
    IReadOnlyList<string> ExcludedFiles, IReadOnlyList<string> SkippedLinks, string Mode);

public sealed record FileListSource(string Path, int FileCount, long TotalSize,
    string? HashAlgorithm, ScanScope? Scan);

public sealed record ComparisonSummary(
    int OnlyA, int OnlyB, int Different, int MatchedWithHash, int MatchedSizeOnly, int OutsideScope);

public sealed record Difference(ManifestFile A, ManifestFile B,
    bool SizeDifferent, bool HashDifferent, bool HashCompared);

public sealed record UncomparedFile(string PresentIn, ManifestFile File);

public sealed record ComparisonResult(IReadOnlyList<string> AInputs, IReadOnlyList<string> BInputs,
    ComparisonSummary Summary, IReadOnlyList<ManifestFile> OnlyA, IReadOnlyList<ManifestFile> OnlyB,
    IReadOnlyList<Difference> Different, IReadOnlyList<UncomparedFile> OutsideScope);

public sealed record ExclusionRuleDocument(IReadOnlyList<string> ExcludedDirectories = null!,
    IReadOnlyList<string> ExcludedFiles = null!);

public sealed record ExclusionRuleInput(IReadOnlyList<string> ExcludedDirectories = null!,
    IReadOnlyList<string> ExcludedFiles = null!, ExclusionRuleDocument Scan = null!);

public static class FileValues
{
    public const string Md5 = "MD5";
    public const string LightScan = "light";
    public const string HeavyScan = "heavy";
    public const string SideA = "a";
    public const string SideB = "b";
}
