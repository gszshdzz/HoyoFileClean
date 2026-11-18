using Xunit;
using static HoyoFileClean.Desktop.Tests.NativeTestHost;

namespace HoyoFileClean.Desktop.Tests;

[Collection("Windows Forms")]
public sealed class NativeTestHostTests
{
    [Fact]
    public void LayoutBatchDefersNestedLayoutAndResumesAfterFailure() => OnSta(() =>
    {
        using var root = new Panel();
        using var child = new Panel();
        root.Controls.Add(child);
        int layouts = 0;
        child.Layout += (_, _) => layouts++;
        var expected = new InvalidOperationException("批量布局故障注入");
        Action mutate = () =>
        {
            using var batch = new LayoutBatch(root);
            for (int i = 0; i < 10; i++) child.Padding = new Padding(i);
            Assert.Equal(0, layouts);
            throw expected;
        };
        var actual = Assert.Throws<InvalidOperationException>(mutate);
        Assert.Same(expected, actual);
        Assert.Equal(1, layouts);
        child.Padding = new Padding(20);
        Assert.Equal(2, layouts);
    });

    [Fact]
    public void SourceChangesBatchLayoutInsteadOfRecomputingForEveryField() => OnSta(() =>
    {
        using var form = new MainForm(new WizardSession());
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-20000, -20000);
        form.Show();
        int layouts = 0;
        void Observe(Control control)
        {
            control.Layout += (_, _) => layouts++;
            foreach (Control child in control.Controls) Observe(child);
        }
        Observe(form);
        for (int i = 0; i < 5; i++)
        {
            Find<RadioButton>(form, "ImportGameSource").Checked = true;
            Find<RadioButton>(form, "ScanGameSource").Checked = true;
        }
        Console.WriteLine($"layouts for ten source changes: {layouts}");
        Assert.InRange(layouts, 1, 300);
    });

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
