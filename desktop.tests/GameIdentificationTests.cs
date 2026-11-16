using HoyoFileClean.Desktop;
using Xunit;
using static HoyoFileClean.Desktop.Tests.NativeTestHost;

namespace HoyoFileClean.Desktop.Tests;

[Collection("Windows Forms")]
public sealed class GameIdentificationTests
{
    [Fact]
    public void GenshinLoadsFileExclusionsAndScanKeepsPrereadIdentification() => OnSta(() =>
    {
        string root = CreateRoot();
        WriteGameConfig(root);
        File.WriteAllText(Path.Combine(root, "beyond_pkg_version"), "beyond");
        File.WriteAllText(Path.Combine(root, "pkg_version"), "body");
        File.WriteAllText(Path.Combine(root, "Audio_Chinese_pkg_version"), "voice");
        using var form = CreateForm(out WizardSession session);
        Find<TextBox>(form, "GameRoot").Text = root;
        ReadGamePresetAndWait(form, session);
        Assert.Equal(["config.ini", "beyond_pkg_version"],
            Find<ListBox>(form, "ExcludedFiles").Items.Cast<string>().ToArray());
        using var lockedConfig = new FileStream(Path.Combine(root, "config.ini"), FileMode.Open,
            FileAccess.ReadWrite, FileShare.None);
        ScanAndWait(form, session);
        Assert.NotNull(session.Game);
        Assert.DoesNotContain("config.ini", session.Game.Files.Keys);
        Assert.DoesNotContain("beyond_pkg_version", session.Game.Files.Keys);
        Assert.Contains("pkg_version", session.Game.Files.Keys);
        Assert.Contains("Audio_Chinese_pkg_version", session.Game.Files.Keys);
        Assert.Equal("hk4e_cn", Assert.Single(session.Identification!.Information["biz"].Values).RawValue);
        Assert.Equal("7.2.0", Assert.Single(session.Identification.Information["version"].Values).RawValue);
    });

    [Fact]
    public void PresetReadIsNotScanAndChangingRootClearsOnlyAutomaticRules() => OnSta(() =>
    {
        string root = CreateRoot();
        WriteGameConfig(root);
        File.WriteAllText(Path.Combine(root, "pkg_version"), "body");
        File.WriteAllText(Path.Combine(root, "Audio_Chinese_pkg_version"), "voice");
        using var form = CreateForm(out WizardSession session);
        Find<TextBox>(form, "GameRoot").Text = root;
        ListBox dirs = Find<ListBox>(form, "ExcludedDirectories");
        dirs.Items.Add("manual");
        Button load = Find<Button>(form, "LoadGamePreset");
        Button scan = Find<Button>(form, "ReadGame");
        Assert.False(scan.Enabled);
        scan.PerformClick();
        Assert.False(session.IsBusy);
        Assert.Null(session.Game);
        Assert.Equal("读取游戏并加载预置排除", load.Text);
        load.PerformClick();
        WaitUntil(() => !session.IsBusy);
        Assert.Null(session.Game);
        Assert.False(Find<Button>(form, "Next").Enabled);
        Assert.Contains("hk4e_cn", Find<Label>(form, "IdentificationResult").Text);
        Assert.Contains("ldiff", dirs.Items.Cast<string>());
        Assert.True(scan.Enabled);
        Assert.Empty(form.Controls.Find("ComparisonExcludedFiles", true));
        Assert.Equal(["config.ini", "beyond_pkg_version"],
            Find<ListBox>(form, "ExcludedFiles").Items.Cast<string>().ToArray());
        int count = dirs.Items.Count;
        load.PerformClick();
        WaitUntil(() => !session.IsBusy);
        Assert.Equal(count, dirs.Items.Count);
        ScanAndWait(form, session);
        Assert.Contains("pkg_version", session.Game!.Files.Keys);
        Assert.Contains("Audio_Chinese_pkg_version", session.Game.Files.Keys);
        string second = CreateRoot();
        Find<TextBox>(form, "GameRoot").Text = second;
        Assert.Equal(["manual"], dirs.Items.Cast<string>().ToArray());
        Assert.False(scan.Enabled);
        load.PerformClick();
        WaitUntil(() => !session.IsBusy);
        Assert.Null(session.Game);
        Assert.DoesNotContain("hk4e_cn", Find<Label>(form, "IdentificationResult").Text);
        Assert.False(scan.Enabled);
    });

