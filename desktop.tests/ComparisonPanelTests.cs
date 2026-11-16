using System.Text.Json;
using HoyoFileClean.Desktop;
using Xunit;
using static HoyoFileClean.Desktop.Tests.NativeTestHost;

namespace HoyoFileClean.Desktop.Tests;

[Collection("Windows Forms")]
public sealed class ComparisonPanelTests
{
    private const string Hash = "900150983cd24fb0d6963f7d28e17f72";
    private readonly string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Temp", "opencode", "HoyoFileClean.Desktop.Tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComparisonUsesPageActionsAlignedLabelsAndRealCounts(bool heavy) => OnSta(() =>
    {
        var session = Session(heavy);
        using var form = new MainForm(session) { StartPosition = FormStartPosition.Manual,
            Location = new Point(-20000, -20000), ShowInTaskbar = false, Size = new Size(800, 650) };
        form.Show();
        Application.DoEvents();
        Button compare = Find<Button>(form, "Execute"), next = Find<Button>(form, "Next");
        Assert.NotSame(next.Parent, compare.Parent);
        Assert.Equal(new[] { "Back", "Next", "CloseWizard" }, next.Parent!.Controls.Cast<Control>().Select(control => control.Name));
        Assert.Empty(form.Controls.Find("Import", true));
        var summaryLayout = Assert.IsType<TableLayoutPanel>(Find<ComparisonPanel>(form, "ComparisonResult").Controls[0]);
        Assert.Equal(4, summaryLayout.ColumnCount);
        Assert.Equal(0, summaryLayout.GetColumn(Find<TableLayoutPanel>(form, "ComparisonScope")));
        Assert.Empty(Descendants(Find<ComparisonPanel>(form, "ComparisonResult")).OfType<TextBox>());
        Assert.False(next.Enabled);
        Assert.Equal("共有但不同数：—", Find<Label>(form, "ComparisonDifferent").Text);
        Assert.Equal("hk4e_cn", Find<Label>(form, "ComparisonGameGame").Text);
        Assert.Equal("7.2.0", Find<Label>(form, "ComparisonGameVersion").Text);
        Assert.Equal("game; zh-cn", Find<Label>(form, "ComparisonReferenceLocation").Text);
        Assert.Equal("3", Find<Label>(form, "ComparisonReferenceFiles").Text);
        Assert.Equal(heavy ? "2/2" : "0/2", Find<Label>(form, "ComparisonGameMd5").Text);
        Assert.Equal("3/3", Find<Label>(form, "ComparisonReferenceMd5").Text);
        foreach (string field in new[] { "Game", "Version", "Location", "Files", "Size", "Md5" })
        {
            Label left = Find<Label>(form, "ComparisonGame" + field), right = Find<Label>(form, "ComparisonReference" + field);
            Assert.Equal(left.RectangleToScreen(left.ClientRectangle).Top, right.RectangleToScreen(right.ClientRectangle).Top);
        }
        compare.PerformClick();
        WaitUntil(() => !session.IsBusy && !Find<ProgressBar>(form, "ComparisonProgress").Visible && next.Enabled);
        Assert.Equal(2, session.Step);
        Assert.Equal("共有但不同数：1", Find<Label>(form, "ComparisonDifferent").Text);
        Assert.Equal("参考独有数：1", Find<Label>(form, "ComparisonOnlyReference").Text);
        Assert.Equal(heavy ? "MD5 相同数：1" : "仅大小相同数：1", Find<Label>(form, "ComparisonMatched").Text);
        Rectangle client = form.RectangleToScreen(form.ClientRectangle);
        ComparisonPanel summary = Find<ComparisonPanel>(form, "ComparisonResult");
        Rectangle summaryClient = summary.RectangleToScreen(summary.ClientRectangle);
        foreach (Control label in Descendants(summary).Where(control => control is Label))
        {
            Assert.True(label.Height >= label.Font.Height, label.Name + " 文字高度不足");
            Assert.True(client.Contains(label.RectangleToScreen(label.ClientRectangle)), label.Name + " 超出窗口");
            Assert.True(summaryClient.Contains(label.RectangleToScreen(label.ClientRectangle)), label.Name + " 被摘要区域裁切");
        }
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(Path.Combine(directory, heavy ? "comparison-heavy.png" : "comparison-light.png"));
        next.PerformClick();
        Assert.Equal(3, session.Step);
        Find<Button>(form, "Back").PerformClick();
        session.InvalidateComparison();
        compare.PerformClick();
        WaitUntil(() => !session.IsBusy && next.Enabled);
        Assert.Equal(2, session.Step);
        form.Close();
    });

