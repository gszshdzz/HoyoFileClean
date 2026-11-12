namespace HoyoFileClean.Desktop;

public sealed partial class MainForm : Form
{
    private readonly WizardSession session;
    private readonly Panel content = new() { Dock = DockStyle.Fill };
    private readonly Label[] steps = new Label[4];
    private readonly Panel[] pages;
    private readonly Button back = MakeButton(L.Get("Button_Back"), "Back");
    private readonly Button skip = MakeButton(L.Get("Button_Import"), "Import");
    private readonly Button execute = MakeButton(L.Get("Button_Execute"), "Execute");
    private readonly Button next = MakeButton(L.Get("Button_Next"), "Next");
    private readonly Button close = MakeButton(L.Get("Button_CloseWizard"), "CloseWizard");
    private readonly Button cancelParsing = MakeButton(L.Get("Button_CancelParsing"), "CancelParsing");
    private readonly ReferenceParser referenceParser;
    private CancellationTokenSource? parsingCancellation;
    private readonly Button parseManualReference = MakeButton(L.Get("Button_Execute"), "ParseManualReference");
    private readonly ProgressBar parsingProgress = new() { Name = "OperationProgress", Dock = DockStyle.Fill, Visible = false };
    private readonly ProgressBar comparisonProgress = new() { Name = "ComparisonProgress", Dock = DockStyle.Fill, Visible = false };
    private ProgressBar progress => session.Step == 2 ? comparisonProgress : parsingProgress;
    private int displayedStep = -1;
    private readonly Label status = new() { Name = "Status", Dock = DockStyle.Fill, AutoSize = true,
        BackColor = SystemColors.Control, Padding = new Padding(0, 3, 0, 3) };
    private readonly ManifestTable manifests = new("Manifests");
    private readonly Label referenceResult = new() { Name = "ReferenceResult", AutoSize = true, Dock = DockStyle.Top };
    private readonly RadioButton networkSource = new() { Name = "NetworkSource", Text = L.Get("NetworkSourceLabel"), AutoSize = true, Checked = true };
    private readonly RadioButton localSource = new() { Name = "LocalSource", Text = L.Get("LocalSourceLabel"), AutoSize = true };
    private readonly RadioButton manualSource = new() { Name = "ManualSource", Text = L.Get("ManualSourceLabel"), AutoSize = true };
    private readonly Panel referenceModeContent = new() { Dock = DockStyle.Fill };
    private Panel manualReferencePage = null!;
    private readonly TextBox gameRoot = new() { Name = "GameRoot", Width = 570 };
    private readonly CheckBox heavy = new() { Text = L.Get("HeavyScanHint"), AutoSize = true, Name = "HeavyScan" };
    private readonly TextBox comparisonResult = OutputBox("ComparisonResult");
    private readonly DifferencePanel differences;
    private string referenceOrigin = "";
    private string scanOrigin = "";
    private string reportOrigin = "";

    public MainForm() : this(new WizardSession()) { }

