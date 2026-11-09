using System.Runtime.ExceptionServices;
using System.Text.Json;
using HoyoFileClean.Desktop;
using Xunit;

namespace HoyoFileClean.Desktop.Tests;

public sealed class WizardTests
{
    private readonly string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Temp", "opencode", "HoyoFileClean.Desktop.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void EveryNextStepRequiresAnEffectiveResult()
    {
        var session = new WizardSession();
        Assert.False(session.CanAdvance);
        Assert.Throws<InvalidOperationException>(session.Next);
        var (game, reference, root) = Inputs();
        session.AcceptReference(reference);
        session.Next();
        Assert.Equal(1, session.Step);
        Assert.Throws<InvalidOperationException>(session.Next);
        session.AcceptGame(game, root);
        session.Next();
        Assert.Throws<InvalidOperationException>(session.Next);
        session.AcceptComparison(FileComparer.Compare(game, reference));
        session.Next();
        Assert.Equal(3, session.Step);
        Assert.False(session.CanAdvance);
        session.Back();
        Assert.True(session.CanAdvance);
    }

    [Fact]
    public void UpstreamChangesInvalidateOnlyDependentResultsAndSelection()
    {
        WizardSession session = Ready();
        session.Select("game-only", true);
        session.ConfirmSelection(true);
        Assert.True(session.CanDemonstrateDeletion);
        session.InvalidateReference();
        Assert.Null(session.Reference);
        Assert.NotNull(session.Game);
        Assert.Null(session.Comparison);
        Assert.Empty(session.Rows);
        Assert.Equal(0, session.SelectedCount);
        Assert.False(session.SelectionConfirmed);
        Assert.False(session.CanDemonstrateDeletion);
    }

    [Fact]
    public void ScanOptionsChangesInvalidateScanAndComparisonButKeepReference()
    {
        WizardSession session = Ready();
        session.InvalidateGame();
        Assert.NotNull(session.Reference);
        Assert.Null(session.Game);
        Assert.Null(session.Comparison);
    }

