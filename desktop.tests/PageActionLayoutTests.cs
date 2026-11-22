using HoyoFileClean.Desktop;
using Xunit;
using static HoyoFileClean.Desktop.Tests.NativeTestHost;

namespace HoyoFileClean.Desktop.Tests;

[Collection("Windows Forms")]
public sealed class PageActionLayoutTests
{
    [Theory]
    [InlineData(800, 650, false)]
    [InlineData(1040, 1000, false)]
    [InlineData(800, 650, true)]
    [InlineData(1040, 1000, true)]
    public void MainActionsStayBelowContentAboveProgressAndOutsideNavigation(int width, int height, bool import) => OnSta(() =>
    {
        WizardSession session = ReferenceSession();
        session.Back();
        using var form = new MainForm(session) { StartPosition = FormStartPosition.Manual,
            Location = new Point(-20000, -20000), ShowInTaskbar = false, Size = new Size(width, height) };
        form.Show();
        Application.DoEvents();
        if (import)
        {
            Find<RadioButton>(form, "ImportGameSource").Checked = true;
            Application.DoEvents();
            Check(["ReadGame"], "GameJson", "ScanFileProgress");
            return;
        }
        Check(["LoadGamePreset", "ReadGame"], "ExcludedDirectories", "ScanFileProgress");
        Find<Button>(form, "Next").PerformClick();
        foreach (var mode in new[]
        {
            ("AutomaticSource", "NetworkManifests", new[] { "ParseNetworkReference" }),
            ("NetworkSource", "NetworkManifests", new[] { "QueryNetwork", "GetNetworkManifests", "ParseNetworkReference", "CancelNetwork" }),
            ("LocalSource", "LocalManifests", new[] { "GetLocalManifests", "ParseLocalReference" }),
            ("ManualSource", "Manifests", new[] { "AddManifest", "RemoveManifest", "ParseManualReference" })
        })
        {
            Find<RadioButton>(form, mode.Item1).Checked = true;
            Application.DoEvents();
            Check(mode.Item3, mode.Item2, "OperationProgress");
        }
        session.AcceptReference(session.Game!);
        // Refresh the navigation state without changing input.
        Find<Button>(form, "Back").PerformClick();
        Find<Button>(form, "Next").PerformClick();
        Find<Button>(form, "Next").PerformClick();
        Application.DoEvents();
        Assert.Equal(2, session.Step);
        Check(["Execute"], "ComparisonResult", "ComparisonProgress");

        void Check(string[] buttons, string contentName, string progressName)
        {
            Control body = Find<Control>(form, contentName);
            Assert.True(body.Visible);
            Button navigation = Find<Button>(form, "Back");
            ProgressBar progress = Find<ProgressBar>(form, progressName);
            bool progressWasVisible = progress.Visible;
            // Make the existing progress region visible to verify its bounds without starting work.
            if (progressName == "ScanFileProgress") progress.Parent!.Visible = true;
            else progress.Visible = true;
            Application.DoEvents();
            foreach (string name in buttons)
            {
                Button button = Find<Button>(form, name);
                Rectangle bounds = button.RectangleToScreen(button.ClientRectangle);
                Assert.True(button.Visible, name);
                Assert.True(bounds.Top >= body.RectangleToScreen(body.ClientRectangle).Bottom,
                    $"{name} must be below {contentName}");
                Assert.True(bounds.Bottom <= progress.RectangleToScreen(progress.ClientRectangle).Top,
                    $"{name} must be above {progressName}");
                Assert.True(bounds.Bottom < navigation.RectangleToScreen(navigation.ClientRectangle).Top);
                Assert.NotSame(navigation.Parent, button.Parent);
                Assert.True(button.Parent!.ClientRectangle.Contains(button.Bounds), $"{name} is clipped");
            }
            Assert.Equal(new[] { "Back", "Next", "CloseWizard" },
                navigation.Parent!.Controls.Cast<Control>().Select(control => control.Name));
            if (progressName == "ScanFileProgress") progress.Parent!.Visible = progressWasVisible;
            else progress.Visible = progressWasVisible;
            Application.DoEvents();
        }
    });
}
