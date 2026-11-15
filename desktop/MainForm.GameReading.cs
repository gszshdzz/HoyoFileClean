using System.Text.Json;

namespace HoyoFileClean.Desktop;

public sealed partial class MainForm
{
    private readonly RadioButton scanGameSource = new() { Name = "ScanGameSource", Text = L.Get("ScanGameSourceLabel"), AutoSize = true, Checked = true };
    private readonly RadioButton importGameSource = new() { Name = "ImportGameSource", Text = L.Get("ImportJsonSourceLabel"), AutoSize = true };
    private readonly TextBox gameJson = new() { Name = "GameJson" };
    private readonly TextBox exclusionJson = new() { Name = "ExclusionJson" };
    private readonly TextBox excludedDirectoryInput = new() { Name = "ExcludedDirectoryInput" };
    private readonly TextBox excludedFileInput = new() { Name = "ExcludedFileInput" };
    private readonly ListBox excludedDirectories = new() { Name = "ExcludedDirectories", Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended, HorizontalScrollbar = true, IntegralHeight = false };
    private readonly ListBox excludedFiles = new() { Name = "ExcludedFiles", Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended, HorizontalScrollbar = true, IntegralHeight = false };
    private readonly ProgressBar scanFileProgress = new() { Name = "ScanFileProgress", Dock = DockStyle.Fill };
    private readonly ProgressBar scanByteProgress = new() { Name = "ScanByteProgress", Dock = DockStyle.Fill };
    private readonly TableLayoutPanel scanProgressPanel = new() { Dock = DockStyle.Top, AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 2, Visible = false };
    private Panel scanOptions = null!;
    private TableLayoutPanel gameJsonRow = null!;
    private readonly Button readGame = MakeButton(L.Get("ScanAction"), "ReadGame");
    private readonly Button cancelScan = MakeButton(L.Get("Button_CancelScan"), "CancelScan");
    private readonly Label identificationResult = new() { Name = "IdentificationResult", AutoSize = true, Dock = DockStyle.Top };

    private Panel BuildScanPage()
    {
        var input = CompactGameInputs();
        heavy.Text = L.Get("HeavyScanLabel");
        input.Controls.Add(Buttons(scanGameSource, importGameSource));
        Button browse = MakeButton(L.Get("Button_BrowseGame"), "BrowseGame");
        browse.Click += (_, _) => BrowseDirectory(gameRoot);
        input.Controls.Add(LabeledRow(L.Get("GameRootLabel"), gameRoot, browse));
        EnablePathDrop(gameRoot, directory: true);
        Button browseJson = MakeButton(L.Get("Button_BrowseGame"), "BrowseGameJson");
        browseJson.Click += (_, _) => BrowseFile(gameJson, json: true);
        gameJsonRow = LabeledRow(L.Get("HistoricalScanJsonLabel"), gameJson, browseJson);
        input.Controls.Add(gameJsonRow);
        EnablePathDrop(gameJson, directory: false);
        gameJson.TextChanged += (_, _) => GameChanged();
        readGame.Click += async (_, _) => await ExecuteAsync();
        cancelScan.Click += (_, _) =>
        {
            session.CancelOperation();
            RefreshView();
            status.Text = L.Get("CancellingScan");
        };
        input.Controls.Add(Buttons(readGame, heavy, cancelScan));
        input.Controls.Add(identificationResult);

        var options = CompactGameInputs();
        Button browseRules = MakeButton(L.Get("Button_BrowseGame"), "BrowseExclusionJson");
        browseRules.Click += (_, _) => BrowseFile(exclusionJson, json: true);
        Button importRules = MakeButton(L.Get("Button_ImportExclusionRules"), "ImportExclusionRules");
        importRules.Click += async (_, _) => await ImportExclusionRulesAsync();
        options.Controls.Add(LabeledRow(L.Get("ExclusionRulesJsonLabel"), exclusionJson, browseRules, importRules));
        EnablePathDrop(exclusionJson, directory: false);
        Label exclusionHint = Note(L.Get("ExclusionPathHint"));
        exclusionHint.Padding = Padding.Empty;
        options.Controls.Add(exclusionHint);

        var lists = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        lists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        lists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        lists.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        lists.Controls.Add(BuildExclusionEditor(true), 0, 0);
        lists.Controls.Add(BuildExclusionEditor(false), 1, 0);
        scanOptions = new Panel { Dock = DockStyle.Fill };
        scanOptions.Controls.Add(GameReadingRows([options, lists], 1));
        Label scanFileStatus = Note(L.Get("ScanFileCountLabel")), scanByteStatus = Note(L.Get("ScanByteCountLabel"));
        scanFileStatus.Padding = scanByteStatus.Padding = Padding.Empty;
        scanFileStatus.Margin = scanByteStatus.Margin = Padding.Empty;
        TableLayoutPanel results = scanProgressPanel;
        results.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        results.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        results.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        results.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        results.Controls.Add(scanFileStatus, 0, 0);
        results.Controls.Add(scanFileProgress, 1, 0);
        results.Controls.Add(scanByteStatus, 0, 1);
        results.Controls.Add(scanByteProgress, 1, 1);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(input, 0, 0);
        layout.Controls.Add(scanOptions, 0, 1);
        layout.Controls.Add(results, 0, 2);
        foreach (RadioButton option in new[] { scanGameSource, importGameSource })
            option.CheckedChanged += (_, _) => { if (option.Checked) GameChanged(); };
        var page = new Panel { Dock = DockStyle.Fill };
        page.Controls.Add(layout);
        return page;
    }

