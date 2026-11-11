namespace HoyoFileClean.Cli;

internal sealed record NetworkConvertOptions(string OutputPath, string Game, string Region, string? Version, string? Package)
{
    internal static bool TryParse(string[] args, out NetworkConvertOptions? options, out string? error)
    {
        options = null;
        error = null;
        string? output = null;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        bool network = false;
        for (int i = 1; i < args.Length; i++)
        {
            string key = args[i];
            if (key == "--network") { if (network) return false; network = true; continue; }
            if (key is "--game" or "--region" or "--version" or "--package")
            {
                if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]) || args[i].StartsWith("--")) return false;
                if (!values.TryAdd(key, args[i]))
                {
                    if (key == "--package") error = "--package 最多指定一次，每次网络转换只能选择一个资源包。";
                    return false;
                }
            }
            else if (key.StartsWith("--") || output is not null || string.IsNullOrWhiteSpace(key)) return false;
            else output = key;
        }
        if (!network || output is null || !values.TryGetValue("--game", out string? game)
            || !values.TryGetValue("--region", out string? region)
            || !values.TryGetValue("--version", out string? version)) return false;
        if (version != "latest" && (!System.Version.TryParse(version, out var parsed) || parsed.Build < 0 || parsed.Revision >= 0)) return false;
        options = new(output, game, region, version == "latest" ? null : version, values.GetValueOrDefault("--package"));
        return true;
    }
}
