using System.Globalization;
using System.Net;
using System.Text.Json;

namespace HoyoFileClean;

public enum GenshinChannel { China, Global }

public sealed record RemoteGame(string Game, string Region);

public sealed class RemoteGameVersion
{
    public GenshinChannel Channel { get; }
    public RemoteGame Game { get; }
    public string Version { get; }
    public string Biz => Branch.GetProperty("game").GetProperty("biz").GetString()!;
    public string Id => Branch.GetProperty("game").TryGetProperty("id", out JsonElement id) ? id.GetString() ?? "" : "";
    internal JsonElement Branch { get; }

    internal RemoteGameVersion(GenshinChannel channel, RemoteGame game, string version, JsonElement branch)
    {
        Channel = channel;
        Game = game;
        Version = version;
        Branch = branch.Clone();
    }
}

public sealed record RemoteManifest(string MatchingField, string Category, Uri Url,
    long DownloadSize, long DecodedSize, string Md5, bool Compressed)
{
    private Uri? officialUrl;
    internal bool HasOfficialSource => officialUrl == Url;
    internal RemoteManifest FromOfficialResponse() => this with { officialUrl = Url };
}

public sealed record RemoteManifestCatalog(GenshinChannel Channel, string Version, string BuildId,
    IReadOnlyList<RemoteManifest> Manifests);

// Only metadata and manifests. No game chunks, disk cache, retries or frontend policy.
public sealed class NetworkManifestClient(HttpClient client)
{
    private const int MetadataLimit = 2 * 1024 * 1024;
    private const int CompressedLimit = 64 * 1024 * 1024;
    private const int DecodedLimit = 128 * 1024 * 1024;

    // Use this factory for real requests: automatic redirects could leave official servers.
    public static HttpClient CreateHttpClient() => new(CreateHttpHandler());

