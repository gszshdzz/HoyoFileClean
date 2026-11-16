using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Resources;
using HoyoFileClean.Presentation;
using Xunit;

namespace HoyoFileClean.Tests;

public sealed class LocalizationTests
{
    [Fact]
    public void LanguageHasUniqueKeysAndValidTemplates()
    {
        string name = $"HoyoFileClean.Presentation.Languages.{PresentationOptions.DefaultLanguage}.json";
        using Stream input = typeof(Text).Assembly.GetManifestResourceStream(name)!;
        using JsonDocument fallback = JsonDocument.Parse(input);
        string[] resources = typeof(Text).Assembly.GetManifestResourceNames()
            .Where(resource => resource.StartsWith("HoyoFileClean.Presentation.Languages.", StringComparison.Ordinal)
                && resource.EndsWith(".json", StringComparison.Ordinal)).ToArray();
        Assert.Contains(name, resources);
        foreach (string resource in resources)
        {
            using Stream localizedInput = typeof(Text).Assembly.GetManifestResourceStream(resource)!;
            using JsonDocument language = JsonDocument.Parse(localizedInput);
            JsonProperty[] entries = language.RootElement.EnumerateObject().ToArray();
            Assert.Equal(entries.Length, entries.Select(entry => entry.Name).Distinct().Count());
            foreach (JsonProperty entry in entries)
            {
                Assert.True(fallback.RootElement.TryGetProperty(entry.Name, out JsonElement original), $"{resource}: {entry.Name}");
                string template = entry.Value.GetString()!;
                Assert.False(string.IsNullOrWhiteSpace(template), $"{resource}: {entry.Name}");
                CompositeFormat format = CompositeFormat.Parse(template);
                Assert.Equal(CompositeFormat.Parse(original.GetString()!).MinimumArgumentCount, format.MinimumArgumentCount);
                object[] values = Enumerable.Repeat<object>(123, format.MinimumArgumentCount).ToArray();
                Assert.NotNull(string.Format(CultureInfo.InvariantCulture, format, values));
            }
        }
    }

