using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HoyoFileClean;

public static class GameIdentifier
{
    public static IdentificationConfiguration<GameDetectionDefinition> LoadConfiguration()
    {
        IdentificationConfiguration<GameDetectionDefinition> configuration = GameIdentificationRules.Create();
        Validate(configuration);
        return configuration;
    }

    public static GameIdentification Identify(string rootPath, CancellationToken cancellationToken = default)
        => Identify(rootPath, LoadConfiguration(), cancellationToken);

    public static GameIdentification Identify(string rootPath, IdentificationConfiguration<GameDetectionDefinition> configuration,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(configuration);
        string root = DirectoryScanner.NormalizeRootPath(rootPath);
        DirectoryScanner.ValidateDirectoryPath(root);
        var texts = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var detections = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string id, GameDetectionDefinition definition) in configuration.Detections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matches = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            Regex pattern = CreatePattern(definition.Pattern);
            foreach (string relative in definition.Paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string path = Path.Combine(root, NormalizeCandidatePath(relative));
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
                || key.Contains(':') != CreatePattern(definition.Pattern).GetGroupNames().Contains("item"))
                throw new CoreException(CoreErrorCode.GameDetectionInvalid, input: key);
            Resolve([value], definition.CanCompare, key);
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
        var information = IdentificationEvaluator.Evaluate(clues, configuration.Results, cancellationToken)
            .ToDictionary(pair => pair.Key, pair => new IdentifiedInformation(
                configuration.Results[pair.Key].LabelTextId!, pair.Value.Values), StringComparer.Ordinal);
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
            if (configuration.Detections.Count == 0 || !configuration.Results.Values.Any(info => info.Required))
                throw ConfigurationError();
            foreach ((string id, GameDetectionDefinition detection) in configuration.Detections)
            {
                if (!IsValidItem(id) || detection is null || detection.Paths.Length == 0
                    || string.IsNullOrWhiteSpace(detection.Pattern)
                    || !CreatePattern(detection.Pattern).GetGroupNames().Contains("value")) throw ConfigurationError();
                foreach (string path in detection.Paths) NormalizeCandidatePath(path);
            }
            IdentificationEvaluator.Validate(configuration.Detections.Keys, configuration.Results);
            if (configuration.Results.Values.Any(info => string.IsNullOrWhiteSpace(info.LabelTextId)))
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
