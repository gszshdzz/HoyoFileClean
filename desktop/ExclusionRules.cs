using System.Text.Json;

namespace HoyoFileClean.Desktop;

internal sealed record ExclusionRules(IReadOnlyList<string> Directories, IReadOnlyList<string> Files)
{
    internal static ExclusionRules Read(string path, string root)
    {
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowDuplicateProperties = false });
        JsonElement rules = document.RootElement;
        if (rules.ValueKind != JsonValueKind.Object) throw new JsonException();
        if (rules.TryGetProperty("scan", out JsonElement scan)) rules = scan;
        if (rules.ValueKind != JsonValueKind.Object) throw new JsonException();
        if (!rules.TryGetProperty("excludedDirectories", out _) && !rules.TryGetProperty("excludedFiles", out _))
            throw new JsonException();
        string[] ReadArray(string name)
        {
            if (!rules.TryGetProperty(name, out JsonElement array)) return [];
            if (array.ValueKind != JsonValueKind.Array) throw new JsonException();
            return array.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String
                ? item.GetString()! : throw new JsonException()).ToArray();
        }
        return new ExclusionRules(DirectoryScanner.NormalizeExclusionPaths(root, ReadArray("excludedDirectories")),
            DirectoryScanner.NormalizeExclusionPaths(root, ReadArray("excludedFiles")));
    }
}
