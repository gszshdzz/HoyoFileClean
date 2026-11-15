using HoyoFileClean.Desktop;
using Xunit;

namespace HoyoFileClean.Desktop.Tests;

public sealed class OperationLifecycleTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task CancelledWorkStaysBusyUntilItExitsAndCannotPublish(int operationKind)
    {
        var session = new WizardSession();
        var kind = (DesktopOperation)operationKind;
        bool network = kind == DesktopOperation.Network;
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool published = false, finished = false;
        Task operation = session.RunAsync(token =>
        {
            entered.SetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            // Deliberately ignore cancellation: publication must still be refused.
            return 1;
        }, _ => published = true, kind, () =>
        {
            Assert.True(session.IsCancellationRequested);
            finished = true;
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(kind, session.Operation);
            Assert.True(session.CanCancel);
            session.CancelOperation();
            session.CancelOperation();
            Assert.True(session.IsBusy);
            Assert.False(session.CanCancel);
            Assert.False(session.RequestClose());
            Assert.Equal(network, session.CloseRequested);
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.RunAsync(() => 0, _ => { }));
            Assert.Throws<InvalidOperationException>(session.InvalidateReference);
        }
        finally { release.Set(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.False(published);
        Assert.True(finished);
        AssertIdle(session);
        await session.RunAsync(() => 2, value => Assert.Equal(2, value));
        Assert.False(session.CloseRequested);
    }

    [Fact]
    public async Task OrdinaryWorkCannotBeCancelledOrClosedWhileRunning()
    {
        var session = new WizardSession();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task operation = session.RunAsync(() =>
        {
            entered.SetResult();
            return release.Wait(TimeSpan.FromSeconds(10));
        }, completed => Assert.True(completed));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(session.CanCancel);
            session.CancelOperation();
            Assert.False(session.RequestClose());
            Assert.False(session.CloseRequested);
            Assert.True(session.IsBusy);
        }
        finally { release.Set(); }
        await operation;
        AssertIdle(session);
        Assert.True(session.RequestClose());
    }

    [Theory]
    [InlineData("work")]
    [InlineData("apply")]
    [InlineData("finish")]
    public async Task FailureAtEveryStageReleasesOperationStateAndAllowsRetry(string stage)
    {
        var session = new WizardSession();
        var expected = new IOException("生命周期故障注入");
        int finished = 0;
        var actual = await Assert.ThrowsAsync<IOException>(() => session.RunAsync(token =>
        {
            Assert.True(token.CanBeCanceled);
            if (stage == "work") throw expected;
            return 1;
        }, _ =>
        {
            if (stage == "apply") throw expected;
        }, DesktopOperation.Network, () =>
        {
            finished++;
            if (stage == "finish") throw expected;
        }));
        Assert.Same(expected, actual);
        Assert.Equal(1, finished);
        AssertIdle(session);
        await session.RunAsync(_ => 2, value => Assert.Equal(2, value), DesktopOperation.Parsing);
        AssertIdle(session);
    }

    private static void AssertIdle(WizardSession session)
    {
        Assert.False(session.IsBusy);
        Assert.Null(session.Operation);
        Assert.False(session.CanCancel);
        Assert.False(session.IsCancellationRequested);
    }

    [Fact]
    public async Task PublicationAndCleanupStayLockedAndRejectReentrantOperations()
    {
        var session = new WizardSession();
        Task? fromApply = null, fromCleanup = null;
        bool cleaned = false;
        await session.RunAsync(_ => 1, _ =>
        {
            Assert.True(session.IsBusy);
            Assert.False(session.RequestClose());
            // Publishing a list can synchronously invalidate its dependent results.
            session.InvalidateReference();
            session.InvalidateGame();
            Assert.Throws<InvalidOperationException>(session.Back);
            fromApply = session.RunAsync(() => 2, _ => { });
        }, DesktopOperation.Work, () =>
        {
            Assert.True(session.IsBusy);
            Assert.Equal(DesktopOperation.Work, session.Operation);
            Assert.Throws<InvalidOperationException>(session.Back);
            Assert.Throws<InvalidOperationException>(session.InvalidateComparison);
            fromCleanup = session.RunAsync(() => 3, _ => { });
            cleaned = true;
        });
        Assert.True(cleaned);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fromApply!);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fromCleanup!);
        AssertIdle(session);
        await session.RunAsync(() => 4, value => Assert.Equal(4, value));
    }
}
