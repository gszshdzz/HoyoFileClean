using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace HoyoFileClean.Tests;

public sealed class CliPresentationTests
{
    private readonly string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Temp", "opencode", "HoyoFileClean.Tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("invalid-list", "清单 JSON 格式无效", "ListJsonInvalid")]
    [InlineData("existing-output", "输出已存在，不能覆盖", "OutputExists")]
    [InlineData("unsafe-path", "文件路径不是安全的相对路径", "RelativePathInvalid")]
    [InlineData("same-side-conflict", "同一侧的清单存在相互矛盾", "InputConflict")]
    [InlineData("inside-root", "输出必须放在扫描根目录之外", "OutputInsideScanRoot")]
    [InlineData("broken-manifest", "清单损坏或不是支持的 manifest", "ManifestEncodingInvalid")]
    public void CoreErrorsArePresentedByCliWithoutRawExceptionText(string scenario, string message, string code)
    {
        Directory.CreateDirectory(directory);
        string output = Path.Combine(directory, "output.json");
        string a = WriteList("a", "file", 3), b = WriteList("b", "FILE", 4);
        string[] arguments = ["compare", output, "--a", a, "--b", b];
        switch (scenario)
        {
            case "invalid-list": File.WriteAllText(a, "{"); break;
            case "existing-output": File.WriteAllText(output, "keep"); break;
            case "unsafe-path": a = WriteList("a", "../escape", 3); break;
            case "same-side-conflict": arguments = ["compare", output, "--a", a, b, "--b", b]; break;
            case "inside-root": arguments = ["scan", directory, output]; break;
            case "broken-manifest":
                File.WriteAllBytes(a, [0x0a, 0xff]);
                arguments = ["convert", a, output];
                break;
        }
        var result = Run(arguments);
        Assert.Equal(1, result.Code);
        Assert.Empty(result.Output);
        Assert.Contains("错误：" + message, result.Error);
        Assert.Contains("错误代码：" + code, result.Error);
        Assert.DoesNotContain("CoreException", result.Error);
        Assert.DoesNotContain("System.", result.Error);
        Assert.DoesNotContain(" at ", result.Error);
        Assert.DoesNotContain("   在 ", result.Error);
        Assert.DoesNotContain("游戏", result.Error);
        if (scenario == "existing-output") Assert.Equal("keep", File.ReadAllText(output));
        else Assert.False(File.Exists(output));
        if (scenario == "same-side-conflict") Assert.Contains(b, result.Error);
    }

    [Fact]
    public void SystemFileErrorsAreAlsoPresentedWithoutRawSystemMessage()
    {
        Directory.CreateDirectory(directory);
        string missing = Path.Combine(directory, "missing.manifest");
        var result = Run(["convert", missing, Path.Combine(directory, "output.json")]);
        Assert.Equal(1, result.Code);
        Assert.Contains("错误：输入文件不存在。", result.Error);
        Assert.Contains(missing, result.Error);
        Assert.Contains("诊断代码：0x", result.Error);
        Assert.DoesNotContain("FileNotFoundException", result.Error);
        Assert.DoesNotContain("Could not find", result.Error);
        Assert.DoesNotContain("System.", result.Error);
    }

    [Fact]
    public void CoreCarriesFactsInsteadOfUserAdvice()
    {
        string a = WriteList("a", "file", 3), b = WriteList("b", "FILE", 4);
        CoreException error = Assert.Throws<CoreException>(() => FileComparer.ReadInputs([a, b]));
        Assert.Equal(CoreErrorCode.InputConflict, error.Code);
        Assert.Equal("FILE", error.Path);
        Assert.Equal(b, error.Input);
        Assert.Equal(nameof(CoreErrorCode.InputConflict), error.Message);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void RemovedCompressionOptionIsNotAdvertisedOrAccepted()
    {
        var help = Run(["--help"]);
        Assert.Equal(0, help.Code);
        Assert.DoesNotContain("--uncompressed", help.Output);
        Assert.DoesNotContain("protobuf", help.Output);
        Assert.DoesNotContain("zstd", help.Output);
        Assert.Equal(2, Run(["convert", "input", "output", "--uncompressed"]).Code);
    }

    [Fact]
    public void JsonParserFailureIsPreservedForDiagnosticsNotPresentedToUser()
    {
        Directory.CreateDirectory(directory);
        string input = Path.Combine(directory, "invalid.json");
        File.WriteAllText(input, "{");
        CoreException error = Assert.Throws<CoreException>(() => FileComparer.ReadInputs([input]));
        Assert.Equal(CoreErrorCode.ListJsonInvalid, error.Code);
        Assert.IsType<JsonException>(error.InnerException);
        Assert.Equal(input, error.Path);
    }

    private string WriteList(string name, string path, long size)
    {
        Directory.CreateDirectory(directory);
        string input = Path.Combine(directory, name + ".json");
        File.WriteAllText(input, JsonSerializer.Serialize(new { fileCount = 1, totalSize = size, files = new[] { new { path, size } } }));
        return input;
    }

    private static (int Code, string Output, string Error) Run(string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "HoyoFileClean.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 CLI。");
        Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(20000), "CLI 未在 20 秒内结束。");
        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }
}
