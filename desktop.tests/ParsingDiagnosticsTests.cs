using System.Diagnostics;
using System.Runtime.ExceptionServices;
using HoyoFileClean.Desktop;
using Xunit;

namespace HoyoFileClean.Desktop.Tests;

// TableLayout uses shared mutable sizing proxies, so native UI tests must not lay out concurrently.
[Collection("Windows Forms")]
public sealed class ParsingDiagnosticsTests
{
    [Fact(Explicit = true)]
    public void RealLocalBatchRemainsResponsive()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { Measure(); }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Measure()
    {
        var files = ManifestDiscovery.Find(MainForm.DefaultManifestDirectory);
        var session = new WizardSession();
        using var form = new MainForm(session)
        {
            StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false
        };
        T Find<T>(string name) where T : Control => (T)form.Controls.Find(name, true).Single();
        form.Show();
        Find<RadioButton>("LocalSource").Checked = true;
        var table = Find<ManifestTable>("LocalManifests");
        table.AddFiles(files, check: true);
        var watch = Stopwatch.StartNew();
        long last = 0, maxPause = 0, peakManaged = 0, peakPrivate = 0;
        int ticks = 0, statusUpdates = 0;
        Find<Label>("Status").TextChanged += (_, _) => statusUpdates++;
        using var timer = new System.Windows.Forms.Timer { Interval = 15 };
        timer.Tick += (_, _) =>
        {
            long now = watch.ElapsedMilliseconds;
            maxPause = Math.Max(maxPause, now - last);
            last = now;
            ticks++;
            peakManaged = Math.Max(peakManaged, GC.GetTotalMemory(false));
            using Process process = Process.GetCurrentProcess();
            peakPrivate = Math.Max(peakPrivate, process.PrivateMemorySize64);
        };
        for (int round = 1; round <= 2; round++)
        {
            watch.Restart();
            last = maxPause = peakManaged = peakPrivate = 0;
            ticks = statusUpdates = 0;
            timer.Start();
            long before = GC.GetTotalMemory(false);
            Find<Button>("ParseLocalReference").PerformClick();
            long startMs = watch.ElapsedMilliseconds;
            while ((session.IsBusy || Find<ProgressBar>("OperationProgress").Visible
                    || Find<Label>("Status").Text.StartsWith("正在", StringComparison.Ordinal))
                && watch.Elapsed < TimeSpan.FromMinutes(4)) Application.DoEvents();
            long finishMs = watch.ElapsedMilliseconds;
            maxPause = Math.Max(maxPause, finishMs - last);
            long after = GC.GetTotalMemory(false);
            using Process process = Process.GetCurrentProcess();
            Console.WriteLine($"REAL threads={ReferenceParser.DefaultConcurrency}, round={round}, files={files.Count}, bytes={files.Sum(file => file.Size)}, start={startMs}ms, total={finishMs}ms, maxUiPause={maxPause}ms, ticks={ticks}, statusUpdates={statusUpdates}, managedBefore={before}, managedPeak={peakManaged}, managedAfter={after}, privatePeak={peakPrivate}, privateAfter={process.PrivateMemorySize64}, valid={session.Reference is not null}, status={Find<Label>("Status").Text}");
            timer.Stop();
            Assert.False(session.IsBusy);
            Assert.True(maxPause < 500, $"UI 停顿 {maxPause}ms，启动同步阶段 {startMs}ms");
            Assert.True(peakManaged < 256L * 1024 * 1024, $"托管峰值 {peakManaged}");
        }
        form.Close();
    }
}
