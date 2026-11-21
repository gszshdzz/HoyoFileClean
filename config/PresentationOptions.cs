namespace HoyoFileClean.Presentation;

// Build-time presentation defaults; no resource loading or UI behavior here.
public static class PresentationOptions
{
    public const string DefaultLanguage = "zh-CN";
    public const int FileSizeSignificantDigits = 3;
    public const int FileSizeUnitBase = 1024;
    public const int FileOperationWarningExampleCount = 5;
    public static IReadOnlyList<string> FileSizeUnitTextIds { get; } =
        Array.AsReadOnly(new[] { "UnitB", "UnitKB", "UnitMB", "UnitGB", "UnitTB", "UnitPB", "UnitEB" });

    public static IReadOnlyDictionary<string, string> ManifestBranchTextIds { get; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["main"] = "ManifestBranchMain",
            ["predownload"] = "ManifestBranchPreDownload"
        });
}
