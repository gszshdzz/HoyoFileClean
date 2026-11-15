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
        var result = CliRunner.Run(arguments);
        Assert.Equal(1, result.ExitCode);
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
        var result = CliRunner.Run(["convert", missing, Path.Combine(directory, "output.json")]);
        Assert.Equal(1, result.ExitCode);
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
        var help = CliRunner.Run(["--help"]);
        Assert.Equal(0, help.ExitCode);
        Assert.DoesNotContain("--uncompressed", help.Output);
        Assert.DoesNotContain("protobuf", help.Output);
        Assert.DoesNotContain("zstd", help.Output);
        Assert.Equal(2, CliRunner.Run(["convert", "input", "output", "--uncompressed"]).ExitCode);
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

    [Theory]
    [InlineData("--network")]
    [InlineData("--network --game genshin --region china")]
    [InlineData("--network --game genshin --version latest")]
    [InlineData("--network --region china --version latest")]
    [InlineData("--network --game genshin --region china --version 7.1")]
    [InlineData("--network --game genshin --region china --version latest --version 7.1.0")]
    [InlineData("--network --service china --biz hk4e_cn --version latest")]
    [InlineData("--network --biz hk4e_cn --version latest --package game")]
    [InlineData("--network --service china --version latest --package game")]
    [InlineData("--network --service china --biz hk4e_cn --package game")]
    [InlineData("--network --service china --biz hk4e_cn --version 7.1 --package game")]
    [InlineData("--network --service china --biz hk4e_cn --version latest --package game --biz other")]
    public void NetworkConvertRequiresExplicitValidSelection(string arguments)
    {
        var result = CliRunner.Run(new[] { "convert", "unused.json" }.Concat(arguments.Split(' ')).ToArray());
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("--network", result.Output);
        Assert.Empty(result.Error);
    }

    [Theory]
    [InlineData("hk4e_cn", "china", "zh-cn")]
    [InlineData("future-game", "future-region", "future-package")]
    public void NetworkConvertRefusesExistingOutputBeforeConnecting(string biz, string service, string package)
    {
        Directory.CreateDirectory(directory);
        string output = Path.Combine(directory, "keep.json");
        File.WriteAllText(output, "keep");
        var result = CliRunner.Run(["convert", output, "--network", "--biz", biz, "--service", service, "--version", "latest", "--package", package]);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("OutputExists", result.Error);
        Assert.Equal("keep", File.ReadAllText(output));
    }

    [Fact]
    public void NetworkUsageHasGenericPlaceholdersAndExamplesBelow()
    {
        string help = CliRunner.Run(["--help"]).Output;
        string usage = Assert.Single(help.Split('\n'), line => line.Contains("convert <output.json> --network"));
        Assert.Contains("--service <service> --biz <biz> --version <version>", usage);
        Assert.Contains("--package <package>", usage);
        Assert.DoesNotContain("hk4e", usage);
        Assert.DoesNotContain("zh-cn", usage);
        Assert.Contains("提示示例", help);
    }

    [Theory]
    [InlineData("game")]
    [InlineData("gamwe")]
    public void NetworkConvertRejectsMoreThanOnePackageBeforeAnyWork(string secondPackage)
    {
        Directory.CreateDirectory(directory);
        string output = Path.Combine(directory, "keep.json");
        File.WriteAllText(output, "keep");
        var result = CliRunner.Run(["convert", output, "--service", "china", "--biz", "hk4e_cn", "--version", "latest",
            "--package", "game", "--package", secondPackage, "--network"]);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("--package 最多指定一次", result.Error);
        Assert.DoesNotContain("OutputExists", result.Error);
        Assert.Equal("keep", File.ReadAllText(output));
    }

    [Fact]
    public void NetworkParserErrorsDoNotPrintUrlQueryParameters()
    {
        byte[] bytes = [0x0a, 0xff];
        const string url = "https://autopatchcn.yuanshen.com/test/manifest?signature=private-value";
        var manifest = new RemoteManifest("game", "本体", new Uri(url), bytes.Length, bytes.Length,
            Convert.ToHexString(System.Security.Cryptography.MD5.HashData(bytes)), false);
        CoreException error = Assert.Throws<CoreException>(() => NetworkManifestClient.Read(manifest, bytes, TestContext.Current.CancellationToken));
        using var output = new StringWriter();
        TextWriter original = Console.Error;
        try
        {
            Console.SetError(output);
            System.Reflection.Assembly.Load("HoyoFileClean").GetType("HoyoFileClean.Cli.CliOutput")!
                .GetMethod("Error")!.Invoke(null, [error]);
        }
        finally { Console.SetError(original); }
        Assert.Contains("https://autopatchcn.yuanshen.com/test/manifest", output.ToString());
        Assert.DoesNotContain("signature", output.ToString());
        Assert.DoesNotContain("private-value", output.ToString());
    }

    private string WriteList(string name, string path, long size)
    {
        Directory.CreateDirectory(directory);
        string input = Path.Combine(directory, name + ".json");
        File.WriteAllText(input, JsonSerializer.Serialize(new { fileCount = 1, totalSize = size, files = new[] { new { path, size } } }));
        return input;
    }

}
