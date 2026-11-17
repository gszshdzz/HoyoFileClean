using System.Reflection;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace HoyoFileClean.Desktop.Tests;

public sealed class DifferencePanelTests
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessage(nint handle, uint message, nint wParam, nint lParam);

    private static WizardSession Ready(bool known = true, string detection = "exe_hk4e_cn", string executable = "YuanShen.exe", bool extraLocal = false)
    {
        string root = Path.Combine(TestTemporaryFiles.Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "folder", "nested"));
        File.WriteAllText(Path.Combine(root, "pkg_version"), "metadata");
        File.WriteAllText(Path.Combine(root, "Audio_Chinese_pkg_version"), "stable");
        File.WriteAllText(Path.Combine(root, "folder", "nested", "local"), "local");
        if (extraLocal) File.WriteAllText(Path.Combine(root, "folder", "nested", "local-two"), "second");
        File.WriteAllText(Path.Combine(root, "same"), "same");
        var game = DirectoryScanner.Scan(root);
        string referencePath = Path.Combine(TestTemporaryFiles.Root, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(referencePath, JsonSerializer.Serialize(new FileListDocument(2, 8,
            [new ManifestFile("same", 4), new ManifestFile("folder/nested/reference", 4)]),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        var reference = FileComparer.ReadInputs([referencePath]);
        var session = new WizardSession();
        session.AcceptGame(game, root, known ? GameIdentifier.FromDetections(new Dictionary<string, string> { [detection] = executable }) : null);
        session.AcceptReference(reference);
        session.AcceptComparison(FileComparer.Compare(game, reference));
        return session;
    }

    [Theory]
    [InlineData("exe_hk4e_cn", "YuanShen.exe")]
    [InlineData("exe_hkrpg", "StarRail.exe")]
    [InlineData("exe_nap", "ZenlessZoneZero.exe")]
    [InlineData("exe_bh3", "BH3.exe")]
    public void StableVoiceMetadataIsNotHiddenAndPresetsDoNotRepeatScanExclusions(string detection, string executable) => NativeTestHost.OnSta(() =>
    {
        WizardSession session = Ready(detection: detection, executable: executable);
        Assert.False(session.IsResultHidden("Audio_Chinese_pkg_version"));
        Assert.Contains(session.VisibleRows, row => row.Path == "Audio_Chinese_pkg_version");
        var preset = GameIdentifier.GetExclusionPreset(session.Identification!)!;
        Assert.Empty((preset.ResultHiddenFiles ?? []).Intersect(preset.ScanFiles, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(["pkg_version"], preset.ResultHiddenFiles);
    });

    [Fact]
    public void FileTextCellsDoNotRemainSelected() => NativeTestHost.OnSta(() =>
    {
        using var panel = new DifferencePanel(Ready());
        var grid = NativeTestHost.Find<DataGridView>(panel, "DifferenceGrid");
        Assert.Equal(DataGridViewColumnHeadersHeightSizeMode.DisableResizing, grid.ColumnHeadersHeightSizeMode);
        grid.CurrentCell = grid.Rows[0].Cells[1];
        grid.Rows[0].Cells[1].Selected = true;
        Assert.Empty(grid.SelectedCells.Cast<DataGridViewCell>());
    });

    [Theory]
    [InlineData(2000)]
    [InlineData(100000)]
    public void FolderToggleDoesNotMaterializeAllFileRows(int count) => NativeTestHost.OnSta(() =>
    {
        string root = Path.Combine(TestTemporaryFiles.Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        ManifestFile[] files = Enumerable.Range(0, count).Select(i => new ManifestFile($"folder/file-{i:D6}", 1)).ToArray();
        string gamePath = Path.Combine(root, "game.json"), referencePath = Path.Combine(root, "reference.json");
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        File.WriteAllText(gamePath, JsonSerializer.Serialize(new FileListDocument(files.Length, files.Length, files,
            Scan: new ScanScope(root, [], [], [], FileValues.LightScan)), options));
        File.WriteAllText(referencePath, JsonSerializer.Serialize(new FileListDocument(0, 0, []), options));
        var session = new WizardSession();
        session.AcceptGame(FileComparer.ReadInputs([gamePath]), root);
        session.AcceptReference(FileComparer.ReadInputs([referencePath]));
        session.AcceptComparison(FileComparer.Compare(session.Game!, session.Reference!));
        using var panel = new DifferencePanel(session);
        var grid = NativeTestHost.Find<DataGridView>(panel, "DifferenceGrid");
        int additions = 0;
        grid.RowsAdded += (_, _) => additions++;
        var timer = Stopwatch.StartNew();
        for (int i = 0; i < 4; i++)
            typeof(DataGridView).GetMethod("OnCellClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(grid, [new DataGridViewCellEventArgs(i % 2 == 0 ? 1 : 3, 0)]);
        timer.Stop();
        Console.WriteLine($"result rows={count}; four folder toggles: {timer.ElapsedMilliseconds}ms, row-add events={additions}");
        Assert.Equal(count + 1, grid.Rows.Count);
        Assert.True(additions < 10, $"折叠逐行添加了 {additions} 次");
        timer.Restart();
        for (int i = 0; i < 50; i++)
        {
            ClickCell(grid, 0, 1);
            Assert.Equal(i % 2 == 0 ? 1 : 0, session.SelectedCount);
            Assert.Equal(i % 2 == 0 ? 1 : 0, session.SelectedSize);
        }
        timer.Stop();
        Console.WriteLine($"result rows={count}; 50 single-file toggles: {timer.ElapsedMilliseconds}ms");
    });

    [Fact]
    public void HiddenPresetOnlyChangesDisplayAndIsNotEditable() => NativeTestHost.OnSta(() =>
    {
        WizardSession session = Ready();
        using var panel = new DifferencePanel(session);
        var grid = NativeTestHost.Find<DataGridView>(panel, "DifferenceGrid");
        Assert.Contains("pkg_version", session.Game!.Files.Keys);
        Assert.Contains(session.Comparison!.OnlyA, file => file.Path == "pkg_version");
        Assert.DoesNotContain(Enumerable.Range(0, grid.RowCount), index => panel.FileAtRow(index) is { Path: "pkg_version" });
        NativeTestHost.Find<CheckBox>(panel, "ShowHiddenResults").Checked = true;
        var metadata = grid.Rows.Cast<DataGridViewRow>().Single(row => panel.FileAtRow(row.Index) is { Path: "pkg_version" });
        metadata.Cells[0].Value = true;
        Assert.Equal(1, session.SelectedCount);
        NativeTestHost.Find<CheckBox>(panel, "ShowHiddenResults").Checked = false;
        Assert.Equal(0, session.SelectedCount);
        Assert.All(grid.Columns.Cast<DataGridViewColumn>().Skip(1), column => Assert.True(column.ReadOnly));
        Assert.Empty(panel.Controls.Find("FilterAll", true));
        Assert.Contains("(2)", NativeTestHost.Find<Button>(panel, "FilterGameOnly").Text);
        NativeTestHost.Find<Button>(panel, "FilterEqual").PerformClick();
        Assert.Contains(grid.Rows.Cast<DataGridViewRow>(), row => Equals(row.Cells[2].Value, "="));
        Assert.Contains(Ready(false).VisibleRows, row => row.Path == "pkg_version");
    });

    [Fact]
    public void CategoryButtonsToggleIndependentlyAndDefaultToNonEqual() => NativeTestHost.OnSta(() =>
    {
        WizardSession session = Ready();
        using var panel = new DifferencePanel(session);
        Assert.Equal(4, session.Filters.Count);
        Assert.DoesNotContain(DifferenceCategory.Equal, session.Filters);
        Assert.DoesNotContain(session.VisibleRows, row => row.Category == DifferenceCategory.Equal);
        Assert.Empty(panel.Controls.Find("FilterAll", true));
        session.Select("folder/nested/local", true);
        NativeTestHost.Find<Button>(panel, "FilterReferenceOnly").PerformClick();
        Assert.Contains(DifferenceCategory.GameOnly, session.Filters);
        Assert.DoesNotContain(DifferenceCategory.ReferenceOnly, session.Filters);
        Assert.Equal(1, session.SelectedCount);
        NativeTestHost.Find<Button>(panel, "FilterEqual").PerformClick();
        Assert.Contains(DifferenceCategory.Equal, session.Filters);
        Assert.Contains(session.VisibleRows, row => row.Category == DifferenceCategory.Equal);
        Assert.Contains(session.VisibleRows, row => row.Category == DifferenceCategory.GameOnly);
        Assert.StartsWith("✓ ", NativeTestHost.Find<Button>(panel, "FilterEqual").Text);
        NativeTestHost.Find<Button>(panel, "FilterGameOnly").PerformClick();
        Assert.Equal(0, session.SelectedCount);
        foreach (DifferenceCategory category in session.Filters.ToArray())
            NativeTestHost.Find<Button>(panel, "Filter" + category).PerformClick();
        Assert.Empty(session.Filters);
        Assert.Empty(session.VisibleRows);
        Assert.Equal(0, NativeTestHost.Find<DataGridView>(panel, "DifferenceGrid").RowCount);
        NativeTestHost.Find<Button>(panel, "FilterReferenceOnly").PerformClick();
        Assert.Single(session.VisibleRows);
    });

    [Fact]
    public void EitherDirectoryCellCollapsesTheSameRows() => NativeTestHost.OnSta(() =>
    {
        using var panel = new DifferencePanel(Ready());
        var grid = NativeTestHost.Find<DataGridView>(panel, "DifferenceGrid");
        var folder = grid.Rows.Cast<DataGridViewRow>().Single(row => Equals(row.Cells[1].Value, "▼ folder/"));
        Assert.Equal(folder.Cells[1].Value, folder.Cells[3].Value);
        int count = grid.Rows.Count;
        typeof(DataGridView).GetMethod("OnCellClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(grid, [new DataGridViewCellEventArgs(3, folder.Index)]);
        Assert.True(grid.Rows.Count < count);
        folder = grid.Rows.Cast<DataGridViewRow>().Single(row => Equals(row.Cells[1].Value, "▶ folder/"));
        Assert.Equal(folder.Cells[1].Value, folder.Cells[3].Value);
        typeof(DataGridView).GetMethod("OnCellClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(grid, [new DataGridViewCellEventArgs(1, folder.Index)]);
        Assert.Equal(count, grid.Rows.Count);
    });

    [Fact]
    public void CheckboxClicksUpdateStateWithoutCellEditing() => NativeTestHost.OnSta(() =>
    {
        WizardSession session = Ready();
        using var form = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
            Location = new Point(-20000, -20000), Size = new Size(900, 600) };
        using var panel = new DifferencePanel(session);
        form.Controls.Add(panel);
        form.Show();
        Application.DoEvents();
        var grid = NativeTestHost.Find<DataGridView>(panel, "DifferenceGrid");
        var row = grid.Rows.Cast<DataGridViewRow>().Single(row => panel.FileAtRow(row.Index) is { Path: "folder/nested/local" });
        grid.CurrentCell = row.Cells[0];
        Assert.False(grid.BeginEdit(false));
        for (int i = 0; i < 10; i++)
        {
            // Exercise the actual cell click; readonly cells have no separate pending edit.
            typeof(DataGridView).GetMethod("OnCellClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(grid, [new DataGridViewCellEventArgs(0, row.Index)]);
            Assert.Equal(i % 2 == 0, session.IsSelected("folder/nested/local"));
            Assert.Equal(i % 2 == 0 ? CheckState.Checked : CheckState.Unchecked, row.Cells[0].Value);
            Assert.Equal(i % 2 == 0 ? 1 : 0, session.SelectedCount);
        }
        form.Close();
    });

    [Fact]
    public void ReloadingTheSameComparisonClearsCachedDirectoryAndHeaderSelection() => NativeTestHost.OnSta(() =>
    {
        WizardSession session = Ready();
        using var panel = new DifferencePanel(session);
        ComparisonResult comparison = session.Comparison!;
        session.Select("folder/nested/local", true);
        panel.Render();
        var header = NativeTestHost.Find<CheckBox>(panel, "SelectAllResults");
        Assert.Equal(CheckState.Indeterminate, header.CheckState);
        session.AcceptComparison(comparison);
        panel.Render();
        Assert.Equal(0, session.SelectedCount);
        Assert.Equal(0, session.SelectedSize);
        Assert.Equal(CheckState.Unchecked, header.CheckState);
        Assert.Equal(CheckState.Unchecked, NativeTestHost.Find<DataGridView>(panel, "DifferenceGrid").Rows[0].Cells[0].Value);
    });

    [Fact]
    public void DirectoryCheckboxIncludesCollapsedDescendantsAndReflectsPartialSelection() => NativeTestHost.OnSta(() =>
    {
        WizardSession session = Ready(extraLocal: true);
        using var panel = new DifferencePanel(session);
        var grid = NativeTestHost.Find<DataGridView>(panel, "DifferenceGrid");
        int local = Enumerable.Range(0, grid.RowCount).Single(index => panel.FileAtRow(index)?.Path == "folder/nested/local");
        ClickCell(grid, 0, local);
        Assert.Equal(CheckState.Indeterminate, grid.Rows[0].Cells[0].Value);
        ClickCell(grid, 1, 0);
        Assert.Equal(1, session.SelectedCount);
        ClickCell(grid, 0, 0);
        Assert.Equal(2, session.SelectedCount);
        Assert.Equal(CheckState.Checked, grid.Rows[0].Cells[0].Value);
        Assert.True(session.IsSelected("folder/nested/local-two"));
        Assert.False(session.IsSelected("folder/nested/reference"));
        Assert.Equal(CheckState.Indeterminate, NativeTestHost.Find<CheckBox>(panel, "SelectAllResults").CheckState);
        ClickCell(grid, 0, 0);
        Assert.Equal(0, session.SelectedCount);
        Assert.Equal(CheckState.Unchecked, grid.Rows[0].Cells[0].Value);
    });

    [Fact]
    public void HeaderCheckboxSelectsOnlyEligibleFilteredFilesAndDisablesEmptyScopes() => NativeTestHost.OnSta(() =>
    {
        WizardSession session = Ready();
        using var panel = new DifferencePanel(session);
        var header = NativeTestHost.Find<CheckBox>(panel, "SelectAllResults");
        ClickHeader(header);
        Assert.Equal(2, session.SelectedCount);
        Assert.Equal(CheckState.Checked, header.CheckState);
        Assert.False(session.IsSelected("pkg_version"));
        Assert.False(session.IsSelected("same"));
        Assert.False(session.IsSelected("folder/nested/reference"));
        ClickHeader(header);
        Assert.Equal(0, session.SelectedCount);
        session.Select("folder/nested/local", true);
        panel.Render();
        Assert.Equal(CheckState.Indeterminate, header.CheckState);
        NativeTestHost.Find<CheckBox>(panel, "ShowHiddenResults").Checked = true;
        ClickHeader(header);
        Assert.Equal(3, session.SelectedCount);
        Assert.True(session.IsSelected("pkg_version"));
        NativeTestHost.Find<Button>(panel, "FilterEqual").PerformClick();
        Assert.Equal(CheckState.Indeterminate, header.CheckState);
        ClickHeader(header);
        Assert.Equal(4, session.SelectedCount);
        Assert.True(session.IsSelected("same"));
        session.SetFilters([DifferenceCategory.ReferenceOnly, DifferenceCategory.OutsideScope]);
        panel.Render();
        Assert.False(header.Enabled);
        Assert.Equal(CheckState.Unchecked, header.CheckState);
        ClickHeader(header);
        ClickCell(NativeTestHost.Find<DataGridView>(panel, "DifferenceGrid"), 0, 0);
        Assert.Equal(0, session.SelectedCount);
    });

    private static void ClickCell(DataGridView grid, int column, int row) =>
        typeof(DataGridView).GetMethod("OnCellClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(grid, [new DataGridViewCellEventArgs(column, row)]);

    private static void ClickHeader(CheckBox header) =>
        typeof(CheckBox).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(header, [EventArgs.Empty]);

    [Fact]
    public void DoubleClickSequenceAndNativeSpacePressReleaseDoNotLeaveStaleSelection() => NativeTestHost.OnSta(() =>
    {
        WizardSession session = Ready();
        using var form = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
            Location = new Point(-20000, -20000), Size = new Size(900, 600) };
        using var panel = new DifferencePanel(session);
        form.Controls.Add(panel);
        form.Show();
        Application.DoEvents();
        var grid = NativeTestHost.Find<DataGridView>(panel, "DifferenceGrid");
        int local = Enumerable.Range(0, grid.RowCount).Single(index => panel.FileAtRow(index)?.Path == "folder/nested/local");
        ClickCell(grid, 0, local);
        Assert.Equal(1, session.SelectedCount);
        typeof(DataGridView).GetMethod("OnCellDoubleClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(grid, [new DataGridViewCellEventArgs(0, local)]);
        Assert.Equal(0, session.SelectedCount);
        Assert.Equal(CheckState.Unchecked, NativeTestHost.Find<CheckBox>(panel, "SelectAllResults").CheckState);
        grid.CurrentCell = grid.Rows[local].Cells[0];
        Assert.Same(grid.Rows[local].Cells[0], grid.CurrentCell);
        int changes = 0;
        session.SelectionChanged += (_, _) => changes++;
        for (int i = 0; i < 4; i++)
        {
            SendMessage(grid.Handle, 0x0100, (nint)Keys.Space, 0); // WM_KEYDOWN
            Assert.Equal(i % 2 == 0 ? 1 : 0, session.SelectedCount);
            Assert.Equal(i + 1, changes);
            SendMessage(grid.Handle, 0x0101, (nint)Keys.Space, 0); // WM_KEYUP must not toggle again.
            Assert.Equal(i % 2 == 0 ? 1 : 0, session.SelectedCount);
            Assert.Equal(i + 1, changes);
            Assert.Equal(i % 2 == 0 ? 5 : 0, session.SelectedSize);
            Assert.Equal(i % 2 == 0 ? CheckState.Checked : CheckState.Unchecked, grid.Rows[local].Cells[0].Value);
            Assert.Equal(i % 2 == 0 ? CheckState.Indeterminate : CheckState.Unchecked,
                NativeTestHost.Find<CheckBox>(panel, "SelectAllResults").CheckState);
        }
        form.Close();
    });

    [Fact]
    public void RepeatedCheckboxEditsAndRefreshKeepSummaryInSync() => NativeTestHost.OnSta(() =>
    {
        WizardSession session = Ready();
        using var panel = new DifferencePanel(session);
        var grid = NativeTestHost.Find<DataGridView>(panel, "DifferenceGrid");
        var row = grid.Rows.Cast<DataGridViewRow>().Single(row => panel.FileAtRow(row.Index) is { Path: "folder/nested/local" });
        for (int i = 0; i < 30; i++)
        {
            bool value = i % 2 == 0;
            row.Cells[0].Value = value;
            panel.Render();
            Assert.Equal(value, session.IsSelected("folder/nested/local"));
            Assert.Equal(value ? CheckState.Checked : CheckState.Unchecked, row.Cells[0].Value);
            Assert.Equal(value ? 1 : 0, session.SelectedCount);
            Assert.Equal(value ? 5 : 0, session.SelectedSize);
            Assert.Contains((value ? 1 : 0).ToString(), NativeTestHost.Find<Label>(panel, "SelectionSummary").Text);
        }
    });
}
