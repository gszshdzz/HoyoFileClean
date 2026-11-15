using System.Text.Json.Serialization;

namespace HoyoFileClean;

public sealed record ManifestIdentificationRule(string Target, string Match, string Id, IReadOnlyDictionary<string, string> Results,
    bool Exact = false, bool IgnoreCase = false, bool Prefix = false);

public sealed record ManifestMetadataMatch(string Id, string Value);

public sealed record ManifestMetadataField(IReadOnlyList<ManifestMetadataMatch> Matches)
{
    public bool Conflict => Matches.Select(match => match.Value).Distinct(StringComparer.Ordinal).Skip(1).Any();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Value => Matches.Count > 0 && !Conflict ? Matches[0].Value : null;
}
