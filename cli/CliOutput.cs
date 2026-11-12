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
        string message = error is CoreException core ? Describe(core.Code) : error switch
        {
            FileNotFoundException => L.Get("InputFileMissing"),
            DirectoryNotFoundException => L.Get("InputDirectoryMissing"),
            UnauthorizedAccessException or System.Security.SecurityException => L.Get("FilePermissionDenied"),
            ArgumentException => L.Get("InvalidPathOrArgument"),
            NotSupportedException => L.Get("UnsupportedPathOrOperation"),
            System.Security.Cryptography.CryptographicException => L.Get("HashCalculationFailed"),
            InvalidDataException => L.Get("InputDataInvalid"),
            HttpRequestException => L.Get("OfficialRequestFailed"),
            OperationCanceledException => L.Get("NetworkCancelledOrTimedOut"),
            IOException => L.Get("FilePublicationFailed"),
            _ => L.Get("ProcessingFailed")
        };
        Console.Error.WriteLine(L.Get("ErrorPrefix") + message);
        if (error is CoreException details)
        {
            Console.Error.WriteLine(L.Get("ErrorCodePrefix") + details.Code);
            if (details.Path is not null) Console.Error.WriteLine(L.Get("PathPrefix") + DisplaySource(details.Path));
            if (details.Input is not null) Console.Error.WriteLine(L.Get("InputPrefix") + DisplaySource(details.Input));
        }
        else
        {
            if (error is FileNotFoundException { FileName: { } path }) Console.Error.WriteLine(L.Get("PathPrefix") + path);
            Console.Error.WriteLine(L.Format("DiagnosticCode", error.HResult));
        }
    }

    private static string DisplaySource(string source)
        => Uri.TryCreate(source, UriKind.Absolute, out var url) && url.Scheme is "https" or "http"
            ? new UriBuilder(url) { UserName = "", Password = "", Query = "", Fragment = "" }.Uri.AbsoluteUri
            : source;

    private static string Describe(CoreErrorCode code) => code switch
    {
        CoreErrorCode.ManifestCompressedSizeLimit => L.Format("CoreErrorCode_ManifestCompressedSizeLimit", Maintenance.CompressedLimit / 1048576),
        CoreErrorCode.ManifestDecodedSizeLimit => L.Format("CoreErrorCode_ManifestDecodedSizeLimit", Maintenance.DecodedLimit / 1048576),
        CoreErrorCode.NetworkConfigurationInvalid => L.Format("CoreErrorCode_NetworkConfigurationInvalid", Maintenance.NetworkServicesRelativePath),
        CoreErrorCode.ManifestCompressionInvalid or CoreErrorCode.ManifestEncodingInvalid => L.Get("ManifestDamagedOrUnsupported"),
        _ => L.Get(ErrorTextId(code))
    };

    private static string ErrorTextId(CoreErrorCode code) => code switch
    {
        CoreErrorCode.OutputExists => "CoreErrorCode_OutputExists",
        CoreErrorCode.OutputInsideScanRoot => "CoreErrorCode_OutputInsideScanRoot",
        CoreErrorCode.OutputFileNameRequired => "CoreErrorCode_OutputFileNameRequired",
        CoreErrorCode.ManifestCompressedSizeLimit => "CoreErrorCode_ManifestCompressedSizeLimit",
        CoreErrorCode.ManifestDecodedSizeLimit => "CoreErrorCode_ManifestDecodedSizeLimit",
        CoreErrorCode.ManifestCompressionInvalid or CoreErrorCode.ManifestEncodingInvalid => "ManifestDamagedOrUnsupported",
        CoreErrorCode.ManifestEmpty => "CoreErrorCode_ManifestEmpty",
        CoreErrorCode.FileMetadataInvalid => "CoreErrorCode_FileMetadataInvalid",
        CoreErrorCode.TotalSizeOverflow => "CoreErrorCode_TotalSizeOverflow",
        CoreErrorCode.RelativePathInvalid => "CoreErrorCode_RelativePathInvalid",
        CoreErrorCode.DuplicatePath => "CoreErrorCode_DuplicatePath",
        CoreErrorCode.FileDirectoryConflict => "CoreErrorCode_FileDirectoryConflict",
        CoreErrorCode.InputsRequired => "CoreErrorCode_InputsRequired",
        CoreErrorCode.ListJsonInvalid => "CoreErrorCode_ListJsonInvalid",
        CoreErrorCode.ListHashModeInvalid => "CoreErrorCode_ListHashModeInvalid",
        CoreErrorCode.ListSummaryInvalid => "CoreErrorCode_ListSummaryInvalid",
        CoreErrorCode.InputConflict => "CoreErrorCode_InputConflict",
        CoreErrorCode.ScopePathMissing => "CoreErrorCode_ScopePathMissing",
        CoreErrorCode.ReportJsonInvalid => "CoreErrorCode_ReportJsonInvalid",
        CoreErrorCode.ReportInputsMismatch => "CoreErrorCode_ReportInputsMismatch",
        CoreErrorCode.ReportContentMismatch => "CoreErrorCode_ReportContentMismatch",
        CoreErrorCode.ScanDirectoryIsLink => "CoreErrorCode_ScanDirectoryIsLink",
        CoreErrorCode.ScanDirectoryMissing => "CoreErrorCode_ScanDirectoryMissing",
        CoreErrorCode.ScanFileTypeChanged => "CoreErrorCode_ScanFileTypeChanged",
        CoreErrorCode.ScanFileSizeChanged => "CoreErrorCode_ScanFileSizeChanged",
        CoreErrorCode.ExclusionPathInvalid => "CoreErrorCode_ExclusionPathInvalid",
        CoreErrorCode.ExclusionOutsideRoot => "CoreErrorCode_ExclusionOutsideRoot",
        CoreErrorCode.DevicePathUnsupported => "CoreErrorCode_DevicePathUnsupported",
        CoreErrorCode.DirectoryIsLink => "CoreErrorCode_DirectoryIsLink",
        CoreErrorCode.DirectoryMissing => "CoreErrorCode_DirectoryMissing",
        CoreErrorCode.NetworkConfigurationInvalid => "CoreErrorCode_NetworkConfigurationInvalid",
        CoreErrorCode.ManifestNetworkMetadataInvalid => "CoreErrorCode_ManifestNetworkMetadataInvalid",
        CoreErrorCode.ManifestChannelUnavailable => "CoreErrorCode_ManifestChannelUnavailable",
        CoreErrorCode.ManifestGameAmbiguous => "CoreErrorCode_ManifestGameAmbiguous",
        CoreErrorCode.ManifestVersionUnavailable => "CoreErrorCode_ManifestVersionUnavailable",
        CoreErrorCode.ManifestPackageUnavailable => "CoreErrorCode_ManifestPackageUnavailable",
        CoreErrorCode.ManifestDownloadMismatch => "CoreErrorCode_ManifestDownloadMismatch",
        CoreErrorCode.ManifestNetworkSizeLimit => "CoreErrorCode_ManifestNetworkSizeLimit",
        CoreErrorCode.ManifestNetworkUnsupported => "CoreErrorCode_ManifestNetworkUnsupported",
        _ => "UnknownCoreError"
    };

    public static void Usage()
    {
        foreach (string id in new[] { "HelpLocalConvert", "HelpNetworkConvert", "HelpScan",
            "HelpCompare", "HelpManifestFormat", "HelpScanMode", "HelpExclusions",
            "HelpReadOnlySafety", "HelpNetworkSelection", "HelpOfficialIdentifiers", "HelpNetworkExample", "HelpComparisonRules" })
            Console.WriteLine(L.Get(id));
    }
}
