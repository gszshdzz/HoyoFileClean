using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HoyoFileClean;

// Only metadata and manifests. No game chunks, disk cache, retries or frontend policy.
public sealed class NetworkManifestClient(HttpClient client)
{
    private static readonly JsonSerializerOptions protocolOptions = new()
    {
        RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true,
        AllowDuplicateProperties = false, NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

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

    public static bool IsValidVersion(string? version)
        => Version.TryParse(version, out var parsed) && parsed.Build >= 0 && parsed.Revision < 0;

    private async Task<IReadOnlyList<OfficialGameBranch>> GetBranchesAsync(NetworkService service, CancellationToken token)
    {
        if (service is null || !service.IsValid) throw new CoreException(CoreErrorCode.NetworkConfigurationInvalid);
        OfficialBranches branches = await GetJsonAsync<OfficialBranches>(service.BranchRequestUrl, token).ConfigureAwait(false);
        if (branches.Games.Any(item => item is null)) throw new CoreException(CoreErrorCode.ManifestNetworkMetadataInvalid);
        return branches.Games;
    }

    private static RemoteGame Identity(OfficialGameBranch item)
    {
        try
        {
            return new RemoteGame(Text(item.Game.Biz), item.Game.Id ?? "");
        }
        catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException or FormatException)
        { throw new CoreException(CoreErrorCode.ManifestNetworkMetadataInvalid, innerException: error); }
    }

