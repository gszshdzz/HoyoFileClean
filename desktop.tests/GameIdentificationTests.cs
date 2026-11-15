using HoyoFileClean.Desktop;
using Xunit;
using static HoyoFileClean.Desktop.Tests.NativeTestHost;

namespace HoyoFileClean.Desktop.Tests;

[Collection("Windows Forms")]
public sealed class GameIdentificationTests
{
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
        Assert.Contains("官服", firstLine);
        Assert.Contains("7.2.0", result);
        Assert.Contains("官服", result);
        Assert.Contains("未检测到", result);
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
        ScanAndWait(form, session);
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
        Assert.False(identification.Information.ContainsKey("package"));
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
        Find<Button>(form, "ReadGame").PerformClick();
        WaitUntil(() => !session.IsBusy);
        Application.DoEvents();
    }
}
