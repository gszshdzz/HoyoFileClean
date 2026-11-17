using System.Text.Json;

namespace HoyoFileClean.Desktop;

internal sealed record ExclusionRules(IReadOnlyList<string> Directories, IReadOnlyList<string> Files)
{
    private static readonly JsonSerializerOptions readerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, AllowDuplicateProperties = false,
        RespectNullableAnnotations = true
    };

    internal void Write(string path)
        => JsonFileOutput.WriteNew(path, new ExclusionRuleDocument(Directories, Files));

    internal static ExclusionRules Read(string path, string root)
    {
        using var stream = File.OpenRead(path);
        ExclusionRuleInput input = JsonSerializer.Deserialize<ExclusionRuleInput>(stream, readerOptions) ?? throw new JsonException();
        IReadOnlyList<string>? directories = input.Scan is { } scan ? scan.ExcludedDirectories : input.ExcludedDirectories;
        IReadOnlyList<string>? files = input.Scan is { } scope ? scope.ExcludedFiles : input.ExcludedFiles;
        if (directories is null && files is null) throw new JsonException();
        if ((directories ?? []).Concat(files ?? []).Any(path => path is null)) throw new JsonException();
        return new ExclusionRules(DirectoryScanner.NormalizeExclusionPaths(root, directories ?? []),
            DirectoryScanner.NormalizeExclusionPaths(root, files ?? []));
    }
}
