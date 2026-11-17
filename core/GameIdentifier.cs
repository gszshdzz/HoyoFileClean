using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HoyoFileClean;

public static class GameIdentifier
{
    public static bool HasIncompleteRules(GameIdentification? identification)
        => identification is not null && new[] { "game", "biz" }.Any(field =>
            identification.Information.GetValueOrDefault(field)?.Values.Any(value =>
                value.RawValue is { } raw && GameIdentificationRules.IncompleteGames.Contains(raw)) == true);

    public static IdentificationConfiguration<GameDetectionDefinition> LoadConfiguration()
    {
        IdentificationConfiguration<GameDetectionDefinition> configuration = GameIdentificationRules.Create();
        Validate(configuration);
        return configuration;
    }

    public static GameIdentification Identify(string rootPath, CancellationToken cancellationToken = default)
        => Identify(rootPath, LoadConfiguration(), cancellationToken);

    public static GameIdentification Identify(string rootPath, IdentificationConfiguration<GameDetectionDefinition> configuration,
        CancellationToken cancellationToken = default, IEnumerable<string>? scannedPaths = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(configuration);
        string root = DirectoryScanner.NormalizeRootPath(rootPath);
        DirectoryScanner.ValidateDirectoryPath(root);
        string[] paths = (scannedPaths ?? ReadCandidatePaths(root, configuration, cancellationToken)).ToArray();
        var availablePaths = new HashSet<string>(paths, StringComparer.Ordinal);
        var texts = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var detections = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string id, GameDetectionDefinition definition) in configuration.Detections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (definition.Pattern is null)
            {
                string[] candidates = definition.Paths.Select(candidate =>
                    NormalizeCandidatePath(definition.Prefix ? candidate.TrimEnd('/', '\\') : candidate)
                    + (definition.Prefix ? "/" : "")).ToArray();
                string? matched = paths.FirstOrDefault(path =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return candidates.Any(candidate => definition.Prefix
                        ? path.StartsWith(candidate, StringComparison.Ordinal) : path == candidate);
                });
                if (matched is not null) detections.Add(id, matched);
                continue;
            }
            var matches = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            Regex pattern = CreatePattern(definition.Pattern);
            foreach (string relative in definition.Paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string candidate = NormalizeCandidatePath(relative);
                if (!availablePaths.Contains(candidate)) continue;
                string path = Path.Combine(root, candidate);
                if (!texts.TryGetValue(path, out string? text))
                {
                    text = ReadText(path, cancellationToken);
                    texts.Add(path, text);
                }
                if (text is null) continue;
                try
                {
                    foreach (Match match in pattern.Matches(text))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Group group = match.Groups["value"];
                        if (!group.Success || string.IsNullOrWhiteSpace(group.Value))
                            throw new CoreException(CoreErrorCode.GameDetectionInvalid, input: id);
                        string item = match.Groups["item"].Success ? match.Groups["item"].Value : "";
                        if (item.Length > 0 && !IsValidItem(item))
                            throw new CoreException(CoreErrorCode.GameDetectionInvalid, input: id);
                        string key = item.Length == 0 ? id : id + ":" + item;
                        if (!matches.TryGetValue(key, out List<string>? values)) matches.Add(key, values = []);
                        if (definition.JsonObjectKeys)
                        {
                            using JsonDocument document = JsonDocument.Parse(group.Value,
                                new JsonDocumentOptions { AllowDuplicateProperties = false });
                            if (document.RootElement.ValueKind != JsonValueKind.Object)
                                throw new CoreException(CoreErrorCode.GameDetectionInvalid, input: id);
                            values.AddRange(document.RootElement.EnumerateObject().Select(property => property.Name)
                                .Where(name => !(definition.IgnoredKeys ?? []).Contains(name, StringComparer.Ordinal)));
                        }
                        else values.Add(group.Value.Trim());
                    }
                }
                catch (RegexMatchTimeoutException error)
                {
                    throw new CoreException(CoreErrorCode.GameDetectionInvalid, input: id, innerException: error);
                }
                catch (JsonException error)
                {
                    throw new CoreException(CoreErrorCode.GameDetectionInvalid, input: id, innerException: error);
                }
            }
            foreach ((string key, List<string> values) in matches)
                if (values.Count > 0) detections.Add(key, Resolve(values, definition.CanCompare, id));
        }
        return Interpret(detections, configuration, cancellationToken);
    }

    // Only configured candidates are inspected; no hashes or complete installation inventory.
    private static IEnumerable<string> ReadCandidatePaths(string root,
        IdentificationConfiguration<GameDetectionDefinition> configuration, CancellationToken token)
    {
        var visited = new HashSet<(string Path, bool Prefix)>();
        foreach (GameDetectionDefinition detection in configuration.Detections.Values)
        foreach (string candidate in detection.Paths)
        {
            token.ThrowIfCancellationRequested();
            string relative = NormalizeCandidatePath(candidate.TrimEnd('/', '\\'));
            if (!visited.Add((relative, detection.Prefix))) continue;
            string path = Path.Combine(root, relative);
            if (detection.Prefix)
            {
                string? first = FirstFile(path, token);
                if (first is not null) yield return Path.GetRelativePath(root, first).Replace('\\', '/');
            }
            else
            {
                try
                {
                    DirectoryScanner.ValidateDirectoryPath(Path.GetDirectoryName(path)!);
                    FileAttributes attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        continue;
                    if ((attributes & FileAttributes.Directory) != 0) continue;
                }
                catch (FileNotFoundException) { continue; }
                catch (DirectoryNotFoundException) { continue; }
                catch (CoreException error) when (error.Code is CoreErrorCode.DirectoryIsLink or CoreErrorCode.DirectoryMissing) { continue; }
                yield return relative;
            }
        }
    }

    private static string? FirstFile(string directory, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try { DirectoryScanner.ValidateDirectoryPath(directory); }
        catch (DirectoryNotFoundException) { return null; }
        catch (FileNotFoundException) { return null; }
        catch (CoreException error) when (error.Code is CoreErrorCode.DirectoryIsLink or CoreErrorCode.DirectoryMissing) { return null; }
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
        {
            token.ThrowIfCancellationRequested();
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
            if ((attributes & FileAttributes.Directory) == 0) return path;
            string? first = FirstFile(path, token);
            if (first is not null) return first;
        }
        return null;
    }

    public static GameExclusionPreset? GetExclusionPreset(GameIdentification identification)
    {
        if (identification.Information.TryGetValue("biz", out IdentifiedInformation? biz))
            foreach (IdentifiedValue value in biz.Values)
                if (value.RawValue is { } key)
                {
                    if (GameExclusionRules.PresetGames.TryGetValue(key, out string? gameKey)) key = gameKey;
                    if (GameExclusionRules.ExclusionPresets.TryGetValue(key, out GameExclusionPreset? preset)) return preset;
                }
        if (identification.Information.TryGetValue("game", out IdentifiedInformation? game))
            foreach (IdentifiedValue value in game.Values)
                if (value.RawValue is { } key && GameExclusionRules.ExclusionPresets.TryGetValue(key, out GameExclusionPreset? preset))
                    return preset;
        return null;
    }

    // Reconstruct recorded identification without opening any game file.
    public static GameIdentification FromDetections(IReadOnlyDictionary<string, string> detections)
        => FromDetections(detections, LoadConfiguration());

    public static GameIdentification FromDetections(IReadOnlyDictionary<string, string> detections,
        IdentificationConfiguration<GameDetectionDefinition> configuration)
    {
        Validate(configuration);
        ValidateRecordedDetections(detections);
        foreach ((string key, string value) in detections)
        {
            string id = key.Split(':')[0];
            if (!configuration.Detections.TryGetValue(id, out GameDetectionDefinition? definition)
                || string.IsNullOrWhiteSpace(value) || key.EndsWith(':') || key.Count(character => character == ':') > 1
                || key.Contains(':') != (definition.Pattern is not null
                    && CreatePattern(definition.Pattern).GetGroupNames().Contains("item")))
                throw new CoreException(CoreErrorCode.GameDetectionInvalid, input: key);
            if (definition.Pattern is null)
            {
                if (!definition.Paths.Any(path => definition.Prefix
                    ? value.StartsWith(NormalizeCandidatePath(path.TrimEnd('/', '\\')) + "/", StringComparison.Ordinal)
                    : value == NormalizeCandidatePath(path)))
                    throw new CoreException(CoreErrorCode.GameDetectionInvalid, input: key);
                FilePathRules.NormalizeRelative(value);
            }
            else Resolve([value], definition.CanCompare, key);
        }
        return Interpret(new Dictionary<string, string>(detections, StringComparer.Ordinal), configuration, default);
    }

    // File-list transport validates shape without assuming which adaptation produced it.
    internal static IReadOnlyDictionary<string, string> ValidateRecordedDetections(
        IReadOnlyDictionary<string, string> detections)
    {
        foreach ((string key, string value) in detections)
        {
            string[] parts = key.Split(':');
            if (parts.Length > 2 || parts.Any(part => !IsValidItem(part)) || string.IsNullOrWhiteSpace(value))
                throw new CoreException(CoreErrorCode.GameDetectionInvalid, input: key);
        }
        return new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(detections, StringComparer.Ordinal));
    }

    private static bool IsValidItem(string item) => item.Length > 0
        && !item.Any(character => char.IsWhiteSpace(character) || char.IsControl(character) || character is ':' or '=');

    private static GameIdentification Interpret(Dictionary<string, string> detections,
        IdentificationConfiguration<GameDetectionDefinition> configuration, CancellationToken cancellationToken)
    {
        IdentificationClue[] clues = detections.Select(pair =>
        {
            int separator = pair.Key.IndexOf(':');
            return separator < 0 ? new IdentificationClue(pair.Key, pair.Value)
                : new IdentificationClue(pair.Key[..separator], pair.Value, pair.Key[(separator + 1)..]);
        }).ToArray();
        var meanings = IdentificationEvaluator.Compile(configuration.Detections.Select(pair => (pair.Key, pair.Value.Meanings)));
        var information = IdentificationEvaluator.Evaluate(clues, meanings, cancellationToken)
            .Where(pair => pair.Value.Values.Count > 0)
            .ToDictionary(pair => pair.Key, pair => new IdentifiedInformation(
                meanings[pair.Key].Policy.LabelTextId!, pair.Value.Values), StringComparer.Ordinal);
        if (!information.ContainsKey("game") && !information.ContainsKey("biz"))
            throw new CoreException(CoreErrorCode.GameIdentificationFailed, input: "game");
        return new GameIdentification(new ReadOnlyDictionary<string, string>(detections),
            new ReadOnlyDictionary<string, IdentifiedInformation>(information));
    }

    private static string Resolve(List<string> values, bool comparable, string id)
    {
        if (values.Any(string.IsNullOrWhiteSpace))
            throw new CoreException(CoreErrorCode.GameDetectionInvalid, input: id);
        if (!comparable)
        {
            if (values.Any(value => value != values[0]))
                throw new CoreException(CoreErrorCode.GameDetectionConflict, input: id);
            return values[0];
        }
        Version? maximum = null;
        string result = values[0];
        foreach (string value in values)
        {
            if (!Version.TryParse(value, out Version? version))
                throw new CoreException(CoreErrorCode.GameDetectionInvalid, input: id);
            if (maximum is null || version > maximum) { maximum = version; result = value; }
        }
        return result;
    }

    private static string? ReadText(string path, CancellationToken token)
    {
        string parent = Path.GetDirectoryName(path)!;
        // Missing candidates are normal; links and unreadable existing files are not.
        try { DirectoryScanner.ValidateDirectoryPath(parent); }
        catch (Exception error) when (error is DirectoryNotFoundException or FileNotFoundException) { return null; }
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new CoreException(CoreErrorCode.ScanFileTypeChanged, path);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > Maintenance.MetadataLimit)
            throw new CoreException(CoreErrorCode.GameDetectionInvalid, path);
        using var reader = new StreamReader(input, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        var text = new StringBuilder();
        char[] buffer = new char[4096];
        int count;
        while ((count = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            token.ThrowIfCancellationRequested();
            text.Append(buffer, 0, count);
            if (text.Length > Maintenance.MetadataLimit)
                throw new CoreException(CoreErrorCode.GameDetectionInvalid, path);
        }
        return text.ToString();
    }

    private static Regex CreatePattern(string pattern)
        => new(pattern, RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
            Maintenance.IdentificationMatchTimeout);

    private static string NormalizeCandidatePath(string path)
        => FilePathRules.NormalizeRelative(path.StartsWith("./", StringComparison.Ordinal)
            || path.StartsWith(@".\", StringComparison.Ordinal) ? path[2..] : path);

    private static void Validate(IdentificationConfiguration<GameDetectionDefinition> configuration)
    {
        try
        {
            if (configuration.Detections.Count == 0)
                throw ConfigurationError();
            foreach ((string id, GameDetectionDefinition detection) in configuration.Detections)
            {
                if (!IsValidItem(id) || detection is null || detection.Paths.Length == 0
                    || detection.Pattern is not null && (string.IsNullOrWhiteSpace(detection.Pattern)
                        || !CreatePattern(detection.Pattern).GetGroupNames().Contains("value"))
                    || detection.Pattern is null && (detection.CanCompare || detection.JsonObjectKeys)
                    || detection.Pattern is not null && detection.Prefix) throw ConfigurationError();
                foreach (string path in detection.Paths) NormalizeCandidatePath(detection.Prefix ? path.TrimEnd('/', '\\') : path);
            }
            var meanings = IdentificationEvaluator.Compile(configuration.Detections.Select(pair => (pair.Key, pair.Value.Meanings)));
            if (meanings.Values.Any(field => string.IsNullOrWhiteSpace(field.Policy.LabelTextId)))
                throw ConfigurationError();
        }
        catch (Exception error) when (error is ArgumentException or FormatException or NullReferenceException
            || error is CoreException { Code: not CoreErrorCode.GameIdentificationConfigurationInvalid })
        { throw ConfigurationError(error); }
    }

    private static CoreException ConfigurationError(Exception? error = null)
        => new(CoreErrorCode.GameIdentificationConfigurationInvalid, Maintenance.GameIdentificationRelativePath,
            innerException: error);
}
