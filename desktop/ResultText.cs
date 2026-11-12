using System.Text;

namespace HoyoFileClean.Desktop;

internal static class ResultText
{
    internal static string DescribeGameStatus(FileListSnapshot snapshot, bool imported)
    {
        ScanScope scan = snapshot.Sources[0].Scan!;
        string mode = scan.Mode == FileValues.HeavyScan ? L.Get("HeavyScanLabel") : L.Get("LightScanDescription");
        string text = imported ? L.Format("HistoricalScanCompleted", mode) : L.Format("MemoryScanCompleted", mode);
        int excluded = scan.ExcludedDirectories.Count + scan.ExcludedFiles.Count;
        if (excluded > 0) text += L.Format("ExcludedItemCount", excluded);
        if (scan.SkippedLinks.Count > 0) text += L.Format("SkippedLinkCount", scan.SkippedLinks.Count);
        if (excluded > 0 || scan.SkippedLinks.Count > 0) text += L.Get("ScopeNotScanned");
        return text;
    }

    public static string Describe(FileListSnapshot? snapshot, string title)
    {
        if (snapshot is null) return title + L.Get("ResultMissingSuffix");
        var text = new StringBuilder();
        text.AppendLine(L.Format("SnapshotSummary", title, snapshot.FileCount, snapshot.TotalSize));
        text.AppendLine(L.Format("RecordedHashCount", snapshot.HashedFileCount));
        foreach (FileListSource source in snapshot.Sources)
        {
            text.AppendLine();
            text.AppendLine(L.Get("SourcePrefix") + DiagnosticText.DisplaySource(source.Path));
            text.AppendLine(L.Format("SourceSummary", source.FileCount, source.TotalSize, source.HashAlgorithm ?? L.Get("SizeOnlyHashDescription")));
            if (source.Scan is { } scan)
            {
                text.AppendLine(L.Get("RecordedRootPrefix") + scan.Root);
                text.AppendLine(L.Get("ScanModePrefix") + (scan.Mode == FileValues.LightScan ? L.Get("LightScanDescription") : L.Get("HeavyScanLabel")));
                text.AppendLine(L.Get("ExcludedDirectoriesPrefix") + Join(scan.ExcludedDirectories));
                text.AppendLine(L.Get("ExcludedFilesPrefix") + Join(scan.ExcludedFiles));
                text.AppendLine(L.Get("SkippedLinksPrefix") + Join(scan.SkippedLinks));
                if (scan.ExcludedDirectories.Count + scan.ExcludedFiles.Count + scan.SkippedLinks.Count > 0)
                    text.AppendLine(L.Get("UnscannedScopeWarning"));
            }
            else text.AppendLine(L.Get("ManifestCoverageWarning"));
        }
        return text.ToString();
    }

    public static string Describe(ComparisonResult? result)
    {
        if (result is null) return L.Get("ComparisonNotStarted");
        ComparisonSummary summary = result.Summary;
        return L.Format("DifferenceCounts", summary.OnlyA, summary.OnlyB, summary.Different, summary.OutsideScope)
            + L.Format("VerificationCounts", summary.MatchedWithHash, summary.MatchedSizeOnly)
            + L.Get("DifferenceMeaningWarning");
    }

    private static string Join(IEnumerable<string> paths) => string.Join(L.Get("ListSeparator"), paths) is { Length: > 0 } value ? value : L.Get("NoneLabel");
}
