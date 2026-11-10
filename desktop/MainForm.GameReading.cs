using System.Text.Json;

namespace HoyoFileClean.Desktop;

public sealed partial class MainForm
{
    private readonly RadioButton scanGameSource = new() { Name = "ScanGameSource", Text = "扫描游戏", AutoSize = true, Checked = true };
    private readonly RadioButton importGameSource = new() { Name = "ImportGameSource", Text = "导入 JSON", AutoSize = true };
    private readonly TextBox gameJson = new() { Name = "GameJson" };
    private readonly TextBox exclusionJson = new() { Name = "ExclusionJson" };
    private readonly TextBox excludedDirectoryInput = new() { Name = "ExcludedDirectoryInput" };
    private readonly TextBox excludedFileInput = new() { Name = "ExcludedFileInput" };
    private readonly ListBox excludedDirectories = new() { Name = "ExcludedDirectories", Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended, HorizontalScrollbar = true, IntegralHeight = false };
    private readonly ListBox excludedFiles = new() { Name = "ExcludedFiles", Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended, HorizontalScrollbar = true, IntegralHeight = false };
    private readonly ProgressBar scanFileProgress = new() { Name = "ScanFileProgress", Dock = DockStyle.Fill };
    private readonly ProgressBar scanByteProgress = new() { Name = "ScanByteProgress", Dock = DockStyle.Fill };
    private readonly Label scanFileStatus = Note("文件数量：尚未读取");
    private readonly Label scanByteStatus = Note("文件大小：尚未读取");
    private Panel scanOptions = null!;
    private TableLayoutPanel gameJsonRow = null!;
    private readonly Button readGame = MakeButton("扫描", "ReadGame");

    private Panel BuildScanPage()
    {
        var input = CompactGameInputs();
        heavy.Text = "MD5 重扫";
        input.Controls.Add(Buttons(scanGameSource, importGameSource, heavy, readGame));
        Button browse = MakeButton("浏览…", "BrowseGame");
        browse.Click += (_, _) => BrowseDirectory(gameRoot);
        input.Controls.Add(LabeledRow("游戏根目录", gameRoot, browse));
        EnablePathDrop(gameRoot, directory: true);
        Button browseJson = MakeButton("浏览…", "BrowseGameJson");
        browseJson.Click += (_, _) => BrowseFile(gameJson, json: true);
        gameJsonRow = LabeledRow("历史扫描 JSON", gameJson, browseJson);
        input.Controls.Add(gameJsonRow);
        EnablePathDrop(gameJson, directory: false);
        gameJson.TextChanged += (_, _) => GameChanged();
        readGame.Click += async (_, _) => await ExecuteAsync();

        var options = CompactGameInputs();
        Button browseRules = MakeButton("浏览…", "BrowseExclusionJson");
        browseRules.Click += (_, _) => BrowseFile(exclusionJson, json: true);
        Button importRules = MakeButton("导入规则", "ImportExclusionRules");
        importRules.Click += async (_, _) => await ImportExclusionRulesAsync();
        options.Controls.Add(LabeledRow("排除规则 JSON", exclusionJson, browseRules, importRules));
        EnablePathDrop(exclusionJson, directory: false);
        Label exclusionHint = Note("排除项可填游戏目录内的相对／完整路径，添加后生效；不支持通配符。");
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
        scanFileStatus.Padding = scanByteStatus.Padding = Padding.Empty;
        var results = GameReadingRows([scanFileStatus, scanFileProgress, scanByteStatus, scanByteProgress]);
        results.AutoSize = true;
        results.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        results.Dock = DockStyle.Top;
        results.RowStyles[1] = new RowStyle(SizeType.Absolute, 22);
        results.RowStyles[3] = new RowStyle(SizeType.Absolute, 22);
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
        Button browse = MakeButton("浏览…", "BrowseExcluded" + suffix);
        browse.Click += (_, _) => { if (directory) BrowseDirectory(entry); else BrowseFile(entry); };
        Button add = MakeButton("添加", "AddExcluded" + suffix);
        add.Click += (_, _) => AddExclusion(entry, list);
        entry.KeyDown += (_, args) =>
        {
            if (args.KeyCode != Keys.Enter) return;
            args.SuppressKeyPress = true;
            AddExclusion(entry, list);
        };
        Button remove = MakeButton("移除选中", "RemoveExcluded" + suffix);
        remove.Click += (_, _) =>
        {
            if (session.IsBusy || list.SelectedItems.Count == 0) return;
            foreach (object item in list.SelectedItems.Cast<object>().ToArray()) list.Items.Remove(item);
            GameChanged();
        };
        EnablePathDrop(entry, directory);
        var input = CompactGameInputs();
        input.Controls.Add(Buttons(Note(directory ? "排除目录" : "排除文件"), add, remove));
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
            { status.Text = "整批未添加：排除项必须全部位于指定游戏根目录内。"; }
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
        using var dialog = new FolderBrowserDialog { Description = "选择目录", UseDescriptionForTitle = true,
            ShowNewFolderButton = false, SelectedPath = gameRoot.Text.Trim() };
        if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.SelectedPath;
    }

    private void BrowseFile(TextBox target, bool json = false)
    {
        using var dialog = new OpenFileDialog { Title = json ? "选择 JSON 文件" : "选择排除文件",
            InitialDirectory = gameRoot.Text.Trim(), Filter = json ? "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*" : "所有文件 (*.*)|*.*" };
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
        { status.Text = "无法添加排除项：请先指定游戏目录，并填写目录内的有效路径。"; }
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
        status.Text = "正在读取排除规则…";
        RefreshView();
        try
        {
            await operation;
            status.Text = "排除规则已合并导入；可在下方列表选择移除。";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException
            or CoreException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        { status.Text = "规则导入失败，原列表未改变。请检查 JSON 的 excludedDirectories／excludedFiles 数组及游戏目录内的路径。"; }
        finally { RefreshView(); }
    }

    private void ResetScanProgress()
    {
        scanFileProgress.Style = scanByteProgress.Style = ProgressBarStyle.Continuous;
        scanFileProgress.Value = scanByteProgress.Value = 0;
        scanFileStatus.Text = "文件数量：尚未读取";
        scanByteStatus.Text = "文件大小：尚未读取";
    }

    private void ShowScanProgress(ScanProgress value)
    {
        scanFileProgress.Style = scanByteProgress.Style = ProgressBarStyle.Continuous;
        scanFileProgress.Value = value.TotalFiles == 0 ? 100 : (int)Math.Clamp(100.0 * value.CompletedFiles / value.TotalFiles, 0, 100);
        scanByteProgress.Value = value.TotalBytes == 0 ? scanFileProgress.Value : (int)Math.Clamp(100.0 * value.ProcessedBytes / value.TotalBytes, 0, 100);
        scanFileStatus.Text = $"文件数量：{value.CompletedFiles:N0} / {value.TotalFiles:N0}";
        scanByteStatus.Text = $"文件大小：{value.ProcessedBytes:N0} / {value.TotalBytes:N0} 字节";
    }
}