    [Fact]
    public void MixedHashesAndScopeAreNotMisrepresentedAndInvalidationClearsOutput() => OnSta(() =>
    {
        var session = Session(true);
        FileListSnapshot same = Write("hashed", [new("YuanShen.exe", 3, Hash)], false);
        FileListSnapshot different = Write("unhashed", [new("different", 4)], false);
        session.AcceptReference(FileComparer.Combine([same, different]));
        session.AcceptComparison(FileComparer.Compare(session.Game!, session.Reference!));
        using var panel = new ComparisonPanel();
        panel.Render(session);
        Assert.Equal("1/2", Find<Label>(panel, "ComparisonReferenceMd5").Text);
        Assert.Equal("Cache" + Environment.NewLine + "Logs", Find<Label>(panel, "ComparisonGameExcludedDirectories").Text);
        Assert.True(Find<Label>(panel, "ComparisonGameExcludedDirectories").AutoSize);
        Assert.False(Find<Label>(panel, "ComparisonGameExcludedDirectories").AutoEllipsis);
        Assert.Equal("settings.ini", Find<Label>(panel, "ComparisonGameExcludedFiles").Text);
        Assert.Equal("linked", Find<Label>(panel, "ComparisonGameSkippedLinks").Text);
        Assert.Equal("相同数：MD5 1；仅大小 0", Find<Label>(panel, "ComparisonMatched").Text);
        FileListSnapshot matchingSize = Write("matching-size", [new("different", 5)], false);
        session.AcceptReference(FileComparer.Combine([same, matchingSize]));
        session.AcceptComparison(FileComparer.Compare(session.Game!, session.Reference!));
        panel.Render(session);
        Assert.Equal("相同数：MD5 1；仅大小 1", Find<Label>(panel, "ComparisonMatched").Text);
        session.InvalidateReference();
        panel.Render(session);
        Assert.Equal("共有但不同数：—", Find<Label>(panel, "ComparisonDifferent").Text);
        Assert.Equal("—", Find<Label>(panel, "ComparisonReferenceFiles").Text);
    });

    [Fact]
    public void OfficialNetworkMetadataOverridesContentAndKeepsSelectedPackages() => OnSta(() =>
    {
        var session = Session(false);
        using var table = new ManifestTable("test");
        table.AddNetworkFiles(new[] { "new_package", "new_voice" }.Select(package => new ReferenceInput(
            new LocalManifestFile(package, package, 1, default),
            new RemoteManifest(package, package, new Uri("https://example.com/" + package), 1, 1, Hash, false))),
            "future_game_global", "99.0.1");
        foreach (DataGridViewRow row in table.Rows) row.Cells[0].Value = true;
        table.ShowResults(table.CheckedInputs.Select(input => (input, ReferenceState.Parsed, (int?)1)));
        using var panel = new ComparisonPanel();
        panel.Render(session, table.CheckedNetworkMetadata);
        Assert.Equal("future_game_global", Find<Label>(panel, "ComparisonReferenceGame").Text);
        Assert.Equal("99.0.1", Find<Label>(panel, "ComparisonReferenceVersion").Text);
        Assert.Equal("new_package; new_voice", Find<Label>(panel, "ComparisonReferenceLocation").Text);
    });

    private WizardSession Session(bool heavy)
    {
        var session = new WizardSession();
        session.AcceptGame(Write("game", [new("YuanShen.exe", 3, heavy ? Hash : null), new("different", 5, heavy ? Hash : null)], true), directory,
            GameIdentifier.FromDetections(new Dictionary<string, string> { ["ini_game_id"] = "hk4e_cn", ["ini_version"] = "7.2.0",
                ["ini_channel"] = "1", ["ini_sub_channel"] = "1" }));
        FileListSnapshot first = Write("manifest-game", [new("YuanShen.exe", 3, Hash), new("different", 4, Hash)], false);
        FileListSnapshot second = Write("manifest-voice", [new("different", 4, Hash),
            new("YuanShen_Data/StreamingAssets/AudioAssets/Chinese/voice", 1, Hash)], false);
        session.AcceptReference(FileComparer.Combine([first, second]));
        session.Next();
        session.Next();
        return session;
    }

    [Fact]
    public void GameOnlyIdentityUsesTheSameFallbackAsFirstStep() => OnSta(() =>
    {
        var session = Session(false);
        var identification = GameIdentifier.FromDetections(new Dictionary<string, string> { ["exe_hkrpg"] = "StarRail.exe" });
        session.AcceptGame(session.Game!, directory, identification);
        using var panel = new ComparisonPanel();
        panel.Render(session);
        Assert.Equal("hkrpg", Find<Label>(panel, "ComparisonGameGame").Text);
        Assert.Contains("hkrpg", ResultText.DescribeIdentification(identification));
    });

