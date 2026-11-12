using Xunit;

namespace HoyoFileClean.Tests;

public sealed class NetworkServiceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("[null]")]
    [InlineData("[{}]")]
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
