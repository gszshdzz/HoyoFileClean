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
            TimeSpan.FromSeconds(1.5), token).ConfigureAwait(false);
    }

    // Each address family starts one attempt, then adds one every 500 ms; attempts expire independently.
    internal static async ValueTask<Stream> ConnectAddressesAsync(IEnumerable<IPAddress> addresses,
        Func<IPAddress, CancellationToken, ValueTask<Stream>> connect, TimeSpan addressTimeout, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var queues = addresses.Distinct()
            .Where(address => address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
            .GroupBy(address => address.AddressFamily)
            .Select(group => (Family: group.Key, Addresses: new Queue<IPAddress>(group))).ToArray();
        using var race = CancellationTokenSource.CreateLinkedTokenSource(token);
        var pending = new List<Task<Stream>>();
        Stream? winner = null;
        Exception? last = null;
        try
        {
            StartRound();
            Task? tick = queues.Any(queue => queue.Addresses.Count > 0)
                ? Task.Delay(TimeSpan.FromMilliseconds(500), race.Token) : null;
            while (pending.Count > 0 || tick is not null)
            {
                await Task.WhenAny(tick is null
                    ? pending.Cast<Task>()
                    : pending.Cast<Task>().Append(tick)).ConfigureAwait(false);
                foreach (Task<Stream> attempt in pending.Where(attempt => attempt.IsCompleted).ToArray())
                {
                    pending.Remove(attempt);
                    try
                    {
                        winner = await attempt.ConfigureAwait(false);
                        break;
                    }
                    catch (Exception error) when (error is SocketException || error is OperationCanceledException && !token.IsCancellationRequested)
                    { last = error; }
                }
                if (winner is not null) break;
                token.ThrowIfCancellationRequested();
                if (tick is not null && tick.IsCompleted)
                {
                    await tick.ConfigureAwait(false);
                    StartRound();
                    tick = queues.Any(queue => queue.Addresses.Count > 0)
                        ? Task.Delay(TimeSpan.FromMilliseconds(500), race.Token) : null;
                }
            }
        }
        finally
        {
            race.Cancel();
            foreach (var attempt in pending)
            {
                // A second success may race with cancellation; it must also be disposed.
                try { (await attempt.ConfigureAwait(false)).Dispose(); }
                catch (Exception) { /* Observe failures from unused connections. */ }
            }
        }
        if (token.IsCancellationRequested)
        {
            winner?.Dispose();
            token.ThrowIfCancellationRequested();
        }
        if (winner is not null) return winner;
        throw new HttpRequestException("No DNS address could be connected.", last);

        void StartRound()
        {
            foreach (var queue in queues)
            {
                token.ThrowIfCancellationRequested();
                if (queue.Addresses.Count > 0)
                    pending.Add(AttemptAsync(queue.Addresses.Dequeue()));
            }
        }

        async Task<Stream> AttemptAsync(IPAddress address)
        {
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(race.Token);
            attempt.CancelAfter(addressTimeout);
            return await connect(address, attempt.Token).ConfigureAwait(false);
        }
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