    [Fact]
    public void PackagesInOneManifestRemainALegalSet() => OnSta(() =>
    {
        var session = Session(false);
        session.AcceptReference(Write("combined-packages", [new("YuanShen.exe", 3),
            new("YuanShen_Data/StreamingAssets/AudioAssets/Chinese/voice", 1)], false));
        using var panel = new ComparisonPanel();
        panel.Render(session);
        Assert.Equal("game; zh-cn", Find<Label>(panel, "ComparisonReferenceLocation").Text);
    });

    [Fact]
    public void ManifestTableDisplaysTheSameLegalPackageSetAsComparison() => OnSta(() =>
    {
        var snapshot = Write("table-packages", [new("YuanShen.exe", 3),
            new("YuanShen_Data/StreamingAssets/AudioAssets/Chinese/voice", 1)], false);
        string path = Path.Combine(directory, "table-packages.json");
        using var table = new ManifestTable("test");
        table.AddFiles([new LocalManifestFile(path, "table-packages.json", 1, default)], check: true);
        var inputs = table.CheckedInputs;
        Assert.Single(inputs);
        table.ShowResults(inputs.Select(input => (input, ReferenceState.Parsed, (int?)2)));
        table.ShowMetadata(inputs.Select(input => (input, snapshot.Sources[0].Metadata)));
        string text = Assert.IsType<string>(table.Rows[0].Cells["Package"].Value);
        Assert.Contains("game", text);
        Assert.Contains("zh-cn", text);
        Assert.DoesNotContain("冲突", text);
        Assert.Equal("hk4e_cn", table.Rows[0].Cells["Game"].Value);
    });

    [Fact]
    public void DifferentSourceIdentitiesAreSummarizedWithoutInventingConflicts() => OnSta(() =>
    {
        var session = Session(false);
        session.AcceptReference(FileComparer.Combine([
            Write("cn", [new("YuanShen.exe", 1)], false),
            Write("global", [new("GenshinImpact.exe", 1)], false)]));
        using var panel = new ComparisonPanel();
        panel.Render(session);
        Assert.Equal("hk4e_cn; hk4e_global", Find<Label>(panel, "ComparisonReferenceGame").Text);
    });

    [Fact]
    public void ActualIdentityConflictInOneSourceIsNotHiddenByGameFallback() => OnSta(() =>
    {
        var session = Session(false);
        session.AcceptReference(Write("conflicting-identity", [new("YuanShen.exe", 1), new("GenshinImpact.exe", 1)], false));
        using var panel = new ComparisonPanel();
        panel.Render(session);
        Assert.Contains("冲突", Find<Label>(panel, "ComparisonReferenceGame").Text);
        Assert.Contains("hk4e_cn", Find<Label>(panel, "ComparisonReferenceGame").Text);
        Assert.Contains("hk4e_global", Find<Label>(panel, "ComparisonReferenceGame").Text);
    });

    [Fact]
    public void SameSnapshotsRefreshChangedMetadataAndIdentificationAndKeepLiteralAmpersands() => OnSta(() =>
    {
        var session = Session(false);
        using var panel = new ComparisonPanel();
        var metadata = new Dictionary<string, ManifestMetadataField> { ["biz"] = new([new("network", "first&game")]) };
        panel.Render(session, [metadata]);
        Assert.Equal("first&game", Find<Label>(panel, "ComparisonReferenceGame").Text);
        metadata["biz"] = new([new("network", "second&game")]);
        session.AcceptGame(session.Game!, directory, GameIdentifier.FromDetections(
            new Dictionary<string, string> { ["exe_hkrpg"] = "StarRail.exe" }));
        panel.Render(session, [metadata]);
        Assert.Equal("second&game", Find<Label>(panel, "ComparisonReferenceGame").Text);
        Assert.Equal("hkrpg", Find<Label>(panel, "ComparisonGameGame").Text);
        foreach (string name in new[] { "ComparisonReferenceGame", "ComparisonGameLocation", "ComparisonGameExcludedDirectories" })
            Assert.False(Find<Label>(panel, name).UseMnemonic);
    });

    private FileListSnapshot Write(string name, ManifestFile[] files, bool scan)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            fileCount = files.Length, totalSize = files.Sum(file => file.Size), files,
            hashAlgorithm = files.Any(file => file.Hash is not null) ? "MD5" : null,
            scan = scan ? new { root = directory, mode = files.All(file => file.Hash is not null) ? "heavy" : "light",
                excludedDirectories = new[] { "Cache", "Logs" }, excludedFiles = new[] { "settings.ini" }, skippedLinks = new[] { "linked" } } : null
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        return FileComparer.ReadInputs([path]);
    }

    private static IEnumerable<Control> Descendants(Control control)
        => control.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));
}