    public async Task<IReadOnlyList<RemoteGame>> GetGamesAsync(NetworkService service, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Maintenance.RequestTimeout);
        IReadOnlyList<OfficialGameBranch> branches = await GetBranchesAsync(service, timeout.Token).ConfigureAwait(false);
        return Array.AsReadOnly(branches.Select(Identity).Distinct().ToArray());
    }

    public async Task<RemoteManifestCatalog> GetCatalogAsync(NetworkService service, string biz,
        CancellationToken cancellationToken = default, string? version = null, string? gameId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(biz);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Maintenance.RequestTimeout);
        IReadOnlyList<OfficialGameBranch> branches = await GetBranchesAsync(service, timeout.Token).ConfigureAwait(false);
        OfficialGameBranch[] games = branches.Where(item =>
        {
            RemoteGame identity = Identity(item);
            return identity.Biz == biz && (gameId is null || identity.Id == gameId);
        }).ToArray();
        if (games.Length == 0) throw new CoreException(CoreErrorCode.ManifestChannelUnavailable);
        if (games.Length != 1) throw new CoreException(CoreErrorCode.ManifestGameAmbiguous);
        return await GetBuildAsync(service, games[0], version, timeout.Token).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RemoteGameVersion>> GetGameVersionsAsync(NetworkService service,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Maintenance.RequestTimeout);
        IReadOnlyList<OfficialGameBranch> branches = await GetBranchesAsync(service, timeout.Token).ConfigureAwait(false);
        try
        {
            var versions = new List<RemoteGameVersion>();
            foreach (OfficialGameBranch item in branches)
            {
                RemoteGame identity = Identity(item);
                string version = Text(item.Main?.Tag);
                if (!IsValidVersion(version))
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
        timeout.CancelAfter(Maintenance.RequestTimeout);
        return await GetBuildAsync(game.Service, game.Branch, version, timeout.Token).ConfigureAwait(false);
    }

    private async Task<RemoteManifestCatalog> GetBuildAsync(NetworkService service, OfficialGameBranch game,
        string? version, CancellationToken token)
    {
        try
        {
            OfficialBuildRequest main = game.Main ?? throw new FormatException();
            string requestedVersion = version ?? Text(main.Tag);
            if (!IsValidVersion(requestedVersion))
                throw new ArgumentException("A three-part version is required.", nameof(version));
            string query = OfficialProtocol.BuildQuery(main with { Tag = requestedVersion });
            OfficialBuild data = await GetJsonAsync<OfficialBuild>(new Uri(
                $"{service.BuildUrl.AbsoluteUri}?{query}"), token).ConfigureAwait(false);
            if (Text(data.Tag) != requestedVersion) throw new CoreException(CoreErrorCode.ManifestVersionUnavailable);
            var manifests = new List<RemoteManifest>();
            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (OfficialManifest? item in data.Manifests)
            {
                if (item is null) throw new FormatException();
                OfficialManifestFile file = item.Manifest;
                OfficialManifestDownload download = item.Download;
                if (download.Encryption != OfficialProtocol.NoEncryption) throw new CoreException(CoreErrorCode.ManifestNetworkUnsupported);
                long compression = download.Compression;
                if (compression is not ((long)OfficialCompression.None or (long)OfficialCompression.Zstandard))
                    throw new CoreException(CoreErrorCode.ManifestNetworkUnsupported);
                bool compressed = compression == (long)OfficialCompression.Zstandard;
                string field = Text(item.MatchingField), id = Text(file.Id), md5 = Text(file.Checksum);
                if (!fields.Add(field) || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-' or '.'))
                    || id is "." or ".." || md5.Length != 32 || md5.Any(c => !Uri.IsHexDigit(c))) throw new FormatException();
                string suffix = download.UrlSuffix ?? "";
                var url = new Uri(Text(download.UrlPrefix).TrimEnd('/') + "/" + id + suffix);
                ValidateCdn(url);
                long decodedSize = file.UncompressedSize;
                long downloadSize = compressed ? file.CompressedSize ?? throw new FormatException() : decodedSize;
                ValidateSizes(downloadSize, decodedSize, compressed);
                manifests.Add(new RemoteManifest(field, Text(item.CategoryName), url,
                    downloadSize, decodedSize, md5, compressed)
                    { Branch = data.Branch ?? main.Branch }.FromOfficialResponse());
            }
            if (manifests.Count == 0) throw new FormatException();
            return new RemoteManifestCatalog(service, requestedVersion, Text(data.BuildId), manifests.AsReadOnly());
        }
        catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new CoreException(CoreErrorCode.ManifestNetworkMetadataInvalid, innerException: error);
        }
    }

    public async Task<FileListSnapshot> ReadAsync(RemoteManifest manifest, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Maintenance.RequestTimeout);
        byte[] bytes = await FetchAsync(manifest, timeout.Token).ConfigureAwait(false);
        return Read(manifest, bytes, timeout.Token);
    }

    public static FileListSnapshot Read(RemoteManifest manifest, byte[] bytes, CancellationToken cancellationToken = default)
    {
        ValidateManifest(manifest);
        if (bytes.LongLength != manifest.DownloadSize) throw new CoreException(CoreErrorCode.ManifestDownloadMismatch);
        using var input = new MemoryStream(bytes, writable: false);
        FileListSnapshot snapshot = ManifestConverter.Read(input, manifest.Url.AbsoluteUri,
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
        timeout.CancelAfter(Maintenance.RequestTimeout);
        using MemoryStream input = await DownloadAsync(manifest.Url, manifest.DownloadSize, timeout.Token).ConfigureAwait(false);
        if (input.Length != manifest.DownloadSize) throw new CoreException(CoreErrorCode.ManifestDownloadMismatch);
        timeout.Token.ThrowIfCancellationRequested();
        return input.ToArray();
    }

    public async Task<ConversionSummary> ConvertAsync(NetworkService service, string biz, string? version, string outputPath,
        string package, CancellationToken cancellationToken = default, string? gameId = null)
    {
        if (File.Exists(outputPath) || Directory.Exists(outputPath)) throw new CoreException(CoreErrorCode.OutputExists, outputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(package);
        RemoteManifestCatalog catalog = await GetCatalogAsync(service, biz, cancellationToken, version, gameId).ConfigureAwait(false);
        RemoteManifest? manifest = catalog.Manifests.SingleOrDefault(item => item.MatchingField == package);
        if (manifest is null)
            throw new CoreException(CoreErrorCode.ManifestPackageUnavailable);
        FileListSnapshot snapshot = await ReadAsync(manifest, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        JsonFileOutput.WriteNew(outputPath, new FileListDocument(snapshot.FileCount, snapshot.TotalSize,
            snapshot.Files.Values.ToArray(), FileValues.Md5));
        return new ConversionSummary(snapshot.FileCount, snapshot.TotalSize);
    }

    private async Task<T> GetJsonAsync<T>(Uri url, CancellationToken token) where T : class
    {
        using MemoryStream bytes = await DownloadAsync(url, Maintenance.MetadataLimit, token).ConfigureAwait(false);
        try
        {
            OfficialResponse response = JsonSerializer.Deserialize<OfficialResponse>(bytes, protocolOptions) ?? throw new JsonException();
            if (response.Retcode != 0) throw new CoreException(CoreErrorCode.ManifestChannelUnavailable);
            if (response.Data.ValueKind != JsonValueKind.Object) throw new JsonException();
            return response.Data.Deserialize<T>(protocolOptions) ?? throw new JsonException();
        }
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
            byte[] buffer = new byte[Maintenance.DownloadBufferSize];
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

    private static string Text(string? value) => value is { Length: > 0 } ? value : throw new FormatException();

    private static void ValidateSizes(long download, long decoded, bool compressed)
    {
        if (download <= 0 || decoded <= 0 || (!compressed && download != decoded))
            throw new CoreException(CoreErrorCode.ManifestNetworkMetadataInvalid);
        if (download > (compressed ? Maintenance.CompressedLimit : Maintenance.DecodedLimit) || decoded > Maintenance.DecodedLimit)
            throw new CoreException(CoreErrorCode.ManifestNetworkSizeLimit);
    }

    private static void ValidateCdn(Uri url)
    {
        if (!NetworkService.IsHttpsEndpoint(url))
            throw new CoreException(CoreErrorCode.ManifestNetworkUnsupported);
    }
}