    internal static SocketsHttpHandler CreateHttpHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.None,
        ConnectCallback = OfficialConnection.ConnectAsync
    };

    private static readonly Lazy<IReadOnlyList<NetworkService>> services = new(() => NetworkService.Load());
    public static IReadOnlyList<NetworkService> Services => services.Value;

    private static NetworkService Endpoints(GenshinChannel channel)
        => Services.SingleOrDefault(service => service.Channel == channel) ?? throw new ArgumentOutOfRangeException(nameof(channel));

    private async Task<JsonDocument> GetBranchesAsync(GenshinChannel channel, CancellationToken token)
    {
        var endpoints = Endpoints(channel);
        return await GetJsonAsync(endpoints.BranchUrl, token)
            .ConfigureAwait(false);
    }

    private static JsonElement[] Games(JsonDocument branches)
    {
        try { return Data(branches).GetProperty("game_branches").EnumerateArray().ToArray(); }
        catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new CoreException(CoreErrorCode.ManifestNetworkMetadataInvalid, innerException: error);
        }
    }

    private static RemoteGame Identity(JsonElement item)
    {
        string biz;
        try { biz = Text(item.GetProperty("game"), "biz"); }
        catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException or FormatException)
        { throw new CoreException(CoreErrorCode.ManifestNetworkMetadataInvalid, innerException: error); }
        int separator = biz.LastIndexOf('_');
        return separator > 0 && separator < biz.Length - 1
            ? new RemoteGame(biz[..separator], biz[(separator + 1)..])
            : new RemoteGame(biz, "");
    }

    public async Task<IReadOnlyList<RemoteGame>> GetGamesAsync(GenshinChannel service, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        using JsonDocument branches = await GetBranchesAsync(service, timeout.Token).ConfigureAwait(false);
        return Array.AsReadOnly(Games(branches).Select(Identity).Distinct().ToArray());
    }

    public async Task<RemoteManifestCatalog> GetCatalogAsync(GenshinChannel channel,
        CancellationToken cancellationToken = default, string? version = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        using JsonDocument branches = await GetBranchesAsync(channel, timeout.Token).ConfigureAwait(false);
        JsonElement[] games = Games(branches).Where(item =>
        {
            RemoteGame identity = Identity(item);
            return identity.Game + "_" + identity.Region == Endpoints(channel).DefaultBiz;
        }).ToArray();
        if (games.Length == 0) throw new CoreException(CoreErrorCode.ManifestChannelUnavailable);
        if (games.Length != 1) throw new CoreException(CoreErrorCode.ManifestNetworkMetadataInvalid);
        return await GetBuildAsync(channel, games[0], version, timeout.Token).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RemoteGameVersion>> GetGameVersionsAsync(GenshinChannel service, string? game = null,
        CancellationToken cancellationToken = default)
    {
        if (game is not null) ArgumentException.ThrowIfNullOrWhiteSpace(game);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        using JsonDocument branches = await GetBranchesAsync(service, timeout.Token).ConfigureAwait(false);
        try
        {
            var versions = new List<RemoteGameVersion>();
            foreach (JsonElement item in Games(branches))
            {
                RemoteGame identity = Identity(item);
                if (game is not null && identity.Game != game) continue;
                if (game is not null && versions.Any(value => value.Game == identity)) throw new FormatException();
                string version = Text(item.GetProperty("main"), "tag");
                if (!Version.TryParse(version, out var parsed) || parsed.Build < 0 || parsed.Revision >= 0)
                    throw new FormatException();
                versions.Add(new RemoteGameVersion(service, identity, version, item));
            }
            return versions.AsReadOnly();
        }
        catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { throw new CoreException(CoreErrorCode.ManifestNetworkMetadataInvalid, innerException: error); }
    }

    public async Task<RemoteManifestCatalog> GetCatalogAsync(RemoteGameVersion game,
        CancellationToken cancellationToken = default, string? version = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        return await GetBuildAsync(game.Channel, game.Branch, version, timeout.Token).ConfigureAwait(false);
    }

    public async Task<RemoteManifestCatalog> GetCatalogAsync(string game, string region,
        CancellationToken cancellationToken = default, string? version = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(game);
        ArgumentException.ThrowIfNullOrWhiteSpace(region);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        Task<(RemoteManifestCatalog? Catalog, Exception? Error, bool Matched)>[] queries = Services
            .Select(service => FindCatalogAsync(service.Channel, game, region, version, timeout.Token)).ToArray();
        var pending = queries.ToList();
        Exception? failure = null;
        try
        {
            while (pending.Count > 0)
            {
                var completed = await Task.WhenAny(pending).ConfigureAwait(false);
                pending.Remove(completed);
                var result = await completed.ConfigureAwait(false);
                if (result.Catalog is not null) return result.Catalog;
                if (result.Matched && result.Error is not null) throw result.Error;
                failure ??= result.Error;
            }
            throw failure ?? new CoreException(CoreErrorCode.ManifestChannelUnavailable);
        }
        finally
        {
            timeout.Cancel();
            // Observe and release every request before its shared cancellation source is disposed.
            try { await Task.WhenAll(queries).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
    }

    private async Task<(RemoteManifestCatalog? Catalog, Exception? Error, bool Matched)> FindCatalogAsync(
        GenshinChannel service, string game, string region, string? version, CancellationToken token)
    {
        bool matched = false;
        try
        {
            using JsonDocument branches = await GetBranchesAsync(service, token).ConfigureAwait(false);
            JsonElement[] matches = Games(branches).Where(item => Identity(item) == new RemoteGame(game, region)).ToArray();
            matched = matches.Length > 0;
            if (matches.Length > 1) throw new CoreException(CoreErrorCode.ManifestNetworkMetadataInvalid);
            return (matched ? await GetBuildAsync(service, matches[0], version, token).ConfigureAwait(false) : null, null, matched);
        }
        catch (Exception error) when (error is CoreException or HttpRequestException or IOException
            || error is OperationCanceledException && !token.IsCancellationRequested)
        {
            return (null, error, matched);
        }
    }

    private async Task<RemoteManifestCatalog> GetBuildAsync(GenshinChannel channel, JsonElement game,
        string? version, CancellationToken token)
    {
        try
        {
            JsonElement main = game.GetProperty("main");
            string requestedVersion = version ?? Text(main, "tag");
            if (!Version.TryParse(requestedVersion, out var parsed) || parsed.Build < 0 || parsed.Revision >= 0)
                throw new ArgumentException("A three-part version is required.", nameof(version));
            string query = string.Join("&", new[] { "branch", "password", "package_id", "tag" }
                .Select(key => key + "=" + Uri.EscapeDataString(key == "tag" ? requestedVersion : Text(main, key))));
            using JsonDocument build = await GetJsonAsync(new Uri(
                $"{Endpoints(channel).BuildUrl.AbsoluteUri}?{query}"), token).ConfigureAwait(false);
            JsonElement data = Data(build);
            if (Text(data, "tag") != requestedVersion) throw new CoreException(CoreErrorCode.ManifestVersionUnavailable);
            var manifests = new List<RemoteManifest>();
            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement item in data.GetProperty("manifests").EnumerateArray())
            {
                JsonElement file = item.GetProperty("manifest"), download = item.GetProperty("manifest_download");
                if (Number(download, "encryption") != 0) throw new CoreException(CoreErrorCode.ManifestNetworkUnsupported);
                long compression = Number(download, "compression");
                if (compression is not (0 or 1)) throw new CoreException(CoreErrorCode.ManifestNetworkUnsupported);
                string field = Text(item, "matching_field"), id = Text(file, "id"), md5 = Text(file, "checksum");
                if (!fields.Add(field) || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-' or '.'))
                    || id is "." or ".." || md5.Length != 32 || md5.Any(c => !Uri.IsHexDigit(c))) throw new FormatException();
                string suffix = download.GetProperty("url_suffix").GetString() ?? "";
                var url = new Uri(Text(download, "url_prefix").TrimEnd('/') + "/" + id + suffix);
                ValidateCdn(url);
                long decodedSize = Number(file, "uncompressed_size");
                long downloadSize = compression == 1 ? Number(file, "compressed_size") : decodedSize;
                ValidateSizes(downloadSize, decodedSize, compression == 1);
                manifests.Add(new RemoteManifest(field, Text(item, "category_name"), url,
                    downloadSize, decodedSize, md5, compression == 1).FromOfficialResponse());
            }
            if (manifests.Count == 0) throw new FormatException();
            return new RemoteManifestCatalog(channel, requestedVersion, Text(data, "build_id"), manifests.AsReadOnly());
        }
        catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new CoreException(CoreErrorCode.ManifestNetworkMetadataInvalid, innerException: error);
        }
    }

    public async Task<FileListSnapshot> ReadAsync(RemoteManifest manifest, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        byte[] bytes = await FetchAsync(manifest, timeout.Token).ConfigureAwait(false);
        return Read(manifest, bytes, timeout.Token);
    }

    public static FileListSnapshot Read(RemoteManifest manifest, byte[] bytes, CancellationToken cancellationToken = default)
    {
        ValidateManifest(manifest);
        if (bytes.LongLength != manifest.DownloadSize) throw new CoreException(CoreErrorCode.ManifestDownloadMismatch);
        using var input = new MemoryStream(bytes, writable: false);
        FileListSnapshot snapshot = ManifestConverter.Read(input, manifest.Url.AbsoluteUri, out _,
            cancellationToken, manifest.DecodedSize, manifest.Md5, manifest.Compressed);
        cancellationToken.ThrowIfCancellationRequested();
        return snapshot;
    }

    private static void ValidateManifest(RemoteManifest manifest)
    {
        ValidateCdn(manifest.Url);
        ValidateSizes(manifest.DownloadSize, manifest.DecodedSize, manifest.Compressed);
        if (manifest.Md5.Length != 32 || manifest.Md5.Any(c => !Uri.IsHexDigit(c)))
            throw new CoreException(CoreErrorCode.ManifestNetworkMetadataInvalid);
    }

    public async Task<byte[]> FetchAsync(RemoteManifest manifest, CancellationToken cancellationToken = default)
    {
        ValidateManifest(manifest);
        if (!manifest.HasOfficialSource) throw new CoreException(CoreErrorCode.ManifestNetworkUnsupported);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        using MemoryStream input = await DownloadAsync(manifest.Url, manifest.DownloadSize, timeout.Token).ConfigureAwait(false);
        if (input.Length != manifest.DownloadSize) throw new CoreException(CoreErrorCode.ManifestDownloadMismatch);
        timeout.Token.ThrowIfCancellationRequested();
        return input.ToArray();
    }

    public async Task<ConversionSummary> ConvertAsync(GenshinChannel channel, string? version, string outputPath,
        IEnumerable<string>? packages = null, CancellationToken cancellationToken = default)
    {
        if (File.Exists(outputPath) || Directory.Exists(outputPath)) throw new CoreException(CoreErrorCode.OutputExists, outputPath);
        RemoteManifestCatalog catalog = await GetCatalogAsync(channel, cancellationToken, version).ConfigureAwait(false);
        return await ConvertCatalogAsync(catalog, outputPath, packages, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ConversionSummary> ConvertAsync(string game, string region, string? version, string outputPath,
        IEnumerable<string>? packages = null, CancellationToken cancellationToken = default)
    {
        if (File.Exists(outputPath) || Directory.Exists(outputPath)) throw new CoreException(CoreErrorCode.OutputExists, outputPath);
        RemoteManifestCatalog catalog = await GetCatalogAsync(game, region, cancellationToken, version).ConfigureAwait(false);
        return await ConvertCatalogAsync(catalog, outputPath, packages, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ConversionSummary> ConvertCatalogAsync(RemoteManifestCatalog catalog, string outputPath,
        IEnumerable<string>? packages, CancellationToken cancellationToken)
    {
        string[] fields = (packages ?? ["game"]).Distinct(StringComparer.Ordinal).ToArray();
        if (fields.Length == 0 || fields.Any(field => !catalog.Manifests.Any(item => item.MatchingField == field)))
            throw new CoreException(CoreErrorCode.ManifestPackageUnavailable);
        var accumulator = new FileComparer.SnapshotAccumulator();
        for (int i = 0; i < fields.Length; i++)
            accumulator.Add(await ReadAsync(catalog.Manifests.Single(item => item.MatchingField == fields[i]), cancellationToken)
                .ConfigureAwait(false), i, cancellationToken);
        FileListSnapshot snapshot = accumulator.Build(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        JsonFileOutput.WriteNew(outputPath, new ManifestFileList("MD5", snapshot.FileCount, snapshot.TotalSize, snapshot.Files.Values.ToArray()));
        return new ConversionSummary(snapshot.FileCount, snapshot.TotalSize);
    }

    private async Task<JsonDocument> GetJsonAsync(Uri url, CancellationToken token)
    {
        using MemoryStream bytes = await DownloadAsync(url, MetadataLimit, token).ConfigureAwait(false);
        try { return JsonDocument.Parse(bytes); }
        catch (JsonException error)
        {
            throw new CoreException(CoreErrorCode.ManifestNetworkMetadataInvalid, innerException: error);
        }
    }

    private async Task<MemoryStream> DownloadAsync(Uri url, long limit, CancellationToken token)
    {
        using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Headers.Location is not null || response.RequestMessage?.RequestUri != url)
            throw new CoreException(CoreErrorCode.ManifestNetworkUnsupported);
        if (response.Content.Headers.ContentLength > limit) throw new CoreException(CoreErrorCode.ManifestNetworkSizeLimit);
        using Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var output = new MemoryStream();
        try
        {
            byte[] buffer = new byte[81920];
            int count;
            while ((count = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, limit - output.Length + 1)), token).ConfigureAwait(false)) != 0)
            {
                if (output.Length + count > limit) throw new CoreException(CoreErrorCode.ManifestNetworkSizeLimit);
                output.Write(buffer, 0, count);
            }
            output.Position = 0;
            return output;
        }
        catch { output.Dispose(); throw; }
    }

    private static JsonElement Data(JsonDocument document)
    {
        JsonElement root = document.RootElement;
        if (Number(root, "retcode") != 0) throw new CoreException(CoreErrorCode.ManifestChannelUnavailable);
        return root.GetProperty("data");
    }

    private static string Text(JsonElement item, string key)
        => item.GetProperty(key).GetString() is { Length: > 0 } text ? text : throw new FormatException();

    private static long Number(JsonElement item, string key)
    {
        JsonElement value = item.GetProperty(key);
        return value.ValueKind == JsonValueKind.String
            ? long.Parse(value.GetString()!, NumberStyles.Integer, CultureInfo.InvariantCulture) : value.GetInt64();
    }

    private static void ValidateSizes(long download, long decoded, bool compressed)
    {
        if (download <= 0 || decoded <= 0 || (!compressed && download != decoded))
            throw new CoreException(CoreErrorCode.ManifestNetworkMetadataInvalid);
        if (download > (compressed ? CompressedLimit : DecodedLimit) || decoded > DecodedLimit)
            throw new CoreException(CoreErrorCode.ManifestNetworkSizeLimit);
    }

    private static void ValidateCdn(Uri url)
    {
        if (!NetworkService.IsHttpsEndpoint(url))
            throw new CoreException(CoreErrorCode.ManifestNetworkUnsupported);
    }
}