    internal MainForm(WizardSession session, ReferenceParser? referenceParser = null, NetworkManifestClient? networkClient = null,
        Func<IReadOnlyList<NetworkService>>? loadNetworkServices = null)
    {
        this.session = session;
        this.referenceParser = referenceParser ?? new ReferenceParser();
        this.networkClient = networkClient ?? new NetworkManifestClient(networkHttp);
        this.loadNetworkServices = loadNetworkServices ?? (() => NetworkManifestClient.Services);
        Disposed += (_, _) => networkHttp.Dispose();
        Text = L.Get("WizardTitle");
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(800, 650);
        ClientSize = new Size(1040, 780);
        AutoScaleMode = AutoScaleMode.Dpi;
        differences = new DifferencePanel(session);
        pages = [BuildReferencePage(), BuildScanPage(), BuildComparisonPage(), BuildDifferencePage()];
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var flow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Padding = new Padding(0, 0, 0, 12) };
        string[] titles = StepTitles();
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
        layout.Controls.Add(status, 0, 2);
        layout.Controls.Add(buttons, 0, 3);
        Controls.Add(layout);
        back.Click += (_, _) => { session.Back(); RefreshView(); };
        next.Click += (_, _) => { session.Next(); RefreshView(); if (session.Step == 3) differences.Render(); };
        skip.Click += async (_, _) => await ImportReportAsync();
        execute.Click += async (_, _) => await ExecuteAsync();
        close.Click += (_, _) => Close();
        cancelParsing.Click += (_, _) =>
        {
            parsingCancellation?.Cancel();
            cancelParsing.Enabled = false;
            status.Text = L.Get("CancellingParsing");
        };
        gameRoot.TextChanged += (_, _) => GameChanged();
        heavy.CheckedChanged += (_, _) => GameChanged();
        FormClosing += (_, args) =>
        {
            if (!session.IsBusy) return;
            args.Cancel = true;
            if (networkCancellation is not null)
            {
                closeAfterNetwork = true;
                networkCancellation.Cancel();
                status.Text = L.Get("CancellingNetworkAndClosing");
                return;
            }
            status.Text = parsingCancellation is not null
                ? L.Get("CloseDuringParsingHint")
                : L.Get("CloseDuringWorkHint");
        };
        RefreshView();
        ActiveControl = session.Step == 0 ? (manualSource.Checked ? manualSource : networkSource) : execute;
    }

    private Panel BuildReferencePage()
    {
        Button add = MakeButton(L.Get("Button_AddManifest"), "AddManifest");
        Button remove = MakeButton(L.Get("Button_RemoveManifest"), "RemoveManifest");
        add.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Title = L.Get("SelectManifestDialogTitle"), Multiselect = true, Filter = L.Get("AllFileFilter") };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            AddManualFiles(dialog.FileNames);
        };
        manifests.AllowDrop = true;
        manifests.DragEnter += (_, args) => ManualFilesDragEnter(args);
        manifests.DragDrop += (_, args) => ManualFilesDragDrop(args);
        remove.Click += (_, _) =>
        {
            foreach (DataGridViewRow row in manifests.Rows.Cast<DataGridViewRow>().Where(row => row.Cells[0].Value is true).ToArray())
                manifests.Rows.Remove(row);
            ReferenceChanged();
        };
        var input = Vertical();
        input.Controls.Add(Note(L.Get("ManualManifestHint")));
        input.Controls.Add(Buttons(add, remove, parseManualReference));
        parseManualReference.Click += async (_, _) => await ExecuteAsync();
        input.Controls.Add(referenceResult);
        manifests.CheckedFilesChanged += ReferenceChanged;
        manualReferencePage = Page(input, manifests);
        referenceModeContent.Controls.Add(manualReferencePage);
        localReferencePage = BuildLocalReferencePage();
        referenceModeContent.Controls.Add(localReferencePage);
        networkReferencePage = BuildNetworkReferencePage();
        referenceModeContent.Controls.Add(networkReferencePage);
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
                networkManifests.ResetResults();
                ReferenceChanged();
            };
        }
        var page = new Panel { Dock = DockStyle.Fill };
        page.Controls.Add(layout);
        return WithPageProgress(page, parsingProgress);
    }

    private Panel BuildComparisonPage()
    {
        var input = Vertical();
        input.Controls.Add(Note(L.Get("ComparisonInputHint")));
        input.Controls.Add(Note(L.Get("ImportReportHint")));
        return WithPageProgress(Page(input, comparisonResult), comparisonProgress);
    }

    private static Panel WithPageProgress(Control page, ProgressBar bar)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(page, 0, 0);
        layout.Controls.Add(bar, 0, 1);
        return layout;
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
        if (session.Step == 0) status.Text = networkSource.Checked ? DescribeNetworkInput() : L.Get("ReferenceInputChanged");
        differences.Render();
        RefreshView();
    }

    private void GameChanged()
    {
        session.InvalidateGame();
        scanOrigin = reportOrigin = "";
        ResetScanProgress();
        if (session.Step == 1) status.Text = L.Get("GameInputChanged");
        differences.Render();
        RefreshView();
    }

    private void RefreshView()
    {
        if (displayedStep != session.Step)
        {
            displayedStep = session.Step;
            status.Text = session.Step switch
            {
                0 => session.Reference is not null ? L.Get("ReferenceReady")
                    : networkSource.Checked ? DescribeNetworkInput() : L.Get("ReferenceInputRequired"),
                1 => session.Game is { } game ? ResultText.DescribeGameStatus(game, importGameSource.Checked) : L.Get("GameInputRequired"),
                2 => session.Comparison is not null ? L.Get("ComparisonReady") : L.Get("ComparisonInputRequired"),
                _ => L.Get("DifferenceReviewHint")
            };
        }
        for (int i = 0; i < pages.Length; i++)
        {
            pages[i].Visible = i == session.Step;
            if (i < 3) SetInputsBusy(pages[i], session.IsBusy);
            steps[i].Text = (i == session.Step ? L.Get("CurrentStepPrefix") : "") + StepTitles()[i];
        }
        back.Enabled = !session.IsBusy && session.Step > 0;
        skip.Visible = session.Step == 2;
        execute.Visible = session.Step == 2;
        next.Visible = session.Step < 3;
        skip.Enabled = execute.Enabled = !session.IsBusy;
        ShowReferencePage(networkSource.Checked ? networkReferencePage : localSource.Checked ? localReferencePage : manualReferencePage);
        queryNetwork.Enabled = !session.IsBusy && networkSource.Checked && networkRegions.SelectedItem is not null;
        getNetwork.Enabled = !session.IsBusy && networkSource.Checked && networkGames.SelectedItem is not null;
        parseNetwork.Enabled = !session.IsBusy && networkSource.Checked && networkManifests.CheckedInputs.Length > 0;
        networkVersion.Enabled = !session.IsBusy && customNetworkVersion.Checked;
        parseManualReference.Enabled = !session.IsBusy && manualSource.Checked && manifests.CheckedInputs.Length > 0;
        parseLocalReference.Enabled = !session.IsBusy && localSource.Checked && localManifests.CheckedInputs.Length > 0;
        if (session.Step == 2) skip.Enabled = execute.Enabled = !session.IsBusy && session.Game is not null && session.Reference is not null;
        next.Enabled = session.CanAdvance;
        close.Enabled = !session.IsBusy || networkCancellation is not null;
        cancelParsing.Visible = parsingCancellation is not null;
        cancelParsing.Enabled = session.IsBusy && parsingCancellation is { IsCancellationRequested: false };
        cancelNetwork.Enabled = session.IsBusy && networkCancellation is { IsCancellationRequested: false };
        execute.Text = L.Get("CompareAction");
        scanOptions.Visible = scanGameSource.Checked;
        gameJsonRow.Visible = importGameSource.Checked;
        heavy.Visible = scanGameSource.Checked;
        readGame.Text = importGameSource.Checked ? L.Get("ImportAction") : L.Get("ScanAction");
        referenceResult.Text = session.Reference is { } reference
            ? L.Format("ReferenceSummary", reference.FileCount, reference.TotalSize) : L.Get("ReferenceResultMissing");
        comparisonResult.Text = L.Get("GameSideHeading") + scanOrigin + ResultText.Describe(session.Game, L.Get("GameScanTitle"))
            + L.Get("ReferenceSideHeading") + referenceOrigin + ResultText.Describe(session.Reference, L.Get("ReferenceListTitle"))
            + L.Get("ComparisonResultHeading") + reportOrigin + ResultText.Describe(session.Comparison);
    }

    private void ShowReferencePage(Panel target)
    {
        if (referenceModeContent.Controls.GetChildIndex(target) == 0 && target.Visible) return;
        target.Visible = true;
        target.BringToFront();
        foreach (Control page in referenceModeContent.Controls)
            if (page != target) page.Visible = false;
    }

    private static void SetInputsBusy(Control parent, bool busy)
    {
        foreach (Control control in parent.Controls)
        {
            if (control is ManifestTable table)
                table.SetBusy(busy);
            else
            {
                if (control is ButtonBase or ComboBox or ListBox || control is TextBox { ReadOnly: false })
                    control.Enabled = !busy;
                SetInputsBusy(control, busy);
            }
        }
    }

    private static Button MakeButton(string text, string name) => new() { Text = text, Name = name, AutoSize = true, MinimumSize = new Size(90, 30) };
    private static string[] StepTitles() => [L.Get("StepReference"), L.Get("StepGame"),
        L.Get("StepComparison"), L.Get("StepDifferences")];
    private static Label Note(string text)
        => new() { Text = text, AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 5, 0, 5) };
    private static TextBox OutputBox(string name) => new() { Name = name, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
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
