using System.Text.Json;
using HoyoFileClean.Desktop;
using Xunit;
using static HoyoFileClean.Desktop.Tests.NativeTestHost;

namespace HoyoFileClean.Desktop.Tests;

[Collection("Windows Forms")]
public sealed class WizardTests
{
    [Fact]
    public void GameReadingOffersSourcesSingleLineInputsAndEditableExclusionLists() => OnSta(() =>
    {
        WizardSession session = Ready();
        using var form = new MainForm(session);
        form.Location = new Point(-20000, -20000);
        form.StartPosition = FormStartPosition.Manual;
        form.ShowInTaskbar = false;
        form.Show();
        Application.DoEvents();
        Assert.True(Find<RadioButton>(form, "ScanGameSource").Checked);
        Control sourceRow = Find<RadioButton>(form, "ScanGameSource").Parent!;
        Button scanButton = Find<Button>(form, "ReadGame");
        Assert.NotSame(sourceRow, scanButton.Parent);
        Assert.True(scanButton.RectangleToScreen(scanButton.ClientRectangle).Top >=
            Find<TextBox>(form, "GameRoot").RectangleToScreen(Find<TextBox>(form, "GameRoot").ClientRectangle).Bottom);
        foreach (string name in new[] { "GameRoot", "GameJson", "ExclusionJson", "ExcludedDirectoryInput", "ExcludedFileInput" })
        {
            TextBox input = Find<TextBox>(form, name);
            Assert.False(input.Multiline);
            Assert.True(input.AllowDrop);
        }
        Find<TextBox>(form, "GameRoot").Text = directory;
        Find<TextBox>(form, "ExcludedDirectoryInput").Text = "Cache";
        Find<Button>(form, "AddExcludedDirectory").PerformClick();
        ListBox list = Find<ListBox>(form, "ExcludedDirectories");
        Assert.Equal("Cache", Assert.Single(list.Items.Cast<string>()));
        Assert.Null(session.Game);
        list.SelectedIndex = 0;
        Find<Button>(form, "RemoveExcludedDirectory").PerformClick();
        Assert.Empty(list.Items);
        Find<RadioButton>(form, "ImportGameSource").Checked = true;
        Assert.Equal("导入", Find<Button>(form, "ReadGame").Text);
        Assert.False(Find<Button>(form, "Execute").Visible);
        Assert.Empty(form.Controls.Find("Import", true));
        Assert.NotNull(Find<ProgressBar>(form, "ScanFileProgress"));
        Assert.NotNull(Find<ProgressBar>(form, "ScanByteProgress"));
        Assert.Empty(form.Controls.Find("ScanResult", true));
        ProgressBar parsing = Find<ProgressBar>(form, "OperationProgress");
        ProgressBar comparing = Find<ProgressBar>(form, "ComparisonProgress");
        Assert.NotSame(parsing.Parent, comparing.Parent);
        var outer = Assert.IsType<TableLayoutPanel>(form.Controls[0]);
        Assert.Equal(4, outer.RowCount);
        Assert.DoesNotContain(outer.Controls.Cast<Control>(), control => control is ProgressBar);
        Assert.Contains("尚无有效结果", Find<Label>(form, "Status").Text);
    });

    [Fact]
    public void ManifestStateRecoveryDoesNotDependOnDisplayedText() => OnSta(() =>
    {
        foreach (ReferenceState state in Enum.GetValues<ReferenceState>())
            Assert.False(string.IsNullOrWhiteSpace(ManifestTable.StateText(state)));
        using var table = new ManifestTable("state-test");
        table.AddFiles([new LocalManifestFile("file", @"C:\file", 1, DateTime.UtcNow)], check: true);
        ReferenceInput input = Assert.Single(table.CheckedInputs);
        table.ShowResults([(input, ReferenceState.Fetching, null)]);
        table.Rows[0].Cells["State"].Value = "arbitrary translated text";
        table.ResetFetching();
        Assert.Equal(ManifestTable.StateText(ReferenceState.Unfetched), table.Rows[0].Cells["State"].Value);
        table.ShowResults([(input, ReferenceState.Parsed, 1)]);
        table.Rows[0].Cells["State"].Value = "arbitrary parsed text";
        table.ResetFetching();
        Assert.Equal("arbitrary parsed text", table.Rows[0].Cells["State"].Value);
    });

    [Fact]
    public void BatchResultsFollowInputIdentityAfterTableSorting() => OnSta(() =>
    {
        using var table = new ManifestTable("batch-test");
        table.AddFiles([
            new LocalManifestFile("a", @"C:\a", 1, DateTime.UtcNow),
            new LocalManifestFile("b", @"C:\b", 2, DateTime.UtcNow)], check: true);
        ReferenceInput[] inputs = table.CheckedInputs;
        ReferenceInput a = inputs.Single(input => input.File.Name == "a");
        ReferenceInput b = inputs.Single(input => input.File.Name == "b");
        table.Sort(Assert.IsType<DataGridViewTextBoxColumn>(table.Columns["Name"]),
            System.ComponentModel.ListSortDirection.Descending);
        Assert.Same(b, table.CheckedInputs[0]);
        table.ShowResults([(a, ReferenceState.Parsed, 7), (b, ReferenceState.Fetching, null)]);
        foreach (DataGridViewRow row in table.Rows)
        {
            bool parsed = row.Cells["Name"].Value is "a";
            Assert.Equal(ManifestTable.StateText(parsed ? ReferenceState.Parsed : ReferenceState.Fetching), row.Cells["State"].Value);
            Assert.Equal(parsed ? 7 : (int?)null, row.Cells["Entries"].Value);
            row.Cells["State"].Value = "arbitrary translated text";
        }
        table.ResetFetching();
        Assert.Equal(ManifestTable.StateText(ReferenceState.Unfetched), table.Rows[0].Cells["State"].Value);
        Assert.Equal("arbitrary translated text", table.Rows[1].Cells["State"].Value);
        Assert.Equal(7, table.Rows[1].Cells["Entries"].Value);
    });

