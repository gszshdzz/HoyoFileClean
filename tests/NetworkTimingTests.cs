using System.Diagnostics;
using System.Diagnostics.Tracing;
using Xunit;

namespace HoyoFileClean.Tests;

public sealed class NetworkTimingTests
{
    private sealed class ConnectionEvents : EventListener
    {
        private readonly Stopwatch clock = Stopwatch.StartNew();
        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name is "System.Net.NameResolution" or "System.Net.Sockets" or "System.Net.Security")
                EnableEvents(source, EventLevel.Informational);
        }
        protected override void OnEventWritten(EventWrittenEventArgs args)
        {
            string endpoint = args.EventSource.Name == "System.Net.Sockets" && args.EventName == "ConnectStart"
                && args.Payload is not null ? string.Join(",", args.Payload) : "";
            Console.WriteLine($"{clock.ElapsedMilliseconds} ms {args.EventSource.Name} {args.EventName} {endpoint}");
        }
    }

    private sealed class TimingHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            long start = Stopwatch.GetTimestamp();
            HttpResponseMessage response = await base.SendAsync(request, token);
            Console.WriteLine($"{request.RequestUri!.Host} headers: {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F0} ms");
            return response;
        }
    }

    [Theory(Explicit = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OfficialColdAndWarmRequests(bool boundedConnect)
    {
        using var events = new ConnectionEvents();
        using var http = new HttpClient(new TimingHandler
        {
            InnerHandler = boundedConnect ? NetworkManifestClient.CreateHttpHandler() : new HttpClientHandler
            { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = System.Net.DecompressionMethods.None }
        });
        var network = new NetworkManifestClient(http);
        for (int pass = 1; pass <= 2; pass++)
        {
            long start = Stopwatch.GetTimestamp();
            RemoteManifestCatalog catalog = await network.GetCatalogAsync(GenshinChannel.China, TestContext.Current.CancellationToken);
            Console.WriteLine($"pass {pass} catalog total: {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F0} ms");
            start = Stopwatch.GetTimestamp();
            foreach (RemoteManifest manifest in catalog.Manifests)
            {
                byte[] bytes = await network.FetchAsync(manifest, TestContext.Current.CancellationToken);
                Assert.Equal(manifest.DownloadSize, bytes.LongLength);
            }
            Console.WriteLine($"pass {pass} download total: {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F0} ms");
        }
    }
}
