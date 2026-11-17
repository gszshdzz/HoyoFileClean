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
    private static NetworkService China => NetworkManifestClient.Services.Single(service => service.Id == "china");
    private static NetworkService Global => NetworkManifestClient.Services.Single(service => service.Id == "global");
    private const string Cdn = "https://autopatchcn.yuanshen.com/test/manifest";
    private static byte[] Payload(string path = "Data/file") => new SophonManifestProto
    {
        Assets = { new SophonManifestAssetProperty
        {
            AssetName = path, AssetSize = 3, AssetHashMd5 = "900150983cd24fb0d6963f7d28e17f72"
        } }
    }.ToByteArray();

    private static RemoteManifest Info(byte[] decoded, int downloadSize, bool compressed = true)
        => new RemoteManifest("game", "本体", new Uri(Cdn), downloadSize, decoded.Length,
            Convert.ToHexString(MD5.HashData(decoded)), compressed).FromOfficialResponse();

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
    [InlineData("{\"retcode\":1}")]
    [InlineData("{\"retcode\":1,\"data\":{}}")]
    public async Task OfficialErrorDoesNotRequireSuccessPayload(string response)
    {
        using HttpClient http = Client(_ => Json(response));
        CoreException error = await Assert.ThrowsAsync<CoreException>(() => new NetworkManifestClient(http)
            .GetGameVersionsAsync(China, TestContext.Current.CancellationToken));
        Assert.Equal(CoreErrorCode.ManifestChannelUnavailable, error.Code);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"game_branches\":null}")]
    [InlineData("{\"game_branches\":[null]}")]
    [InlineData("{\"game_branches\":[{\"game\":null}]}")]
    public async Task InvalidTypedBranchPayloadUsesStructuredError(string data)
    {
        using HttpClient http = Client(_ => Json("{\"retcode\":0,\"data\":" + data + "}"));
        CoreException error = await Assert.ThrowsAsync<CoreException>(() => new NetworkManifestClient(http)
            .GetGameVersionsAsync(China, TestContext.Current.CancellationToken));
        Assert.Equal(CoreErrorCode.ManifestNetworkMetadataInvalid, error.Code);
    }

    [Theory]
    [InlineData("http://future.example/branches", "https://future.example/build")]
    [InlineData("https://future.example/branches", "http://future.example/build")]
    [InlineData("https://future.example:444/branches", "https://future.example/build")]
    [InlineData("https://future.example/branches?launcher_id=old", "https://future.example/build")]
    public async Task InvalidServiceObjectDoesNotSend(string branchUrl, string buildUrl)
    {
        var service = new NetworkService("future", "Future", new Uri(branchUrl), new Uri(buildUrl), "launcher");
        using HttpClient http = Client(_ => throw new InvalidOperationException("Must not send"));
        CoreException error = await Assert.ThrowsAsync<CoreException>(() => new NetworkManifestClient(http)
            .GetGameVersionsAsync(service, TestContext.Current.CancellationToken));
        Assert.Equal(CoreErrorCode.NetworkConfigurationInvalid, error.Code);
    }

    [Theory]
    [InlineData("opaque")]
    [InlineData("game_with_many_region_parts")]
    public async Task ArbitraryServicePreservesRawIdentityAndRequiresExplicitBranch(string biz)
    {
        var service = new NetworkService("future", "Future", new Uri("https://future.example/branches"),
            new Uri("https://future.example/build"), "a&b");
        int builds = 0;
        using HttpClient http = Client(request =>
        {
            Assert.Equal("future.example", request.RequestUri!.Host);
            if (request.RequestUri.AbsolutePath == "/branches")
            {
                Assert.Equal("?launcher_id=a%26b", request.RequestUri.Query);
                return Json("""
                    {"retcode":0,"data":{"game_branches":[
                    {"game":{"id":"first","biz":"BIZ"},"main":{"tag":"1.0.0","branch":"main","password":"p","package_id":"one"}},
                    {"game":{"id":"second","biz":"BIZ"},"main":{"tag":"1.0.0","branch":"main","password":"p","package_id":"two"}}]}}
                    """.Replace("BIZ", biz));
            }
            builds++;
            Assert.Contains("package_id=two", request.RequestUri.Query);
            return Json(CatalogJson());
        });
        var network = new NetworkManifestClient(http);
        IReadOnlyList<RemoteGameVersion> games = await network.GetGameVersionsAsync(service, TestContext.Current.CancellationToken);
        Assert.All(games, game => Assert.Equal(biz, game.Game.Biz));
        Assert.Equal(new[] { "first", "second" }, games.Select(game => game.Id));
        CoreException error = await Assert.ThrowsAsync<CoreException>(() => network.GetCatalogAsync(service, biz, TestContext.Current.CancellationToken));
        Assert.Equal(CoreErrorCode.ManifestGameAmbiguous, error.Code);
        Assert.Equal(0, builds);
        await Assert.ThrowsAsync<CoreException>(() => network.GetCatalogAsync(service, biz, TestContext.Current.CancellationToken, gameId: "missing"));
        Assert.Equal(0, builds);
        await network.GetCatalogAsync(service, biz, TestContext.Current.CancellationToken, gameId: "second");
        Assert.Equal(1, builds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task MissingPackageDoesNotRequestOrWrite(string? package)
    {
        using HttpClient http = Client(_ => throw new InvalidOperationException("Must not send"));
        string output = Path.Combine(TestTemporaryFiles.Root, Guid.NewGuid().ToString("N") + ".json");
        await Assert.ThrowsAnyAsync<ArgumentException>(() => new NetworkManifestClient(http)
            .ConvertAsync(China, "hk4e_cn", null, output, package!, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(output));
    }

    [Theory]
    [InlineData("\"9223372036854775808\"")]
    [InlineData("9223372036854775808")]
    public async Task OverflowingBranchRetcodeIsReportedAsInvalidMetadata(string retcode)
    {
        using HttpClient http = Client(_ => Json("{\"retcode\":" + retcode + ",\"data\":{\"game_branches\":[]}}"));
        CoreException error = await Assert.ThrowsAsync<CoreException>(() => new NetworkManifestClient(http)
            .GetGamesAsync(China, TestContext.Current.CancellationToken));
        Assert.Equal(CoreErrorCode.ManifestNetworkMetadataInvalid, error.Code);
    }

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
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new NetworkManifestClient(http).GetCatalogAsync(China, "hk4e_cn", cancel.Token));
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
    [InlineData("china", "hk4e_cn", "hyp-api.mihoyo.com", "api-takumi.mihoyo.com")]
    [InlineData("global", "hk4e_global", "sg-hyp-api.hoyoverse.com", "sg-public-api.hoyoverse.com")]
    public async Task CatalogSelectsGameAndCurrentBranch(string serviceId, string biz, string host, string buildHost)
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
        NetworkService service = NetworkManifestClient.Services.Single(item => item.Id == serviceId);
        RemoteManifestCatalog catalog = await new NetworkManifestClient(http).GetCatalogAsync(service, biz, TestContext.Current.CancellationToken);
        Assert.Equal("1.0.0", catalog.Version);
        Assert.Equal("build", catalog.BuildId);
        Assert.Equal(new[] { "game", "zh-cn" }, catalog.Manifests.Select(m => m.MatchingField));
        Assert.All(catalog.Manifests, manifest => Assert.Equal("main", manifest.Branch));
        Assert.Equal(2, requests);
    }

    private static string CatalogJson() => """
        {"retcode":0,"data":{"tag":"1.0.0","build_id":"build","manifests":[
        {"matching_field":"game","category_name":"本体","manifest":{"id":"manifest","checksum":"900150983cd24fb0d6963f7d28e17f72","compressed_size":"10","uncompressed_size":"20"},"manifest_download":{"url_prefix":"https://autopatchcn.yuanshen.com/test","url_suffix":"","compression":1,"encryption":0}},
        {"matching_field":"zh-cn","category_name":"中文","manifest":{"id":"voice","checksum":"900150983cd24fb0d6963f7d28e17f72","compressed_size":10,"uncompressed_size":20},"manifest_download":{"url_prefix":"https://autopatchcn.yuanshen.com/test","url_suffix":"","compression":1,"encryption":0}}]}}
        """;

    [Theory]
    [InlineData("main")]
    [InlineData("predownload")]
    public async Task CatalogUsesBranchFromOfficialBuildResponse(string branch)
    {
        using HttpClient http = Client(request => request.RequestUri!.AbsolutePath.EndsWith("getGameBranches")
            ? Json("""{"retcode":0,"data":{"game_branches":[{"game":{"biz":"hk4e_cn"},"main":{"branch":"main","password":"p","package_id":"pkg","tag":"1.0.0"}}]}}""")
            : Json(CatalogJson().Replace("\"build_id\":\"build\"", "\"build_id\":\"build\",\"branch\":\"" + branch + "\"")));
        var service = NetworkManifestClient.Services.Single(item => item.Id == "china");
        var catalog = await new NetworkManifestClient(http).GetCatalogAsync(service, "hk4e_cn", TestContext.Current.CancellationToken);
        Assert.All(catalog.Manifests, manifest => Assert.Equal(branch, manifest.Branch));
    }

    [Fact]
    public async Task DiscoverVersionsThenBuildUsesOnlyOneRequestPerStage()
    {
        var paths = new List<string>();
        using HttpClient http = Client(request =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            return request.RequestUri.AbsolutePath.EndsWith("getGameBranches")
                ? Json("""{"retcode":0,"data":{"game_branches":[{"game":{"biz":"hk4e_cn"},"main":{"branch":"main","password":"p","package_id":"pkg","tag":"1.0.0"}},{"game":{"biz":"other_cn"},"main":{"tag":"9.0.0"}}]}}""")
                : Json(CatalogJson());
        });
        var network = new NetworkManifestClient(http);
        RemoteGameVersion game = (await network.GetGameVersionsAsync(China, TestContext.Current.CancellationToken)).Single(item => item.Biz == "hk4e_cn");
        Assert.Equal(new RemoteGame("hk4e_cn", ""), game.Game);
        Assert.Equal("1.0.0", game.Version);
        Assert.Single(paths);
        RemoteManifestCatalog catalog = await network.GetCatalogAsync(game, TestContext.Current.CancellationToken);
        Assert.Equal("1.0.0", catalog.Version);
        Assert.Equal(new[] { "/hyp/hyp-connect/api/getGameBranches", "/downloader/sophon_chunk/api/getBuild" }, paths);
    }

    [Fact]
    public async Task UnfilteredDiscoveryKeepsNewGamesAndDuplicateBizBranchesDistinct()
    {
        int requests = 0;
        using HttpClient http = Client(request =>
        {
            requests++;
            Assert.DoesNotContain("sg-", request.RequestUri!.Host);
            if (request.RequestUri.AbsolutePath.EndsWith("getGameBranches"))
                return Json("""
                    {"retcode":0,"data":{"game_branches":[
                    {"game":{"id":"first","biz":"future_cn"},"main":{"branch":"main","password":"p","package_id":"one","tag":"1.0.0"}},
                    {"game":{"id":"second","biz":"future_cn"},"main":{"branch":"main","password":"p","package_id":"two","tag":"1.0.0"}},
                    {"game":{"id":"third","biz":"another_cn"},"main":{"branch":"main","password":"p","package_id":"three","tag":"2.0.0"}}
                    ]}}
                    """);
            Assert.Contains("package_id=two", request.RequestUri.Query);
            return Json(CatalogJson());
        });
        var network = new NetworkManifestClient(http);
        IReadOnlyList<RemoteGameVersion> games = await network.GetGameVersionsAsync(China,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, requests);
        Assert.Equal(new[] { "future_cn", "future_cn", "another_cn" }, games.Select(game => game.Biz));
        Assert.Equal(new[] { "first", "second", "third" }, games.Select(game => game.Id));
        await network.GetCatalogAsync(games[1], TestContext.Current.CancellationToken);
        Assert.Equal(2, requests);
    }

    [Theory]
    [InlineData("autopatchcn.bhsr.com")]
    [InlineData("autopatchcn.juequling.com")]
    [InlineData("autopatchcn.bh3.com")]
    [InlineData("future-cdn.example")]
    public async Task DownloadsUseOfficialResponseUrlWithoutGameOrDomainLists(string host)
    {
        int downloads = 0;
        using HttpClient http = Client(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("getGameBranches"))
                return Json("""{"retcode":0,"data":{"game_branches":[{"game":{"biz":"future_cn"},"main":{"branch":"main","password":"p","package_id":"pkg","tag":"1.0.0"}}]}}""");
            if (request.RequestUri.AbsolutePath.EndsWith("getBuild"))
                return Json(CatalogJson().Replace("autopatchcn.yuanshen.com", host).Replace("\"game\"", "\"unknown-package\""));
            downloads++;
            Assert.Equal(host, request.RequestUri.Host);
            return Bytes(new byte[10]);
        });
        var network = new NetworkManifestClient(http);
        RemoteGameVersion game = Assert.Single(await network.GetGameVersionsAsync(China,
            cancellationToken: TestContext.Current.CancellationToken));
        RemoteManifestCatalog catalog = await network.GetCatalogAsync(game, TestContext.Current.CancellationToken);
        RemoteManifest manifest = catalog.Manifests[0];
        Assert.Equal("unknown-package", manifest.MatchingField);
        Assert.Equal(10, (await network.FetchAsync(manifest, TestContext.Current.CancellationToken)).Length);
        Assert.Equal(1, downloads);
        await Assert.ThrowsAsync<CoreException>(() => network.FetchAsync(manifest with { Url = new Uri("https://tampered.example/manifest") }, TestContext.Current.CancellationToken));
        var untrusted = new RemoteManifest(manifest.MatchingField, manifest.Category, manifest.Url,
            manifest.DownloadSize, manifest.DecodedSize, manifest.Md5, manifest.Compressed);
        await Assert.ThrowsAsync<CoreException>(() => network.FetchAsync(untrusted, TestContext.Current.CancellationToken));
        Assert.Equal(1, downloads);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("encrypted")]
    [InlineData("insecure")]
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
            "insecure" => CatalogJson().Replace("https://", "http://"),
            "oversize" => CatalogJson().Replace("\"20\"", "\"999999999\""),
            _ => CatalogJson().Replace("1.0.0", "2.0.0")
        };
        using HttpClient http = Client(request => request.RequestUri!.Host == "hyp-api.mihoyo.com"
            ? Json("""{"retcode":0,"data":{"game_branches":[{"game":{"biz":"hk4e_cn"},"main":{"branch":"main","password":"p","package_id":"pkg","tag":"1.0.0"}}]}}""")
            : Json(build));
        await Assert.ThrowsAsync<CoreException>(() => new NetworkManifestClient(http).GetCatalogAsync(China, "hk4e_cn", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("newgame_newregion", true)]
    [InlineData("missing", false)]
    [InlineData("newgame", false)]
    public async Task CompleteBizIsMatchedWithoutSplitting(string biz, bool found)
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
        IReadOnlyList<RemoteGame> games = await network.GetGamesAsync(China, TestContext.Current.CancellationToken);
        Assert.Equal(new RemoteGame("newgame_newregion", ""), Assert.Single(games));
        requests = 0;
        if (found)
        {
            RemoteManifestCatalog catalog = await network.GetCatalogAsync(China, biz, TestContext.Current.CancellationToken);
            Assert.Contains(catalog.Manifests, item => item.MatchingField == "new-package");
        }
        else
        {
            CoreException error = await Assert.ThrowsAsync<CoreException>(() => network.GetCatalogAsync(China, biz, TestContext.Current.CancellationToken));
            Assert.Equal(CoreErrorCode.ManifestChannelUnavailable, error.Code);
        }
        Assert.Equal(found ? 2 : 1, requests);
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
            CoreException error = await Assert.ThrowsAsync<CoreException>(() => client.GetCatalogAsync(China, "hk4e_cn",
                TestContext.Current.CancellationToken, "1.0.0"));
            Assert.Equal(CoreErrorCode.ManifestVersionUnavailable, error.Code);
        }
        else Assert.Equal("1.0.0", (await client.GetCatalogAsync(China, "hk4e_cn",
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
        RemoteManifestCatalog catalog = await new NetworkManifestClient(http).GetCatalogAsync(Global, "hk4e_global", deadline.Token);
        Assert.Equal(Global, catalog.Service);
        Assert.Equal("1.0.0", catalog.Version);
        Assert.False(deadline.IsCancellationRequested);
        Assert.False(chinaCancelled);
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
            .GetCatalogAsync(Global, "hk4e_global", deadline.Token));
        Assert.Equal(CoreErrorCode.ManifestVersionUnavailable, error.Code);
        Assert.False(chinaCancelled);
        Assert.False(deadline.IsCancellationRequested);
    }

    [Fact]
    public async Task SelectedServiceFailureDoesNotQueryOtherServices()
    {
        int calls = 0;
        using HttpClient http = Client(_ =>
        {
            calls++;
            throw new HttpRequestException("Both services unavailable.");
        });
        await Assert.ThrowsAsync<HttpRequestException>(() => new NetworkManifestClient(http)
            .GetCatalogAsync(Global, "hk4e_global", TestContext.Current.CancellationToken));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task SelectedServiceCallerCancellationStopsRequest()
    {
        int started = 0, stopped = 0;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var http = new HttpClient(new Handler(async (_, token) =>
        {
            Interlocked.Increment(ref started);
            cancel.Cancel();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { Interlocked.Increment(ref stopped); }
            return new HttpResponseMessage();
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new NetworkManifestClient(http)
            .GetCatalogAsync(Global, "hk4e_global", cancel.Token));
        Assert.Equal(1, started);
        Assert.Equal(1, stopped);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NetworkConversionDownloadsOnlyExplicitPackage(bool selectVoice)
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
        string directory = Path.Combine(TestTemporaryFiles.Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string output = Path.Combine(directory, "network.json");
        ConversionSummary summary = await new NetworkManifestClient(http).ConvertAsync(China, "hk4e_cn", "1.0.0", output,
            selectVoice ? "future-package" : "game", TestContext.Current.CancellationToken);
        Assert.Equal(new[] { selectVoice ? "future-package" : "game" }, downloads);
        Assert.Equal(1, summary.FileCount);
        Assert.Equal(summary.FileCount, FileComparer.ReadInputs([output]).FileCount);
    }

    [Fact(Explicit = true)]
    public async Task OfficialGameCatalogsAndStarRailManifest()
    {
        using HttpClient http = NetworkManifestClient.CreateHttpClient();
        var network = new NetworkManifestClient(http);
        IReadOnlyList<RemoteGameVersion> games = await network.GetGameVersionsAsync(China,
            cancellationToken: TestContext.Current.CancellationToken);
        RemoteManifestCatalog? starRail = null;
        foreach (RemoteGameVersion game in games)
        {
            RemoteManifestCatalog catalog = await network.GetCatalogAsync(game, TestContext.Current.CancellationToken);
            Assert.NotEmpty(catalog.Manifests);
            Console.WriteLine($"{game.Biz} {catalog.Version}: {catalog.Manifests.Count} manifests; hosts={string.Join(',', catalog.Manifests.Select(item => item.Url.Host).Distinct())}");
            if (game.Biz == "hkrpg_cn") starRail = catalog;
        }
        Assert.NotNull(starRail);
        RemoteManifest selected = starRail.Manifests.MinBy(item => item.DownloadSize)!;
        FileListSnapshot snapshot = await network.ReadAsync(selected, TestContext.Current.CancellationToken);
        Assert.True(snapshot.FileCount > 0);
        Console.WriteLine($"Star Rail sample {selected.MatchingField}: {selected.DownloadSize} bytes, {snapshot.FileCount} files; length, MD5 and parse passed");
    }

    [Fact(Explicit = true)]
    public async Task OfficialCustomVersion()
    {
        using HttpClient http = NetworkManifestClient.CreateHttpClient();
        var client = new NetworkManifestClient(http);
        foreach (NetworkService service in NetworkManifestClient.Services)
        {
            string biz = service.Id == "china" ? "hk4e_cn" : "hk4e_global";
            RemoteManifestCatalog catalog = await client.GetCatalogAsync(service, biz, TestContext.Current.CancellationToken, "7.1.0");
            Assert.Equal("7.1.0", catalog.Version);
            FileListSnapshot main = await client.ReadAsync(catalog.Manifests.Single(m => m.MatchingField == "game"), TestContext.Current.CancellationToken);
            Console.WriteLine($"{service.Id} custom {catalog.Version}: {main.FileCount} files, MD5 verified");
        }
    }

    // Opt in explicitly: real requests go only to official API/CDN hosts, never to game directories.
    [Fact(Explicit = true)]
    public async Task OfficialGenshinCatalogsAndAllManifests()
    {
        using HttpClient http = NetworkManifestClient.CreateHttpClient();
        var network = new NetworkManifestClient(http);
        foreach (NetworkService service in NetworkManifestClient.Services)
        {
            string biz = service.Id == "china" ? "hk4e_cn" : "hk4e_global";
            RemoteManifestCatalog catalog = await network.GetCatalogAsync(service, biz, TestContext.Current.CancellationToken);
            var accumulator = new FileComparer.SnapshotAccumulator();
            int order = 0;
            foreach (RemoteManifest manifest in catalog.Manifests)
            {
                FileListSnapshot snapshot = await network.ReadAsync(manifest, TestContext.Current.CancellationToken);
                Console.WriteLine($"{service.Id} {catalog.Version} {manifest.MatchingField}: {snapshot.FileCount} files, {snapshot.TotalSize} bytes, MD5 verified");
                Assert.True(snapshot.FileCount > 0);
                accumulator.Add(snapshot, order++, TestContext.Current.CancellationToken);
            }
            Assert.True(accumulator.Build(TestContext.Current.CancellationToken).FileCount > 0);
        }
    }
}
