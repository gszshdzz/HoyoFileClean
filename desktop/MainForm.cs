namespace HoyoFileClean.Desktop;

public sealed partial class MainForm : Form
{
    private readonly WizardSession session;
    private readonly Panel content = new() { Dock = DockStyle.Fill };
    private readonly Label[] steps = new Label[4];
    private readonly Panel[] pages;
    private readonly Button back = MakeButton("上一步", "Back");
    private readonly Button skip = MakeButton("跳过（导入 JSON）", "Import");
    private readonly Button execute = MakeButton("解析", "Execute");
    private readonly Button next = MakeButton("下一步", "Next");
    private readonly Button close = MakeButton("关闭", "CloseWizard");
    private readonly Button cancelParsing = MakeButton("取消解析", "CancelParsing");
    private readonly ReferenceParser referenceParser;
    private CancellationTokenSource? parsingCancellation;
    private readonly Button parseManualReference = MakeButton("解析", "ParseManualReference");
    private readonly ProgressBar progress = new() { Name = "OperationProgress", Dock = DockStyle.Fill, Visible = false };
    private readonly Label status = new() { Name = "Status", Dock = DockStyle.Fill, AutoSize = true,
        BackColor = SystemColors.Control, Padding = new Padding(0, 3, 0, 3) };
    private readonly ManifestTable manifests = new("Manifests");
    private readonly Label referenceResult = new() { Name = "ReferenceResult", AutoSize = true, Dock = DockStyle.Top };
    private readonly RadioButton networkSource = new() { Name = "NetworkSource", Text = "网络获取", AutoSize = true, Checked = true };
    private readonly RadioButton localSource = new() { Name = "LocalSource", Text = "本地获取", AutoSize = true };
    private readonly RadioButton manualSource = new() { Name = "ManualSource", Text = "手动输入", AutoSize = true };
    private readonly Panel referenceModeContent = new() { Dock = DockStyle.Fill };
    private Panel manualReferencePage = null!;
    private readonly Label referencePlaceholder = new() { Name = "ReferencePlaceholder", AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 12, 0, 0) };
    private readonly TextBox gameRoot = new() { Name = "GameRoot", Width = 570 };
    private readonly CheckBox heavy = new() { Text = "MD5 重扫（读取文件内容，耗时更长）", AutoSize = true, Name = "HeavyScan" };
    private readonly TextBox excludedDirectories = InputLines("ExcludedDirectories");
    private readonly TextBox excludedFiles = InputLines("ExcludedFiles");
    private readonly TextBox scanResult = OutputBox("ScanResult");
    private readonly TextBox comparisonResult = OutputBox("ComparisonResult");
    private readonly DifferencePanel differences;
    private string referenceOrigin = "";
    private string scanOrigin = "";
    private string reportOrigin = "";

    public MainForm() : this(new WizardSession()) { }

    internal MainForm(WizardSession session, ReferenceParser? referenceParser = null)
    {
        this.session = session;
        this.referenceParser = referenceParser ?? new ReferenceParser();
        Text = "HoyoFileClean — 文件差异向导（删除仅演示）";
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(800, 650);
        ClientSize = new Size(1040, 780);
        AutoScaleMode = AutoScaleMode.Dpi;
        differences = new DifferencePanel(session);
        pages = [BuildReferencePage(), BuildScanPage(), BuildComparisonPage(), BuildDifferencePage()];
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var flow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Padding = new Padding(0, 0, 0, 12) };
        string[] titles = ["1 参考清单", "2 游戏扫描", "3 确认与比较", "4 差异与选择"];
        for (int i = 0; i < steps.Length; i++)
        {
            flow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            steps[i] = new Label { Text = titles[i], AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 6) };
            flow.Controls.Add(steps[i], i, 0);
        }
        foreach (Panel page in pages) content.Controls.Add(page);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        buttons.Controls.AddRange([back, skip, execute, next, cancelParsing, close]);
        layout.Controls.Add(flow, 0, 0);
        layout.Controls.Add(content, 0, 1);
        layout.Controls.Add(progress, 0, 2);
        layout.Controls.Add(status, 0, 3);
        layout.Controls.Add(buttons, 0, 4);
        Controls.Add(layout);
        back.Click += (_, _) => { session.Back(); RefreshView(); };
        next.Click += (_, _) => { session.Next(); RefreshView(); if (session.Step == 3) differences.Render(); };
        skip.Click += async (_, _) => await ImportAsync();
        execute.Click += async (_, _) => await ExecuteAsync();
        close.Click += (_, _) => Close();
        cancelParsing.Click += (_, _) =>
        {
            parsingCancellation?.Cancel();
            cancelParsing.Enabled = false;
            status.Text = "正在取消解析，请等待正在执行的解析退出…";
        };
        gameRoot.TextChanged += (_, _) => GameChanged();
        heavy.CheckedChanged += (_, _) => GameChanged();
        excludedDirectories.TextChanged += (_, _) => GameChanged();
        excludedFiles.TextChanged += (_, _) => GameChanged();
        FormClosing += (_, args) =>
        {
            if (!session.IsBusy) return;
            args.Cancel = true;
            status.Text = parsingCancellation is not null
                ? "正在解析，请先点击取消解析，等待退出后关闭。"
                : "正在处理，请等待当前操作完成后关闭；扫描暂不支持中途取消。";
        };
        RefreshView();
        ActiveControl = session.Step == 0 ? (manualSource.Checked ? manualSource : networkSource) : execute;
    }

    private Panel BuildReferencePage()
    {
        Button add = MakeButton("添加清单…", "AddManifest");
        Button remove = MakeButton("移除选中", "RemoveManifest");
        add.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Title = "选择清单（manifest 或 JSON）", Multiselect = true, Filter = "所有文件 (*.*)|*.*" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                manifests.AddFiles(dialog.FileNames.Select(ReadReferenceMetadata), check: true);
                ReferenceChanged();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { status.Text = "无法添加文件：" + DescribeDesktopError(error); }
        };
        remove.Click += (_, _) =>
        {
            foreach (DataGridViewRow row in manifests.Rows.Cast<DataGridViewRow>().Where(row => row.Cells[0].Value is true).ToArray())
                manifests.Rows.Remove(row);
            ReferenceChanged();
        };
        var input = Vertical();
        input.Controls.Add(Note("添加 manifest 或清单 JSON，勾选后统一解析。"));
        input.Controls.Add(Buttons(add, remove, parseManualReference));
        parseManualReference.Click += async (_, _) => await ExecuteAsync();
        input.Controls.Add(referenceResult);
        manifests.CheckedFilesChanged += ReferenceChanged;
        manualReferencePage = Page(input, manifests);
        referenceModeContent.Controls.Add(manualReferencePage);
        localReferencePage = BuildLocalReferencePage();
        referenceModeContent.Controls.Add(localReferencePage);
        referenceModeContent.Controls.Add(referencePlaceholder);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(Buttons(networkSource, localSource, manualSource), 0, 0);
        layout.Controls.Add(referenceModeContent, 0, 1);
        // Set the initial selection after grouping, before wiring invalidation events.
        if (session.Reference is not null) manualSource.Checked = true;
        else networkSource.Checked = true;
        foreach (RadioButton option in new[] { networkSource, localSource, manualSource })
        {
            option.CheckedChanged += (_, _) =>
            {
                if (!option.Checked) return;
                manifests.ResetResults();
                localManifests.ResetResults();
                status.Text = "";
                ReferenceChanged();
            };
        }
        var page = new Panel { Dock = DockStyle.Fill };
        page.Controls.Add(layout);
        return page;
    }

    private Panel BuildScanPage()
    {
        Button browse = MakeButton("浏览目录…", "BrowseGame");
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { Description = "明确选择本次扫描的游戏目录", UseDescriptionForTitle = true };
            if (dialog.ShowDialog(this) == DialogResult.OK) gameRoot.Text = dialog.SelectedPath;
        };
        var pathRow = Buttons(gameRoot, browse);
        pathRow.WrapContents = true;
        var exclusions = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2 };
        exclusions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        exclusions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        exclusions.Controls.Add(Note("排除目录（每行一项）"), 0, 0);
        exclusions.Controls.Add(Note("排除文件（每行一项）"), 1, 0);
        exclusions.Controls.Add(excludedDirectories, 0, 1);
        exclusions.Controls.Add(excludedFiles, 1, 1);
        var input = Vertical();
        input.Controls.Add(Note("游戏目录：默认轻扫，只读取路径与大小。跳过＝导入已有扫描 JSON，仍须核对指定目录。"));
        input.Controls.Add(pathRow);
        input.Controls.Add(heavy);
        input.Controls.Add(exclusions);
        input.Controls.Add(Note("排除项支持目录内的相对路径或完整路径，不支持通配符。链接／重解析点不跟随。"));
        return Page(input, scanResult);
    }

    private Panel BuildComparisonPage()
    {
        var input = Vertical();
        input.Controls.Add(Note("请核对下方两侧来源、文件数、大小、哈希和范围。A＝游戏文件，B＝参考清单。"));
        input.Controls.Add(Note("跳过＝导入已有报告，并核对两侧身份和报告内容；不会自动打开报告记录的路径。"));
        return Page(input, comparisonResult);
    }

    private Panel BuildDifferencePage()
    {
        var page = new Panel { Dock = DockStyle.Fill };
        page.Controls.Add(differences);
        return page;
    }

    private void ReferenceChanged()
    {
        session.InvalidateReference();
        referenceOrigin = reportOrigin = "";
        differences.Render();
        RefreshView();
    }

    private void GameChanged()
    {
        session.InvalidateGame();
        scanOrigin = reportOrigin = "";
        differences.Render();
        RefreshView();
    }

    private void RefreshView()
    {
        for (int i = 0; i < pages.Length; i++)
        {
            pages[i].Visible = i == session.Step;
            if (i < 3) SetInputsBusy(pages[i], session.IsBusy);
            steps[i].Text = (i == session.Step ? "当前 → " : "") + new[] { "1 参考清单", "2 游戏扫描", "3 确认与比较", "4 差异与选择" }[i];
        }
        back.Enabled = !session.IsBusy && session.Step > 0;
        skip.Visible = execute.Visible = session.Step is 1 or 2;
        next.Visible = session.Step < 3;
        skip.Enabled = execute.Enabled = !session.IsBusy;
        manualReferencePage.Visible = manualSource.Checked;
        localReferencePage.Visible = localSource.Checked;
        referencePlaceholder.Visible = networkSource.Checked;
        referencePlaceholder.Text = "网络获取暂未实现。请切换到“本地获取”查看启动器清单，或使用“手动输入”。";
        parseManualReference.Enabled = !session.IsBusy && manualSource.Checked;
        if (session.Step == 0) skip.Enabled = execute.Enabled = !session.IsBusy && manualSource.Checked;
        if (session.Step == 2) skip.Enabled = execute.Enabled = !session.IsBusy && session.Game is not null && session.Reference is not null;
        next.Enabled = session.CanAdvance && (session.Step != 0 || !networkSource.Checked);
        close.Enabled = !session.IsBusy;
        cancelParsing.Visible = parsingCancellation is not null;
        cancelParsing.Enabled = session.IsBusy && parsingCancellation is { IsCancellationRequested: false };
        execute.Text = session.Step switch { 0 => "解析", 1 => "扫描", _ => "比较" };
        referenceResult.Text = session.Reference is { } reference
            ? $"有效参考：{reference.FileCount:N0} 个条目，共 {reference.TotalSize:N0} 字节。" : "尚无有效参考结果。";
        scanResult.Text = scanOrigin + ResultText.Describe(session.Game, "游戏扫描");
        comparisonResult.Text = "【左侧／A：游戏文件】\r\n" + scanOrigin + ResultText.Describe(session.Game, "游戏扫描")
            + "\r\n\r\n【右侧／B：参考清单】\r\n" + referenceOrigin + ResultText.Describe(session.Reference, "参考清单")
            + "\r\n\r\n【比较结果】\r\n" + reportOrigin + ResultText.Describe(session.Comparison);
    }

    private static void SetInputsBusy(Control parent, bool busy)
    {
        foreach (Control control in parent.Controls)
        {
            if (control is ManifestTable table)
                table.SetBusy(busy);
            else
            {
                if (control is ButtonBase or ComboBox || control is TextBox { ReadOnly: false })
                    control.Enabled = !busy;
                SetInputsBusy(control, busy);
            }
        }
    }

    private static Button MakeButton(string text, string name) => new() { Text = text, Name = name, AutoSize = true, MinimumSize = new Size(90, 30) };
    private static Label Note(string text)
        => new() { Text = text, AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 5, 0, 5) };
    private static TextBox OutputBox(string name) => new() { Name = name, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private static TextBox InputLines(string name) => new() { Name = name, Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 64, Dock = DockStyle.Fill };
    private static TableLayoutPanel Vertical()
    {
        var panel = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, 8) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return panel;
    }
    private static FlowLayoutPanel Buttons(params Control[] controls)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = false };
        panel.Controls.AddRange(controls);
        return panel;
    }
    private static Panel Page(Control input, Control result)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(input, 0, 0);
        panel.Controls.Add(result, 0, 1);
        return panel;
    }
}