    private Panel BuildExclusionEditor(bool directory)
    {
        TextBox entry = directory ? excludedDirectoryInput : excludedFileInput;
        ListBox list = directory ? excludedDirectories : excludedFiles;
        string suffix = directory ? "Directory" : "File";
        Button browse = MakeButton(L.Get("Button_BrowseGame"), "BrowseExcluded" + suffix);
        browse.Click += (_, _) => { if (directory) BrowseDirectory(entry); else BrowseFile(entry); };
        Button add = MakeButton(L.Get("AddAction"), "AddExcluded" + suffix);
        add.Click += (_, _) => AddExclusion(entry, list);
        entry.KeyDown += (_, args) =>
        {
            if (args.KeyCode != Keys.Enter) return;
            args.SuppressKeyPress = true;
            AddExclusion(entry, list);
        };
        Button remove = MakeButton(L.Get("Button_RemoveManifest"), "RemoveExcluded" + suffix);
        remove.Click += (_, _) =>
        {
            if (session.IsBusy || list.SelectedItems.Count == 0) return;
            foreach (object item in list.SelectedItems.Cast<object>().ToArray()) list.Items.Remove(item);
            GameChanged();
        };
        EnablePathDrop(entry, directory);
        var input = CompactGameInputs();
        input.Controls.Add(Buttons(Note(directory ? L.Get("ExcludedDirectoriesLabel") : L.Get("ExcludedFilesLabel")), add, remove));
        input.Controls.Add(PathRow(entry, browse));
        EnableExclusionDrop(list, directory);
        return GameReadingRows([input, list], 1);
    }

