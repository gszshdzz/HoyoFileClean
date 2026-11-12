namespace HoyoFileClean.Cli;

internal sealed record NetworkConvertOptions(string OutputPath, string Service, string Biz, string? Version, string Package, string? GameId)
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
            if (key is "--service" or "--biz" or "--game-id" or "--version" or "--package")
            {
                if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]) || args[i].StartsWith("--")) return false;
                if (!values.TryAdd(key, args[i]))
                {
                    if (key == "--package") error = L.Get("OnePackageRequired");
                    return false;
                }
            }
            else if (key.StartsWith("--") || output is not null || string.IsNullOrWhiteSpace(key)) return false;
            else output = key;
        }
        if (!network || output is null || !values.TryGetValue("--service", out string? service)
            || !values.TryGetValue("--biz", out string? biz)
            || !values.TryGetValue("--package", out string? package)
            || !values.TryGetValue("--version", out string? version)) return false;
        if (version != "latest" && (!System.Version.TryParse(version, out var parsed) || parsed.Build < 0 || parsed.Revision >= 0)) return false;
        options = new(output, service, biz, version == "latest" ? null : version, package, values.GetValueOrDefault("--game-id"));
        return true;
    }
}
