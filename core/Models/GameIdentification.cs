namespace HoyoFileClean;

public sealed record DetectionDefinition(string[] Paths, string Pattern, bool CanCompare,
    bool JsonObjectKeys = false, string[]? IgnoredKeys = null);

public sealed record InformationRule(Dictionary<string, string> When, string? Source = null,
    string? TextId = null, string? DisplayFormatTextId = null);

public sealed record InformationDefinition(string LabelTextId, bool Required, bool Multiple,
    InformationRule[] Rules);

public sealed record GameIdentificationConfiguration(
    Dictionary<string, DetectionDefinition> Detections,
    Dictionary<string, InformationDefinition> Information);

public sealed record IdentifiedValue(string? RawValue, string? TextId, string? DisplayFormatTextId,
    string? Item = null);

public sealed record IdentifiedInformation(string LabelTextId, IReadOnlyList<IdentifiedValue> Values);

public sealed record GameIdentification(IReadOnlyDictionary<string, string> Detections,
    IReadOnlyDictionary<string, IdentifiedInformation> Information);