    private static TableLayoutPanel GameReadingRows(Control[] controls, int fillRow = -1)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = controls.Length };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < controls.Length; i++)
        {
            layout.RowStyles.Add(new RowStyle(i == fillRow ? SizeType.Percent : SizeType.AutoSize, i == fillRow ? 100 : 0));
            layout.Controls.Add(controls[i], 0, i);
        }
        return layout;
    }

    private static TableLayoutPanel CompactGameInputs()
    {
        var panel = new TableLayoutPanel { ColumnCount = 1, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Margin = Padding.Empty };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ControlAdded += (_, _) =>
        {
            panel.RowCount = panel.Controls.Count;
            while (panel.RowStyles.Count < panel.RowCount) panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        };
        return panel;
    }

    private static TableLayoutPanel LabeledRow(string label, TextBox entry, params Button[] buttons)
    {
        TableLayoutPanel row = PathRow(entry, buttons);
        row.ColumnCount++;
        row.ColumnStyles.Insert(0, new ColumnStyle(SizeType.AutoSize));
        foreach (Control control in row.Controls.Cast<Control>().ToArray())
            row.SetColumn(control, row.GetColumn(control) + 1);
        row.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        return row;
    }

    private void EnableExclusionDrop(ListBox list, bool directory)
    {
        list.AllowDrop = true;
        string[]? Paths(DragEventArgs args) => !session.IsBusy && list.Enabled
            && (args.AllowedEffect & DragDropEffects.Copy) != 0
            && args.Data is ITypedDataObject data && data.TryGetData(DataFormats.FileDrop, out string[]? paths)
            && paths is { Length: > 0 } && paths.All(path => directory ? Directory.Exists(path) : File.Exists(path))
                ? paths : null;
        list.DragEnter += (_, args) => args.Effect = Paths(args) is null ? DragDropEffects.None : DragDropEffects.Copy;
        list.DragDrop += (_, args) =>
        {
            if (Paths(args) is not { } paths) return;
            try
            {
                // Materialize and validate the entire batch before touching the list.
                IReadOnlyList<string> normalized = DirectoryScanner.NormalizeExclusionPaths(gameRoot.Text.Trim(), paths);
                MergeExclusions(list, normalized);
                GameChanged();
            }
            catch (Exception error) when (error is CoreException or ArgumentException or NotSupportedException)
            { status.Text = L.Get("ExclusionDropRejected"); }
        };
    }

    private static TableLayoutPanel PathRow(TextBox entry, params Button[] buttons)
    {
        entry.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        var row = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = buttons.Length + 1, RowCount = 1 };
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.Controls.Add(entry, 0, 0);
        for (int i = 0; i < buttons.Length; i++)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.Controls.Add(buttons[i], i + 1, 0);
        }
        return row;
    }

    private void BrowseDirectory(TextBox target)
    {
        using var dialog = new FolderBrowserDialog { Description = L.Get("SelectDirectoryDialogTitle"), UseDescriptionForTitle = true,
            ShowNewFolderButton = false, SelectedPath = gameRoot.Text.Trim() };
        if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.SelectedPath;
    }

    private void BrowseFile(TextBox target, bool json = false)
    {
        using var dialog = new OpenFileDialog { Title = json ? L.Get("SelectJsonDialogTitle") : L.Get("SelectExcludedFileDialogTitle"),
            InitialDirectory = gameRoot.Text.Trim(), Filter = json ? L.Get("JsonFileFilter") : L.Get("AllFileFilter") };
        if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.FileName;
    }

    private void EnablePathDrop(TextBox target, bool directory)
    {
        target.AllowDrop = true;
        string? DroppedPath(DragEventArgs args) => !session.IsBusy && target.Enabled
            && (args.AllowedEffect & DragDropEffects.Copy) != 0
            && args.Data is ITypedDataObject data && data.TryGetData(DataFormats.FileDrop, out string[]? paths)
            && paths is { Length: 1 } && (directory ? Directory.Exists(paths[0]) : File.Exists(paths[0])) ? paths[0] : null;
        target.DragEnter += (_, args) => args.Effect = DroppedPath(args) is null ? DragDropEffects.None : DragDropEffects.Copy;
        target.DragDrop += (_, args) => { if (DroppedPath(args) is { } path) target.Text = path; };
    }

    private void AddExclusion(TextBox entry, ListBox list)
    {
        if (session.IsBusy) return;
        try
        {
            string[] paths = DirectoryScanner.NormalizeExclusionPaths(gameRoot.Text.Trim(), [entry.Text.Trim()]).ToArray();
            MergeExclusions(list, paths);
            entry.Clear();
            GameChanged();
        }
        catch (Exception error) when (error is CoreException or ArgumentException or NotSupportedException)
        { status.Text = L.Get("AddExclusionFailed"); }
    }

    private static void MergeExclusions(ListBox list, IEnumerable<string> paths)
    {
        var known = list.Items.Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths) if (known.Add(path)) list.Items.Add(path);
    }

    private async Task ImportExclusionRulesAsync()
    {
        if (session.IsBusy) return;
        string path = exclusionJson.Text.Trim(), root = gameRoot.Text.Trim();
        Task operation = session.RunAsync(() => ExclusionRules.Read(path, root), rules =>
        {
            MergeExclusions(excludedDirectories, rules.Directories);
            MergeExclusions(excludedFiles, rules.Files);
            GameChanged();
        });
        status.Text = L.Get("ImportingExclusionRules");
        RefreshView();
        try
        {
            await operation;
            status.Text = L.Get("ExclusionRulesImported");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException
            or CoreException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        { status.Text = L.Get("ExclusionRulesImportFailed"); }
        finally { RefreshView(); }
    }

    private void ResetScanProgress()
    {
        scanProgressPanel.Visible = false;
        scanFileProgress.Style = scanByteProgress.Style = ProgressBarStyle.Continuous;
        scanFileProgress.Value = scanByteProgress.Value = 0;
    }

    private void ShowScanProgress(ScanProgress value)
    {
        scanFileProgress.Style = scanByteProgress.Style = ProgressBarStyle.Continuous;
        scanFileProgress.Value = value.TotalFiles == 0 ? 100 : (int)Math.Clamp(100.0 * value.CompletedFiles / value.TotalFiles, 0, 100);
        scanByteProgress.Value = value.TotalBytes == 0 ? scanFileProgress.Value : (int)Math.Clamp(100.0 * value.ProcessedBytes / value.TotalBytes, 0, 100);
    }
}