    [Fact]
    public void CancelledPresetReadDoesNotPublishAndCanRetry() => OnSta(() =>
    {
        string root = CreateRoot();
        WriteGameConfig(root);
        using var form = CreateForm(out WizardSession session);
        Find<TextBox>(form, "GameRoot").Text = root;
        Button load = Find<Button>(form, "LoadGamePreset");
        load.PerformClick();
        Assert.True(session.IsBusy);
        Find<Button>(form, "CancelScan").PerformClick();
        WaitUntil(() => !session.IsBusy);
        Assert.Null(session.Game);
        Assert.Empty(Find<ListBox>(form, "ExcludedDirectories").Items.Cast<string>());
        Assert.DoesNotContain("hk4e_cn", Find<Label>(form, "IdentificationResult").Text);
        Assert.False(Find<Button>(form, "ReadGame").Enabled);
        load.PerformClick();
        WaitUntil(() => !session.IsBusy);
        Assert.Contains("hk4e_cn", Find<Label>(form, "IdentificationResult").Text);
        Assert.Null(session.Game);
        Assert.True(Find<Button>(form, "ReadGame").Enabled);
    });

    [Fact]
    public void ScanIdentifiesBeforeEnumerationAndFailureClearsResultsThenAllowsRetry() => OnSta(() =>
    {
        string root = CreateRoot();
        string path = WriteGameConfig(root);
        using var form = CreateForm(out WizardSession session);
        Find<TextBox>(form, "GameRoot").Text = root;
        ScanAndWait(form, session);
        Assert.NotNull(session.Game);
        Assert.NotNull(session.Identification);
        string result = Find<Label>(form, "IdentificationResult").Text;
        Assert.Contains("hk4e_cn", result);
        Assert.DoesNotContain("原神", result);
        string firstLine = result.Split(Environment.NewLine)[0];
        Assert.Contains("hk4e_cn", firstLine);
        Assert.Contains("7.2.0", firstLine);
        Assert.DoesNotContain("官服", firstLine);
        Assert.Contains("7.2.0", result);
        Assert.DoesNotContain("官服", result);
        Assert.DoesNotContain("未检测到", result);
        Label information = Find<Label>(form, "IdentificationResult");
        ListBox exclusions = Find<ListBox>(form, "ExcludedDirectories");
        Assert.True(information.RectangleToScreen(information.ClientRectangle).Bottom <=
            exclusions.RectangleToScreen(exclusions.ClientRectangle).Top);
        Assert.True(exclusions.Height > 30);
        string screenshot = Path.Combine(root, "identified-minimum-window.png");
        using (var bitmap = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(screenshot);
        }
        Assert.True(Find<Button>(form, "Next").Enabled);
        Find<Button>(form, "Next").PerformClick();
        Assert.Equal(1, session.Step);
        Assert.True(Find<RadioButton>(form, "NetworkSource").Visible);
        Find<Button>(form, "Back").PerformClick();
        Assert.Contains("hk4e_cn", Find<Label>(form, "IdentificationResult").Text);

        File.AppendAllText(path, "\nchannel=2");
        ReadGamePresetAndWait(form, session);
        Assert.Null(session.Game);
        Assert.Null(session.Identification);
        Assert.False(Find<Button>(form, "Next").Enabled);
        Assert.Contains("矛盾", Find<Label>(form, "Status").Text);
        Assert.DoesNotContain("hk4e_cn", Find<Label>(form, "IdentificationResult").Text);

        WriteGameConfig(root);
        ScanAndWait(form, session);
        Assert.NotNull(session.Game);
        Assert.True(Find<Button>(form, "Next").Enabled);
    });

