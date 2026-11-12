namespace HoyoFileClean;

public enum CoreErrorCode
{
    OutputExists, OutputInsideScanRoot, OutputFileNameRequired,
    ManifestCompressedSizeLimit, ManifestDecodedSizeLimit, ManifestCompressionInvalid,
    ManifestEncodingInvalid, ManifestEmpty, FileMetadataInvalid, TotalSizeOverflow,
    RelativePathInvalid, DuplicatePath, FileDirectoryConflict,
    InputsRequired, ListJsonInvalid, ListHashModeInvalid, ListSummaryInvalid,
    InputConflict, ScopePathMissing, ReportJsonInvalid, ReportInputsMismatch, ReportContentMismatch,
    ScanDirectoryIsLink, ScanDirectoryMissing, ScanFileTypeChanged, ScanFileSizeChanged,
    ExclusionPathInvalid, ExclusionOutsideRoot, DevicePathUnsupported, DirectoryIsLink, DirectoryMissing,
    ManifestNetworkMetadataInvalid, ManifestChannelUnavailable, ManifestDownloadMismatch,
    ManifestNetworkSizeLimit, ManifestNetworkUnsupported, ManifestVersionUnavailable, ManifestPackageUnavailable,
    NetworkConfigurationInvalid
}

// Diagnostic data only. Front ends own wording, localization and recovery advice.
public sealed class CoreException(CoreErrorCode code, string? path = null,
    string? input = null, Exception? innerException = null)
    : Exception(code.ToString(), innerException)
{
    public CoreErrorCode Code { get; } = code;
    public string? Path { get; } = path;
    public string? Input { get; } = input;
}
