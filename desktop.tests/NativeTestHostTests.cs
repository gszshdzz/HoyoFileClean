using Xunit;
using static HoyoFileClean.Desktop.Tests.NativeTestHost;

namespace HoyoFileClean.Desktop.Tests;

[Collection("Windows Forms")]
public sealed class NativeTestHostTests
{
    [Fact]
    public void ActionsRunOnStaWithAnActiveMessageLoopAndCanRunAgain()
    {
        int completed = 0;
        for (int i = 0; i < 2; i++) OnSta(() =>
        {
            Assert.Equal(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());
            Assert.True(Application.MessageLoop);
            using var control = new Control();
            control.CreateControl();
            bool applied = false;
            control.BeginInvoke(() => applied = true);
            WaitUntil(() => applied);
            completed++;
        });
        Assert.Equal(2, completed);
    }

    [Fact]
    public void ActionFailureReachesTheCallerWithoutReplacingTheException()
    {
        var expected = new InvalidOperationException("宿主故障注入");
        var actual = Assert.Throws<InvalidOperationException>(() => OnSta(() => throw expected));
        Assert.Same(expected, actual);
        OnSta(() => Assert.True(Application.MessageLoop));
    }

    [Fact]
    public void UnsatisfiedWaitFailsWithItsConditionAndDoesNotBlockTheNextWait() => OnSta(() =>
    {
        bool completed = false;
        var error = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => WaitUntil(() => completed, timeoutSeconds: 0));
        Assert.Contains("completed", error.Message);
        completed = true;
        WaitUntil(() => completed);
    });
}
