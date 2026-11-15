namespace HoyoFileClean;

public sealed record GameDetectionDefinition(string[] Paths, string Pattern, bool CanCompare,
    bool JsonObjectKeys = false, string[]? IgnoredKeys = null);

public sealed record IdentifiedInformation(string LabelTextId, IReadOnlyList<IdentifiedValue> Values);

public sealed record GameIdentification(IReadOnlyDictionary<string, string> Detections,
    IReadOnlyDictionary<string, IdentifiedInformation> Information);
