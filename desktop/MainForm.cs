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
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly TextBox status = OutputBox("Status");
    private readonly ListBox manifests = new() { Name = "Manifests", Height = 100, Dock = DockStyle.Fill, HorizontalScrollbar = true };
    private readonly TextBox referenceResult = OutputBox("ReferenceResult");
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

    internal MainForm(WizardSession session)
    {
        this.session = session;
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
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
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
        buttons.Controls.AddRange([back, skip, execute, next, close]);
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
        gameRoot.TextChanged += (_, _) => GameChanged();
        heavy.CheckedChanged += (_, _) => GameChanged();
        excludedDirectories.TextChanged += (_, _) => GameChanged();
        excludedFiles.TextChanged += (_, _) => GameChanged();
        FormClosing += (_, args) =>
        {
            if (!session.IsBusy) return;
            args.Cancel = true;
            status.Text = "正在处理，请等待当前操作完成后关闭。当前模块不支持中途取消，不会在关闭窗口后继续后台扫描。";
        };
        RefreshView();
        ActiveControl = session.Step == 0 ? (manualSource.Checked ? manualSource : networkSource) : execute;
    }

    private Panel BuildReferencePage()
    {
        Button add = MakeButton("添加 manifest…", "AddManifest");
        Button remove = MakeButton("移除选中", "RemoveManifest");
        add.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Title = "选择一份或多份 manifest", Multiselect = true, Filter = "所有文件 (*.*)|*.*" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            bool changed = false;
            foreach (string path in dialog.FileNames)
            {
                if (manifests.Items.Cast<string>().Contains(path, StringComparer.OrdinalIgnoreCase)) continue;
                manifests.Items.Add(path);
                changed = true;
            }
            if (changed) ReferenceChanged();
        };
        remove.Click += (_, _) =>
        {
            if (manifests.SelectedIndex < 0) return;
            manifests.Items.RemoveAt(manifests.SelectedIndex);
            ReferenceChanged();
        };
        var input = Vertical();
        input.Controls.Add(Note("手动选择参考 manifest。“解析”只转换清单，不扫描游戏。"));
        input.Controls.Add(Buttons(add, remove));
        input.Controls.Add(manifests);
        input.Controls.Add(Note("跳过＝导入一份或多份现成清单 JSON；不会无输入前进。游戏名称／包识别暂未支持。"));
        manualReferencePage = Page(input, referenceResult);
        referenceModeContent.Controls.Add(manualReferencePage);
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
                status.Clear();
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
            pages[i].Enabled = !session.IsBusy;
            steps[i].Text = (i == session.Step ? "当前 → " : "") + new[] { "1 参考清单", "2 游戏扫描", "3 确认与比较", "4 差异与选择" }[i];
        }
        back.Enabled = !session.IsBusy && session.Step > 0;
        skip.Visible = execute.Visible = next.Visible = session.Step < 3;
        skip.Enabled = execute.Enabled = !session.IsBusy;
        manualReferencePage.Visible = manualSource.Checked;
        referencePlaceholder.Visible = !manualSource.Checked;
        referencePlaceholder.Text = networkSource.Checked
            ? "网络获取暂未实现。请切换到“手动输入”使用现有功能。"
            : "本地获取暂未实现。请切换到“手动输入”使用现有功能。";
        if (session.Step == 0) skip.Enabled = execute.Enabled = !session.IsBusy && manualSource.Checked;
        if (session.Step == 2) skip.Enabled = execute.Enabled = !session.IsBusy && session.Game is not null && session.Reference is not null;
        next.Enabled = session.CanAdvance && (session.Step != 0 || manualSource.Checked);
        close.Enabled = !session.IsBusy;
        execute.Text = session.Step switch { 0 => "解析", 1 => "扫描", _ => "比较" };
        referenceResult.Text = referenceOrigin + ResultText.Describe(session.Reference, "参考清单");
        scanResult.Text = scanOrigin + ResultText.Describe(session.Game, "游戏扫描");
        comparisonResult.Text = "【左侧／A：游戏文件】\r\n" + scanOrigin + ResultText.Describe(session.Game, "游戏扫描")
            + "\r\n\r\n【右侧／B：参考清单】\r\n" + referenceOrigin + ResultText.Describe(session.Reference, "参考清单")
            + "\r\n\r\n【比较结果】\r\n" + reportOrigin + ResultText.Describe(session.Comparison);
    }

    private static Button MakeButton(string text, string name) => new() { Text = text, Name = name, AutoSize = true, MinimumSize = new Size(90, 30) };
    private static Label Note(string text)
    {
        var label = new Label { Text = text, AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 5, 0, 5) };
        label.ParentChanged += (_, _) =>
        {
            if (label.Parent is not { } parent) return;
            void Wrap() => label.MaximumSize = new Size(Math.Max(120, parent.ClientSize.Width - parent.Padding.Horizontal - label.Margin.Horizontal), 0);
            parent.SizeChanged += (_, _) => Wrap();
            Wrap();
        };
        return label;
    }
    private static TextBox OutputBox(string name) => new() { Name = name, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private static TextBox InputLines(string name) => new() { Name = name, Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 64, Dock = DockStyle.Fill };
    private static TableLayoutPanel Vertical() => new() { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, 8) };
    private static FlowLayoutPanel Buttons(params Control[] controls)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = false };
        panel.Controls.AddRange(controls);
        return panel;
    }
    private static Panel Page(Control input, Control result)
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(result);
        panel.Controls.Add(input);
        return panel;
    }
}
