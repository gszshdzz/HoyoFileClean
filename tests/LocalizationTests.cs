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
        using JsonDocument language = JsonDocument.Parse(input);
        JsonProperty[] entries = language.RootElement.EnumerateObject().ToArray();
        Assert.Equal(entries.Length, entries.Select(entry => entry.Name).Distinct().Count());
        foreach (JsonProperty entry in entries)
        {
            string id = entry.Name;
            string template = Text.Get(id);
            Assert.False(string.IsNullOrWhiteSpace(template), id.ToString());
            CompositeFormat format = CompositeFormat.Parse(template);
            object[] values = Enumerable.Repeat<object>(123, format.MinimumArgumentCount).ToArray();
            Assert.NotNull(Text.Format(id, values));
        }
    }

    [Fact]
    public void LiteralCallSiteKeysExistWithoutSeparateIdList()
    {
        string root = RepositoryRoot();
        int checkedCalls = 0;
        foreach (string directory in new[] { "cli", "desktop" })
        foreach (string file in Directory.EnumerateFiles(Path.Combine(root, directory), "*.cs"))
        {
            string source = File.ReadAllText(file);
            foreach (Match call in Regex.Matches(source, "\\bL\\.(?:Get|Format|FormatInvariant)\\(\\s*\"(?<id>[^\"]+)\""))
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

    [Fact]
    public void EveryCoreErrorHasExplicitCliTextMapping()
    {
        Type cli = System.Reflection.Assembly.Load("HoyoFileClean").GetType("HoyoFileClean.Cli.CliOutput")!;
        var map = cli.GetMethod("ErrorTextId", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var describe = cli.GetMethod("Describe", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        foreach (CoreErrorCode code in Enum.GetValues<CoreErrorCode>())
        {
            string id = Assert.IsType<string>(map.Invoke(null, [code]));
            Assert.NotEqual("UnknownCoreError", id);
            Assert.False(string.IsNullOrWhiteSpace(Text.Get(id)));
            string text = Assert.IsType<string>(describe.Invoke(null, [code]));
            Assert.DoesNotContain("{0}", text);
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
