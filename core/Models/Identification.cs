namespace HoyoFileClean;

public sealed record IdentificationMeaning(string? RawValue = null, bool FromDetection = false,
    string? LabelTextId = null, bool Required = false, bool Multiple = false,
    bool RetainConflicts = false, string? TextId = null, string? DisplayFormatTextId = null);

public sealed record IdentificationConfiguration<TDetection>(Dictionary<string, TDetection> Detections);

public sealed record IdentifiedValue(string? RawValue, string? TextId, string? DisplayFormatTextId,
    string? Item = null);

internal sealed record IdentificationClue(string Id, string Value, string? Item = null);
