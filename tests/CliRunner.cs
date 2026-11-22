using System.Diagnostics;
using System.Text;
using Xunit;

namespace HoyoFileClean.Tests;

internal static class CliRunner
{
    internal static (int ExitCode, string Output, string Error) Run(params string[] arguments)
        => Run(false, arguments);

    internal static (int ExitCode, string Output, string Error) RunWithLegacyConsoleEncoding(params string[] arguments)
        => Run(true, arguments);

    private static (int ExitCode, string Output, string Error) Run(bool legacyEncoding, string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "HoyoFileClean.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (legacyEncoding)
        {
            start.Environment["DOTNET_STARTUP_HOOKS"] = typeof(CliRunner).Assembly.Location;
        }
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 CLI。");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
            Assert.True(process.WaitForExit(20000), "CLI 未在 20 秒内结束。");
            // A child holding a redirected pipe open must not leave stream collection unbounded either.
            string[] streams = Task.WhenAll(output, error).WaitAsync(timeout.Token).GetAwaiter().GetResult();
            return (process.ExitCode, streams[0], streams[1]);
        }
        finally
        {
            timeout.Cancel();
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                Assert.True(process.WaitForExit(5000), "CLI 终止后未在 5 秒内退出。");
            }
        }
    }
}
