using System.Net;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using HoyoFileClean.Protos;
using Xunit;
using ZstdSharp;

namespace HoyoFileClean.Tests;

public sealed class NetworkManifestTests
{
    private const string Cdn = "https://autopatchcn.yuanshen.com/test/manifest";
    private static byte[] Payload(string path = "Data/file") => new SophonManifestProto
    {
        Assets = { new SophonManifestAssetProperty
        {
            AssetName = path, AssetSize = 3, AssetHashMd5 = "900150983cd24fb0d6963f7d28e17f72"
        } }
    }.ToByteArray();

    private static RemoteManifest Info(byte[] decoded, int downloadSize, bool compressed = true)
        => new("game", "本体", new Uri(Cdn), downloadSize, decoded.Length,
            Convert.ToHexString(MD5.HashData(decoded)), compressed);

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => send(request, token);
    }

    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> response)
        => new(new Handler((request, token) =>
        {
            token.ThrowIfCancellationRequested();
            HttpResponseMessage result = response(request);
            result.RequestMessage = request;
            return Task.FromResult(result);
        }));

    private static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private static HttpResponseMessage Json(string text) => Bytes(Encoding.UTF8.GetBytes(text));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DownloadsAndUsesExistingValidatedParser(bool compressed)
    {
        byte[] decoded = Payload();
        using var compressor = new Compressor();
        byte[] bytes = compressed ? compressor.Wrap(decoded).ToArray() : decoded;
        using HttpClient http = Client(_ => Bytes(bytes));
        FileListSnapshot result = await new NetworkManifestClient(http).ReadAsync(Info(decoded, bytes.Length, compressed), TestContext.Current.CancellationToken);
        Assert.Equal("Data/file", Assert.Single(result.Files.Values).Path);
        Assert.Equal(Cdn, Assert.Single(result.Sources).Path);
        Assert.Equal(1, result.HashedFileCount);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("decoded-size")]
    [InlineData("short")]
    [InlineData("long")]
    public async Task RejectsMismatchedDownload(string mutation)
    {
        byte[] decoded = Payload();
        using var compressor = new Compressor();
        byte[] bytes = compressor.Wrap(decoded).ToArray();
        RemoteManifest info = Info(decoded, bytes.Length);
        info = mutation switch
        {
            "hash" => info with { Md5 = new string('0', 32) },
            "decoded-size" => info with { DecodedSize = decoded.Length + 1 },
            "short" => info with { DownloadSize = bytes.Length + 1 },
            _ => info with { DownloadSize = bytes.Length - 1 }
        };
        using HttpClient http = Client(_ => Bytes(bytes));
        CoreException error = await Assert.ThrowsAsync<CoreException>(() => new NetworkManifestClient(http).ReadAsync(info, TestContext.Current.CancellationToken));
        Assert.Equal(mutation == "long" ? CoreErrorCode.ManifestNetworkSizeLimit : CoreErrorCode.ManifestDownloadMismatch, error.Code);
    }

    [Fact]
    public async Task ReusesDangerousPathRejection()
    {
        byte[] bytes = Payload("../outside");
        using HttpClient http = Client(_ => Bytes(bytes));
        CoreException error = await Assert.ThrowsAsync<CoreException>(() => new NetworkManifestClient(http).ReadAsync(Info(bytes, bytes.Length, false), TestContext.Current.CancellationToken));
        Assert.Equal(CoreErrorCode.RelativePathInvalid, error.Code);
    }

    [Theory]
    [InlineData("https://example.com/manifest")]
    [InlineData("http://autopatchcn.yuanshen.com/manifest")]
    [InlineData("https://autopatchcn.yuanshen.com.example.com/manifest")]
    [InlineData("https://autopatchhk.yuanshen.com:444/manifest")]
    public async Task RejectsUnofficialUrlsBeforeSending(string url)
    {
        using HttpClient http = Client(_ => throw new InvalidOperationException("Must not send"));
        byte[] bytes = Payload();
        CoreException error = await Assert.ThrowsAsync<CoreException>(() => new NetworkManifestClient(http)
            .ReadAsync(Info(bytes, bytes.Length, false) with { Url = new Uri(url) }, TestContext.Current.CancellationToken));
        Assert.Equal(CoreErrorCode.ManifestNetworkUnsupported, error.Code);
    }

    [Fact]
    public async Task RejectsRedirectsAndHttpErrors()
    {
        byte[] bytes = Payload();
        foreach (HttpStatusCode status in new[] { HttpStatusCode.Redirect, HttpStatusCode.NotFound })
        {
            using HttpClient http = Client(_ => new HttpResponseMessage(status));
            await Assert.ThrowsAsync<HttpRequestException>(() => new NetworkManifestClient(http).ReadAsync(Info(bytes, bytes.Length, false), TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task CancellationBeforeRequestDoesNotSend()
    {
        using HttpClient http = Client(_ => throw new InvalidOperationException("Must not send"));
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new NetworkManifestClient(http).GetCatalogAsync(GenshinChannel.China, cancel.Token));
    }

    private sealed class ForwardStream(byte[] bytes, Action? beforeRead = null) : Stream
    {
        private readonly MemoryStream inner = new(bytes);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            beforeRead?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return inner.ReadAsync(buffer, cancellationToken);
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }

    [Fact]
    public async Task EnforcesLimitWithoutContentLength()
    {
        byte[] bytes = Payload();
        using HttpClient http = Client(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new ForwardStream(bytes))
        });
        CoreException error = await Assert.ThrowsAsync<CoreException>(() => new NetworkManifestClient(http)
            .ReadAsync(Info(bytes, bytes.Length, false) with { DownloadSize = bytes.Length - 1, DecodedSize = bytes.Length - 1 },
                TestContext.Current.CancellationToken));
        Assert.Equal(CoreErrorCode.ManifestNetworkSizeLimit, error.Code);
    }

    [Fact]
    public async Task CancellationReachesResponseBody()
    {
        byte[] bytes = Payload();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using HttpClient http = Client(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new ForwardStream(bytes, cancel.Cancel))
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new NetworkManifestClient(http)
            .ReadAsync(Info(bytes, bytes.Length, false), cancel.Token));
    }

    [Fact]
    public async Task RejectsIncorrectCompressionFlag()
    {
        byte[] bytes = Payload();
        using HttpClient http = Client(_ => Bytes(bytes));
        CoreException error = await Assert.ThrowsAsync<CoreException>(() => new NetworkManifestClient(http)
            .ReadAsync(Info(bytes, bytes.Length), TestContext.Current.CancellationToken));
        Assert.Equal(CoreErrorCode.ManifestDownloadMismatch, error.Code);
    }

    [Fact]
    public async Task NetworkDoesNotAcceptLocalJsonFormat()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("{}");
        using HttpClient http = Client(_ => Bytes(bytes));
        CoreException error = await Assert.ThrowsAsync<CoreException>(() => new NetworkManifestClient(http)
            .ReadAsync(Info(bytes, bytes.Length, false), TestContext.Current.CancellationToken));
        Assert.Equal(CoreErrorCode.ManifestNetworkUnsupported, error.Code);
    }

    [Theory]
    [InlineData(GenshinChannel.China, "hk4e_cn", "hyp-api.mihoyo.com", "api-takumi.mihoyo.com")]
    [InlineData(GenshinChannel.Global, "hk4e_global", "sg-hyp-api.hoyoverse.com", "sg-public-api.hoyoverse.com")]
    public async Task CatalogSelectsGameAndCurrentBranch(GenshinChannel channel, string biz, string host, string buildHost)
    {
        int requests = 0;
        using HttpClient http = Client(request =>
        {
            requests++;
            if (requests == 1)
            {
                Assert.Equal(host, request.RequestUri!.Host);
                return Json("""
                    {"retcode":0,"data":{"game_branches":[
                    {"game":{"biz":"other"}},
                    {"game":{"biz":"BIZ"},"main":{"branch":"main","password":"a&b","package_id":"pkg","tag":"1.0.0"},"pre_download":{"tag":"2.0.0"}}]}}
                    """.Replace("BIZ", biz));
            }
            Assert.Equal(buildHost, request.RequestUri!.Host);
            Assert.Contains("password=a%26b", request.RequestUri.Query);
            Assert.Contains("tag=1.0.0", request.RequestUri.Query);
            return Json(CatalogJson());
        });
        RemoteManifestCatalog catalog = await new NetworkManifestClient(http).GetCatalogAsync(channel, TestContext.Current.CancellationToken);
        Assert.Equal("1.0.0", catalog.Version);
        Assert.Equal("build", catalog.BuildId);
        Assert.Equal(new[] { "game", "zh-cn" }, catalog.Manifests.Select(m => m.MatchingField));
        Assert.Equal(2, requests);
    }

    private static string CatalogJson() => """
        {"retcode":0,"data":{"tag":"1.0.0","build_id":"build","manifests":[
        {"matching_field":"game","category_name":"本体","manifest":{"id":"manifest","checksum":"900150983cd24fb0d6963f7d28e17f72","compressed_size":"10","uncompressed_size":"20"},"manifest_download":{"url_prefix":"https://autopatchcn.yuanshen.com/test","url_suffix":"","compression":1,"encryption":0}},
        {"matching_field":"zh-cn","category_name":"中文","manifest":{"id":"voice","checksum":"900150983cd24fb0d6963f7d28e17f72","compressed_size":10,"uncompressed_size":20},"manifest_download":{"url_prefix":"https://autopatchcn.yuanshen.com/test","url_suffix":"","compression":1,"encryption":0}}]}}
        """;

    [Theory]
    [InlineData("invalid")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("encrypted")]
    [InlineData("unofficial")]
    [InlineData("oversize")]
    [InlineData("version")]
    public async Task RejectsInvalidMetadata(string mutation)
    {
        string build = mutation switch
        {
            "invalid" => "{",
            "missing" => "{\"retcode\":0,\"data\":{}}",
            "duplicate" => CatalogJson().Replace("\"zh-cn\"", "\"game\""),
            "encrypted" => CatalogJson().Replace("\"encryption\":0", "\"encryption\":1"),
            "unofficial" => CatalogJson().Replace("autopatchcn.yuanshen.com", "example.com"),
            "oversize" => CatalogJson().Replace("\"20\"", "\"999999999\""),
            _ => CatalogJson().Replace("1.0.0", "2.0.0")
        };
        using HttpClient http = Client(request => request.RequestUri!.Host == "hyp-api.mihoyo.com"
            ? Json("""{"retcode":0,"data":{"game_branches":[{"game":{"biz":"hk4e_cn"},"main":{"branch":"main","password":"p","package_id":"pkg","tag":"1.0.0"}}]}}""")
            : Json(build));
        await Assert.ThrowsAsync<CoreException>(() => new NetworkManifestClient(http).GetCatalogAsync(GenshinChannel.China, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("newgame", "newregion", true)]
    [InlineData("missing", "newregion", false)]
    [InlineData("newgame", "missing", false)]
    public async Task GameAndRegionAreResolvedFromOfficialResponse(string game, string region, bool found)
    {
        int requests = 0;
        using HttpClient http = Client(request =>
        {
            requests++;
            if (request.RequestUri!.Host == "sg-hyp-api.hoyoverse.com")
                return Json("""{"retcode":0,"data":{"game_branches":[]}}""");
            if (request.RequestUri!.AbsolutePath.EndsWith("getGameBranches"))
                return Json("""{"retcode":0,"data":{"game_branches":[{"game":{"biz":"newgame_newregion"},"main":{"branch":"main","password":"p","package_id":"pkg","tag":"1.0.0"}}]}}""");
            return Json(CatalogJson().Replace("zh-cn", "new-package"));
        });
        var network = new NetworkManifestClient(http);
        IReadOnlyList<RemoteGame> games = await network.GetGamesAsync(GenshinChannel.China, TestContext.Current.CancellationToken);
        Assert.Equal(new RemoteGame("newgame", "newregion"), Assert.Single(games));
        requests = 0;
        if (found)
        {
            RemoteManifestCatalog catalog = await network.GetCatalogAsync(game, region, TestContext.Current.CancellationToken);
            Assert.Contains(catalog.Manifests, item => item.MatchingField == "new-package");
        }
        else
        {
            CoreException error = await Assert.ThrowsAsync<CoreException>(() => network.GetCatalogAsync(game, region, TestContext.Current.CancellationToken));
            Assert.Equal(CoreErrorCode.ManifestChannelUnavailable, error.Code);
        }
        Assert.Equal(found ? 3 : 2, requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomVersionIsSentAndNeverFallsBack(bool wrongVersion)
    {
        using HttpClient http = Client(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("getGameBranches"))
                return Json("""{"retcode":0,"data":{"game_branches":[{"game":{"biz":"hk4e_cn"},"main":{"branch":"main","password":"p","package_id":"pkg","tag":"2.0.0"}}]}}""");
            Assert.Contains("tag=1.0.0", request.RequestUri.Query);
            return Json(wrongVersion ? CatalogJson().Replace("1.0.0", "2.0.0") : CatalogJson());
        });
        var client = new NetworkManifestClient(http);
        if (wrongVersion)
        {
            CoreException error = await Assert.ThrowsAsync<CoreException>(() => client.GetCatalogAsync(GenshinChannel.China,
                TestContext.Current.CancellationToken, "1.0.0"));
            Assert.Equal(CoreErrorCode.ManifestVersionUnavailable, error.Code);
        }
        else Assert.Equal("1.0.0", (await client.GetCatalogAsync(GenshinChannel.China,
            TestContext.Current.CancellationToken, "1.0.0")).Version);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GlobalCatalogDoesNotDependOnChinaService(bool stalled)
    {
        bool chinaCancelled = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            HttpResponseMessage response;
            if (request.RequestUri!.Host == "hyp-api.mihoyo.com")
            {
                if (stalled)
                {
                    try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                    finally { chinaCancelled = true; }
                }
                throw new HttpRequestException("Simulated unrelated service failure.");
            }
            if (request.RequestUri.AbsolutePath.EndsWith("getGameBranches"))
                response = Json("""{"retcode":0,"data":{"game_branches":[{"game":{"biz":"hk4e_global"},"main":{"branch":"main","password":"p","package_id":"pkg","tag":"1.0.0"}}]}}""");
            else response = Json(CatalogJson());
            response.RequestMessage = request;
            return response;
        }));
        RemoteManifestCatalog catalog = await new NetworkManifestClient(http).GetCatalogAsync("hk4e", "global", deadline.Token);
        Assert.Equal(GenshinChannel.Global, catalog.Channel);
        Assert.Equal("1.0.0", catalog.Version);
        Assert.False(deadline.IsCancellationRequested);
        Assert.Equal(stalled, chinaCancelled);
    }

    [Fact]
    public async Task MatchedServiceVersionErrorIsNotHiddenByUnrelatedStall()
    {
        bool chinaCancelled = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            if (request.RequestUri!.Host == "hyp-api.mihoyo.com")
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                finally { chinaCancelled = true; }
            }
            HttpResponseMessage response = request.RequestUri.AbsolutePath.EndsWith("getGameBranches")
                ? Json("""{"retcode":0,"data":{"game_branches":[{"game":{"biz":"hk4e_global"},"main":{"branch":"main","password":"p","package_id":"pkg","tag":"1.0.0"}}]}}""")
                : Json(CatalogJson().Replace("1.0.0", "2.0.0"));
            response.RequestMessage = request;
            return response;
        }));
        CoreException error = await Assert.ThrowsAsync<CoreException>(() => new NetworkManifestClient(http)
            .GetCatalogAsync("hk4e", "global", deadline.Token));
        Assert.Equal(CoreErrorCode.ManifestVersionUnavailable, error.Code);
        Assert.True(chinaCancelled);
        Assert.False(deadline.IsCancellationRequested);
    }

    [Fact]
    public async Task DynamicCatalogRetainsFailureWhenBothServicesFail()
    {
        int calls = 0;
        using HttpClient http = Client(_ =>
        {
            calls++;
            throw new HttpRequestException("Both services unavailable.");
        });
        await Assert.ThrowsAsync<HttpRequestException>(() => new NetworkManifestClient(http)
            .GetCatalogAsync("hk4e", "global", TestContext.Current.CancellationToken));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task DynamicCatalogCallerCancellationStopsBothRequests()
    {
        int started = 0, stopped = 0;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var http = new HttpClient(new Handler(async (_, token) =>
        {
            if (Interlocked.Increment(ref started) == 2) cancel.Cancel();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { Interlocked.Increment(ref stopped); }
            return new HttpResponseMessage();
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new NetworkManifestClient(http)
            .GetCatalogAsync("hk4e", "global", cancel.Token));
        Assert.Equal(2, started);
        Assert.Equal(2, stopped);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NetworkConversionDefaultsToMainAndCanSelectPackages(bool selectVoice)
    {
        byte[] main = Payload("main"), voice = Payload("voice");
        var downloads = new List<string>();
        using HttpClient http = Client(request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("getGameBranches"))
                return Json("""{"retcode":0,"data":{"game_branches":[{"game":{"biz":"hk4e_cn"},"main":{"branch":"main","password":"p","package_id":"pkg","tag":"1.0.0"}}]}}""");
            if (path.EndsWith("getBuild")) return Json(System.Text.Json.JsonSerializer.Serialize(new
            {
                retcode = 0, data = new { tag = "1.0.0", build_id = "build", manifests = new[] { ("game", main), ("future-package", voice) }
                    .Select(pair => new
                    {
                        matching_field = pair.Item1, category_name = pair.Item1,
                        manifest = new { id = pair.Item1, checksum = Convert.ToHexString(MD5.HashData(pair.Item2)),
                            compressed_size = pair.Item2.Length, uncompressed_size = pair.Item2.Length },
                        manifest_download = new { url_prefix = "https://autopatchcn.yuanshen.com/test", url_suffix = "", compression = 0, encryption = 0 }
                    }) }
            }));
            string field = path.Split('/').Last();
            downloads.Add(field);
            return Bytes(field == "game" ? main : voice);
        });
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "opencode", "HoyoFileClean.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string output = Path.Combine(directory, "network.json");
        ConversionSummary summary = await new NetworkManifestClient(http).ConvertAsync(GenshinChannel.China, "1.0.0", output,
            selectVoice ? ["game", "future-package"] : null, TestContext.Current.CancellationToken);
        Assert.Equal(selectVoice ? new[] { "game", "future-package" } : new[] { "game" }, downloads);
        Assert.Equal(selectVoice ? 2 : 1, summary.FileCount);
        Assert.Equal(summary.FileCount, FileComparer.ReadInputs([output]).FileCount);
    }

    [Fact(Explicit = true)]
    public async Task OfficialCustomVersion()
    {
        using HttpClient http = NetworkManifestClient.CreateHttpClient();
        var client = new NetworkManifestClient(http);
        foreach (GenshinChannel channel in Enum.GetValues<GenshinChannel>())
        {
            RemoteManifestCatalog catalog = await client.GetCatalogAsync(channel, TestContext.Current.CancellationToken, "7.1.0");
            Assert.Equal("7.1.0", catalog.Version);
            FileListSnapshot main = await client.ReadAsync(catalog.Manifests.Single(m => m.MatchingField == "game"), TestContext.Current.CancellationToken);
            Console.WriteLine($"{channel} custom {catalog.Version}: {main.FileCount} files, MD5 verified");
        }
    }

    // Opt in explicitly: real requests go only to official API/CDN hosts, never to game directories.
    [Fact(Explicit = true)]
    public async Task OfficialGenshinCatalogsAndAllManifests()
    {
        using HttpClient http = NetworkManifestClient.CreateHttpClient();
        var network = new NetworkManifestClient(http);
        foreach (GenshinChannel channel in Enum.GetValues<GenshinChannel>())
        {
            RemoteManifestCatalog catalog = await network.GetCatalogAsync(channel, TestContext.Current.CancellationToken);
            var accumulator = new FileComparer.SnapshotAccumulator();
            int order = 0;
            foreach (RemoteManifest manifest in catalog.Manifests)
            {
                FileListSnapshot snapshot = await network.ReadAsync(manifest, TestContext.Current.CancellationToken);
                Console.WriteLine($"{channel} {catalog.Version} {manifest.MatchingField}: {snapshot.FileCount} files, {snapshot.TotalSize} bytes, MD5 verified");
                Assert.True(snapshot.FileCount > 0);
                accumulator.Add(snapshot, order++, TestContext.Current.CancellationToken);
            }
            Assert.True(accumulator.Build(TestContext.Current.CancellationToken).FileCount > 0);
        }
    }
}