    [Fact]
    public void MissingIdentificationFailsWithoutPublishingScan() => OnSta(() =>
    {
        string root = CreateRoot();
        // No config.ini: report failed identification, not a successful empty scan.
        using var form = CreateForm(out WizardSession session);
        Find<TextBox>(form, "GameRoot").Text = root;
        ScanAndWait(form, session);
        Assert.Null(session.Game);
        Assert.Contains("无法识别", Find<Label>(form, "Status").Text);
        Assert.False(Find<Button>(form, "Next").Enabled);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScanCancellationCannotPublishAndCanRetry(bool hashes) => OnSta(() =>
    {
        string root = CreateRoot();
        WriteGameConfig(root);
        using var form = CreateForm(out WizardSession session);
        Find<TextBox>(form, "GameRoot").Text = root;
        Find<CheckBox>(form, "HeavyScan").Checked = hashes;
        ReadGamePresetAndWait(form, session);
        Button scan = Find<Button>(form, "ReadGame"), cancel = Find<Button>(form, "CancelScan");
        Assert.False(cancel.Visible);
        scan.PerformClick();
        // The UI continuation has not run: even very fast work must reject publication.
        Assert.True(session.IsBusy);
        Assert.True(cancel.Visible && cancel.Enabled);
        var actions = Assert.IsType<TableLayoutPanel>(cancel.Parent);
        Assert.Equal("ScanActions", actions.Name);
        Assert.Equal(new TableLayoutPanelCellPosition(1, 0), actions.GetPositionFromControl(cancel));
        Assert.True(actions.ClientSize.Width - cancel.Right <= cancel.Margin.Right + 1);
        cancel.PerformClick();
        Assert.False(cancel.Enabled);
        Assert.False(Find<Button>(form, "Next").Enabled);
        WaitUntil(() => !session.IsBusy);
        Application.DoEvents();
        Assert.Null(session.Game);
        Assert.Null(session.Identification);
        Assert.Contains("已取消", Find<Label>(form, "Status").Text);
        Assert.False(cancel.Visible);
        Assert.True(scan.Enabled);
        ScanAndWait(form, session);
        Assert.NotNull(session.Game);
        Assert.NotNull(session.Identification);
    });

    [Fact]
    public void IdentifiedGameAndManifestKeepBizAndPluginMeaningsThroughComparison() => OnSta(() =>
    {
        string root = CreateRoot();
        WriteGameConfig(root);
        File.AppendAllText(Path.Combine(root, "config.ini"), "\nplugin_newPlugin_version=3.0.0");
        File.WriteAllText(Path.Combine(root, "YuanShen.exe"), "game");
        string reference = root + ".reference.json";
        File.WriteAllText(reference, """
            {"fileCount":1,"totalSize":4,"files":[{"path":"YuanShen.exe","size":4}]}
            """);
        using var form = CreateForm(out WizardSession session);
        Find<TextBox>(form, "GameRoot").Text = root;
        ScanAndWait(form, session);
        GameIdentification identification = Assert.IsType<GameIdentification>(session.Identification);
        Assert.Equal("newPlugin", Assert.Single(identification.Information["plugins"].Values).Item);
        Assert.Equal("game", Assert.Single(identification.Information["package"].Values).RawValue);
        Assert.Contains("发现的包体", Find<Label>(form, "IdentificationResult").Text);
        Find<Button>(form, "Next").PerformClick();
        Find<RadioButton>(form, "ManualSource").Checked = true;
        var file = new FileInfo(reference);
        var table = Find<ManifestTable>(form, "Manifests");
        table.AddFiles([new LocalManifestFile(file.Name, file.FullName, file.Length, file.LastWriteTimeUtc)], check: true);
        Find<Button>(form, "ParseManualReference").PerformClick();
        WaitUntil(() => !session.IsBusy);
        var metadata = Assert.Single(session.Reference!.Sources).Metadata!;
        Assert.Equal(Assert.Single(identification.Information["biz"].Values).RawValue, metadata["biz"].Value);
        Assert.Equal("game", metadata["package"].Value);
        Assert.Equal("hk4e_cn", table.Rows[0].Cells["Game"].Value);
        Assert.Same(identification, session.Identification);
        Find<Button>(form, "Next").PerformClick();
        Find<Button>(form, "Execute").PerformClick();
        WaitUntil(() => !session.IsBusy);
        Assert.NotNull(session.Comparison);
        Assert.Equal(1, session.Comparison.Summary.MatchedSizeOnly);
        Find<Button>(form, "Next").PerformClick();
        Assert.Equal(3, session.Step);
        Assert.True(File.Exists(Path.Combine(root, "config.ini")));
        Assert.Equal("game", File.ReadAllText(Path.Combine(root, "YuanShen.exe")));
    });

    [Fact]
    public void ScanReusesPrereadIdentificationWithoutReadingChangedConfig() => OnSta(() =>
    {
        string root = CreateRoot();
        WriteGameConfig(root);
        File.AppendAllText(Path.Combine(root, "config.ini"), "\nplugin_original_version=3.0.0");
        using var form = CreateForm(out WizardSession session);
        Find<TextBox>(form, "GameRoot").Text = root;
        ReadGamePresetAndWait(form, session);
        File.WriteAllText(Path.Combine(root, "config.ini"), "changed after identification");
        string beforeScan = Find<Label>(form, "IdentificationResult").Text;
        Find<Button>(form, "ReadGame").PerformClick();
        Assert.Equal("正在扫描游戏文件…", Find<Label>(form, "Status").Text);
        Assert.Equal(beforeScan, Find<Label>(form, "IdentificationResult").Text);
        WaitUntil(() => !session.IsBusy);
        Assert.NotNull(session.Game);
        var identification = Assert.IsType<GameIdentification>(session.Identification);
        Assert.Equal("original", Assert.Single(identification.Information["plugins"].Values).Item);
    });

    [Fact]
    public void ImportRestoresRecordedIdentificationWithoutReopeningMissingRoot() => OnSta(() =>
    {
        string root = CreateRoot();
        WriteGameConfig(root);
        File.AppendAllText(Path.Combine(root, "config.ini"), "\nplugin_anyNewPlugin_version=9.1.0");
        var identification = GameIdentifier.Identify(root, TestContext.Current.CancellationToken);
        string output = root + ".json";
        DirectoryScanner.Scan(root, output, cancellationToken: TestContext.Current.CancellationToken,
            identification: identification);
        Directory.Move(root, root + "-moved");
        using var form = CreateForm(out WizardSession session);
        Find<RadioButton>(form, "ImportGameSource").Checked = true;
        Find<TextBox>(form, "GameRoot").Text = root;
        Find<TextBox>(form, "GameJson").Text = output;
        ScanAndWait(form, session);
        Assert.NotNull(session.Identification);
        string result = Find<Label>(form, "IdentificationResult").Text;
        Assert.Contains("hk4e_cn", result);
        Assert.Contains("anyNewPlugin（9.1.0）", result);
        Assert.True(Find<Button>(form, "Next").Enabled);
        Assert.False(Directory.Exists(root));
    });

    private static string CreateRoot()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "opencode", "game-gui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrereadIdentificationSurvivesScanExclusionsInBothHashModes(bool hashes) => OnSta(() =>
    {
        string root = CreateRoot();
        WriteGameConfig(root);
        string relative = "YuanShen_Data/StreamingAssets/AudioAssets/Chinese";
        File.WriteAllText(Path.Combine(root, "pkg_version"), "body");
        Directory.CreateDirectory(Path.Combine(root, relative));
        File.WriteAllText(Path.Combine(root, relative, "voice.pck"), "resource");
        using var form = CreateForm(out WizardSession session);
        Find<TextBox>(form, "GameRoot").Text = root;
        Find<CheckBox>(form, "HeavyScan").Checked = hashes;
        Find<ListBox>(form, "ExcludedDirectories").Items.Add(relative);
        ScanAndWait(form, session);
        Assert.NotNull(session.Game);
        Assert.NotNull(session.Identification);
        Assert.Contains(session.Identification.Information["package"].Values, value => value.RawValue == "zh-cn");
        Assert.DoesNotContain(session.Game.Files.Keys, path => path.StartsWith(relative + "/", StringComparison.Ordinal));
        Assert.Equal(hashes, session.Game.HashedFileCount > 0);
    });

    [Theory]
    [InlineData("YuanShen_Data/StreamingAssets/AudioAssets/", "Chinese", "hk4e_cn")]
    [InlineData("StarRail_Data/Persistent/Audio/AudioPackage/Windows/", "Chinese(PRC)", "hkrpg")]
    public void PathOnlyIdentificationDisplaysBizOrGameAndEveryDetectedLanguage(string prefix, string chinese, string identity) => OnSta(() =>
    {
        string root = CreateRoot();
        foreach (string language in new[] { chinese, "Japanese" })
        {
            string folder = Path.Combine(root, prefix, language);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "voice.pck"), "resource");
        }
        using var form = CreateForm(out WizardSession session);
        Find<TextBox>(form, "GameRoot").Text = root;
        ScanAndWait(form, session);
        Assert.NotNull(session.Game);
        Assert.NotNull(session.Identification);
        string result = Find<Label>(form, "IdentificationResult").Text;
        Assert.Contains(identity, result.Split(Environment.NewLine)[0]);
        Assert.Contains("zh-cn", result);
        Assert.Contains("ja-jp", result);
        Assert.Contains("发现的包体", result);
        Assert.DoesNotContain("未检测到", result);
        Assert.DoesNotContain("官服", result);
        Assert.False(session.Identification.Information.ContainsKey("version"));
        Assert.True(Find<Button>(form, "Next").Enabled);
        Assert.Empty(Find<ManifestTable>(form, "NetworkManifests").Rows.Cast<DataGridViewRow>());
    });

    private static MainForm CreateForm(out WizardSession session)
    {
        session = new WizardSession();
        var form = new MainForm(session) { StartPosition = FormStartPosition.Manual,
            Location = new Point(-20000, -20000), ShowInTaskbar = false, Size = new Size(800, 650) };
        form.Show();
        var outer = Assert.IsType<TableLayoutPanel>(form.Controls[0]);
        var flow = Assert.IsType<TableLayoutPanel>(outer.GetControlFromPosition(0, 0));
        Label[] steps = flow.Controls.OfType<Label>().ToArray();
        Assert.Contains("1 游戏扫描", steps[0].Text);
        Assert.Equal("2 参考清单", steps[1].Text);
        return form;
    }

    private static void ScanAndWait(MainForm form, WizardSession session)
    {
        if (!Find<Button>(form, "ReadGame").Enabled) ReadGamePresetAndWait(form, session);
        Find<Button>(form, "ReadGame").PerformClick();
        WaitUntil(() => !session.IsBusy);
        Application.DoEvents();
    }
}
