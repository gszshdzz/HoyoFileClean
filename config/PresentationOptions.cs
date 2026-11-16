namespace HoyoFileClean.Presentation;

// Build-time presentation defaults; no resource loading or UI behavior here.
public static class PresentationOptions
{
    public const string DefaultLanguage = "zh-CN";

    public static IReadOnlyDictionary<string, string> ManifestBranchTextIds { get; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["main"] = "ManifestBranchMain",
            ["predownload"] = "ManifestBranchPreDownload"
        });
}
