namespace HoyoFileClean;

public sealed record GameDetectionDefinition(string[] Paths, string? Pattern = null, bool CanCompare = false,
    bool JsonObjectKeys = false, string[]? IgnoredKeys = null,
    Dictionary<string, IdentificationMeaning>? Meanings = null, bool Prefix = false);

public sealed record IdentifiedInformation(string LabelTextId, IReadOnlyList<IdentifiedValue> Values);

public sealed record GameIdentification(IReadOnlyDictionary<string, string> Detections,
    IReadOnlyDictionary<string, IdentifiedInformation> Information);

public sealed record GameFilePreset(IReadOnlyList<string> ScanDirectories,
    IReadOnlyList<string> ScanFiles, IReadOnlyList<string>? ResultHiddenFiles = null);
