using Xunit;

namespace HoyoFileClean.Tests;

public sealed class ManifestVersionTests
{
    [Theory]
    [InlineData("0.0.0", true)]
    [InlineData("7.2.0", true)]
    [InlineData("10.20.300", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("latest", false)]
    [InlineData("7.2", false)]
    [InlineData("7.2.0.1", false)]
    [InlineData("-1.2.3", false)]
    [InlineData("7.2.preview", false)]
    [InlineData("7.2.2147483648", false)]
    public void NetworkVersionRequiresExactlyThreeNumericParts(string? version, bool expected)
        => Assert.Equal(expected, NetworkManifestClient.IsValidVersion(version));
}
