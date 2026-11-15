using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Xunit;

namespace HoyoFileClean.Desktop.Tests;

// Exercise controls on their owning thread with the same message loop as the application.
internal static class NativeTestHost
{
    internal static WizardSession ReferenceSession()
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "opencode", "HoyoFileClean.Desktop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var session = new WizardSession();
        session.AcceptGame(DirectoryScanner.Scan(root), root);
        session.Next();
        return session;
    }

    internal static MainForm ReferenceForm() => new(ReferenceSession());

    internal static string WriteGameConfig(string root)
    {
        string path = Path.Combine(root, "config.ini");
        File.WriteAllText(path, """
            [General]
            game_version=7.2.0
            channel=1
            sub_channel=1
            uapc={"hk4e_cn":{"uapc":""},"hyp":{"uapc":""}}
            """);
        return path;
    }

    internal static void OnSta(Action action, [CallerMemberName] string testName = "")
    {
        Exception? failure = null;
        bool executed = false;
        var thread = new Thread(() =>
        {
            try
            {
                using var host = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                    Location = new Point(-20000, -20000), Size = new Size(1, 1) };
                host.Shown += (_, _) => host.BeginInvoke(() =>
                {
                    try { executed = true; action(); }
                    catch (Exception error) { failure = error; }
                    finally { host.Close(); }
                });
                Application.Run(host);
            }
            catch (Exception error) { failure = error; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), $"{testName}: 原生控件测试未在 30 秒内完成。");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        Assert.True(executed, $"{testName}: 消息循环退出前未执行测试动作。");
    }

    internal static void WaitUntil(Func<bool> condition, int timeoutSeconds = 5,
        [CallerArgumentExpression(nameof(condition))] string conditionText = "")
    {
        var timer = Stopwatch.StartNew();
        while (!condition() && timer.Elapsed < TimeSpan.FromSeconds(timeoutSeconds))
        {
            Application.DoEvents();
            Thread.Yield();
        }
        Assert.True(condition(), $"等待 {timeoutSeconds} 秒后条件仍未满足：{conditionText}");
    }

    internal static T Find<T>(Control parent, string name) where T : Control
        => Assert.IsAssignableFrom<T>(Assert.Single(parent.Controls.Find(name, true)));
}