    [Fact]
    public void LiteralCallSiteKeysExistWithoutSeparateIdList()
    {
        string root = RepositoryRoot();
        int checkedCalls = 0;
        foreach (string directory in new[] { "cli", "desktop", "presentation" })
        foreach (string file in Directory.EnumerateFiles(Path.Combine(root, directory), "*.cs"))
        {
            string source = File.ReadAllText(file);
            foreach (Match call in Regex.Matches(source, "\\b(?:L|Text)\\.(?:Get|Format|FormatInvariant)\\(\\s*\"(?<id>[^\"]+)\""))
            {
                string id = call.Groups["id"].Value;
                Assert.True(Text.Contains(id), $"{file}: {id}");
                checkedCalls++;
            }
        }
        Assert.True(checkedCalls > 0);
    }

    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string path = "")
        => Path.GetDirectoryName(Path.GetDirectoryName(path))!;

    [Fact]
    public void MissingKeyFailsExplicitlyInsteadOfShowingId()
        => Assert.Throws<MissingManifestResourceException>(() => Text.Get("not-a-real-resource-key"));

    [Theory]
    [InlineData("file", "InputFileMissing", "DesktopFileMissing")]
    [InlineData("directory", "InputDirectoryMissing", "DesktopDirectoryMissing")]
    [InlineData("permission", "FilePermissionDenied", "DesktopPermissionDenied")]
    [InlineData("data", "InputDataInvalid", "DesktopBatchInvalid")]
    [InlineData("network", "OfficialRequestFailed", "OfficialRequestFailed")]
    [InlineData("cancel", "NetworkCancelledOrTimedOut", "DesktopNetworkCancelled")]
    [InlineData("io", "FilePublicationFailed", "DesktopIoFailed")]
    [InlineData("argument", "InvalidPathOrArgument", "DesktopArgumentInvalid")]
    [InlineData("unsupported", "UnsupportedPathOrOperation", "DesktopArgumentInvalid")]
    [InlineData("unknown", "ProcessingFailed", "DesktopOperationFailed")]
    public void ErrorPresentationKeepsFrontendSpecificWording(string kind, string cliId, string desktopId)
    {
        Exception error = kind switch
        {
            "file" => new FileNotFoundException(),
            "directory" => new DirectoryNotFoundException(),
            "permission" => new UnauthorizedAccessException(),
            "data" => new InvalidDataException(),
            "network" => new HttpRequestException(),
            "cancel" => new OperationCanceledException(),
            "io" => new IOException(),
            "argument" => new ArgumentException(),
            "unsupported" => new NotSupportedException(),
            _ => new Exception()
        };
        Assert.Equal(Text.Get(cliId), DiagnosticText.ForCli(error));
        Assert.Equal(Text.Get(desktopId), DiagnosticText.ForDesktop(error));
    }

    [Fact]
    public void SourcePresentationRedactsOnlyWebCredentialsAndKeepsIdentityUntouched()
    {
        const string source = "https://example.invalid/manifest?signature=private-value#fragment";
        Assert.Equal("https://example.invalid/manifest", DiagnosticText.DisplaySource(source));
        Assert.Equal("https://example.invalid/manifest", DiagnosticText.DisplaySource("https://user:pass@example.invalid/manifest"));
        Assert.Equal(@"C:\manifests\local", DiagnosticText.DisplaySource(@"C:\manifests\local"));
        Assert.Equal("https://example.invalid/manifest?signature=private-value#fragment", source);
    }

    [Fact]
    public void ErrorLimitsAndConfigurationPathComeFromMaintenance()
    {
        Assert.Equal(Text.Format("CoreErrorCode_ManifestCompressedSizeLimit", Maintenance.CompressedLimit / 1048576),
            DiagnosticText.ForCli(new CoreException(CoreErrorCode.ManifestCompressedSizeLimit)));
        Assert.Equal(Text.Format("CoreErrorCode_ManifestDecodedSizeLimit", Maintenance.DecodedLimit / 1048576),
            DiagnosticText.ForCli(new CoreException(CoreErrorCode.ManifestDecodedSizeLimit)));
        var configuration = new CoreException(CoreErrorCode.NetworkConfigurationInvalid);
        Assert.Equal(Text.Format("CoreErrorCode_NetworkConfigurationInvalid", Maintenance.NetworkServicesRelativePath), DiagnosticText.ForCli(configuration));
        Assert.Equal(Text.Format("DesktopConfigurationInvalid", Maintenance.NetworkServicesRelativePath), DiagnosticText.ForDesktop(configuration));
    }

    [Fact]
    public void EveryCoreErrorHasTextThroughActualPresentationEntrypoints()
    {
        foreach (CoreErrorCode code in Enum.GetValues<CoreErrorCode>())
        {
            var error = new CoreException(code);
            string cli = DiagnosticText.ForCli(error), desktop = DiagnosticText.ForDesktop(error);
            Assert.NotEqual(Text.Get("UnknownCoreError"), cli);
            Assert.False(string.IsNullOrWhiteSpace(cli));
            Assert.False(string.IsNullOrWhiteSpace(desktop));
            Assert.DoesNotContain("{0}", cli);
            Assert.DoesNotContain("{0}", desktop);
        }
    }

    [Fact]
    public void IdentificationRuleTextIdsExistAndTemplatesAcceptExtractedValues()
    {
        var configuration = GameIdentifier.LoadConfiguration();
        foreach (IdentificationMeaning information in configuration.Detections.Values
            .SelectMany(detection => detection.Meanings?.Values ?? Enumerable.Empty<IdentificationMeaning>()))
        {
            Assert.NotNull(information.LabelTextId);
            Assert.True(Text.Contains(information.LabelTextId), information.LabelTextId);
            Assert.Equal(0, CompositeFormat.Parse(Text.Get(information.LabelTextId)).MinimumArgumentCount);
            IdentificationMeaning rule = information;
                if (rule.TextId is { } textId)
                {
                    Assert.True(Text.Contains(textId), textId);
                    Assert.Equal(0, CompositeFormat.Parse(Text.Get(textId)).MinimumArgumentCount);
                }
                if (rule.DisplayFormatTextId is { } formatId)
                {
                    Assert.True(Text.Contains(formatId), formatId);
                    Assert.InRange(CompositeFormat.Parse(Text.Get(formatId)).MinimumArgumentCount, 1, 2);
                    Assert.NotEmpty(Text.Format(formatId, "1.0.0", "dynamic-plugin"));
                }
        }
    }

    [Fact]
    public void ConfiguredDisplayNamesAreLanguageIdsAndUnknownCultureFallsBack()
    {
        foreach (NetworkService service in NetworkManifestClient.Services) Assert.True(Text.Contains(service.NameTextId));
        CultureInfo original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal("国服", Text.Get("ServiceChina"));
        }
        finally { CultureInfo.CurrentUICulture = original; }
    }

    [Fact]
    public void NumericFormattingStillUsesCurrentCultureAndInvariantCliProgress()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Contains("1.234", Text.Format("ConversionCompleted", 1234, 0));
            Assert.Contains("1,234", Text.FormatInvariant("HashProgress", 1.5, 1234, 1234, 1.5, 2.5));
        }
        finally { CultureInfo.CurrentCulture = original; }
    }
}
