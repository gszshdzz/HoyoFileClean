namespace HoyoFileClean;

public sealed record IdentificationResultRule(Dictionary<string, string> When, string? Source = null,
    string? TextId = null, string? DisplayFormatTextId = null, string? RawValue = null,
    string[]? AnyDetection = null);

public sealed record IdentificationResultDefinition(string? LabelTextId = null, bool Required = false,
    bool Multiple = false, IdentificationResultRule[] Rules = null!, bool RetainConflicts = false);

public sealed record IdentificationConfiguration<TDetection>(Dictionary<string, TDetection> Detections,
    Dictionary<string, IdentificationResultDefinition> Results);

public sealed record IdentifiedValue(string? RawValue, string? TextId, string? DisplayFormatTextId,
    string? Item = null);

internal sealed record IdentificationClue(string Id, string Value, string? Item = null);
