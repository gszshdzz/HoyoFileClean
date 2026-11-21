using HoyoFileClean.Presentation;
using Xunit;

namespace HoyoFileClean.Desktop.Tests;

public sealed class FileSizeTextTests
{
    [Theory]
    [InlineData(0L, "0B")]
    [InlineData(3L, "3B")]
    [InlineData(1024L, "1KB")]
    [InlineData(125952L, "123KB")]
    [InlineData(1861222L, "1.77MB")]
    [InlineData(132499741081L, "123GB")]
    [InlineData(1073634449817L, "0.976TB")]
    [InlineData(1099511627776L, "1TB")]
    [InlineData(long.MaxValue, "8EB")]
    public void AllGuiSizesUseOneCompactFormat(long bytes, string expected)
        => Assert.Equal(expected, FileSizeText.Format(bytes));
}
