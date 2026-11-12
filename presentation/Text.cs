using System.Globalization;
using System.Collections.Concurrent;
using System.Resources;
using System.Text.Json;

namespace HoyoFileClean.Presentation;

// IDs are the call contract; templates and punctuation live only in language files.
public static class Text
{
    private static readonly IReadOnlyDictionary<string, string> fallback = Load(PresentationOptions.DefaultLanguage);
    private static readonly HashSet<string> languages = typeof(Text).Assembly.GetManifestResourceNames().ToHashSet(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> translations = new();
    private static string ResourceName(string language) => $"HoyoFileClean.Presentation.Languages.{language}.json";

    private static IReadOnlyDictionary<string, string> Messages
    {
        get
        {
            for (CultureInfo culture = CultureInfo.CurrentUICulture; culture.Name.Length > 0; culture = culture.Parent)
                if (languages.Contains(ResourceName(culture.Name)))
                    return culture.Name == PresentationOptions.DefaultLanguage ? fallback : translations.GetOrAdd(culture.Name, Load);
            return fallback;
        }
    }

    private static IReadOnlyDictionary<string, string> Load(string language)
    {
        using Stream input = typeof(Text).Assembly.GetManifestResourceStream(
            ResourceName(language))
            ?? throw new MissingManifestResourceException(language);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(input,
            new JsonSerializerOptions { AllowDuplicateProperties = false, RespectNullableAnnotations = true })
            ?? throw new MissingManifestResourceException(language);
    }

    public static bool Contains(string id) => fallback.ContainsKey(id);
    public static string Get(string id) => Messages.TryGetValue(id, out string? value) || fallback.TryGetValue(id, out value)
        ? value : throw new MissingManifestResourceException(id);

    public static string Format(string id, params object?[] values)
        => string.Format(CultureInfo.CurrentCulture, Get(id), values);

    public static string FormatInvariant(string id, params object?[] values)
        => string.Format(CultureInfo.InvariantCulture, Get(id), values);
}
