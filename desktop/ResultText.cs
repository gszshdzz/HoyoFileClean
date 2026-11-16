namespace HoyoFileClean.Desktop;

internal static class ResultText
{
    internal static string IdentityField<T>(IReadOnlyDictionary<string, T>? information)
        => information?.ContainsKey("biz") == true ? "biz" : "game";

    internal static string DescribeIdentification(GameIdentification identification)
    {
        string DescribeValue(IdentifiedValue value) => value.TextId is { } textId
            ? L.Get(textId) : value.DisplayFormatTextId is { } formatId
                ? L.Format(formatId, value.RawValue!, value.Item ?? "") : value.RawValue!;
        string Describe(IdentifiedInformation info) =>
            L.Format("IdentifiedInformation", L.Get(info.LabelTextId), info.Values.Count == 0
                ? L.Get("IdentificationNotDetected")
                : string.Join(L.Get("ListSeparator"), info.Values.Select(DescribeValue)));
        string identity = IdentityField(identification.Information);
        string[] primary = [identity, "version"];
        string header = string.Join(L.Get("ListSeparator"), primary.Where(identification.Information.ContainsKey)
            .Select(id => Describe(identification.Information[id])));
        return string.Join(Environment.NewLine, new[] { header }.Where(value => value.Length > 0)
            .Concat(identification.Information.Where(pair => !primary.Contains(pair.Key)
                && pair.Key != "game").Select(pair => Describe(pair.Value))));
    }

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

    public static string Describe(ComparisonResult? result)
    {
        if (result is null) return L.Get("ComparisonNotStarted");
        ComparisonSummary summary = result.Summary;
        return L.Format("DifferenceCounts", summary.OnlyA, summary.OnlyB, summary.Different, summary.OutsideScope)
            + L.Format("VerificationCounts", summary.MatchedWithHash, summary.MatchedSizeOnly)
            + L.Get("DifferenceMeaningWarning");
    }
}
