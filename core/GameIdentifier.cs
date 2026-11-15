using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HoyoFileClean;

public static class GameIdentifier
{
    public static GameIdentificationConfiguration LoadConfiguration()
    {
        GameIdentificationConfiguration configuration = GameIdentificationRules.Create();
        Validate(configuration);
        return configuration;
    }

    public static GameIdentification Identify(string rootPath, CancellationToken cancellationToken = default)
        => Identify(rootPath, LoadConfiguration(), cancellationToken);

    public static GameIdentification Identify(string rootPath, GameIdentificationConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(configuration);
        string root = DirectoryScanner.NormalizeRootPath(rootPath);
        DirectoryScanner.ValidateDirectoryPath(root);
        var texts = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var detections = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string id, DetectionDefinition definition) in configuration.Detections)
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
        GameIdentificationConfiguration configuration)
    {
        Validate(configuration);
        ValidateRecordedDetections(detections);
        foreach ((string key, string value) in detections)
        {
            string id = key.Split(':')[0];
            if (!configuration.Detections.TryGetValue(id, out DetectionDefinition? definition)
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
        GameIdentificationConfiguration configuration, CancellationToken cancellationToken)
    {
        var information = new Dictionary<string, IdentifiedInformation>(StringComparer.Ordinal);
        foreach ((string id, InformationDefinition definition) in configuration.Information)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var values = new List<IdentifiedValue>();
            foreach (InformationRule rule in definition.Rules)
            {
                if (!rule.When.All(condition => detections.TryGetValue(condition.Key, out string? value)
                    && value == condition.Value)) continue;
                if (rule.Source is null)
                {
                    var result = new IdentifiedValue(null, rule.TextId, null);
                    if (!values.Contains(result)) values.Add(result);
                    continue;
                }
                foreach ((string key, string rawValue) in detections.Where(pair => pair.Key == rule.Source
                    || pair.Key.StartsWith(rule.Source + ":", StringComparison.Ordinal)))
                {
                    string? item = key == rule.Source ? null : key[(rule.Source.Length + 1)..];
                    var result = new IdentifiedValue(rawValue, null, rule.DisplayFormatTextId, item);
                    if (!values.Contains(result)) values.Add(result);
                }
            }
            if (values.Count == 0 && definition.Required)
                throw new CoreException(CoreErrorCode.GameIdentificationFailed, input: id);
            if (values.Count > 1 && !definition.Multiple)
                throw new CoreException(CoreErrorCode.GameDetectionConflict, input: id);
            information.Add(id, new IdentifiedInformation(definition.LabelTextId, values.AsReadOnly()));
        }
        cancellationToken.ThrowIfCancellationRequested();
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

    private static void Validate(GameIdentificationConfiguration configuration)
    {
        try
        {
            if (configuration.Detections.Count == 0 || configuration.Information.Count == 0
                || !configuration.Information.Values.Any(info => info.Required)) throw ConfigurationError();
            foreach ((string id, DetectionDefinition detection) in configuration.Detections)
            {
                if (!IsValidItem(id) || detection is null || detection.Paths.Length == 0
                    || string.IsNullOrWhiteSpace(detection.Pattern)
                    || !CreatePattern(detection.Pattern).GetGroupNames().Contains("value")) throw ConfigurationError();
                foreach (string path in detection.Paths) NormalizeCandidatePath(path);
            }
            foreach ((string id, InformationDefinition info) in configuration.Information)
            {
                if (string.IsNullOrWhiteSpace(id) || info is null || string.IsNullOrWhiteSpace(info.LabelTextId)
                    || info.Rules.Length == 0) throw ConfigurationError();
                foreach (InformationRule rule in info.Rules)
                {
                    if (rule is null || rule.When is null
                        || rule.When.Any(pair => !configuration.Detections.ContainsKey(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                        || (rule.Source is null) == (rule.TextId is null)
                        || (rule.Source is not null && !configuration.Detections.ContainsKey(rule.Source))
                        || (rule.TextId is not null && string.IsNullOrWhiteSpace(rule.TextId))
                        || (rule.DisplayFormatTextId is not null && (rule.Source is null
                            || string.IsNullOrWhiteSpace(rule.DisplayFormatTextId)))) throw ConfigurationError();
                }
            }
        }
        catch (Exception error) when (error is ArgumentException or FormatException or NullReferenceException
            || error is CoreException { Code: not CoreErrorCode.GameIdentificationConfigurationInvalid })
        { throw ConfigurationError(error); }
    }

    private static CoreException ConfigurationError(Exception? error = null)
        => new(CoreErrorCode.GameIdentificationConfigurationInvalid, Maintenance.GameIdentificationRelativePath,
            innerException: error);
}
