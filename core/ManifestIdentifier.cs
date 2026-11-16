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
    private readonly IReadOnlyDictionary<string, IdentificationEvaluator.Definition> meanings;

    public ManifestIdentifier(IdentificationConfiguration<ManifestDetectionDefinition[]> configuration)
    {
        if (configuration?.Detections is null)
            throw new CoreException(CoreErrorCode.IdentificationConfigurationInvalid);
        foreach (var (id, selectors) in configuration.Detections)
        {
            if (string.IsNullOrWhiteSpace(id) || selectors is not { Length: > 0 }
                || selectors.Any(selector => selector is null || selector.Meanings is not { Count: > 0 }))
                throw new CoreException(CoreErrorCode.IdentificationConfigurationInvalid, input: id);
            var first = selectors[0].Meanings!;
            if (selectors.Any(selector => selector.Meanings!.Count != first.Count
                || first.Any(pair => !selector.Meanings.TryGetValue(pair.Key, out var meaning) || meaning != pair.Value)))
                throw new CoreException(CoreErrorCode.IdentificationConfigurationInvalid, input: id);
        }
        meanings = IdentificationEvaluator.Compile(configuration.Detections.SelectMany(pair =>
            pair.Value.Select(selector => (pair.Key, selector.Meanings))));
        if (meanings.Values.SelectMany(field => field.Mappings)
            .Any(mapping => mapping.Meaning.TextId is not null || mapping.Meaning.DisplayFormatTextId is not null))
            throw new CoreException(CoreErrorCode.IdentificationConfigurationInvalid);
        rules = configuration.Detections.Select(pair =>
        {
            return (pair.Key, pair.Value.Select(rule =>
            {
                if (rule is null || string.IsNullOrWhiteSpace(rule.Target) || string.IsNullOrEmpty(rule.Match)
                    || (rule.Exact && rule.Prefix))
                    throw new CoreException(CoreErrorCode.IdentificationConfigurationInvalid, input: pair.Key);
                StringComparison comparison = rule.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                bool Match(string value) => rule.Exact ? value.Equals(rule.Match, comparison)
                    : rule.Prefix ? value.StartsWith(rule.Match, comparison) : value.Contains(rule.Match, comparison);
                return CompileRead(typeof(FileListDocument), rule.Target.Split('.'), 0, Match);
            }).ToArray(), meanings.Values.SelectMany(field => field.Mappings)
                .Any(mapping => mapping.Id == pair.Key && mapping.Meaning.FromDetection));
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
        var identified = IdentificationEvaluator.Evaluate(detections, meanings, cancellationToken);
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
            throw new CoreException(CoreErrorCode.IdentificationConfigurationInvalid, input: string.Join('.', path));
        var next = CompileRead(property.PropertyType, path, index + 1, match);
        return (value, collect, token) => value is not null && next(get(value), collect, token);
    }
}
