namespace HoyoFileClean;

// Maintainer-owned build-time policy. These are not end-user settings.
public static class Maintenance
{
    public const string NetworkServicesRelativePath = "config/NetworkServices.cs";
    public const string GameIdentificationRelativePath = "config/GameIdentificationRules.cs";
    public static readonly TimeSpan IdentificationMatchTimeout = TimeSpan.FromSeconds(1);
    public static readonly IReadOnlyList<string> ManifestDirectorySegments =
        Array.AsReadOnly(new[] { "miHoYo", "HYP", "1_1", "modules", "sophon", "manifest" });

    public const int MetadataLimit = 2 * 1024 * 1024;
    public const int CompressedLimit = 64 * 1024 * 1024;
    public const int DecodedLimit = 128 * 1024 * 1024;
    public const int HashBufferSize = 128 * 1024;
    public const int DownloadBufferSize = 81920;
    public const int FormatProbeLimit = 4096;
    public const int ScanProgressIntervalMs = 500;
    public const int ParsingProgressIntervalMs = 100;
    public const int NetworkChoiceVisibleRows = 6;
    public const int DefaultParsingConcurrency = 1;
    public const int MaximumParsingConcurrency = 128;
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan AddressTimeout = TimeSpan.FromSeconds(1.5);
    public static readonly TimeSpan AddressRampInterval = TimeSpan.FromMilliseconds(500);
}
