namespace HoyoFileClean.Presentation;

// Shared interpretation; the front ends still own output layout and interaction.
public static class DiagnosticText
{
    public static string ForCli(Exception error) => error switch
    {
        CoreException core => DescribeCore(core.Code),
        FileNotFoundException => Text.Get("InputFileMissing"),
        DirectoryNotFoundException => Text.Get("InputDirectoryMissing"),
        UnauthorizedAccessException or System.Security.SecurityException => Text.Get("FilePermissionDenied"),
        ArgumentException => Text.Get("InvalidPathOrArgument"),
        NotSupportedException => Text.Get("UnsupportedPathOrOperation"),
        System.Security.Cryptography.CryptographicException => Text.Get("HashCalculationFailed"),
        InvalidDataException => Text.Get("InputDataInvalid"),
        HttpRequestException => Text.Get("OfficialRequestFailed"),
        OperationCanceledException => Text.Get("NetworkCancelledOrTimedOut"),
        IOException => Text.Get("FilePublicationFailed"),
        _ => Text.Get("ProcessingFailed")
    };

    public static string ForDesktop(Exception error) => error switch
    {
        CoreException { Code: CoreErrorCode.OutputExists } core => DescribeCore(core.Code),
        CoreException { Code: CoreErrorCode.InputConflict } => Text.Get("DesktopInputConflict"),
        CoreException { Code: CoreErrorCode.OutputInsideScanRoot } => Text.Get("DesktopOutputInsideRoot"),
        CoreException { Code: CoreErrorCode.ManifestChannelUnavailable or CoreErrorCode.ManifestVersionUnavailable } => Text.Get("DesktopVersionUnavailable"),
        CoreException { Code: CoreErrorCode.ManifestGameAmbiguous } => Text.Get("DesktopGameAmbiguous"),
        CoreException { Code: CoreErrorCode.ManifestDownloadMismatch } => Text.Get("DesktopDownloadMismatch"),
        CoreException { Code: CoreErrorCode.ManifestNetworkSizeLimit } core => DescribeCore(core.Code),
        CoreException { Code: CoreErrorCode.NetworkConfigurationInvalid } => Text.Format("DesktopConfigurationInvalid", Maintenance.NetworkServicesRelativePath),
        CoreException { Code: CoreErrorCode.IdentificationConfigurationInvalid or CoreErrorCode.GameIdentificationConfigurationInvalid or CoreErrorCode.GameIdentificationFailed
            or CoreErrorCode.GameDetectionInvalid or CoreErrorCode.GameDetectionConflict } core => DescribeCore(core.Code),
        CoreException { Code: CoreErrorCode.ManifestNetworkMetadataInvalid or CoreErrorCode.ManifestNetworkUnsupported } => Text.Get("DesktopNetworkMetadataInvalid"),
        CoreException => Text.Get("DesktopListInvalid"),
        FileNotFoundException => Text.Get("DesktopFileMissing"),
        DirectoryNotFoundException => Text.Get("DesktopDirectoryMissing"),
        UnauthorizedAccessException or System.Security.SecurityException => Text.Get("DesktopPermissionDenied"),
        InvalidDataException => Text.Get("DesktopBatchInvalid"),
        HttpRequestException => ForCli(error),
        OperationCanceledException => Text.Get("DesktopNetworkCancelled"),
        IOException => Text.Get("DesktopIoFailed"),
        ArgumentException or NotSupportedException => Text.Get("DesktopArgumentInvalid"),
        _ => Text.Get("DesktopOperationFailed")
    };

    public static string DisplaySource(string source)
        => Uri.TryCreate(source, UriKind.Absolute, out var url) && url.Scheme is "https" or "http"
            ? new UriBuilder(url) { UserName = "", Password = "", Query = "", Fragment = "" }.Uri.AbsoluteUri
            : source;

