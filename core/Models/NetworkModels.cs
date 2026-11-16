namespace HoyoFileClean;

public sealed record RemoteGame(string Biz, string Id);

public sealed class RemoteGameVersion
{
    public NetworkService Service { get; }
    public RemoteGame Game { get; }
    public string Version { get; }
    public string Biz => Game.Biz;
    public string Id => Game.Id;
    internal OfficialGameBranch Branch { get; }

    internal RemoteGameVersion(NetworkService service, RemoteGame game, string version, OfficialGameBranch branch)
    {
        Service = service;
        Game = game;
        Version = version;
        Branch = branch;
    }
}

public sealed record RemoteManifest(string MatchingField, string Category, Uri Url,
    long DownloadSize, long DecodedSize, string Md5, bool Compressed)
{
    public string? Branch { get; init; }
    private Uri? officialUrl;
    internal bool HasOfficialSource => officialUrl == Url;
    internal RemoteManifest FromOfficialResponse() => this with { officialUrl = Url };
}

public sealed record RemoteManifestCatalog(NetworkService Service, string Version, string BuildId,
    IReadOnlyList<RemoteManifest> Manifests);
