using Xunit;

namespace HoyoFileClean.Tests;

public sealed class NetworkServiceTests
{
    [Fact]
    public void ArbitraryServicesLoadWithoutEnumOrDefaultGameAndEncodeLauncherId()
    {
        NetworkService service = Assert.Single(NetworkService.Load([
            new("future-service", "FutureService", new Uri("https://future.example/branches"),
                new Uri("https://future.example/build"), "a&b +/?")]));
        Assert.Equal("future-service", service.Id);
        Assert.Equal("?launcher_id=a%26b%20%2B%2F%3F", service.BranchRequestUrl.Query);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("null-item")]
    [InlineData("empty-id")]
    [InlineData("empty-label")]
    [InlineData("query")]
    [InlineData("http")]
    [InlineData("empty-launcher")]
    [InlineData("duplicate")]
    public void InvalidConfigurationUsesStructuredError(string kind)
    {
        var valid = new NetworkService("a", "A", new Uri("https://a.example/query"), new Uri("https://a.example/build"), "new");
        NetworkService[] services = kind switch
        {
            "empty" => [],
            "null-item" => [null!],
            "empty-id" => [valid with { Id = "" }],
            "empty-label" => [valid with { NameTextId = "" }],
            "query" => [valid with { BranchUrl = new Uri("https://a.example/query?launcher_id=old") }],
            "http" => [valid with { BranchUrl = new Uri("http://a.example/query") }],
            "empty-launcher" => [valid with { LauncherId = "" }],
            _ => [valid, valid with { NameTextId = "B" }]
        };
        CoreException error = Assert.Throws<CoreException>(() => NetworkService.Load(services));
        Assert.Equal(CoreErrorCode.NetworkConfigurationInvalid, error.Code);
        Assert.Equal(Maintenance.NetworkServicesRelativePath, error.Path);
    }
}