    private static string DescribeCore(CoreErrorCode code) => code switch
    {
        CoreErrorCode.ManifestCompressedSizeLimit => Text.Format("CoreErrorCode_ManifestCompressedSizeLimit", Maintenance.CompressedLimit / 1048576),
        CoreErrorCode.ManifestDecodedSizeLimit => Text.Format("CoreErrorCode_ManifestDecodedSizeLimit", Maintenance.DecodedLimit / 1048576),
        CoreErrorCode.NetworkConfigurationInvalid => Text.Format("CoreErrorCode_NetworkConfigurationInvalid", Maintenance.NetworkServicesRelativePath),
        CoreErrorCode.ManifestCompressionInvalid or CoreErrorCode.ManifestEncodingInvalid => Text.Get("ManifestDamagedOrUnsupported"),
        CoreErrorCode.OutputExists => Text.Get("CoreErrorCode_OutputExists"),
        CoreErrorCode.OutputInsideScanRoot => Text.Get("CoreErrorCode_OutputInsideScanRoot"),
        CoreErrorCode.OutputFileNameRequired => Text.Get("CoreErrorCode_OutputFileNameRequired"),
        CoreErrorCode.ManifestEmpty => Text.Get("CoreErrorCode_ManifestEmpty"),
        CoreErrorCode.FileMetadataInvalid => Text.Get("CoreErrorCode_FileMetadataInvalid"),
        CoreErrorCode.TotalSizeOverflow => Text.Get("CoreErrorCode_TotalSizeOverflow"),
        CoreErrorCode.RelativePathInvalid => Text.Get("CoreErrorCode_RelativePathInvalid"),
        CoreErrorCode.DuplicatePath => Text.Get("CoreErrorCode_DuplicatePath"),
        CoreErrorCode.FileDirectoryConflict => Text.Get("CoreErrorCode_FileDirectoryConflict"),
        CoreErrorCode.InputsRequired => Text.Get("CoreErrorCode_InputsRequired"),
        CoreErrorCode.ListJsonInvalid => Text.Get("CoreErrorCode_ListJsonInvalid"),
        CoreErrorCode.ListHashModeInvalid => Text.Get("CoreErrorCode_ListHashModeInvalid"),
        CoreErrorCode.ListSummaryInvalid => Text.Get("CoreErrorCode_ListSummaryInvalid"),
        CoreErrorCode.InputConflict => Text.Get("CoreErrorCode_InputConflict"),
        CoreErrorCode.ScopePathMissing => Text.Get("CoreErrorCode_ScopePathMissing"),
        CoreErrorCode.ReportJsonInvalid => Text.Get("CoreErrorCode_ReportJsonInvalid"),
        CoreErrorCode.ReportInputsMismatch => Text.Get("CoreErrorCode_ReportInputsMismatch"),
        CoreErrorCode.ReportContentMismatch => Text.Get("CoreErrorCode_ReportContentMismatch"),
        CoreErrorCode.ScanDirectoryIsLink => Text.Get("CoreErrorCode_ScanDirectoryIsLink"),
        CoreErrorCode.ScanDirectoryMissing => Text.Get("CoreErrorCode_ScanDirectoryMissing"),
        CoreErrorCode.ScanFileTypeChanged => Text.Get("CoreErrorCode_ScanFileTypeChanged"),
        CoreErrorCode.ScanFileSizeChanged => Text.Get("CoreErrorCode_ScanFileSizeChanged"),
        CoreErrorCode.ExclusionPathInvalid => Text.Get("CoreErrorCode_ExclusionPathInvalid"),
        CoreErrorCode.ExclusionOutsideRoot => Text.Get("CoreErrorCode_ExclusionOutsideRoot"),
        CoreErrorCode.DevicePathUnsupported => Text.Get("CoreErrorCode_DevicePathUnsupported"),
        CoreErrorCode.DirectoryIsLink => Text.Get("CoreErrorCode_DirectoryIsLink"),
        CoreErrorCode.DirectoryMissing => Text.Get("CoreErrorCode_DirectoryMissing"),
        CoreErrorCode.ManifestNetworkMetadataInvalid => Text.Get("CoreErrorCode_ManifestNetworkMetadataInvalid"),
        CoreErrorCode.ManifestChannelUnavailable => Text.Get("CoreErrorCode_ManifestChannelUnavailable"),
        CoreErrorCode.ManifestGameAmbiguous => Text.Get("CoreErrorCode_ManifestGameAmbiguous"),
        CoreErrorCode.ManifestVersionUnavailable => Text.Get("CoreErrorCode_ManifestVersionUnavailable"),
        CoreErrorCode.ManifestPackageUnavailable => Text.Get("CoreErrorCode_ManifestPackageUnavailable"),
        CoreErrorCode.ManifestDownloadMismatch => Text.Get("CoreErrorCode_ManifestDownloadMismatch"),
        CoreErrorCode.ManifestNetworkSizeLimit => Text.Get("CoreErrorCode_ManifestNetworkSizeLimit"),
        CoreErrorCode.ManifestNetworkUnsupported => Text.Get("CoreErrorCode_ManifestNetworkUnsupported"),
        CoreErrorCode.IdentificationConfigurationInvalid => Text.Get("CoreErrorCode_IdentificationConfigurationInvalid"),
        CoreErrorCode.GameIdentificationConfigurationInvalid => Text.Get("CoreErrorCode_GameIdentificationConfigurationInvalid"),
        CoreErrorCode.GameIdentificationFailed => Text.Get("CoreErrorCode_GameIdentificationFailed"),
        CoreErrorCode.GameDetectionInvalid => Text.Get("CoreErrorCode_GameDetectionInvalid"),
        CoreErrorCode.GameDetectionConflict => Text.Get("CoreErrorCode_GameDetectionConflict"),
        _ => Text.Get("UnknownCoreError")
    };
}
