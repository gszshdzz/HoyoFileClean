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

    internal sealed record Definition(IdentificationMeaning Policy,
        IReadOnlyList<(string Id, IdentificationMeaning Meaning)> Mappings);

    internal static IReadOnlyDictionary<string, Definition> Compile(
        IEnumerable<(string Id, Dictionary<string, IdentificationMeaning>? Meanings)> detections)
    {
        var fields = new Dictionary<string, List<(string Id, IdentificationMeaning Meaning)>>(StringComparer.Ordinal);
        foreach (var (id, meanings) in detections)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new CoreException(CoreErrorCode.IdentificationConfigurationInvalid);
            foreach (var (field, meaning) in meanings ?? [])
            {
                if (string.IsNullOrWhiteSpace(field) || meaning is null
                    || (meaning.FromDetection ? 1 : 0) + (meaning.RawValue is null ? 0 : 1)
                        + (meaning.TextId is null ? 0 : 1) != 1
                    || meaning.RawValue is not null && string.IsNullOrWhiteSpace(meaning.RawValue)
                    || meaning.TextId is not null && string.IsNullOrWhiteSpace(meaning.TextId)
                    || meaning.Multiple && meaning.RetainConflicts
                    || meaning.DisplayFormatTextId is not null && (!meaning.FromDetection
                        || string.IsNullOrWhiteSpace(meaning.DisplayFormatTextId)))
                    throw new CoreException(CoreErrorCode.IdentificationConfigurationInvalid, input: field);
                if (!fields.TryGetValue(field, out var mappings)) fields.Add(field, mappings = []);
                if (mappings.Any(mapping => mapping.Id == id && mapping.Meaning != meaning))
                    throw new CoreException(CoreErrorCode.IdentificationConfigurationInvalid, input: id);
                mappings.Add((id, meaning));
            }
        }
        if (fields.Count == 0) throw new CoreException(CoreErrorCode.IdentificationConfigurationInvalid);
        return fields.ToDictionary(pair => pair.Key, pair =>
        {
            IdentificationMeaning first = pair.Value[0].Meaning;
            if (pair.Value.Any(mapping => mapping.Meaning.LabelTextId != first.LabelTextId
                || mapping.Meaning.Multiple != first.Multiple
                || mapping.Meaning.RetainConflicts != first.RetainConflicts))
                throw new CoreException(CoreErrorCode.IdentificationConfigurationInvalid, input: pair.Key);
            return new Definition(first with { Required = pair.Value.Any(mapping => mapping.Meaning.Required) },
                pair.Value.Distinct().ToArray());
        }, StringComparer.Ordinal);
    }

    internal static IReadOnlyDictionary<string, Result> Evaluate(IReadOnlyList<IdentificationClue> detections,
        IReadOnlyDictionary<string, Definition> definitions, CancellationToken token)
    {
        var results = new Dictionary<string, Result>(StringComparer.Ordinal);
        foreach (var (id, definition) in definitions)
        {
            token.ThrowIfCancellationRequested();
            var matches = new HashSet<Match>();
            foreach (var (detectionId, meaning) in definition.Mappings)
            {
                token.ThrowIfCancellationRequested();
                foreach (IdentificationClue clue in detections.Where(clue => clue.Id == detectionId))
                {
                    token.ThrowIfCancellationRequested();
                    string key = clue.Item is null ? clue.Id : clue.Id + ":" + clue.Item;
                    matches.Add(new(key, meaning.FromDetection
                        ? new IdentifiedValue(clue.Value, null, meaning.DisplayFormatTextId, clue.Item)
                        : new IdentifiedValue(meaning.RawValue, meaning.TextId, null)));
                }
            }
            IdentifiedValue[] values = matches.Select(match => match.Value).Distinct().ToArray();
            if (values.Length == 0 && definition.Policy.Required)
                throw new CoreException(CoreErrorCode.GameIdentificationFailed, input: id);
            if (IsConflict(values, definition.Policy.Multiple) && !definition.Policy.RetainConflicts)
                throw new CoreException(CoreErrorCode.GameDetectionConflict, input: id);
            results.Add(id, new Result(Array.AsReadOnly(values), Array.AsReadOnly(matches
                .OrderBy(match => match.Id, StringComparer.Ordinal).ThenBy(match => match.Value.RawValue, StringComparer.Ordinal)
                .ToArray()), definition.Policy.Multiple));
        }
        token.ThrowIfCancellationRequested();
        return new ReadOnlyDictionary<string, Result>(results);
    }

}
