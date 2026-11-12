using Xunit;

namespace HoyoFileClean.Tests;

public sealed class NetworkServiceTests
{
    [Fact]
    public void ArbitraryServicesLoadWithoutEnumOrDefaultGameAndEncodeLauncherId()
    {
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "opencode", "network-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "services.json");
        File.WriteAllText(path, """
            [{"Id":"future-service","NameTextId":"FutureService","BranchUrl":"https://future.example/branches",
              "BuildUrl":"https://future.example/build","LauncherId":"a&b +/?"}]
            """);
        NetworkService service = Assert.Single(NetworkService.Load(path));
        Assert.Equal("future-service", service.Id);
        Assert.Equal("?launcher_id=a%26b%20%2B%2F%3F", service.BranchRequestUrl.Query);
        Assert.DoesNotContain("DefaultBiz", File.ReadAllText(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("[null]")]
    [InlineData("[{}]")]
    [InlineData("[{\"Id\":\"a\",\"NameTextId\":\"A\",\"BranchUrl\":\"https://a.example/query?launcher_id=old\",\"BuildUrl\":\"https://a.example/build\",\"LauncherId\":\"new\"}]")]
    [InlineData("[{\"Id\":\"a\",\"NameTextId\":\"A\",\"BranchUrl\":\"http://a.example/query\",\"BuildUrl\":\"https://a.example/build\",\"LauncherId\":\"new\"}]")]
    [InlineData("[{\"Id\":\"a\",\"NameTextId\":\"A\",\"BranchUrl\":\"https://a.example/query\",\"BuildUrl\":\"https://a.example/build\",\"LauncherId\":\"\"}]")]
    [InlineData("[{\"Id\":\"a\",\"NameTextId\":\"A\",\"BranchUrl\":\"https://a.example/query\",\"BuildUrl\":\"https://a.example/build\",\"LauncherId\":\"new\"},{\"Id\":\"a\",\"NameTextId\":\"B\",\"BranchUrl\":\"https://b.example/query\",\"BuildUrl\":\"https://b.example/build\",\"LauncherId\":\"new\"}]")]
    public void InvalidConfigurationUsesStructuredError(string? content)
    {
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "opencode", "network-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "services.json");
        if (content is not null) File.WriteAllText(path, content);
        CoreException error = Assert.Throws<CoreException>(() => NetworkService.Load(path));
        Assert.Equal(CoreErrorCode.NetworkConfigurationInvalid, error.Code);
        Assert.Equal(path, error.Path);
    }
}
