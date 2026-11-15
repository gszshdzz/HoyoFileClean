using System.Text.Json.Serialization;

namespace HoyoFileClean;

public sealed record ManifestDetectionDefinition(string Target, string Match,
    bool Exact = false, bool IgnoreCase = false, bool Prefix = false);

public sealed record ManifestMetadataMatch(string Id, string Value);

public sealed record ManifestMetadataField(IReadOnlyList<ManifestMetadataMatch> Matches,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Multiple = false)
{
    public bool Conflict => IdentificationEvaluator.IsConflict(Matches.Select(match => match.Value), Multiple);

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Value => Matches.Select(match => match.Value).Distinct(StringComparer.Ordinal).Take(2).ToArray()
        is [var value] ? value : null;
}
