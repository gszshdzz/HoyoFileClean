using System.Collections.ObjectModel;

namespace HoyoFileClean;

// Both readers supply clues; mapping, deduplication, evidence and constraints live here.
internal static class IdentificationEvaluator
{
    internal sealed record Match(string Id, IdentifiedValue Value);
    internal sealed record Result(IReadOnlyList<IdentifiedValue> Values, IReadOnlyList<Match> Matches,
        bool Multiple)
    {
        internal bool Conflict => IsConflict(Values, Multiple);
    }

    internal static bool IsConflict<T>(IEnumerable<T> values, bool multiple)
        => !multiple && values.Distinct().Skip(1).Any();

    internal static void Validate(IEnumerable<string> detectionIds,
        IReadOnlyDictionary<string, IdentificationResultDefinition> results)
    {
        ArgumentNullException.ThrowIfNull(detectionIds);
        ArgumentNullException.ThrowIfNull(results);
        var known = detectionIds.ToHashSet(StringComparer.Ordinal);
        if (results.Count == 0) throw new ArgumentException("Identification requires results.");
        foreach (var (id, definition) in results)
        {
            if (string.IsNullOrWhiteSpace(id) || definition is null || definition.Rules is not { Length: > 0 }
                || (definition.Multiple && definition.RetainConflicts))
                throw new ArgumentException("Invalid identification result definition.");
            var unconditionalOutputs = new Dictionary<string, IdentifiedValue>(StringComparer.Ordinal);
            foreach (IdentificationResultRule rule in definition.Rules)
            {
                if (rule is null || rule.When is null
                    || rule.When.Any(pair => !known.Contains(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                    || (rule.AnyDetection is not null && (rule.AnyDetection.Length == 0
                        || rule.AnyDetection.Any(id => !known.Contains(id))))
                    || new[] { rule.Source, rule.TextId, rule.RawValue }.Count(value => value is not null) != 1
                    || (rule.Source is not null && !known.Contains(rule.Source))
                    || (rule.TextId is not null && string.IsNullOrWhiteSpace(rule.TextId))
                    || (rule.RawValue is not null && string.IsNullOrWhiteSpace(rule.RawValue))
                    || (rule.DisplayFormatTextId is not null && (rule.Source is null
                        || string.IsNullOrWhiteSpace(rule.DisplayFormatTextId))))
                    throw new ArgumentException("Invalid identification result rule.");
                if (rule.When.Count == 0 && rule.AnyDetection is not null && rule.Source is null)
                {
                    var output = new IdentifiedValue(rule.RawValue, rule.TextId, null);
                    foreach (string detection in rule.AnyDetection)
                    {
                        if (unconditionalOutputs.TryGetValue(detection, out var previous) && previous != output)
                            throw new ArgumentException("One detection cannot unconditionally map a field to different values.");
                        unconditionalOutputs[detection] = output;
                    }
                }
            }
        }
    }

    internal static IReadOnlyDictionary<string, Result> Evaluate(IReadOnlyList<IdentificationClue> detections,
        IReadOnlyDictionary<string, IdentificationResultDefinition> definitions, CancellationToken token)
    {
        var results = new Dictionary<string, Result>(StringComparer.Ordinal);
        foreach (var (id, definition) in definitions)
        {
            token.ThrowIfCancellationRequested();
            var matches = new HashSet<Match>();
            foreach (IdentificationResultRule rule in definition.Rules)
            {
                token.ThrowIfCancellationRequested();
                if (!rule.When.All(condition => detections.Any(clue => clue.Id == condition.Key
                    && clue.Value == condition.Value))) continue;
                string[]? alternatives = rule.AnyDetection?.Where(id => detections.Any(clue => clue.Id == id)).ToArray();
                if (alternatives is { Length: 0 }) continue;
                if (rule.Source is null)
                {
                    var value = new IdentifiedValue(rule.RawValue, rule.TextId, null);
                    foreach (string evidence in (alternatives ?? rule.When.Keys.ToArray()).DefaultIfEmpty(id))
                        matches.Add(new(evidence, value));
                }
                else
                {
                    foreach (IdentificationClue clue in detections)
                    {
                        token.ThrowIfCancellationRequested();
                        if (clue.Id != rule.Source) continue;
                        string key = clue.Item is null ? clue.Id : clue.Id + ":" + clue.Item;
                        matches.Add(new(key, new IdentifiedValue(clue.Value, null, rule.DisplayFormatTextId, clue.Item)));
                    }
                }
            }
            IdentifiedValue[] values = matches.Select(match => match.Value).Distinct().ToArray();
            if (values.Length == 0 && definition.Required)
                throw new CoreException(CoreErrorCode.GameIdentificationFailed, input: id);
            if (IsConflict(values, definition.Multiple) && !definition.RetainConflicts)
                throw new CoreException(CoreErrorCode.GameDetectionConflict, input: id);
            results.Add(id, new Result(Array.AsReadOnly(values), Array.AsReadOnly(matches
                .OrderBy(match => match.Id, StringComparer.Ordinal).ThenBy(match => match.Value.RawValue, StringComparer.Ordinal)
                .ToArray()), definition.Multiple));
        }
        token.ThrowIfCancellationRequested();
        return new ReadOnlyDictionary<string, Result>(results);
    }

}
