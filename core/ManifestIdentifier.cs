using System.Collections;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace HoyoFileClean;

// Rules contain all adaptation data. This engine only selects strings and collects matches.
public sealed class ManifestIdentifier
{
    private static readonly JsonSerializerOptions Contract = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };
    private readonly (string Id, Func<object?, Func<string, bool>, CancellationToken, bool>[] Read,
        bool CollectAll)[] rules;
    private readonly IReadOnlyDictionary<string, IdentificationResultDefinition> results;

    public ManifestIdentifier(IdentificationConfiguration<ManifestDetectionDefinition[]> configuration)
    {
        IdentificationEvaluator.Validate(configuration.Detections.Keys, configuration.Results);
        if (configuration.Results.Values.SelectMany(result => result.Rules)
            .Any(rule => rule.TextId is not null || rule.DisplayFormatTextId is not null))
            throw new ArgumentException("Manifest metadata requires raw values, not presentation text IDs.", nameof(configuration));
        results = configuration.Results.ToDictionary(pair => pair.Key,
            pair => pair.Value with { Rules = pair.Value.Rules.Select(rule => rule with
            {
                When = new Dictionary<string, string>(rule.When, StringComparer.Ordinal),
                AnyDetection = rule.AnyDetection?.ToArray()
            }).ToArray() }, StringComparer.Ordinal);
        rules = configuration.Detections.Select(pair =>
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is not { Length: > 0 })
                throw new ArgumentException("Each detection requires an ID and selectors.", nameof(configuration));
            return (pair.Key, pair.Value.Select(rule =>
            {
                if (rule is null || string.IsNullOrWhiteSpace(rule.Target) || string.IsNullOrEmpty(rule.Match)
                    || (rule.Exact && rule.Prefix))
                    throw new ArgumentException("Each selector requires a target and match.", nameof(configuration));
                StringComparison comparison = rule.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                bool Match(string value) => rule.Exact ? value.Equals(rule.Match, comparison)
                    : rule.Prefix ? value.StartsWith(rule.Match, comparison) : value.Contains(rule.Match, comparison);
                return CompileRead(typeof(FileListDocument), rule.Target.Split('.'), 0, Match);
            }).ToArray(), configuration.Results.Values.SelectMany(result => result.Rules)
                .Any(result => result.Source == pair.Key || result.When.ContainsKey(pair.Key)));
        }).ToArray();
    }

    public IReadOnlyDictionary<string, ManifestMetadataField> Identify(FileListDocument document,
        CancellationToken cancellationToken = default)
    {
        var detections = new List<IdentificationClue>();
        foreach (var (id, selectors, collectAll) in rules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var values = new HashSet<string>(StringComparer.Ordinal);
            bool Collect(string value) { values.Add(value); return !collectAll; }
            foreach (var read in selectors)
                if (read(document, Collect, cancellationToken)) break;
            foreach (string value in values) detections.Add(new(id, value));
        }
        var identified = IdentificationEvaluator.Evaluate(detections, results, cancellationToken);
        return new ReadOnlyDictionary<string, ManifestMetadataField>(identified.Where(pair => pair.Value.Matches.Count > 0)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key,
                pair => new ManifestMetadataField(Array.AsReadOnly(pair.Value.Matches.Select(match =>
                    new ManifestMetadataMatch(match.Id, match.Value.RawValue!)).ToArray()), pair.Value.Multiple), StringComparer.Ordinal));
    }

    private static Func<object?, Func<string, bool>, CancellationToken, bool> CompileRead(Type type, string[] path,
        int index, Func<string, bool> match)
    {
        if (type == typeof(string) && index == path.Length)
            return (value, collect, _) => value is string text && match(text) && collect(text);
        Type? sequence = type.GetInterfaces().Append(type)
            .FirstOrDefault(item => item.IsGenericType && item.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        if (sequence is not null && type != typeof(string))
        {
            var readItem = CompileRead(sequence.GetGenericArguments()[0], path, index, match);
            return (value, collect, token) =>
            {
                if (value is not IEnumerable items) return false;
                foreach (object? item in items)
                {
                    token.ThrowIfCancellationRequested();
                    if (readItem(item, collect, token)) return true;
                }
                return false;
            };
        }
        JsonPropertyInfo? property = index < path.Length
            ? Contract.GetTypeInfo(type).Properties.FirstOrDefault(item => item.Name == path[index]) : null;
        if (property?.Get is not { } get)
            throw new ArgumentException($"Target '{string.Join('.', path)}' must select an existing string field.", nameof(path));
        var next = CompileRead(property.PropertyType, path, index + 1, match);
        return (value, collect, token) => value is not null && next(get(value), collect, token);
    }
}
