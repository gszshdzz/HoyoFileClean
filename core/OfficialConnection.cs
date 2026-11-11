using System.Net;
using System.Net.Sockets;

namespace HoyoFileClean;

internal static class OfficialConnection
{
    internal static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken token)
    {
        DnsEndPoint endpoint = context.DnsEndPoint;
        IPAddress[] addresses = await Dns.GetHostAddressesAsync(endpoint.Host, endpoint.AddressFamily, token).ConfigureAwait(false);
        return await ConnectAddressesAsync(addresses, (address, attempt) => ConnectSocketAsync(address, endpoint.Port, attempt),
            TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
    }

    // A bad CDN address must not consume the OS's ~21-second TCP timeout before trying the next one.
    internal static async ValueTask<Stream> ConnectAddressesAsync(IEnumerable<IPAddress> addresses,
        Func<IPAddress, CancellationToken, ValueTask<Stream>> connect, TimeSpan addressTimeout, CancellationToken token)
    {
        Exception? last = null;
        foreach (IPAddress address in addresses)
        {
            token.ThrowIfCancellationRequested();
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(token);
            attempt.CancelAfter(addressTimeout);
            try { return await connect(address, attempt.Token).ConfigureAwait(false); }
            catch (Exception error) when (error is SocketException || error is OperationCanceledException && !token.IsCancellationRequested)
            { last = error; }
        }
        token.ThrowIfCancellationRequested();
        throw new HttpRequestException("No DNS address could be connected.", last);
    }

    private static async ValueTask<Stream> ConnectSocketAsync(IPAddress address, int port, CancellationToken token)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, port), token).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch { socket.Dispose(); throw; }
    }
}
