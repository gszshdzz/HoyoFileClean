using System.Net;
using System.Net.Sockets;
using Xunit;

namespace HoyoFileClean.Tests;

public sealed class OfficialConnectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedAddressDoesNotPreventNextDnsAddress(bool timeout)
    {
        IPAddress[] addresses = [IPAddress.Loopback, IPAddress.IPv6Loopback];
        var called = new List<IPAddress>();
        using var expected = new MemoryStream();
        Stream actual = await OfficialConnection.ConnectAddressesAsync(addresses, async (address, token) =>
        {
            called.Add(address);
            if (address.Equals(addresses[0]))
            {
                if (timeout) await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new SocketException((int)SocketError.ConnectionRefused);
            }
            return expected;
        }, TimeSpan.FromMilliseconds(20), TestContext.Current.CancellationToken);
        Assert.Same(expected, actual);
        Assert.Equal(addresses, called);
    }

    [Fact]
    public async Task CallerCancellationDoesNotStartAnotherAddress()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        int calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await OfficialConnection.ConnectAddressesAsync(
            [IPAddress.Loopback, IPAddress.IPv6Loopback], (_, token) =>
            {
                calls++;
                cancel.Cancel();
                token.ThrowIfCancellationRequested();
                return ValueTask.FromResult<Stream>(Stream.Null);
            }, TimeSpan.FromSeconds(2), cancel.Token));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ExhaustedAddressesFailWithoutAStream()
    {
        await Assert.ThrowsAsync<HttpRequestException>(async () => await OfficialConnection.ConnectAddressesAsync(
            [IPAddress.Loopback], (_, _) => throw new SocketException((int)SocketError.ConnectionRefused),
            TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
    }
}
