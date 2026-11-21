using HoyoFileClean.Desktop;
using Xunit;
using static HoyoFileClean.Desktop.Tests.NativeTestHost;

namespace HoyoFileClean.Desktop.Tests;

[Collection("Windows Forms")]
public sealed class GameChannelTests
{
    [Theory]
    [InlineData("14", "0", "bilibili")]
    [InlineData("1", "1", "official")]
    [InlineData("future", "0", null)]
    [InlineData("14", "1", null)]
    [InlineData(null, null, null)]
    public void ChannelIdIsDisplayedForLocalGameButNotInventedForReference(string? channel, string? subChannel, string? id) => OnSta(() =>
    {
        string root = Root(channel, subChannel);
        using var form = Form(out WizardSession session);
        Find<TextBox>(form, "GameRoot").Text = root;
        ReadGamePresetAndWait(form, session);
        string header = Find<Label>(form, "IdentificationResult").Text.Split(Environment.NewLine)[0];
        Assert.Contains("hk4e_cn", header);
        if (id is not null) Assert.Contains("渠道：" + id, header);
        else Assert.DoesNotContain("渠道", Find<Label>(form, "IdentificationResult").Text);
        Find<Button>(form, "ReadGame").PerformClick();
        WaitUntil(() => !session.IsBusy);
        Assert.NotNull(session.Game);
        Find<Button>(form, "Next").PerformClick();
        string expectedIdentity = id is null ? "hk4e_cn" : "hk4e_cn / " + id;
        Assert.Contains("游戏：" + expectedIdentity + "    版本：", Find<Label>(form, "AutomaticNetworkInformation").Text);
        string reference = Path.Combine(root, "reference.json");
        File.WriteAllText(reference, """
            {"fileCount":1,"totalSize":4,"files":[{"path":"YuanShen.exe","size":4}]}
            """);
        session.AcceptReference(FileComparer.ReadInputs([reference]));
        session.Next();
        using var panel = new ComparisonPanel();
        panel.Render(session);
        Assert.Equal(expectedIdentity, Find<Label>(panel, "ComparisonGameGame").Text);
        Assert.Equal("hk4e_cn", Find<Label>(panel, "ComparisonReferenceGame").Text);
    });

