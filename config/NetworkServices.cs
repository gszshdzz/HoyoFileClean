namespace HoyoFileClean;

// Maintainer-owned adaptation data, compiled into the application. No runtime overrides.
internal static class NetworkServices
{
    internal static NetworkService[] Create() =>
    [
        new("china", "ServiceChina",
            new Uri("https://hyp-api.mihoyo.com/hyp/hyp-connect/api/getGameBranches"),
            new Uri("https://api-takumi.mihoyo.com/downloader/sophon_chunk/api/getBuild"),
            "jGHBHlcOq1"),
        new("global", "ServiceGlobal",
            new Uri("https://sg-hyp-api.hoyoverse.com/hyp/hyp-connect/api/getGameBranches"),
            new Uri("https://sg-public-api.hoyoverse.com/downloader/sophon_chunk/api/getBuild"),
            "VYTpXlbWo8")
    ];
}
