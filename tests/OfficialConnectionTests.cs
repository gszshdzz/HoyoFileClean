using System.Net;
using System.Net.Sockets;
using Xunit;

namespace HoyoFileClean.Tests;

public sealed class OfficialConnectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SecondAttemptPrefersOtherAddressFamilyBeforeDifferentPrefix(bool ipv6First)
    {
        IPAddress first = IPAddress.Parse(ipv6First ? "2001:db8:1::1" : "192.0.2.1");
        IPAddress differentPrefix = IPAddress.Parse(ipv6First ? "2001:db8:2::1" : "198.51.100.1");
        IPAddress otherFamily = IPAddress.Parse(ipv6First ? "192.0.2.2" : "2001:db8:3::1");
        var calls = new List<IPAddress>();
        using var expected = new MemoryStream();
        Stream actual = await OfficialConnection.ConnectAddressesAsync([first, differentPrefix, otherFamily], async (address, token) =>
        {
            calls.Add(address);
            if (!address.Equals(otherFamily)) await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return expected;
        }, TimeSpan.FromMilliseconds(30), TestContext.Current.CancellationToken);
        Assert.Same(expected, actual);
        Assert.Equal(new[] { first, otherFamily }, calls);
    }

    [Fact]
    public async Task SecondAddressCanWinWithoutWaitingForFirstTimeout()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var expected = new MemoryStream();
        bool firstCancelled = false;
        Task<Stream> connection = OfficialConnection.ConnectAddressesAsync(
            [IPAddress.Loopback, IPAddress.IPv6Loopback], async (address, token) =>
            {
                if (address.Equals(IPAddress.Loopback))
                {
                    try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                    finally { firstCancelled = token.IsCancellationRequested; }
                }
                secondStarted.SetResult();
                return expected;
            }, TimeSpan.FromSeconds(30), cancel.Token).AsTask();
        try
        {
            await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            Assert.Same(expected, await connection);
            Assert.True(firstCancelled);
        }
        finally
        {
            cancel.Cancel();
            try { await connection; } catch (OperationCanceledException) { }
        }
    }

    [Theory]
    [InlineData("192.0.2.1", "192.0.2.2", "198.51.100.1")]
    [InlineData("2001:db8:1::1", "2001:db8:1::2", "2001:db8:2::1")]
    public async Task SingleStackPreservesDnsOrderWithoutPrefixSelection(string first, string same, string different)
    {
        IPAddress[] addresses = [IPAddress.Parse(first), IPAddress.Parse(same), IPAddress.Parse(different)];
        var called = new List<IPAddress>();
        using var expected = new MemoryStream();
        Stream actual = await OfficialConnection.ConnectAddressesAsync(addresses, async (address, token) =>
        {
            called.Add(address);
            if (address.Equals(addresses[0])) await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return expected;
        }, TimeSpan.FromMilliseconds(30), TestContext.Current.CancellationToken);
        Assert.Same(expected, actual);
        Assert.Equal(new[] { addresses[0], addresses[1] }, called);
    }

    [Fact]
    public async Task ImmediateFailuresStillTryEveryAddress()
    {
        IPAddress[] addresses = Enumerable.Range(1, 5).Select(index => IPAddress.Parse($"192.0.2.{index}")).ToArray();
        int active = 0, maximum = 0, calls = 0;
        await Assert.ThrowsAsync<HttpRequestException>(async () => await OfficialConnection.ConnectAddressesAsync(
            addresses, async (_, token) =>
            {
                int count = Interlocked.Increment(ref active);
                maximum = Math.Max(maximum, count);
                Interlocked.Increment(ref calls);
                try
                {
                    await Task.Yield();
                    throw new SocketException((int)SocketError.ConnectionRefused);
                }
                finally { Interlocked.Decrement(ref active); }
            }, TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        Assert.InRange(maximum, 1, 3);
        Assert.Equal(addresses.Length, calls);
        Assert.Equal(0, active);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SingleStackStartsOneThenAddsCandidateBeforeFirstTimeout(bool ipv6)
    {
        IPAddress[] addresses = ipv6
            ? [IPAddress.Parse("2001:db8::1"), IPAddress.Parse("2001:db8::2")]
            : [IPAddress.Parse("192.0.2.1"), IPAddress.Parse("192.0.2.2")];
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var expected = new MemoryStream();
        bool firstStopped = false;
        int calls = 0;
        Task<Stream> result = OfficialConnection.ConnectAddressesAsync(addresses, async (address, token) =>
        {
            calls++;
            if (address.Equals(addresses[0]))
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                finally { firstStopped = token.IsCancellationRequested; }
            }
            Assert.False(firstStopped);
            secondStarted.SetResult();
            return expected;
        }, TimeSpan.FromSeconds(10), cancel.Token).AsTask();
        try
        {
            Assert.Equal(1, calls);
            await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            Assert.Same(expected, await result);
            Assert.True(firstStopped);
        }
        finally
        {
            cancel.Cancel();
            try { await result; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task BothFamiliesAddTheirNextAddressOnTheSameTick()
    {
        IPAddress[] addresses = [IPAddress.Parse("192.0.2.1"), IPAddress.Parse("192.0.2.2"),
            IPAddress.Parse("2001:db8::1"), IPAddress.Parse("2001:db8::2")];
        var calls = new List<IPAddress>();
        var expanded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task<Stream> result = OfficialConnection.ConnectAddressesAsync(addresses, async (address, token) =>
        {
            calls.Add(address);
            if (calls.Count == 4) expanded.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Stream.Null;
        }, TimeSpan.FromSeconds(10), cancel.Token).AsTask();
        try
        {
            Assert.Equal(new[] { addresses[0], addresses[2] }, calls);
            await expanded.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            Assert.Equal(new[] { addresses[0], addresses[2], addresses[1], addresses[3] }, calls);
        }
        finally
        {
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => result);
        }
    }

    [Fact]
    public async Task EmptyDnsResultFailsWithoutStartingConnections()
    {
        await Assert.ThrowsAsync<HttpRequestException>(async () => await OfficialConnection.ConnectAddressesAsync(
            [], (_, _) => throw new InvalidOperationException("Must not start"), TimeSpan.FromSeconds(1.5),
            TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SingleAddressTimesOutAndDuplicateIsNotRetried(bool ipv6)
    {
        IPAddress address = ipv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback;
        int calls = 0, stopped = 0;
        await Assert.ThrowsAsync<HttpRequestException>(async () => await OfficialConnection.ConnectAddressesAsync(
            [address, address], async (_, token) =>
            {
                calls++;
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                finally { stopped++; }
                return Stream.Null;
            }, TimeSpan.FromMilliseconds(30), TestContext.Current.CancellationToken));
        Assert.Equal(1, calls);
        Assert.Equal(1, stopped);
    }

    [Fact]
    public async Task SuccessfulUnusedStreamIsDisposed()
    {
        using var winner = new MemoryStream();
        using var unused = new MemoryStream();
        int calls = 0;
        Stream actual = await OfficialConnection.ConnectAddressesAsync(
            [IPAddress.Loopback, IPAddress.IPv6Loopback], (_, _) => ValueTask.FromResult<Stream>(++calls == 1 ? winner : unused),
            TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Same(winner, actual);
        Assert.True(winner.CanRead);
        Assert.False(unused.CanRead);
    }

    [Fact]
    public async Task CancellationStopsBothActiveAttemptsWithoutStartingRemainingAddress()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        int calls = 0, stopped = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await OfficialConnection.ConnectAddressesAsync(
            [IPAddress.Loopback, IPAddress.IPv6Loopback, IPAddress.Parse("192.0.2.1")], async (_, token) =>
            {
                if (++calls == 2) cancel.Cancel();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                finally { Interlocked.Increment(ref stopped); }
                return Stream.Null;
            }, TimeSpan.FromMilliseconds(30), cancel.Token));
        Assert.Equal(2, calls);
        Assert.Equal(2, stopped);
    }

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