    [Fact]
    public async Task BusyStateBlocksRepeatedOperationsInputChangesNavigationAndDeletion()
    {
        WizardSession session = Ready();
        session.Select("game-only", true);
        session.ConfirmSelection(true);
        using var release = new ManualResetEventSlim();
        bool applied = false;
        Task operation = session.RunAsync(() => release.Wait(TimeSpan.FromSeconds(10)), result => applied = result);
        try
        {
            Assert.False(session.CanAdvance);
            Assert.False(session.CanDemonstrateDeletion);
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.RunAsync(() => 0, _ => { }));
            Assert.Throws<InvalidOperationException>(session.Back);
            Assert.Throws<InvalidOperationException>(session.Next);
            Assert.Throws<InvalidOperationException>(session.InvalidateGame);
            Assert.Throws<InvalidOperationException>(() => session.Select("game-only", false));
        }
        finally { release.Set(); await operation; }
        Assert.False(session.IsBusy);
        Assert.True(applied);
    }

    [Fact]
    public async Task FailedOperationDoesNotPublishResultsAndAlwaysUnlocks()
    {
        WizardSession session = Ready();
        session.InvalidateGame();
        await Assert.ThrowsAsync<IOException>(() => session.RunAsync<int>(
            () => throw new IOException("故障注入"), _ => throw new Xunit.Sdk.XunitException("失败时不应应用结果")));
        Assert.False(session.IsBusy);
        Assert.Null(session.Game);
        Assert.Null(session.Comparison);
        Assert.False(session.CanDemonstrateDeletion);
        await session.RunAsync(() => 3, value => Assert.Equal(3, value));
        Assert.False(session.IsBusy);
    }

    [Fact]
    public async Task FailedApplicationOfResultAlsoUnlocks()
    {
        var session = new WizardSession();
        await Assert.ThrowsAsync<InvalidDataException>(() => session.RunAsync(() => 3,
            _ => throw new InvalidDataException("结果不符合当前输入")));
        Assert.False(session.IsBusy);
        Assert.False(session.CanAdvance);
    }

    [Fact]
    public void AnyActualSelectionChangeClearsConfirmationButRedundantEventsDoNot()
    {
        WizardSession session = Ready();
        Assert.False(session.CanDemonstrateDeletion);
        session.ConfirmSelection(true);
        Assert.False(session.SelectionConfirmed);
        session.Select("game-only", true);
        Assert.Equal(3, session.SelectedSize);
        Assert.False(session.CanDemonstrateDeletion);
        session.ConfirmSelection(true);
        session.Select("GAME-ONLY", true);
        Assert.True(session.SelectionConfirmed);
        session.Select("different", true);
        Assert.Equal(2, session.SelectedCount);
        Assert.Equal(8, session.SelectedSize);
        Assert.False(session.SelectionConfirmed);
        session.ConfirmSelection(true);
        session.Select("game-only", false);
        Assert.False(session.SelectionConfirmed);
    }

    [Fact]
    public void ReferenceAndOutsideScopeCannotBeSelected()
    {
        WizardSession session = Ready();
        Assert.Throws<ArgumentException>(() => session.Select("reference-only", true));
        Assert.Throws<ArgumentException>(() => session.Select("excluded/unknown", true));
        Assert.Throws<ArgumentException>(() => session.Select("not-a-result", true));
        Assert.Equal(0, session.SelectedCount);
    }

    [Fact]
    public void FilterRemovesHiddenSelectionAndClearsConfirmation()
    {
        WizardSession session = Ready();
        session.Select("game-only", true);
        session.Select("different", true);
        session.ConfirmSelection(true);
        session.SetFilter(DifferenceCategory.GameOnly);
        Assert.True(session.IsSelected("game-only"));
        Assert.False(session.IsSelected("different"));
        Assert.False(session.SelectionConfirmed);
        Assert.Single(session.VisibleRows);
        session.SetFilter(DifferenceCategory.OutsideScope);
        Assert.Equal(0, session.SelectedCount);
        Assert.All(session.VisibleRows, row => Assert.False(row.CanSelect));
    }

    [Fact]
    public void RecomparisonResetsSelectionAndConfirmation()
    {
        WizardSession session = Ready();
        session.Select("game-only", true);
        session.ConfirmSelection(true);
        session.AcceptComparison(FileComparer.Compare(session.Game!, session.Reference!));
        Assert.False(session.SelectionConfirmed);
        Assert.Equal(0, session.SelectedCount);
    }

    [Fact]
    public void GameImportRequiresAnExplicitMatchingRootAndScanMetadata()
    {
        var (game, reference, root) = Inputs();
        WizardSession.ValidateGameInput(game, root);
        Assert.Throws<ArgumentException>(() => WizardSession.ValidateGameInput(game, ""));
        Assert.Throws<ArgumentException>(() => WizardSession.ValidateGameInput(game, "relative"));
        Assert.Throws<InvalidDataException>(() => WizardSession.ValidateGameInput(game, root + "-other"));
        Assert.Throws<InvalidDataException>(() => WizardSession.ValidateGameInput(reference, root));
    }

    [Fact]
    public void LightScanIsNotPresentedAsHashVerification()
    {
        WizardSession session = Ready();
        Assert.Equal(1, session.Comparison!.Summary.MatchedSizeOnly);
        Assert.Equal(0, session.Comparison.Summary.MatchedWithHash);
        Assert.All(session.Comparison.Different, item => Assert.False(item.HashCompared));
    }

    [Fact]
    public void NativeTreesEnforceSelectionAndReflectConfirmationState() => OnSta(() =>
    {
        WizardSession session = Ready();
        using var panel = new DifferencePanel(session);
        panel.CreateControl();
        TreeView left = Find<TreeView>(panel, "GameTree"), right = Find<TreeView>(panel, "ReferenceTree");
        Assert.True(left.CheckBoxes);
        Assert.False(right.CheckBoxes);
        TreeNode onlyGame = left.Nodes.Cast<TreeNode>().Single(node => node.Tag is DifferenceRow { Category: DifferenceCategory.GameOnly });
        onlyGame.Checked = true;
        Assert.Equal(1, session.SelectedCount);
        CheckBox confirm = Find<CheckBox>(panel, "ConfirmSelection");
        Button delete = Find<Button>(panel, "DemonstrateDelete");
        Assert.False(delete.Enabled);
        confirm.Checked = true;
        Assert.True(delete.Enabled);
        TreeNode different = left.Nodes.Cast<TreeNode>().Single(node => node.Tag is DifferenceRow { Category: DifferenceCategory.Different });
        different.Checked = true;
        Assert.False(confirm.Checked);
        Assert.False(delete.Enabled);
        TreeNode absent = left.Nodes.Cast<TreeNode>().Single(node => node.Tag is DifferenceRow { Category: DifferenceCategory.ReferenceOnly });
        absent.Checked = true;
        Assert.False(absent.Checked);
        TreeNode folder = left.Nodes.Cast<TreeNode>().Single(node => node.Text.StartsWith("excluded"));
        folder.Checked = true;
        Assert.False(folder.Checked);
        folder.Expand();
        Assert.Single(folder.Nodes.Cast<TreeNode>());
        Assert.IsType<DifferenceRow>(folder.Nodes[0].Tag);
        folder.Nodes[0].Checked = true;
        Assert.False(folder.Nodes[0].Checked);
    });

    [Fact]
    public void MainFormStartsWithNoWayToAdvanceWithoutInput() => OnSta(() =>
    {
        using var form = new MainForm();
        form.CreateControl();
        form.PerformLayout();
        Assert.False(Find<Button>(form, "Next").Enabled);
        Assert.False(Find<Button>(form, "Back").Enabled);
        Assert.True(Find<Button>(form, "Execute").Enabled);
        Assert.False(Find<CheckBox>(form, "HeavyScan").Checked);
        Assert.True(Find<TextBox>(form, "ReferenceResult").ReadOnly);
    });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void NativeLayoutFitsTheMinimumWindowAndCanBeRenderedOffscreen(int step) => OnSta(() =>
    {
        WizardSession session = Ready();
        for (int i = 0; i < step; i++) session.Next();
        using var form = new MainForm(session)
        {
            StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000),
            ShowInTaskbar = false, Size = new Size(800, 650)
        };
        form.Show();
        Application.DoEvents();
        Rectangle client = form.RectangleToScreen(form.ClientRectangle);
        string[] controls = step switch
        {
            0 => ["Manifests", "ReferenceResult", "Next", "Execute"],
            1 => ["GameRoot", "ExcludedDirectories", "ExcludedFiles", "ScanResult", "Next", "Execute"],
            2 => ["ComparisonResult", "Next", "Execute"],
            _ => ["GameTree", "ReferenceTree", "ConfirmSelection", "DemonstrateDelete"]
        };
        foreach (string name in controls.Concat(["CloseWizard", "Status"]))
        {
            Control control = Assert.Single(form.Controls.Find(name, true));
            Assert.True(client.Contains(control.RectangleToScreen(control.ClientRectangle)), name + " 超出窗口范围");
        }
        if (step == 3)
        {
            var warning = Find<Label>(form, "DifferenceWarning");
            Assert.True(warning.Height >= warning.GetPreferredSize(new Size(warning.Width, 0)).Height, "底部警告文字被裁切");
            Assert.NotEmpty(Find<ComboBox>(form, "DifferenceFilter").Text);
        }
        Directory.CreateDirectory(directory);
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(Path.Combine(directory, $"native-minimum-step-{step + 1}.png"));
        form.Close();
    });

    [Fact]
    public void NativeNavigationAndOptionChangeUseTheSameSession() => OnSta(() =>
    {
        WizardSession session = Ready();
        using var form = new MainForm(session)
        {
            StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false
        };
        form.Show();
        Application.DoEvents();
        Button next = Find<Button>(form, "Next"), back = Find<Button>(form, "Back");
        next.PerformClick();
        Assert.Equal(1, session.Step);
        Assert.True(next.Enabled);
        Find<CheckBox>(form, "HeavyScan").Checked = true;
        Assert.Null(session.Game);
        Assert.Null(session.Comparison);
        Assert.False(next.Enabled);
        back.PerformClick();
        Assert.Equal(0, session.Step);
        Assert.NotNull(session.Reference);
        Assert.True(next.Enabled);
        form.Close();
    });

    [Fact]
    public void RealConversionScanComparisonAndReportImportUseSharedCore()
    {
        Directory.CreateDirectory(directory);
        string root = Path.Combine(directory, "game");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "game-only"), "abc");
        // The same protobuf format as the original converter regression fixtures.
        var manifest = new Protos.SophonManifestProto();
        manifest.Assets.Add(new Protos.SophonManifestAssetProperty
        {
            AssetName = "reference-only", AssetSize = 3, AssetHashMd5 = "900150983cd24fb0d6963f7d28e17f72", AssetType = 0
        });
        string raw = Path.Combine(directory, "manifest"), converted = Path.Combine(directory, "reference.json");
        File.WriteAllBytes(raw, Google.Protobuf.MessageExtensions.ToByteArray(manifest));
        ManifestConverter.Convert(raw, converted, uncompressed: true);
        string scanned = Path.Combine(directory, "scan.json");
        DirectoryScanner.Scan(root, scanned);
        var session = new WizardSession();
        session.AcceptReference(FileComparer.ReadInputs([converted]));
        session.Next();
        session.AcceptGame(FileComparer.ReadInputs([scanned]), root);
        session.Next();
        ComparisonResult comparison = FileComparer.Compare(session.Game!, session.Reference!);
        string report = Path.Combine(directory, "comparison.json");
        FileComparer.WriteReport(report, comparison);
        session.AcceptComparison(FileComparer.ImportReport(report, session.Game!, session.Reference!));
        session.Next();
        Assert.Equal(3, session.Step);
        Assert.Equal(1, session.Comparison!.Summary.OnlyA);
        Assert.Equal(1, session.Comparison.Summary.OnlyB);
        session.Select("game-only", true);
        session.ConfirmSelection(true);
        Assert.True(session.CanDemonstrateDeletion);
        Assert.Equal("abc", File.ReadAllText(Path.Combine(root, "game-only")));
    }

    private WizardSession Ready()
    {
        var (game, reference, root) = Inputs();
        var session = new WizardSession();
        session.AcceptReference(reference);
        session.AcceptGame(game, root);
        session.AcceptComparison(FileComparer.Compare(game, reference));
        return session;
    }

    private (FileListSnapshot Game, FileListSnapshot Reference, string Root) Inputs()
    {
        Directory.CreateDirectory(directory);
        string root = Path.Combine(directory, "game");
        string game = WriteList("game", root, [new("game-only", 3), new("different", 5), new("same", 1)]);
        string reference = WriteList("reference", null, [new("reference-only", 7), new("different", 6), new("same", 1), new("excluded/unknown", 1)]);
        return (FileComparer.ReadInputs([game]), FileComparer.ReadInputs([reference]), root);
    }

    private string WriteList(string name, string? root, ManifestFile[] files)
    {
        string path = Path.Combine(directory, name + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            fileCount = files.Length, totalSize = files.Sum(file => file.Size), files,
            scan = root is null ? null : new { root, mode = "light", excludedDirectories = new[] { "excluded" }, excludedFiles = Array.Empty<string>(), skippedLinks = Array.Empty<string>() }
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        return path;
    }

    private static T Find<T>(Control control, string name) where T : Control => (T)Assert.Single(control.Controls.Find(name, true));

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "原生控件测试未在 20 秒内完成。");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