    [Fact]
    public void ReloadAndRootChangeRemoveOnlyAutomaticChannelRules() => OnSta(() =>
    {
        string root = Root("14", "0");
        using var form = Form(out WizardSession session);
        Find<TextBox>(form, "GameRoot").Text = root;
        ListBox directories = Find<ListBox>(form, "ExcludedDirectories");
        directories.Items.Add("manual");
        ReadGamePresetAndWait(form, session);
        Assert.Contains("YuanShen_Data/Plugins/BLPlatform64", directories.Items.Cast<string>());
        Assert.Contains("sdk_pkg_version", Find<ListBox>(form, "ExcludedFiles").Items.Cast<string>());
        File.WriteAllText(Path.Combine(root, "config.ini"), "uapc={\"hk4e_cn\":{}}\nchannel=1\nsub_channel=1");
        ReadGamePresetAndWait(form, session);
        Assert.DoesNotContain("YuanShen_Data/Plugins/BLPlatform64", directories.Items.Cast<string>());
        Assert.DoesNotContain("sdk_pkg_version", Find<ListBox>(form, "ExcludedFiles").Items.Cast<string>());
        Assert.Contains("YuanShen_Data/Persistent", directories.Items.Cast<string>());
        Assert.Contains("manual", directories.Items.Cast<string>());
        Assert.DoesNotContain("bilibili", Find<Label>(form, "IdentificationResult").Text);
        Assert.Contains("official", Find<Label>(form, "IdentificationResult").Text);
        Find<TextBox>(form, "GameRoot").Text = Root("unknown", "0");
        Assert.Equal(new[] { "manual" }, directories.Items.Cast<string>().ToArray());
        ReadGamePresetAndWait(form, session);
        Assert.DoesNotContain("渠道：", Find<Label>(form, "IdentificationResult").Text);
        Assert.Contains("YuanShen_Data/Persistent", directories.Items.Cast<string>());
        Assert.True(Find<Button>(form, "ReadGame").Enabled);
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ExplicitManualChannelPathSurvivesReloadEvenWhenItWasInitiallyAutomatic(bool directory, bool removeFirst) => OnSta(() =>
    {
        string root = Root("14", "0");
        using var form = Form(out WizardSession session);
        Find<TextBox>(form, "GameRoot").Text = root;
        ReadGamePresetAndWait(form, session);
        string path = directory ? "YuanShen_Data/Plugins/BLPlatform64" : "sdk_pkg_version";
        string suffix = directory ? "Directory" : "File";
        ListBox list = Find<ListBox>(form, directory ? "ExcludedDirectories" : "ExcludedFiles");
        Assert.Contains(path, list.Items.Cast<string>());
        if (removeFirst)
        {
            list.SelectedItem = path;
            Find<Button>(form, "RemoveExcluded" + suffix).PerformClick();
            Assert.DoesNotContain(path, list.Items.Cast<string>());
        }
        Find<TextBox>(form, "Excluded" + suffix + "Input").Text = path.ToUpperInvariant();
        Find<Button>(form, "AddExcluded" + suffix).PerformClick();
        Assert.Equal(1, list.Items.Cast<string>().Count(item => item.Equals(path, StringComparison.OrdinalIgnoreCase)));
        File.WriteAllText(Path.Combine(root, "config.ini"), "uapc={\"hk4e_cn\":{}}\nchannel=1\nsub_channel=1");
        ReadGamePresetAndWait(form, session);
        Assert.Equal(1, list.Items.Cast<string>().Count(item => item.Equals(path, StringComparison.OrdinalIgnoreCase)));
        Assert.Contains("official", Find<Label>(form, "IdentificationResult").Text);
        Find<TextBox>(form, "GameRoot").Text = Root(null, null);
        Assert.Equal(1, list.Items.Cast<string>().Count(item => item.Equals(path, StringComparison.OrdinalIgnoreCase)));
    });

    [Fact]
    public void ImportedChannelPathsBecomeManualWithoutDuplicatingPresetEntries() => OnSta(() =>
    {
        string root = Root("14", "0");
        using var form = Form(out WizardSession session);
        Find<TextBox>(form, "GameRoot").Text = root;
        ReadGamePresetAndWait(form, session);
        string rules = Path.Combine(root, "rules.json");
        File.WriteAllText(rules, """
            {"excludedDirectories":["YUANSHEN_DATA/PLUGINS/BLPLATFORM64"],"excludedFiles":["SDK_PKG_VERSION"]}
            """);
        Find<TextBox>(form, "ExclusionJson").Text = rules;
        Find<Button>(form, "ImportExclusionRules").PerformClick();
        WaitUntil(() => !session.IsBusy);
        ListBox directories = Find<ListBox>(form, "ExcludedDirectories"), files = Find<ListBox>(form, "ExcludedFiles");
        Assert.Contains("导入", Find<Label>(form, "Status").Text);
        ReadGamePresetAndWait(form, session);
        File.WriteAllText(Path.Combine(root, "config.ini"), "uapc={\"hk4e_cn\":{}}\nchannel=1\nsub_channel=1");
        ReadGamePresetAndWait(form, session);
        Assert.Equal(1, directories.Items.Cast<string>().Count(path =>
            path.Equals("YuanShen_Data/Plugins/BLPlatform64", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(1, files.Items.Cast<string>().Count(path => path.Equals("sdk_pkg_version", StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain("YuanShen_Data/Plugins/PCGameSDK.dll", files.Items.Cast<string>());
    });

    private static string Root(string? channel, string? subChannel)
    {
        string root = Path.Combine(TestTemporaryFiles.Root, "channel-gui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "config.ini"), "uapc={\"hk4e_cn\":{}}\ngame_version=7.2.0"
            + (channel is null ? "" : "\nchannel=" + channel)
            + (subChannel is null ? "" : "\nsub_channel=" + subChannel));
        File.WriteAllText(Path.Combine(root, "YuanShen.exe"), "game");
        return root;
    }

    private static MainForm Form(out WizardSession session)
    {
        session = new WizardSession();
        var form = new MainForm(session) { StartPosition = FormStartPosition.Manual,
            Location = new Point(-20000, -20000), ShowInTaskbar = false, Size = new Size(800, 650) };
        form.Show();
        Application.DoEvents();
        return form;
    }
}
