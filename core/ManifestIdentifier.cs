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
    private readonly (string Id, KeyValuePair<string, string>[] Results, Func<object?, CancellationToken, bool> Matches)[] rules;

    public ManifestIdentifier(IEnumerable<ManifestIdentificationRule> detections)
    {
        var definitions = new Dictionary<string, KeyValuePair<string, string>[]>(StringComparer.Ordinal);
        this.rules = detections.Select(rule =>
        {
            if (string.IsNullOrWhiteSpace(rule.Target) || string.IsNullOrEmpty(rule.Match)
                || string.IsNullOrWhiteSpace(rule.Id) || rule.Results is null || rule.Results.Count == 0
                || (rule.Exact && rule.Prefix))
                throw new ArgumentException("Each detection requires a target, match, ID and results.", nameof(detections));
            var outputs = rule.Results.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
            if (definitions.TryGetValue(rule.Id, out var previous) && !previous.SequenceEqual(outputs))
                throw new ArgumentException("Detections sharing an ID must have identical results.", nameof(detections));
            definitions[rule.Id] = outputs;
            foreach (var (field, value) in outputs)
            {
                if (string.IsNullOrWhiteSpace(field) || string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException("Each result requires a field and value.", nameof(detections));
            }
            StringComparison comparison = rule.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            bool Match(string value) => rule.Exact ? value.Equals(rule.Match, comparison)
                : rule.Prefix ? value.StartsWith(rule.Match, comparison) : value.Contains(rule.Match, comparison);
            return (rule.Id, outputs, CompileMatch(typeof(FileListDocument), rule.Target.Split('.'), 0, Match));
        }).ToArray();
    }

    public IReadOnlyDictionary<string, ManifestMetadataField> Identify(FileListDocument document,
        CancellationToken cancellationToken = default)
    {
        var matches = new Dictionary<string, Dictionary<string, ManifestMetadataMatch>>(StringComparer.Ordinal);
        foreach (var (id, outputs, matchesTarget) in rules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!matchesTarget(document, cancellationToken)) continue;
            foreach (var (field, value) in outputs)
            {
                if (!matches.TryGetValue(field, out var found)) matches[field] = found = new(StringComparer.Ordinal);
                found.TryAdd(id, new ManifestMetadataMatch(id, value));
            }
        }
        return new ReadOnlyDictionary<string, ManifestMetadataField>(matches.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => new ManifestMetadataField(Array.AsReadOnly(pair.Value.Values
                .OrderBy(match => match.Id, StringComparer.Ordinal).ToArray())), StringComparer.Ordinal));
    }

    private static Func<object?, CancellationToken, bool> CompileMatch(Type type, string[] path, int index, Func<string, bool> match)
    {
        if (type == typeof(string) && index == path.Length)
            return (value, _) => value is string text && match(text);
        Type? sequence = type.GetInterfaces().Append(type)
            .FirstOrDefault(item => item.IsGenericType && item.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        if (sequence is not null && type != typeof(string))
        {
            var matchItem = CompileMatch(sequence.GetGenericArguments()[0], path, index, match);
            return (value, token) =>
            {
                if (value is not IEnumerable items) return false;
                foreach (object? item in items)
                {
                    token.ThrowIfCancellationRequested();
                    if (matchItem(item, token)) return true;
                }
                return false;
            };
        }
        JsonPropertyInfo? property = index < path.Length
            ? Contract.GetTypeInfo(type).Properties.FirstOrDefault(item => item.Name == path[index]) : null;
        if (property?.Get is not { } get)
            throw new ArgumentException($"Target '{string.Join('.', path)}' must select an existing string field.", nameof(path));
        var next = CompileMatch(property.PropertyType, path, index + 1, match);
        return (value, token) => value is not null && next(get(value), token);
    }
}