    [Fact]
    public void ExclusionRuleModelsRejectExplicitNullAndKeepOptionalArrays()
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "rules-null.json");
        foreach (string json in new[] { "{}", "{\"scan\":null,\"excludedFiles\":[]}",
            "{\"excludedFiles\":null}", "{\"excludedFiles\":[null]}", "{\"scan\":{\"excludedDirectories\":null}}" })
        {
            File.WriteAllText(path, json);
            Assert.Throws<JsonException>(() => ExclusionRules.Read(path, directory));
        }
        File.WriteAllText(path, "{\"excludedFiles\":[]}");
        Assert.Empty(ExclusionRules.Read(path, directory).Directories);
    }

    [Fact]
    public void ExclusionRulesValidateBothArraysBeforePublishing()
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "rules.json");
        File.WriteAllText(path, """{"excludedDirectories":["Cache","cache"],"excludedFiles":["settings.ini"]}""");
        ExclusionRules rules = ExclusionRules.Read(path, directory);
        Assert.Single(rules.Directories);
        Assert.Equal("settings.ini", Assert.Single(rules.Files));
        File.WriteAllText(path, """{"excludedDirectories":["Cache"],"excludedFiles":["../outside"]}""");
        Assert.Throws<CoreException>(() => ExclusionRules.Read(path, directory));
    }

    private readonly string directory = Path.Combine(TestTemporaryFiles.Root, Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(650)]
    [InlineData(1000)]
    public void GameListsGrowWithoutOverlappingResultsAndRejectMixedDrops(int height) => OnSta(() =>
    {
        Directory.CreateDirectory(directory);
        string folder = Path.Combine(directory, "Cache"), other = Path.Combine(directory, "Logs");
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(other);
        string file = Path.Combine(directory, "settings.ini");
        File.WriteAllText(file, "test");
        WizardSession session = Ready();
        using var form = new MainForm(session) { StartPosition = FormStartPosition.Manual,
            Location = new Point(-20000, -20000), ShowInTaskbar = false, Size = new Size(800, height) };
        form.Show();
        Application.DoEvents();
        Find<TextBox>(form, "GameRoot").Text = directory;
        Application.DoEvents();
        ListBox dirs = Find<ListBox>(form, "ExcludedDirectories"), files = Find<ListBox>(form, "ExcludedFiles");
        foreach (ListBox list in new[] { dirs, files })
        {
            Assert.True(list.Height > 30, string.Join(" / ", Ancestors(list).Select(c => $"{c.GetType().Name}:{c.Size}")));
            Assert.True(list.RectangleToScreen(list.ClientRectangle).Bottom <=
                Find<Label>(form, "Status").RectangleToScreen(Rectangle.Empty).Top);
        }
        DragEventArgs Raise(ListBox list, string method, params string[] paths)
        {
            var args = new DragEventArgs(new DataObject(DataFormats.FileDrop, paths), 0, 0, 0, DragDropEffects.Copy, DragDropEffects.None);
            typeof(Control).GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(list, [args]);
            return args;
        }
        static IEnumerable<Control> Ancestors(Control c)
        {
            for (Control? current = c; current is not null; current = current.Parent) yield return current;
        }
        foreach (ListBox list in new[] { dirs, files })
        {
            Assert.Equal(DragDropEffects.None, Raise(list, "OnDragEnter", folder, file).Effect);
            Raise(list, "OnDragDrop", folder, file);
            Assert.Empty(list.Items);
        }
        Raise(dirs, "OnDragDrop", folder, other);
        Assert.Equal(2, dirs.Items.Count);
        Raise(files, "OnDragDrop", file);
        Assert.Single(files.Items.Cast<string>());
        Raise(dirs, "OnDragDrop", folder, other);
        Assert.Equal(2, dirs.Items.Count);
    });

    [Fact]
    public void EveryNextStepRequiresAnEffectiveResult()
    {
        var session = new WizardSession();
        Assert.False(session.CanAdvance);
        Assert.Throws<InvalidOperationException>(session.Next);
        var (game, reference, root) = Inputs();
        session.AcceptGame(game, root);
        session.Next();
        Assert.Equal(1, session.Step);
        Assert.Throws<InvalidOperationException>(session.Next);
        session.AcceptReference(reference);
        session.Next();
        Assert.Throws<InvalidOperationException>(session.Next);
        session.AcceptComparison(FileComparer.Compare(game, reference));
        session.Next();
        Assert.Equal(3, session.Step);
        Assert.False(session.CanAdvance);
        session.Back();
        Assert.True(session.CanAdvance);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GameStatusBrieflyReportsSkippedLinksAndExcludedScope(bool imported)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "scope.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            fileCount = 0, totalSize = 0, files = Array.Empty<object>(),
            scan = new { root = directory, mode = "light", excludedDirectories = new[] { "Cache" },
                excludedFiles = Array.Empty<string>(), skippedLinks = new[] { "link-a", "link-b" } }
        }));
        string text = ResultText.DescribeGameStatus(FileComparer.ReadInputs([path]), imported);
        Assert.Contains("排除 1 项", text);
        Assert.Contains("跳过链接／重解析点 2 项（未跟随）", text);
        Assert.Contains("上述范围未扫描", text);
        Assert.DoesNotContain("link-a", text);
        Assert.Equal(imported, text.Contains("未重新扫描"));
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
        Assert.False(Find<Button>(form, "Execute").Visible);
        Assert.Empty(form.Controls.Find("Import", true));
        Assert.True(Find<RadioButton>(form, "NetworkSource").Checked);
        Assert.False(Find<CheckBox>(form, "HeavyScan").Checked);
        Assert.True(Find<DataGridView>(form, "Manifests").Columns["Name"]!.ReadOnly);
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
            1 => ["Manifests", "ReferenceResult", "Next", "ParseManualReference"],
            0 => ["GameRoot", "ExcludedDirectories", "ExcludedFiles", "Next", "ReadGame"],
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
        back.PerformClick();
        Find<CheckBox>(form, "HeavyScan").Checked = true;
        Assert.Null(session.Game);
        Assert.Null(session.Comparison);
        Assert.False(next.Enabled);
        Assert.Equal(0, session.Step);
        Assert.NotNull(session.Reference);
        Assert.False(next.Enabled);
        form.Close();
    });

    [Theory]
    [InlineData("NetworkSource", false)]
    [InlineData("LocalSource", false)]
    [InlineData("ManualSource", true)]
    public void ReferenceModesShowOnlyTheirOwnInterface(string mode, bool manual) => OnSta(() =>
    {
        using var form = new MainForm(ReferenceSession())
        {
            StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000),
            ShowInTaskbar = false, Size = new Size(800, 650)
        };
        form.Show();
        Find<RadioButton>(form, mode).Checked = true;
        Application.DoEvents();
        Assert.True(Find<RadioButton>(form, mode).Checked);
        Assert.Single(new[] { "NetworkSource", "LocalSource", "ManualSource" },
            name => Find<RadioButton>(form, name).Checked);
        Assert.Equal(manual, Find<DataGridView>(form, "Manifests").Visible);
        Assert.Equal(manual, Find<Label>(form, "ReferenceResult").Visible);
        Assert.Equal(mode == "NetworkSource", Find<DataGridView>(form, "NetworkManifests").Visible);
        Assert.Equal(mode == "LocalSource", Find<DataGridView>(form, "LocalManifests").Visible);
        if (mode == "LocalSource")
        {
            TextBox path = Find<TextBox>(form, "LocalManifestDirectory");
            Assert.True(path.Visible);
            Assert.True(path.Width > 200 && path.Height >= 20, "路径输入框必须有可见、可输入的尺寸");
            Assert.True(form.RectangleToScreen(form.ClientRectangle).Contains(path.RectangleToScreen(path.ClientRectangle)));
        }
        Assert.False(Find<Button>(form, "Execute").Visible);
        Assert.Empty(form.Controls.Find("Import", true));
        Assert.Equal(manual, Find<Button>(form, "ParseManualReference").Visible);
        Assert.Empty(form.Controls.Find("ImportManualReference", true));
        Assert.False(Find<Button>(form, "Next").Enabled);
        if (mode == "NetworkSource") Assert.Contains("不会自动联网", Find<Label>(form, "Status").Text);
        Assert.Empty(form.Controls.Find("NetworkSummary", true));
        Rectangle client = form.RectangleToScreen(form.ClientRectangle);
        foreach (string name in new[] { "NetworkSource", "LocalSource", "ManualSource", manual ? "Manifests" : mode == "LocalSource" ? "LocalManifests" : "NetworkManifests" })
        {
            Control control = Assert.Single(form.Controls.Find(name, true));
            Assert.True(client.Contains(control.RectangleToScreen(control.ClientRectangle)), name + " 超出窗口范围");
        }
        Directory.CreateDirectory(directory);
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(Path.Combine(directory, "reference-mode-" + mode + ".png"));
        form.Close();
    });

    [Theory]
    [InlineData("Network", "Local")]
    [InlineData("Network", "Manual")]
    [InlineData("Local", "Manual")]
    [InlineData("Local", "Network")]
    [InlineData("Manual", "Network")]
    [InlineData("Manual", "Local")]
    public void ReferenceModeSwitchAlwaysCoversContentBeforeHidingOldPage(string from, string to) => OnSta(() =>
    {
        using var form = new MainForm(ReferenceSession())
        {
            StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false
        };
        form.Show();
        Find<RadioButton>(form, from + "Source").Checked = true;
        Application.DoEvents();
        Control[] pages = new[] { "NetworkManifests", "LocalManifests", "Manifests" }
            .Select(name => Find<ManifestTable>(form, name).Parent!).ToArray();
        Control target = Find<ManifestTable>(form, to == "Manual" ? "Manifests" : to + "Manifests").Parent!;
        var coverage = new List<bool>();
        var replacements = new List<bool>();
        foreach (Control page in pages)
            page.VisibleChanged += (_, _) =>
            {
                coverage.Add(pages.Any(item => item.Visible));
                if (!page.Visible)
                    replacements.Add(target.Visible && target.Parent!.Controls.GetChildIndex(target) == 0);
            };
        Find<RadioButton>(form, to + "Source").Checked = true;
        Application.DoEvents();
        Assert.NotEmpty(coverage);
        Assert.All(coverage, Assert.True);
        Assert.NotEmpty(replacements);
        Assert.All(replacements, Assert.True);
        Assert.Same(target, Assert.Single(pages, page => page.Visible));
        int changes = coverage.Count;
        // Selection invalidation refreshes the view without changing the active source.
        Find<ManifestTable>(form, to == "Manual" ? "Manifests" : to + "Manifests")
            .AddFiles([new LocalManifestFile("candidate", @"C:\candidate", 1, DateTime.UtcNow)], check: true);
        Assert.Equal(changes, coverage.Count);
        form.Close();
    });

    [Fact]
    public void SwitchingReferenceModeInvalidatesReferenceAndDownstreamButKeepsScan() => OnSta(() =>
    {
        WizardSession session = Ready();
        session.Next();
        session.Select("game-only", true);
        session.ConfirmSelection(true);
        using var form = new MainForm(session)
        {
            StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false
        };
        Assert.True(Find<RadioButton>(form, "ManualSource").Checked);
        Assert.NotNull(session.Reference);
        form.Show();
        Assert.True(Find<RadioButton>(form, "ManualSource").Checked, "显示窗口和设置焦点不应改变已有模式");
        Assert.NotNull(session.Reference);
        Assert.True(Find<Button>(form, "Next").Enabled);
        Find<ManifestTable>(form, "Manifests").AddFiles([new LocalManifestFile("retain", @"D:\retain.manifest", 3, DateTime.UtcNow)]);
        Find<RadioButton>(form, "LocalSource").Checked = true;
        Assert.Null(session.Reference);
        Assert.NotNull(session.Game);
        Assert.Null(session.Comparison);
        Assert.Equal(0, session.SelectedCount);
        Assert.False(session.SelectionConfirmed);
        Assert.False(Find<Button>(form, "Next").Enabled);
        Find<RadioButton>(form, "ManualSource").Checked = true;
        Assert.Single(Find<DataGridView>(form, "Manifests").Rows.Cast<DataGridViewRow>());
        Assert.False(Find<Button>(form, "ParseManualReference").Enabled, "保留行尚未勾选，不能解析");
        Assert.False(Find<Button>(form, "Next").Enabled);
        form.Close();
    });

    [Fact]
    public void LocalDirectoryDefaultsAndBrowserAlwaysStartAtLauncherDefault() => OnSta(() =>
    {
        using var form = new MainForm();
        string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "miHoYo", "HYP", "1_1", "modules", "sophon", "manifest");
        TextBox path = Find<TextBox>(form, "LocalManifestDirectory");
        Assert.Equal(expected, path.Text);
        path.Text = @"D:\custom-manifests";
        using var dialog = MainForm.CreateManifestDirectoryDialog();
        Assert.Equal(expected, dialog.SelectedPath);
        Assert.False(dialog.ShowNewFolderButton);
    });

    [Fact]
    public void LocalGetPopulatesReadOnlyTableWithoutParsingAndPathChangeClearsIt() => OnSta(() =>
    {
        Directory.CreateDirectory(directory);
        string first = Path.Combine(directory, "old"), second = Path.Combine(directory, "new");
        File.WriteAllText(first, "invalid manifest");
        File.WriteAllText(second, "also invalid");
        // These instants straddle New York's clock rollback: local wall time would sort incorrectly.
        DateTime older = new(2026, 11, 1, 5, 50, 0, DateTimeKind.Utc);
        DateTime newer = new(2026, 11, 1, 6, 10, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(first, older);
        File.SetLastWriteTimeUtc(second, newer);
        var session = ReferenceSession();
        using var form = new MainForm(session)
        {
            StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false
        };
        form.Show();
        Find<RadioButton>(form, "LocalSource").Checked = true;
        var path = Find<TextBox>(form, "LocalManifestDirectory");
        path.Text = directory;
        Find<Button>(form, "GetLocalManifests").PerformClick();
        PumpUntilLocalGetFinishes(form, session);
        var grid = Find<DataGridView>(form, "LocalManifests");
        Assert.False(grid.Columns["Checked"]!.ReadOnly);
        Assert.All(grid.Columns.Cast<DataGridViewColumn>().Skip(1), column => Assert.True(column.ReadOnly));
        Assert.False(grid.Columns.Contains("Path"));
        Assert.Equal(10, grid.Columns.Count);
        Assert.False(grid.AllowUserToAddRows);
        Assert.False(grid.AllowUserToDeleteRows);
        Assert.Equal(2, grid.Rows.Count);
        Assert.All(grid.Rows.Cast<DataGridViewRow>(), row => Assert.Null(row.Cells["Package"].Value));
        Assert.Equal("new", grid.Rows[0].Cells["Name"].Value);
        Assert.Equal("old", grid.Rows[1].Cells["Name"].Value);
        Assert.Equal(newer, grid.Rows[0].Cells["Modified"].Value);
        Assert.Equal(DateTimeKind.Utc, ((DateTime)grid.Rows[0].Cells["Modified"].Value!).Kind);
        Assert.Equal(newer.ToLocalTime().ToString("yyyy-MM-dd HH'时'"), grid.Rows[0].Cells["Modified"].FormattedValue);
        Assert.Equal(12L, grid.Rows[0].Cells["Size"].Value);
        Assert.Equal(SortOrder.Descending, grid.SortOrder);
        Assert.Null(session.Reference);
        Assert.False(Find<Button>(form, "Next").Enabled);
        Assert.Empty(form.Controls.Find("LocalManifestSummary", true));
        Assert.Contains("已获取 2 份候选清单，尚未解析", Find<Label>(form, "Status").Text);
        using (var bitmap = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(Path.Combine(directory, "local-manifests-populated.png"));
        }
        path.Text = Path.Combine(directory, "missing");
        Assert.Empty(grid.Rows.Cast<DataGridViewRow>());
        Find<Button>(form, "GetLocalManifests").PerformClick();
        PumpUntilLocalGetFinishes(form, session);
        Assert.Empty(grid.Rows.Cast<DataGridViewRow>());
        Assert.Contains("处理失败", Find<Label>(form, "Status").Text);
        Assert.False(Find<Button>(form, "Next").Enabled);
        form.Close();
    });

    private static void PumpUntilLocalGetFinishes(MainForm form, WizardSession session)
    {
        WaitUntil(() => !session.IsBusy && !Find<ProgressBar>(form, "OperationProgress").Visible
            && Find<Button>(form, "GetLocalManifests").Enabled
            && !Find<Label>(form, "Status").Text.StartsWith("正在", StringComparison.Ordinal), timeoutSeconds: 10);
        Application.DoEvents();
        Assert.False(session.IsBusy, "异步获取未按时完成");
        Assert.False(Find<Label>(form, "Status").Text.StartsWith("正在", StringComparison.Ordinal), "界面未完成结果处理");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CheckedManifestParsingStaysInMemoryAndInvalidatesWhenSelectionChanges(bool local) => OnSta(() =>
    {
        Directory.CreateDirectory(directory);
        string source = Path.Combine(directory, "manifest");
        var manifest = new Protos.SophonManifestProto();
        manifest.Assets.Add(new Protos.SophonManifestAssetProperty
        {
            AssetName = "file", AssetSize = 3, AssetHashMd5 = "900150983cd24fb0d6963f7d28e17f72", AssetType = 0
        });
        byte[] bytes = Google.Protobuf.MessageExtensions.ToByteArray(manifest);
        File.WriteAllBytes(source, bytes);
        var session = ReferenceSession();
        using var form = new MainForm(session)
        {
            StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false
        };
        form.Show();
        Find<RadioButton>(form, local ? "LocalSource" : "ManualSource").Checked = true;
        var table = Find<ManifestTable>(form, local ? "LocalManifests" : "Manifests");
        table.AddFiles([new LocalManifestFile("manifest", source, bytes.Length, DateTime.UtcNow)], check: true);
        // An unchecked invalid file must never be read by parsing.
        table.AddFiles([new LocalManifestFile("missing", Path.Combine(directory, "missing"), 0, DateTime.UtcNow)]);
        Find<Button>(form, local ? "ParseLocalReference" : "ParseManualReference").PerformClick();
        PumpUntilLocalGetFinishes(form, session);
        Assert.NotNull(session.Reference);
        Assert.Equal(1, session.Reference.FileCount);
        Assert.Equal(source, Assert.Single(session.Reference.Sources).Path);
        Find<Button>(form, "Next").PerformClick();
        Assert.Equal("1", Find<Label>(form, "ComparisonReferenceFiles").Text);
        Assert.Empty(Find<ComparisonPanel>(form, "ComparisonResult").Controls.OfType<TextBox>());
        Find<Button>(form, "Back").PerformClick();
        Assert.Equal(new[] { source }, Directory.GetFiles(directory, "*", SearchOption.AllDirectories));
        Assert.False(Directory.Exists(Path.Combine(directory, ".temp")));
        Find<Button>(form, local ? "ParseLocalReference" : "ParseManualReference").PerformClick();
        PumpUntilLocalGetFinishes(form, session);
        Assert.Equal(new[] { source }, Directory.GetFiles(directory, "*", SearchOption.AllDirectories));
        DataGridViewRow row = table.Rows.Cast<DataGridViewRow>().Single(row => row.Cells["Name"].Value is "manifest");
        Assert.Equal("已解析", row.Cells["State"].Value);
        Assert.Equal(1, row.Cells["Entries"].Value);
        Assert.Equal("—", row.Cells["Game"].Value);
        Assert.Equal("—", row.Cells["Version"].Value);
        Assert.True(Find<Button>(form, "Next").Enabled);
        row.Cells["Checked"].Value = false;
        Assert.Null(session.Reference);
        Assert.False(Find<Button>(form, "Next").Enabled);
        Assert.Contains("请重新解析", Find<Label>(form, "Status").Text);
        Assert.DoesNotContain("点击下一步", Find<Label>(form, "Status").Text);
        Assert.Equal(bytes, File.ReadAllBytes(source));
        form.Close();
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParsingFailureUpdatesRowWithoutPublishingPartialReference(bool local) => OnSta(() =>
    {
        Directory.CreateDirectory(directory);
        string source = Path.Combine(directory, "invalid");
        File.WriteAllBytes(source, [0xff]);
        var session = ReferenceSession();
        using var form = new MainForm(session)
        {
            StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false
        };
        form.Show();
        Find<RadioButton>(form, local ? "LocalSource" : "ManualSource").Checked = true;
        var table = Find<ManifestTable>(form, local ? "LocalManifests" : "Manifests");
        table.AddFiles([new LocalManifestFile("invalid", source, 1, DateTime.UtcNow)], check: true);
        Assert.Equal("", table.Rows[0].Cells["Entries"].FormattedValue);
        Find<Button>(form, local ? "ParseLocalReference" : "ParseManualReference").PerformClick();
        PumpUntilLocalGetFinishes(form, session);
        Assert.Equal("失败", table.Rows[0].Cells["State"].Value);
        Assert.Equal("—", table.Rows[0].Cells["Entries"].FormattedValue);
        Assert.Null(session.Reference);
        Assert.Contains("处理失败", Find<Label>(form, "Status").Text);
        form.Close();
    });

    [Fact]
    public void InMemoryScanAndComparisonDoNotCreateOutputsOrConsumeOldTemp()
    {
        Directory.CreateDirectory(Path.Combine(directory, ".temp"));
        string sentinel = Path.Combine(directory, ".temp", "untrusted.json");
        File.WriteAllText(sentinel, "untrusted");
        string root = Path.Combine(directory, "game");
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "file");
        File.WriteAllText(file, "abc");
        FileListSnapshot light = DirectoryScanner.Scan(root, cancellationToken: TestContext.Current.CancellationToken);
        FileListSnapshot heavy = DirectoryScanner.Scan(root, computeHashes: true, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(light.Files["file"].Hash);
        Assert.Equal("900150983cd24fb0d6963f7d28e17f72", heavy.Files["file"].Hash);
        Assert.Equal(1, FileComparer.Compare(light, heavy).Summary.MatchedSizeOnly);
        Assert.Equal(new[] { sentinel, file }.Order(),
            Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Order());
        Assert.Equal("untrusted", File.ReadAllText(sentinel));
    }

    [Fact]
    public void InMemoryParsingDoesNotFollowLinkedTemp()
    {
        string program = Path.Combine(directory, "program"), outside = Path.Combine(directory, "outside");
        Directory.CreateDirectory(program);
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(Path.Combine(program, ".temp"), outside);
        string source = WriteList("reference", null, [new ManifestFile("file", 1)]);
        Assert.Equal(1, ManifestConverter.Read(source, TestContext.Current.CancellationToken).FileCount);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
    }

    [Fact]
    public void InMemoryScanDoesNotNeedAnOutsideOutputDirectory()
    {
        string program = Path.Combine(directory, "program");
        Directory.CreateDirectory(program);
        Assert.Equal(0, DirectoryScanner.Scan(directory, cancellationToken: TestContext.Current.CancellationToken).FileCount);
        Assert.Empty(Directory.EnumerateFileSystemEntries(program));
    }

    [Fact]
    public void ReferenceTablesShareColumnsAndSkipPrecedesManifestActions() => OnSta(() =>
    {
        using var form = new MainForm(ReferenceSession())
        {
            StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false
        };
        form.Show();
        Find<RadioButton>(form, "ManualSource").Checked = true;
        var manual = Find<ManifestTable>(form, "Manifests");
        var local = Find<DataGridView>(form, "LocalManifests");
        Assert.Equal(new[] { "Checked", "Name", "Modified", "Size", "State", "Game", "Version", "Package", "Entries", "Branch" },
            manual.Columns.Cast<DataGridViewColumn>().Select(column => column.Name));
        Assert.Equal(manual.Columns.Cast<DataGridViewColumn>().Select(column => column.HeaderText),
            local.Columns.Cast<DataGridViewColumn>().Select(column => column.HeaderText));
        Button add = Find<Button>(form, "AddManifest");
        Assert.Empty(form.Controls.Find("ImportManualReference", true));
        Assert.Equal(add.Parent, Find<Button>(form, "ParseManualReference").Parent);
        Assert.Equal("", manual.Columns[0].HeaderText);
        Assert.Equal("大小", manual.Columns["Size"]!.HeaderText);
        foreach (DataGridView table in new[] { manual, local })
        {
            Assert.False(table.AllowUserToResizeRows);
            Assert.Equal(DataGridViewColumnHeadersHeightSizeMode.DisableResizing, table.ColumnHeadersHeightSizeMode);
        }
        manual.AddFiles([new LocalManifestFile("a", @"D:\a", 1024, DateTime.UtcNow)]);
        manual.Rows[0].Selected = true;
        Assert.Empty(manual.SelectedRows.Cast<DataGridViewRow>());
        Assert.Empty(manual.SelectedCells.Cast<DataGridViewCell>());
        Assert.Empty(form.Controls.Find("Status", true).OfType<TextBox>());
        Assert.Empty(form.Controls.Find("ReferenceResult", true).OfType<TextBox>());
        form.Close();
    });

    [Fact]
    public void FailedMetadataEnumerationCannotPartiallyChangeCheckedFiles() => OnSta(() =>
    {
        using var table = new ManifestTable("test");
        table.AddFiles([new LocalManifestFile("existing", @"D:\existing", 1, DateTime.UtcNow)], check: true);
        int events = 0;
        table.CheckedFilesChanged += () => events++;
        IEnumerable<LocalManifestFile> FailingBatch()
        {
            yield return new LocalManifestFile("new", @"D:\new", 2, DateTime.UtcNow);
            throw new FileNotFoundException("diagnostic text must not be shown");
        }
        Assert.Throws<FileNotFoundException>(() => table.AddFiles(FailingBatch(), check: true));
        Assert.Single(table.Rows.Cast<DataGridViewRow>());
        Assert.Equal("existing", Assert.Single(table.CheckedInputs).File.Name);
        Assert.Equal(0, events);
    });

    [Fact]
    public void DesktopErrorPresentationDoesNotExposeSystemExceptionMessage()
    {
        const string diagnostic = "private diagnostic stack or system text";
        foreach (Exception error in new Exception[]
        {
            new FileNotFoundException(diagnostic), new DirectoryNotFoundException(diagnostic),
            new IOException(diagnostic), new UnauthorizedAccessException(diagnostic),
            new InvalidDataException(diagnostic), new ArgumentException(diagnostic)
        })
        {
            string text = HoyoFileClean.Presentation.DiagnosticText.ForDesktop(error);
            Assert.NotEmpty(text);
            Assert.DoesNotContain(diagnostic, text);
        }
    }

    [Theory]
    [InlineData(0L, "0KB")]
    [InlineData(1024L, "1KB")]
    [InlineData(125952L, "123KB")]
    [InlineData(1861222L, "1.77MB")]
    [InlineData(1908408L, "1.82MB")]
    [InlineData(102400L, "100KB")]
    [InlineData(1048576L, "1MB")]
    [InlineData(1073741824L, "1GB")]
    public void ManifestSizesUseCompactUnits(long bytes, string expected)
        => Assert.Equal(expected, ManifestTable.FormatSize(bytes));

    [Fact]
    public void GuiScanAndComparisonKeepResultsInMemory() => OnSta(() =>
    {
        Directory.CreateDirectory(directory);
        string root = Path.Combine(directory, "game");
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "file");
        File.WriteAllText(file, "abc");
        string config = WriteGameConfig(root);
        string reference = WriteList("reference", null, [new ManifestFile("file", 3)]);
        var session = new WizardSession();
        session.AcceptReference(ManifestConverter.Read(reference));
        using var form = new MainForm(session)
        {
            StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false
        };
        form.Show();
        Find<TextBox>(form, "GameRoot").Text = root;
        void Wait()
        {
            WaitUntil(() => !session.IsBusy && !Find<ProgressBar>(form, "OperationProgress").Visible,
                timeoutSeconds: 10);
            Assert.False(session.IsBusy);
            Assert.DoesNotContain("处理失败", Find<Label>(form, "Status").Text);
        }
        ReadGamePresetAndWait(form, session);
        Find<Button>(form, "ReadGame").PerformClick();
        Wait();
        Assert.NotNull(session.Game);
        Assert.Contains("仅保留在内存", Find<Label>(form, "Status").Text);
        Find<Button>(form, "Next").PerformClick();
        Find<Button>(form, "Next").PerformClick();
        Find<Button>(form, "Execute").PerformClick();
        Wait();
        Assert.Equal(1, session.Comparison!.Summary.MatchedSizeOnly);
        Assert.Equal("仅大小相同数：1", Find<Label>(form, "ComparisonMatched").Text);
        Assert.Equal(new[] { file, reference, config }.Order(),
            Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Order());
        Assert.Equal(root, Assert.Single(session.Game.Sources).Path);
        form.Close();
    });

    [Fact]
    public void InMemoryReferenceIsIndependentOfLaterInputEdits()
    {
        Directory.CreateDirectory(directory);
        string source = WriteList("reference", null, [new ManifestFile("file", 3)]);
        FileListSnapshot snapshot = ManifestConverter.Read(source, TestContext.Current.CancellationToken);
        File.WriteAllText(source, "changed");
        Assert.Equal(3, snapshot.Files["file"].Size);
        Assert.Equal(source, Assert.Single(snapshot.Sources).Path);
        Assert.Equal(3, FileComparer.Combine([snapshot, snapshot], TestContext.Current.CancellationToken).TotalSize);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1000, false)]
    [InlineData(10000, false)]
    [InlineData(10000, true)]
    public void LargeManifestBatchIsPublishedOnceAndResizeWorkStaysBounded(int count, bool local) => OnSta(() =>
    {
        using var form = new MainForm(ReferenceSession())
        {
            StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false
        };
        form.Show();
        Find<RadioButton>(form, local ? "LocalSource" : "ManualSource").Checked = true;
        ManifestTable table = Find<ManifestTable>(form, local ? "LocalManifests" : "Manifests");
        int additions = 0, layouts = 0, formatting = 0;
        table.RowsAdded += (_, _) => additions++;
        table.Layout += (_, _) => layouts++;
        table.CellFormatting += (_, _) => formatting++;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        table.AddFiles(Enumerable.Range(0, count).Select(index => new LocalManifestFile(
            $"file-{index:D4}", $@"D:\manifests\file-{index:D4}", index, DateTime.UnixEpoch.AddMinutes(index))));
        long addMs = watch.ElapsedMilliseconds;
        int addLayouts = layouts;
        layouts = formatting = 0;
        using var bitmap = new Bitmap(1100, 800);
        watch.Restart();
        for (int i = 0; i < 30; i++)
        {
            form.ClientSize = new Size(1000 + i % 2 * 20, 760 + i % 2 * 20);
            table.DrawToBitmap(bitmap, table.ClientRectangle);
        }
        string measurement = $"rows={count}, local={local}; add: {addMs}ms, events={additions}, layouts={addLayouts}; resize: {watch.ElapsedMilliseconds}ms, layouts={layouts}, formatting={formatting}";
        Console.WriteLine(measurement);
        Assert.True(additions == (count == 0 ? 0 : 1), measurement);
        Assert.True(layouts <= 30 * 10, measurement);
        if (count > 0) Assert.True(formatting > 0, measurement);
        Assert.True(formatting <= 30 * table.Columns.Count * (table.Height / table.RowTemplate.Height + 2) * 2, measurement);
        form.Close();
    });

    [Fact]
    public void LocalDirectoryDropFillsOnlyOneDirectoryWithoutFetchingAndHonorsBusyState() => OnSta(() =>
    {
        Directory.CreateDirectory(directory);
        string other = Path.Combine(directory, "other");
        Directory.CreateDirectory(other);
        string file = Path.Combine(directory, "file");
        File.WriteAllText(file, "keep");
        var session = ReferenceSession();
        using var form = new MainForm(session);
        TextBox box = Find<TextBox>(form, "LocalManifestDirectory");
        ManifestTable table = Find<ManifestTable>(form, "LocalManifests");
        Assert.True(box.AllowDrop);
        DragEventArgs Drag(string[] paths, DragDropEffects allowed = DragDropEffects.Copy)
            => new(new DataObject(DataFormats.FileDrop, paths), 0, 0, 0, allowed, DragDropEffects.None);
        void Raise(string name, DragEventArgs args)
            => typeof(Control).GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(box, [args]);
        string original = box.Text;
        DragEventArgs folder = Drag([directory]);
        Raise("OnDragEnter", folder);
        Assert.Equal(DragDropEffects.None, folder.Effect);
        Raise("OnDragDrop", folder);
        Assert.Equal(original, box.Text);
        Find<RadioButton>(form, "LocalSource").Checked = true;
        table.AddFiles([new LocalManifestFile("old", file, 4, DateTime.UtcNow)]);
        session.AcceptReference(FileComparer.ReadInputs([WriteList("reference", null, [new ManifestFile("file", 4)])]));
        Raise("OnDragEnter", folder);
        Assert.Equal(DragDropEffects.Copy, folder.Effect);
        Raise("OnDragDrop", folder);
        Assert.Equal(directory, box.Text);
        Assert.Empty(table.Rows.Cast<DataGridViewRow>());
        Assert.Null(session.Reference);
        Assert.Contains("请点击获取", Find<Label>(form, "Status").Text);
        foreach (DragEventArgs invalid in new[]
        {
            Drag([file]), Drag([directory, other]), Drag([Path.Combine(directory, "missing")]),
            Drag([other], DragDropEffects.Move),
            new DragEventArgs(new DataObject(DataFormats.Text, other), 0, 0, 0, DragDropEffects.Copy, DragDropEffects.Copy)
        })
        {
            Raise("OnDragEnter", invalid);
            Assert.Equal(DragDropEffects.None, invalid.Effect);
            Raise("OnDragDrop", invalid);
            Assert.Equal(directory, box.Text);
        }
        using var release = new ManualResetEventSlim();
        Task work = session.RunAsync(() => release.Wait(TimeSpan.FromSeconds(10)), _ => { });
        try
        {
            DragEventArgs busy = Drag([other]);
            Raise("OnDragEnter", busy);
            Assert.Equal(DragDropEffects.None, busy.Effect);
            Raise("OnDragDrop", busy);
            Assert.Equal(directory, box.Text);
        }
        finally
        {
            release.Set();
            WaitUntil(() => work.IsCompleted, timeoutSeconds: 10);
            work.GetAwaiter().GetResult();
        }
        Assert.Equal("keep", File.ReadAllText(file));
    });

    [Fact]
    public void ManualTableFileDropAddsCheckedFilesWithoutParsingAndRejectsInvalidBatches() => OnSta(() =>
    {
        Directory.CreateDirectory(directory);
        string first = Path.Combine(directory, "first"), second = Path.Combine(directory, "second.json");
        File.WriteAllText(first, "not parsed on drop");
        File.WriteAllText(second, "invalid json is still a candidate");
        var session = ReferenceSession();
        using var form = new MainForm(session);
        ManifestTable table = Find<ManifestTable>(form, "Manifests");
        Assert.True(table.AllowDrop);
        Assert.False(Find<ManifestTable>(form, "LocalManifests").AllowDrop);
        DragEventArgs Drag(string[] paths, DragDropEffects allowed = DragDropEffects.Copy)
            => new(new DataObject(DataFormats.FileDrop, paths), 0, 0, 0, allowed, DragDropEffects.None);
        void Raise(string name, DragEventArgs args)
            => typeof(Control).GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(table, [args]);
        DragEventArgs files = Drag([first, second]);
        Raise("OnDragEnter", files);
        Assert.Equal(DragDropEffects.None, files.Effect);
        Raise("OnDragDrop", files);
        Assert.Empty(table.Rows.Cast<DataGridViewRow>());
        Find<RadioButton>(form, "ManualSource").Checked = true;
        Raise("OnDragEnter", files);
        Assert.Equal(DragDropEffects.Copy, files.Effect);
        Raise("OnDragDrop", files);
        Assert.Equal(2, table.CheckedInputs.Length);
        Assert.Null(session.Reference);
        Assert.All(table.Rows.Cast<DataGridViewRow>(), row => Assert.Equal("未解析", row.Cells["State"].Value));
        Raise("OnDragDrop", files);
        Assert.Equal(2, table.Rows.Count);
        DragEventArgs text = new(new DataObject(DataFormats.Text, "not a file"), 0, 0, 0,
            DragDropEffects.Copy, DragDropEffects.Copy);
        Raise("OnDragEnter", text);
        Assert.Equal(DragDropEffects.None, text.Effect);
        Raise("OnDragDrop", text);
        DragEventArgs moveOnly = Drag([first], DragDropEffects.Move);
        Raise("OnDragEnter", moveOnly);
        Assert.Equal(DragDropEffects.None, moveOnly.Effect);
        Raise("OnDragDrop", moveOnly);
        string third = Path.Combine(directory, "third");
        File.WriteAllText(third, "keep");
        string priorStatus = Find<Label>(form, "Status").Text;
        foreach (string[] invalidPaths in new[]
        {
            new[] { directory }, new[] { third, directory },
            new[] { third, Path.Combine(directory, "missing") }
        })
        {
            DragEventArgs invalid = Drag(invalidPaths);
            Raise("OnDragEnter", invalid);
            Assert.Equal(DragDropEffects.None, invalid.Effect);
            Raise("OnDragDrop", invalid);
            Assert.Equal(2, table.Rows.Count);
            Assert.Equal(priorStatus, Find<Label>(form, "Status").Text);
        }
        using var release = new ManualResetEventSlim();
        Task work = session.RunAsync(() => release.Wait(TimeSpan.FromSeconds(10)), _ => { });
        try
        {
            DragEventArgs busy = Drag([third]);
            Raise("OnDragEnter", busy);
            Assert.Equal(DragDropEffects.None, busy.Effect);
            Raise("OnDragDrop", busy);
            Assert.Equal(2, table.Rows.Count);
        }
        finally
        {
            release.Set();
            WaitUntil(() => work.IsCompleted, timeoutSeconds: 10);
            work.GetAwaiter().GetResult();
        }
        session.AcceptReference(FileComparer.ReadInputs([WriteList("valid", null, [new ManifestFile("file", 1)])]));
        Raise("OnDragDrop", Drag([third]));
        Assert.Equal(3, table.CheckedInputs.Length);
        Assert.Null(session.Reference);
        Assert.Equal("not parsed on drop", File.ReadAllText(first));
        Assert.Equal("keep", File.ReadAllText(third));
    });

    [Fact]
    public void BusyTableKeepsInformationReadOnlyAndBlocksSelectAll() => OnSta(() =>
    {
        using var table = new ManifestTable("test");
        table.AddFiles([new LocalManifestFile("file", @"D:\file", 1, DateTime.UtcNow)]);
        table.SetBusy(true);
        table.ToggleAll();
        Assert.Empty(table.CheckedInputs);
        Assert.False(Find<CheckBox>(table, "SelectAllManifests").Enabled);
        table.SetBusy(false);
        Assert.False(table.Columns[0].ReadOnly);
        Assert.All(table.Columns.Cast<DataGridViewColumn>().Skip(1), column => Assert.True(column.ReadOnly));
        table.ToggleAll();
        Assert.Single(table.CheckedInputs);
    });

    [Fact]
    public void DifferenceRenderWithUnchangedDataKeepsNativeNodes() => OnSta(() =>
    {
        WizardSession session = Ready();
        using var panel = new DifferencePanel(session);
        TreeView tree = Find<TreeView>(panel, "GameTree");
        TreeNode first = tree.Nodes[0];
        panel.Render();
        Assert.Same(first, tree.Nodes[0]);
        session.InvalidateComparison();
        panel.Render();
        Assert.Empty(tree.Nodes.Cast<TreeNode>());
    });

    [Fact]
    public void GettingLocalManifestsDoesNotTogglePageContainersOrRefreshHiddenResults() => OnSta(() =>
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "candidate"), "metadata only");
        var session = ReferenceSession();
        using var form = new MainForm(session)
        {
            StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false
        };
        form.Show();
        Find<RadioButton>(form, "LocalSource").Checked = true;
        Find<TextBox>(form, "LocalManifestDirectory").Text = directory;
        int containerChanges = 0, hiddenUpdates = 0;
        void Watch(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is Panel or UserControl)
                    child.EnabledChanged += (_, _) => containerChanges++;
                Watch(child);
            }
        }
        Watch(form);
        Find<Label>(form, "ComparisonReferenceFiles").TextChanged += (_, _) => hiddenUpdates++;
        Find<Button>(form, "GetLocalManifests").PerformClick();
        PumpUntilLocalGetFinishes(form, session);
        Assert.Equal(0, containerChanges);
        Assert.Equal(0, hiddenUpdates);
        Assert.Single(Find<ManifestTable>(form, "LocalManifests").Rows.Cast<DataGridViewRow>());
        form.Close();
    });

    [Fact]
    public void StatusTextFitsItsAllocatedRowWhileProgressChanges() => OnSta(() =>
    {
        using var form = new MainForm(ReferenceSession())
        {
            StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false,
            Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 14)
        };
        form.Show();
        form.ClientSize = new Size(784, 700);
        Label label = Find<Label>(form, "Status");
        ProgressBar bar = Find<ProgressBar>(form, "OperationProgress");
        bar.Visible = true;
        bar.Style = ProgressBarStyle.Continuous;
        for (int i = 0; i < 30; i++)
        {
            bar.Value = i;
            label.Text = $"正在解析：已处理 {i:N0}/1,746 份文件（最多 16 线程）…正在等待正在执行的解析退出，请稍候。";
            form.PerformLayout();
            Size needed = label.GetPreferredSize(new Size(label.Width, 0));
            Assert.True(label.Height >= needed.Height, $"status height={label.Height}, needed={needed.Height}, bounds={label.Bounds}");
            Assert.True(label.Top >= bar.Bottom);
        }
        form.Close();
    });

    [Fact]
    public void ParsingThreadOptionsAreNotShown() => OnSta(() =>
    {
        using var form = new MainForm();
        Assert.Empty(form.Controls.Find("ManualParsingThreads", true));
        Assert.Empty(form.Controls.Find("LocalParsingThreads", true));
        Assert.Equal(1, ReferenceParser.DefaultConcurrency);
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ParsingUsesOneWorkerAndDisplaysCompletedFileProgress(bool local, bool cancel) => OnSta(() =>
    {
        Directory.CreateDirectory(directory);
        using var enteredSecond = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int calls = 0;
        var parser = new ReferenceParser((input, token) =>
        {
            if (Interlocked.Increment(ref calls) == 2)
            {
                enteredSecond.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10), token)) throw new TimeoutException();
            }
            return ManifestConverter.Read(input.File.FullPath, token);
        });
        var session = ReferenceSession();
        using var form = new MainForm(session, parser)
        {
            StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false
        };
        form.Show();
        Find<RadioButton>(form, local ? "LocalSource" : "ManualSource").Checked = true;
        var table = Find<ManifestTable>(form, local ? "LocalManifests" : "Manifests");
        for (int i = 0; i < 3; i++)
        {
            string path = WriteList("progress-" + i, null, [new ManifestFile("file", 3)]);
            var file = new FileInfo(path);
            table.AddFiles([new LocalManifestFile(file.Name, path, file.Length, file.LastWriteTimeUtc)], check: true);
        }
        try
        {
            Find<Button>(form, local ? "ParseLocalReference" : "ParseManualReference").PerformClick();
            ProgressBar bar = Find<ProgressBar>(form, "OperationProgress");
            WaitUntil(() => enteredSecond.IsSet && bar.Value == 1, timeoutSeconds: 10);
            Assert.True(enteredSecond.IsSet);
            Assert.Equal(ProgressBarStyle.Continuous, bar.Style);
            Assert.Equal(3, bar.Maximum);
            Assert.Equal(1, bar.Value);
            Assert.Equal(2, Volatile.Read(ref calls));
            Assert.Contains("1/3", Find<Label>(form, "Status").Text);
            Assert.DoesNotContain("线程", Find<Label>(form, "Status").Text);
            if (cancel) Find<Button>(form, "CancelParsing").PerformClick();
            else release.Set();
            PumpUntilLocalGetFinishes(form, session);
            Assert.Equal(cancel ? 1 : 3, bar.Value);
            Assert.Equal(cancel ? 2 : 3, calls);
            Assert.Equal(!cancel, session.Reference is not null);
            Assert.False(bar.Visible);
        }
        finally { release.Set(); }
        form.Close();
    });

    [Fact]
    public void ParsingCancellationDropsOldReferenceAndRestoresUiForRetry() => OnSta(() =>
    {
        Directory.CreateDirectory(directory);
        string path = WriteList("reference", null, [new ManifestFile("file", 3)]);
        var session = ReferenceSession();
        WeakReference oldReference = InstallOldReference(session, path);
        using var entered = new ManualResetEventSlim();
        int calls = 0;
        var parser = new ReferenceParser((input, token) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                entered.Set();
                if (!token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10))) throw new TimeoutException();
                token.ThrowIfCancellationRequested();
            }
            return ManifestConverter.Read(input.File.FullPath, token);
        });
        using var form = new MainForm(session, parser)
        {
            StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false
        };
        form.Show();
        var table = Find<ManifestTable>(form, "Manifests");
        var file = new FileInfo(path);
        table.AddFiles([new LocalManifestFile(file.Name, path, file.Length, file.LastWriteTimeUtc)], check: true);
        Find<Button>(form, "ParseManualReference").PerformClick();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        Assert.Null(session.Reference);
        Assert.Null(session.Comparison);
        // A collection is only used here to verify ownership, never as application memory management.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(oldReference.IsAlive, "重新解析开始后仍持有旧参考快照");
        Assert.Contains("尚无有效参考", Find<Label>(form, "ReferenceResult").Text);
        Assert.False(Find<Button>(form, "Next").Enabled);
        Button cancel = Find<Button>(form, "CancelParsing");
        Assert.True(cancel.Visible && cancel.Enabled);
        Assert.False(Find<RadioButton>(form, "LocalSource").Enabled);
        form.Close();
        Assert.False(form.IsDisposed);
        Assert.True(session.IsBusy);
        Assert.False(session.IsCancellationRequested);
        Assert.False(session.CloseRequested);
        cancel.PerformClick();
        Assert.False(cancel.Enabled);
        PumpUntilLocalGetFinishes(form, session);
        Assert.False(cancel.Visible);
        Assert.Null(session.Reference);
        Assert.Equal("已取消", table.Rows[0].Cells["State"].Value);
        Assert.Contains("已取消解析", Find<Label>(form, "Status").Text);
        Find<Button>(form, "ParseManualReference").PerformClick();
        PumpUntilLocalGetFinishes(form, session);
        Assert.NotNull(session.Reference);
        Assert.True(Find<Button>(form, "Next").Enabled);
        Assert.False(cancel.Visible);
        form.Close();
    });

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference InstallOldReference(WizardSession session, string path)
    {
        FileListSnapshot snapshot = ManifestConverter.Read(path, TestContext.Current.CancellationToken);
        session.AcceptReference(snapshot);
        return new WeakReference(snapshot);
    }

    [Fact]
    public void RapidWidthChangesKeepWrappedReferenceNotesAboveTables() => OnSta(() =>
    {
        using var form = new MainForm(ReferenceSession())
        {
            StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false
        };
        form.Show();
        foreach (bool local in new[] { true, false })
        {
            Find<RadioButton>(form, local ? "LocalSource" : "ManualSource").Checked = true;
            var table = Find<ManifestTable>(form, local ? "LocalManifests" : "Manifests");
            for (int i = 0; i < 120; i++)
            {
                form.ClientSize = new Size(i % 2 == 0 ? 1500 : 784, 700);
                if (i % 7 == 0) Application.DoEvents();
                Control parent = table.Parent!;
                Control input = parent.Controls.Cast<Control>().Single(control => control != table);
                Assert.True(table.Top >= input.Bottom,
                    $"local={local}, width={form.Width}, tableTop={table.Top}, inputBottom={input.Bottom}");
                foreach (Label label in input.Controls.OfType<Label>())
                {
                    Size preferred = label.GetPreferredSize(new Size(label.Width, 0));
                    Assert.True(label.Height >= preferred.Height,
                        $"text={label.Text}; actual={label.Size}; preferred={preferred}");
                    Assert.True(label.Bottom <= input.Height,
                        $"local={local}, iteration={i}, client={form.ClientSize}, name={label.Name}, text={label.Text}, visible={label.Visible}, bounds={label.Bounds}, font={label.Font}, input={input.Bounds}, labelBottom={label.Bottom}, inputHeight={input.Height}");
                }
            }
        }
        form.Close();
    });

    [Fact]
    public void ManifestTablesUseDifferentDefaultSortsAndOnlyLocalTimeSortHasHourBands() => OnSta(() =>
    {
        using var form = new MainForm();
        var manual = Find<ManifestTable>(form, "Manifests");
        var local = Find<ManifestTable>(form, "LocalManifests");
        DateTime hour = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Local).ToUniversalTime();
        LocalManifestFile[] files = [
            new("z", @"D:\z", 1, hour.AddMinutes(5)),
            new("a", @"D:\a", 1, hour.AddHours(-1)),
            new("m", @"D:\m", 1, hour.AddMinutes(40)),
            new("b", @"D:\b", 1, hour.AddDays(-1))];
        manual.AddFiles(files);
        local.AddFiles(files);
        Assert.Equal("Name", manual.SortedColumn!.Name);
        Assert.Equal(SortOrder.Ascending, manual.SortOrder);
        Assert.Equal(new[] { "a", "b", "m", "z" }, manual.Rows.Cast<DataGridViewRow>().Select(row => row.Cells["Name"].Value));
        Assert.Equal("Modified", local.SortedColumn!.Name);
        Assert.Equal(SortOrder.Descending, local.SortOrder);
        Color[] Colors(ManifestTable table)
        {
            var colors = new Color[table.Rows.Count];
            DataGridViewCellFormattingEventHandler capture = (_, args) => colors[args.RowIndex] = args.CellStyle!.BackColor;
            table.CellFormatting += capture;
            foreach (DataGridViewRow row in table.Rows) _ = row.Cells["Name"].FormattedValue;
            table.CellFormatting -= capture;
            return colors;
        }
        Color[] grouped = Colors(local);
        Assert.Equal(grouped[0], grouped[1]);
        Assert.NotEqual(grouped[1], grouped[2]);
        Assert.NotEqual(grouped[2], grouped[3]);
        local.Sort(local.Columns["Name"]!, System.ComponentModel.ListSortDirection.Ascending);
        Assert.Single(Colors(local).Distinct());
        manual.Sort(manual.Columns["Modified"]!, System.ComponentModel.ListSortDirection.Descending);
        Assert.Single(Colors(manual).Distinct());
        local.Sort(local.Columns["Modified"]!, System.ComponentModel.ListSortDirection.Ascending);
        Color[] ascending = Colors(local);
        Assert.NotEqual(ascending[0], ascending[1]);
        Assert.Equal(ascending[2], ascending[3]);
        local.Rows.Clear();
        local.AddFiles(files.Take(2));
        Assert.NotEqual(Colors(local)[0], Colors(local)[1]);
    });

    [Fact]
    public void HeaderCheckboxTracksThreeStatesAndTogglesWholeTableOnce() => OnSta(() =>
    {
        using var table = new ManifestTable("test");
        CheckBox header = Find<CheckBox>(table, "SelectAllManifests");
        Assert.False(header.Enabled);
        Assert.Equal(CheckState.Unchecked, header.CheckState);
        table.ToggleAll();
        table.AddFiles([
            new LocalManifestFile("a", @"D:\a", 1, DateTime.UtcNow),
            new LocalManifestFile("b", @"D:\b", 2, DateTime.UtcNow)]);
        int changes = 0;
        table.CheckedFilesChanged += () => changes++;
        Assert.True(header.Enabled);
        table.Rows[0].Cells[0].Value = true;
        Assert.Equal(CheckState.Indeterminate, header.CheckState);
        Assert.Equal(1, changes);
        table.ToggleAll();
        Assert.Equal(CheckState.Checked, header.CheckState);
        Assert.Equal(2, table.CheckedInputs.Length);
        Assert.Equal(2, changes);
        table.ToggleAll();
        Assert.Equal(CheckState.Unchecked, header.CheckState);
        Assert.Empty(table.CheckedInputs);
        Assert.Equal(3, changes);
        table.AddFiles([new LocalManifestFile("c", @"D:\c", 3, DateTime.UtcNow)], check: true);
        Assert.Equal(CheckState.Indeterminate, header.CheckState);
        table.Rows.Clear();
        Assert.False(header.Enabled);
        Assert.Equal(CheckState.Unchecked, header.CheckState);
    });

    [Fact]
    public void SwitchingSourceParsesOnlyActiveTableEvenWhenBothKeepCheckedRows() => OnSta(() =>
    {
        Directory.CreateDirectory(directory);
        var session = ReferenceSession();
        using var form = new MainForm(session)
        {
            StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false
        };
        form.Show();
        void AddJson(string tableName, string name)
        {
            string path = WriteList(name, null, [new ManifestFile(name, 1)]);
            var file = new FileInfo(path);
            Find<ManifestTable>(form, tableName).AddFiles([
                new LocalManifestFile(file.Name, path, file.Length, file.LastWriteTimeUtc)], check: true);
        }
        AddJson("Manifests", "manual-file");
        AddJson("LocalManifests", "local-file");
        Find<RadioButton>(form, "ManualSource").Checked = true;
        Find<Button>(form, "ParseManualReference").PerformClick();
        PumpUntilLocalGetFinishes(form, session);
        Assert.Equal(new[] { "manual-file" }, session.Reference!.Files.Keys);
        Find<RadioButton>(form, "LocalSource").Checked = true;
        Assert.Null(session.Reference);
        Assert.All(Find<ManifestTable>(form, "Manifests").Rows.Cast<DataGridViewRow>(), row =>
        {
            Assert.Equal("未解析", row.Cells["State"].Value);
            Assert.Null(row.Cells["Entries"].Value);
        });
        Assert.False(Find<Button>(form, "Next").Enabled);
        Find<Button>(form, "ParseLocalReference").PerformClick();
        PumpUntilLocalGetFinishes(form, session);
        Assert.Equal(new[] { "local-file" }, session.Reference!.Files.Keys);
        Assert.Single(Find<ManifestTable>(form, "Manifests").CheckedInputs);
        Assert.True(Find<Button>(form, "Next").Enabled,
            $"busy={session.IsBusy}; advance={session.CanAdvance}; step={session.Step}; local={Find<RadioButton>(form, "LocalSource").Checked}; network={Find<RadioButton>(form, "NetworkSource").Checked}; status={Find<Label>(form, "Status").Text}");
        form.Close();
    });

    [Fact]
    public void JsonRowsWithSameSideConflictDoNotEnableNext() => OnSta(() =>
    {
        Directory.CreateDirectory(directory);
        var session = ReferenceSession();
        using var form = new MainForm(session)
        {
            StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false
        };
        form.Show();
        Find<RadioButton>(form, "ManualSource").Checked = true;
        var table = Find<ManifestTable>(form, "Manifests");
        foreach (int size in new[] { 3, 4 })
        {
            string path = WriteList("conflict-" + size, null, [new ManifestFile("file", size)]);
            var file = new FileInfo(path);
            table.AddFiles([new LocalManifestFile(file.Name, path, file.Length, file.LastWriteTimeUtc)], check: true);
        }
        Find<Button>(form, "ParseManualReference").PerformClick();
        PumpUntilLocalGetFinishes(form, session);
        Assert.Null(session.Reference);
        Assert.False(Find<Button>(form, "Next").Enabled);
        Assert.Contains("同侧冲突", Find<Label>(form, "Status").Text);
        Assert.All(table.Rows.Cast<DataGridViewRow>(), row => Assert.Equal("已解析", row.Cells["State"].Value));
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
        ManifestConverter.Convert(raw, converted);
        string scanned = Path.Combine(directory, "scan.json");
        DirectoryScanner.Scan(root, scanned, cancellationToken: TestContext.Current.CancellationToken);
        var session = new WizardSession();
        session.AcceptGame(FileComparer.ReadInputs([scanned]), root);
        session.Next();
        session.AcceptReference(FileComparer.ReadInputs([converted]));
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

    [Fact]
    public void ScanProgressIsHiddenBeforeReadingAndWhileImporting() => OnSta(() =>
    {
        WizardSession session = Ready();
        using var form = new MainForm(session)
        {
            StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false
        };
        form.Show();
        Assert.False(Find<ProgressBar>(form, "ScanFileProgress").Visible);
        Assert.False(Find<ProgressBar>(form, "ScanByteProgress").Visible);
        Find<RadioButton>(form, "ImportGameSource").Checked = true;
        Assert.False(Find<ProgressBar>(form, "ScanFileProgress").Visible);
        Assert.False(Find<ProgressBar>(form, "ScanByteProgress").Visible);
    });

    [Theory]
    [InlineData(false, 650)]
    [InlineData(true, 650)]
    [InlineData(false, 1000)]
    [InlineData(true, 1000)]
    public void ScanProgressOnlyShowsDuringScanningWithoutDuplicateCounters(bool hashes, int height) => OnSta(() =>
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "file"), "abc");
        WriteGameConfig(directory);
        WizardSession session = Ready();
        using var form = new MainForm(session)
        {
            StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false,
            Size = new Size(800, height)
        };
        form.Show();
        Find<TextBox>(form, "GameRoot").Text = directory;
        Find<CheckBox>(form, "HeavyScan").Checked = hashes;
        ReadGamePresetAndWait(form, session);
        ProgressBar file = Find<ProgressBar>(form, "ScanFileProgress"), bytes = Find<ProgressBar>(form, "ScanByteProgress");
        Button scan = Find<Button>(form, "ReadGame");
        int busyChecks = 0;
        bool fileVisible = false, bytesVisible = false, barsFit = false, listsFit = false;
        string[] labels = [];
        scan.EnabledChanged += (_, _) =>
        {
            if (scan.Enabled) return;
            busyChecks++;
            fileVisible = file.Visible;
            bytesVisible = bytes.Visible;
            labels = file.Parent!.Controls.OfType<Label>().Select(label => label.Text).ToArray();
            Rectangle client = form.RectangleToScreen(form.ClientRectangle);
            barsFit = new[] { file, bytes }.All(bar => client.Contains(bar.RectangleToScreen(bar.ClientRectangle)));
            listsFit = new[] { "ExcludedDirectories", "ExcludedFiles" }.All(name =>
                Find<ListBox>(form, name).RectangleToScreen(Find<ListBox>(form, name).ClientRectangle).Bottom <=
                file.RectangleToScreen(Rectangle.Empty).Top);
        };
        scan.PerformClick();
        WaitForPageWork(form, session, "ReadGame");
        Assert.Equal(1, busyChecks);
        Assert.True(fileVisible);
        Assert.True(bytesVisible);
        Assert.True(barsFit);
        Assert.True(listsFit);
        Assert.Equal(new[] { "文件数量", "文件大小" }, labels);
        Assert.NotNull(session.Game);
        Assert.False(file.Visible);
        Assert.False(bytes.Visible);
        Assert.Contains(hashes ? "MD5 重扫完成" : "轻扫（未校验内容）完成", Find<Label>(form, "Status").Text);
        Find<Button>(form, "Next").PerformClick();
        Find<Button>(form, "Back").PerformClick();
        Assert.False(file.Visible);
        Assert.False(bytes.Visible);
    });

    [Fact]
    public void FailedRescanClearsBothPreviousCompletedBars() => OnSta(() =>
    {
        Directory.CreateDirectory(directory);
        string root = Path.Combine(directory, "game");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "file"), "abc");
        WriteGameConfig(root);
        var session = new WizardSession();
        session.AcceptReference(FileComparer.ReadInputs([WriteList("reference", null, [new ManifestFile("file", 3)])]));
        using var form = new MainForm(session)
        {
            StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false
        };
        form.Show();
        Find<TextBox>(form, "GameRoot").Text = root;
        Button scan = Find<Button>(form, "ReadGame");
        ReadGamePresetAndWait(form, session);
        scan.PerformClick();
        WaitForPageWork(form, session, "ReadGame");
        Assert.Equal(100, Find<ProgressBar>(form, "ScanFileProgress").Value);
        Assert.Equal(100, Find<ProgressBar>(form, "ScanByteProgress").Value);
        Assert.False(Find<ProgressBar>(form, "ScanFileProgress").Visible);
        Assert.False(Find<ProgressBar>(form, "ScanByteProgress").Visible);
        Directory.Move(root, root + "-moved");
        scan.PerformClick();
        WaitForPageWork(form, session, "ReadGame");
        Assert.Null(session.Game);
        Assert.Contains("处理失败", Find<Label>(form, "Status").Text);
        Assert.Equal(0, Find<ProgressBar>(form, "ScanFileProgress").Value);
        Assert.Equal(0, Find<ProgressBar>(form, "ScanByteProgress").Value);
        Assert.False(Find<ProgressBar>(form, "ScanFileProgress").Visible);
        Assert.False(Find<ProgressBar>(form, "ScanByteProgress").Visible);
        form.Close();
    });

    [Fact]
    public void NavigationShowsCurrentResultStatusAndProgressBelongsToPages() => OnSta(() =>
    {
        WizardSession session = Ready();
        using var form = new MainForm(session)
        {
            StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false
        };
        form.Show();
        Label status = Find<Label>(form, "Status");
        ProgressBar parsing = Find<ProgressBar>(form, "OperationProgress");
        ProgressBar comparison = Find<ProgressBar>(form, "ComparisonProgress");
        Assert.NotSame(status.Parent, parsing.Parent);
        Assert.NotSame(status.Parent, comparison.Parent);
        Assert.NotSame(parsing.Parent, comparison.Parent);
        status.Text = "上一个页面的解析完成提示";
        Find<Button>(form, "Next").PerformClick();
        Assert.Contains("参考", status.Text);
        Assert.DoesNotContain("上一个页面", status.Text);
        Assert.False(parsing.Visible);
        Find<Button>(form, "Next").PerformClick();
        Assert.Contains("比较", status.Text);
        Assert.DoesNotContain("轻扫", status.Text);
        Find<Button>(form, "Next").PerformClick();
        Assert.Contains("演示", status.Text);
        Find<Button>(form, "Back").PerformClick();
        Assert.Contains("比较", status.Text);
        Find<Button>(form, "Back").PerformClick();
        Assert.Contains("参考", status.Text);
        Find<Button>(form, "Back").PerformClick();
        Find<CheckBox>(form, "HeavyScan").Checked = true;
        Assert.Contains("尚无有效结果", status.Text);
        form.Close();
    });

    [Fact]
    public void FailedRuleImportPreservesListsAndGameAndUnlocksInputs() => OnSta(() =>
    {
        WizardSession session = Ready();
        FileListSnapshot game = session.Game!;
        using var form = new MainForm(session)
        {
            StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false
        };
        form.Show();
        Find<TextBox>(form, "GameRoot").Text = game.Sources[0].Scan!.Root;
        session.AcceptGame(game, game.Sources[0].Scan!.Root);
        ListBox dirs = Find<ListBox>(form, "ExcludedDirectories"), files = Find<ListBox>(form, "ExcludedFiles");
        dirs.Items.Add("existing-dir");
        files.Items.Add("existing-file");
        string path = Path.Combine(directory, "invalid-rules.json");
        File.WriteAllText(path, """{"excludedDirectories":["new-dir"],"excludedFiles":["../outside"]}""");
        Find<TextBox>(form, "ExclusionJson").Text = path;
        Find<Button>(form, "ImportExclusionRules").PerformClick();
        WaitForPageWork(form, session, "ImportExclusionRules");
        Assert.Equal("existing-dir", Assert.Single(dirs.Items.Cast<string>()));
        Assert.Equal("existing-file", Assert.Single(files.Items.Cast<string>()));
        Assert.Same(game, session.Game);
        Assert.Contains("原列表未改变", Find<Label>(form, "Status").Text);
        Assert.True(dirs.Enabled && files.Enabled && Find<TextBox>(form, "GameRoot").Enabled);
        Assert.True(Find<Button>(form, "Next").Enabled);
        form.Close();
    });

    [Fact]
    public void ExportedExclusionsRoundTripAndRepeatedImportsDeduplicate() => OnSta(() =>
    {
        WizardSession session = Ready();
        string root = session.Game!.Sources[0].Scan!.Root;
        string path = Path.Combine(directory, "exported-exclusions.json");
        using var form = new MainForm(session, chooseExclusionOutput: _ => path)
        {
            StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false
        };
        form.Show();
        Find<TextBox>(form, "GameRoot").Text = root;
        ListBox dirs = Find<ListBox>(form, "ExcludedDirectories"), files = Find<ListBox>(form, "ExcludedFiles");
        dirs.Items.Add("Cache");
        files.Items.Add("settings.ini");
        Button import = Find<Button>(form, "ImportExclusionRules"), export = Find<Button>(form, "ExportExclusionRules");
        var row = Assert.IsType<TableLayoutPanel>(import.Parent);
        Assert.Same(row, export.Parent);
        Assert.Equal(row.GetColumn(import) + 1, row.GetColumn(export));
        export.PerformClick();
        WaitForPageWork(form, session, "ExportExclusionRules");
        ExclusionRules rules = ExclusionRules.Read(path, root);
        Assert.Equal("Cache", Assert.Single(rules.Directories));
        Assert.Equal("settings.ini", Assert.Single(rules.Files));
        dirs.Items.Clear();
        files.Items.Clear();
        dirs.Items.Add("cache");
        files.Items.Add("SETTINGS.INI");
        Find<TextBox>(form, "ExclusionJson").Text = path;
        for (int i = 0; i < 2; i++)
        {
            import.PerformClick();
            WaitForPageWork(form, session, "ImportExclusionRules");
            Assert.Equal("cache", Assert.Single(dirs.Items.Cast<string>()));
            Assert.Equal("SETTINGS.INI", Assert.Single(files.Items.Cast<string>()));
        }
        string original = File.ReadAllText(path);
        export.PerformClick();
        WaitForPageWork(form, session, "ExportExclusionRules");
        Assert.Equal(original, File.ReadAllText(path));
        Assert.Contains("另选文件名", Find<Label>(form, "Status").Text);
        form.Close();
    });

    [Fact]
    public void HistoricalImportNavigationUsesRecordedStatusAndFailureCannotRestoreOldResult() => OnSta(() =>
    {
        WizardSession session = Ready();
        string root = session.Game!.Sources[0].Scan!.Root;
        using var form = new MainForm(session)
        {
            StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false
        };
        form.Show();
        Assert.False(Directory.Exists(root));
        Find<RadioButton>(form, "ImportGameSource").Checked = true;
        Find<TextBox>(form, "GameRoot").Text = root;
        Find<TextBox>(form, "GameJson").Text = Path.Combine(directory, "game.json");
        bool showedScanProgress = false;
        foreach (string name in new[] { "ScanFileProgress", "ScanByteProgress" })
        {
            ProgressBar bar = Find<ProgressBar>(form, name);
            bar.VisibleChanged += (_, _) => showedScanProgress |= bar.Visible;
        }
        Find<Button>(form, "ReadGame").PerformClick();
        WaitForPageWork(form, session, "ReadGame");
        Assert.NotNull(session.Game);
        Label status = Find<Label>(form, "Status");
        Assert.Contains("已导入历史扫描", status.Text);
        Assert.Contains("未重新扫描", status.Text);
        Find<Button>(form, "Next").PerformClick();
        Find<Button>(form, "Back").PerformClick();
        Assert.Contains("已导入历史扫描", status.Text);
        Assert.Contains("未重新扫描", status.Text);
        Assert.Equal(0, Find<ProgressBar>(form, "ScanFileProgress").Value);
        Assert.False(Find<ProgressBar>(form, "ScanFileProgress").Visible);
        Assert.False(Find<ProgressBar>(form, "ScanByteProgress").Visible);
        Find<TextBox>(form, "GameJson").Text = Path.Combine(directory, "missing.json");
        Find<Button>(form, "ReadGame").PerformClick();
        WaitForPageWork(form, session, "ReadGame");
        Assert.Null(session.Game);
        Assert.Contains("处理失败", status.Text);
        Assert.False(Find<Button>(form, "Next").Enabled);
        Assert.False(Find<Button>(form, "Back").Enabled);
        Assert.Contains("处理失败", status.Text);
        Assert.DoesNotContain("已导入历史扫描", status.Text);
        Assert.False(showedScanProgress);
        form.Close();
    });

    private static void WaitForPageWork(MainForm form, WizardSession session, string button)
    {
        WaitUntil(() => !session.IsBusy && Find<Button>(form, button).Enabled
            && !Find<Label>(form, "Status").Text.StartsWith("正在", StringComparison.Ordinal));
        Application.DoEvents();
        Assert.False(session.IsBusy);
        Assert.True(Find<Button>(form, button).Enabled);
        Assert.DoesNotContain("正在", Find<Label>(form, "Status").Text);
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

}
