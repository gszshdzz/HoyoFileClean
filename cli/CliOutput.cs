namespace HoyoFileClean.Cli;

internal static class CliOutput
{
    public static void Conversion(ConversionSummary value)
        => Console.WriteLine(L.Format("ConversionCompleted", value.FileCount, value.TotalSize));

    public static void Scan(ScanSummary value, bool hashes)
        => Console.WriteLine(L.Format("ScanCompleted", hashes ? L.Get("HeavyScanPrefix") : L.Get("LightScanPrefix"), value.FileCount, value.TotalSize, value.SkippedLinks.Count));

    public static void SkippedLink(string path)
        => Console.Error.WriteLine(L.Format("SkippedLinkWarning", path));

    public static void Comparison(ComparisonSummary value)
    {
        Console.WriteLine(L.Format("ComparisonCounts", value.OnlyA, value.OnlyB, value.Different));
        Console.WriteLine(L.Format("ComparisonVerificationCounts", value.MatchedWithHash, value.MatchedSizeOnly, value.OutsideScope));
    }

    public static void Saved(string path) => Console.WriteLine(L.Format("SavedPath", Path.GetFullPath(path)));

    public static void ArgumentError(string message) => Console.Error.WriteLine(L.Get("ArgumentErrorPrefix") + message);

    public static void Error(Exception error)
    {
        string message = DiagnosticText.ForCli(error);
        Console.Error.WriteLine(L.Get("ErrorPrefix") + message);
        if (error is CoreException details)
        {
            Console.Error.WriteLine(L.Get("ErrorCodePrefix") + details.Code);
            if (details.Path is not null) Console.Error.WriteLine(L.Get("PathPrefix") + DiagnosticText.DisplaySource(details.Path));
            if (details.Input is not null) Console.Error.WriteLine(L.Get("InputPrefix") + DiagnosticText.DisplaySource(details.Input));
        }
        else
        {
            if (error is FileNotFoundException { FileName: { } path }) Console.Error.WriteLine(L.Get("PathPrefix") + path);
            Console.Error.WriteLine(L.Format("DiagnosticCode", error.HResult));
        }
    }

    public static void Usage()
    {
        foreach (string id in new[] { "HelpLocalConvert", "HelpNetworkConvert", "HelpScan",
            "HelpCompare", "HelpManifestFormat", "HelpScanMode", "HelpExclusions",
            "HelpReadOnlySafety", "HelpNetworkSelection", "HelpOfficialIdentifiers", "HelpNetworkExample", "HelpComparisonRules" })
            Console.WriteLine(L.Get(id));
    }
}
