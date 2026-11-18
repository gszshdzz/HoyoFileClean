namespace HoyoFileClean.Desktop;

public sealed partial class MainForm : Form
{
    private readonly WizardSession session;
    private readonly Panel content = new() { Dock = DockStyle.Fill };
    private readonly Label[] steps = new Label[5];
    private readonly Panel[] pages;
    private readonly Button back = MakeButton(L.Get("Button_Back"), "Back");
    private readonly Button execute = MakeButton(L.Get("Button_Execute"), "Execute");
    private readonly Button next = MakeButton(L.Get("Button_Next"), "Next");
    private readonly Button close = MakeButton(L.Get("Button_CloseWizard"), "CloseWizard");
    private readonly Button cancelParsing = MakeButton(L.Get("Button_CancelParsing"), "CancelParsing");
    private TableLayoutPanel manualReferenceActions = null!;
    private TableLayoutPanel localReferenceActions = null!;
    private readonly ReferenceParser referenceParser;
    private readonly Func<string, bool> confirmPatchReference;
    private readonly Func<IWin32Window, string?> chooseExclusionOutput;
    private readonly Button parseManualReference = MakeButton(L.Get("Button_Execute"), "ParseManualReference");
    private readonly ProgressBar parsingProgress = new() { Name = "OperationProgress", Dock = DockStyle.Fill, Visible = false };
    private readonly ProgressBar comparisonProgress = new() { Name = "ComparisonProgress", Dock = DockStyle.Fill, Visible = false };
    private ProgressBar progress => session.Step == 2 ? comparisonProgress : parsingProgress;
    private int displayedStep = -1;
    private readonly Label status = new() { Name = "Status", Dock = DockStyle.Fill, AutoSize = true,
        BackColor = SystemColors.Control, Padding = new Padding(0, 3, 0, 3) };
    private readonly ManifestTable manifests = new("Manifests");
    private readonly Label referenceResult = new() { Name = "ReferenceResult", AutoSize = true, Dock = DockStyle.Top };
    private readonly RadioButton automaticSource = new() { Name = "AutomaticSource", Text = L.Get("AutomaticSourceLabel"), AutoSize = true };
    private readonly RadioButton networkSource = new() { Name = "NetworkSource", Text = L.Get("NetworkSourceLabel"), AutoSize = true };
    private readonly RadioButton localSource = new() { Name = "LocalSource", Text = L.Get("LocalSourceLabel"), AutoSize = true };
    private readonly RadioButton manualSource = new() { Name = "ManualSource", Text = L.Get("ManualSourceLabel"), AutoSize = true };
    private readonly Panel referenceModeContent = new() { Dock = DockStyle.Fill };
    private Panel manualReferencePage = null!;
    private readonly TextBox gameRoot = new() { Name = "GameRoot", Width = 570 };
    private readonly CheckBox heavy = new() { Text = L.Get("HeavyScanHint"), AutoSize = true, Name = "HeavyScan" };
    private readonly ComparisonPanel comparisonResult = new();
    private readonly DifferencePanel differences;
    private readonly FileOperationPanel fileOperations;

    public MainForm() : this(new WizardSession()) { }

    private void Advance()
    {
        if (!session.CanAdvance) return;
        if (session.Step == 1)
        {
            string[] patches = session.Reference!.Sources.Where(source => source.ManifestKind == FileValues.SophonPatch)
                .Select(source => Path.GetFileName(source.Path)).ToArray();
            if (patches.Length > 0 && !confirmPatchReference(L.Format("PatchReferenceWarning",
                string.Join(Environment.NewLine, patches)))) return;
        }
        session.Next();
        RefreshView();
        if (session.Step == 3) differences.Render();
        if (session.Step == 4) fileOperations.Render();
    }

    internal MainForm(WizardSession session, ReferenceParser? referenceParser = null, NetworkManifestClient? networkClient = null,
        Func<IReadOnlyList<NetworkService>>? loadNetworkServices = null, Func<string, bool>? confirmPatchReference = null,
        Func<IWin32Window, string?>? chooseExclusionOutput = null)
    {
        this.session = session;
        SuspendLayout();
        content.SuspendLayout();
        this.chooseExclusionOutput = chooseExclusionOutput ?? ChooseExclusionOutput;
        this.referenceParser = referenceParser ?? new ReferenceParser();
        this.confirmPatchReference = confirmPatchReference ?? (message => MessageBox.Show(this, message,
            L.Get("PatchReferenceWarningTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes);
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
        fileOperations = new FileOperationPanel(session);
        var operationPage = new Panel { Dock = DockStyle.Fill };
        operationPage.Controls.Add(fileOperations);
        pages = [BuildScanPage(), BuildReferencePage(), BuildComparisonPage(), BuildDifferencePage(), operationPage];
        foreach (ManifestTable table in new[] { manifests, localManifests, networkManifests })
            table.DetailsRequested += (input, snapshot, information) =>
            {
                using var dialog = new ManifestDetailsForm(input, snapshot, information);
                dialog.ShowDialog(this);
            };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var flow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 5, Padding = new Padding(0, 0, 0, 12) };
        string[] titles = StepTitles();
        for (int i = 0; i < steps.Length; i++)
        {
            flow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
            steps[i] = new Label { Text = titles[i], AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 6) };
            flow.Controls.Add(steps[i], i, 0);
        }
        for (int i = 0; i < pages.Length; i++)
        {
            pages[i].Visible = i == session.Step;
            content.Controls.Add(pages[i]);
        }
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        buttons.Controls.AddRange([back, next, close]);
        layout.Controls.Add(flow, 0, 0);
        layout.Controls.Add(content, 0, 1);
        layout.Controls.Add(status, 0, 2);
        layout.Controls.Add(buttons, 0, 3);
        Controls.Add(layout);
        back.Click += (_, _) => { session.Back(); RefreshView(); };
        next.Click += (_, _) => Advance();
        execute.Click += async (_, _) => await ExecuteAsync();
        close.Click += (_, _) => Close();
        cancelParsing.Click += (_, _) =>
        {
            session.CancelOperation();
            cancelParsing.Enabled = false;
            status.Text = L.Get("CancellingParsing");
        };
        gameRoot.TextChanged += (_, _) => { loadedIdentification = null; ClearPreset(); GameChanged(); };
        heavy.CheckedChanged += (_, _) => GameChanged();
        FormClosing += (_, args) =>
        {
            if (session.RequestClose()) return;
            args.Cancel = true;
            if (session.Operation == DesktopOperation.Network)
            {
                status.Text = L.Get("CancellingNetworkAndClosing");
                return;
            }
            status.Text = session.Operation == DesktopOperation.Parsing
                ? L.Get("CloseDuringParsingHint")
                : L.Get("CloseDuringWorkHint");
        };
        RefreshView();
        ActiveControl = session.Step == 0 ? gameRoot
            : session.Step == 1 ? (manualSource.Checked ? manualSource : automaticSource) : execute;
        content.ResumeLayout(true);
        ResumeLayout(true);
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
        remove.Click += (_, _) => manifests.RemoveCheckedFiles();
        var input = Vertical();
        using var inputBatch = new LayoutBatch(input);
        input.Controls.Add(Note(L.Get("ManualManifestHint")));
        manualReferenceActions = OperationActions("ManualReferenceActions", cancelParsing, add, remove, parseManualReference);
        input.Controls.Add(manualReferenceActions);
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
        using var layoutBatch = new LayoutBatch(layout);
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(Buttons(automaticSource, networkSource, localSource, manualSource), 0, 0);
        layout.Controls.Add(referenceModeContent, 0, 1);
        // Set the initial selection after grouping, before wiring invalidation events.
        if (session.Reference is not null) manualSource.Checked = true;
        else automaticSource.Checked = true;
        foreach (RadioButton option in new[] { automaticSource, networkSource, localSource, manualSource })
        {
            option.CheckedChanged += (_, _) =>
            {
                if (!option.Checked) return;
                if (automaticSource.Checked || networkManifests.SelectionLocked) ClearNetworkFiles();
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
        input.Controls.Add(Buttons(execute));
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
        if (session.Step == 1) status.Text = IsNetworkSource ? DescribeNetworkInput() : L.Get("ReferenceInputChanged");
        differences.Render();
        RefreshView();
    }

    private void GameChanged()
    {
        if (automaticSource.Checked && networkManifests.Rows.Count > 0) ClearNetworkFiles();
        if (loadedIdentification is null) previewIdentification = null;
        session.InvalidateGame();
        ResetScanProgress();
        if (session.Step == 0) status.Text = L.Get("GameInputChanged");
        differences.Render();
        RefreshView();
    }

    private void RefreshView()
    {
        using var layoutBatch = new LayoutBatch(this);
        if (displayedStep != session.Step)
        {
            displayedStep = session.Step;
            status.Text = session.Step switch
            {
                1 => session.Reference is not null ? L.Get("ReferenceReady")
                    : IsNetworkSource ? DescribeNetworkInput() : L.Get("ReferenceInputRequired"),
                0 => session.Game is { } game ? ResultText.DescribeGameStatus(game, importGameSource.Checked) : L.Get("GameInputRequired"),
                2 => session.Comparison is not null ? L.Get("ComparisonReady") : L.Get("ComparisonInputRequired"),
                4 => L.Get("FileOperationHint"),
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
        next.Visible = session.Step < 4;
        automaticSource.Enabled = !session.IsBusy && !GameIdentifier.HasIncompleteRules(session.Identification);
        if (automaticSource.Checked && !session.IsBusy && GameIdentifier.HasIncompleteRules(session.Identification))
        {
            networkSource.Checked = true;
            if (session.Step == 1) status.Text = L.Get("AutomaticNetworkRulesIncomplete");
        }
        ShowReferencePage(IsNetworkSource ? networkReferencePage : localSource.Checked ? localReferencePage : manualReferencePage);
        networkChoices.Visible = !automaticSource.Checked;
        automaticNetworkInformation.Visible = automaticSource.Checked;
        automaticNetworkInformation.Text = DescribeAutomaticNetworkInformation();
        queryNetwork.Visible = getNetwork.Visible = !automaticSource.Checked;
        networkManifests.SelectionLocked = automaticSource.Checked;
        queryNetwork.Enabled = !session.IsBusy && networkSource.Checked && networkRegions.SelectedItem is not null;
        getNetwork.Enabled = !session.IsBusy && networkSource.Checked && networkGames.SelectedItem is not null;
        parseNetwork.Enabled = !session.IsBusy && (automaticSource.Checked
            ? networkConfigurationError is null : networkSource.Checked && networkManifests.CheckedInputs.Length > 0);
        networkVersion.Enabled = !session.IsBusy && customNetworkVersion.Checked;
        parseManualReference.Enabled = !session.IsBusy && manualSource.Checked && manifests.CheckedInputs.Length > 0;
        parseLocalReference.Enabled = !session.IsBusy && localSource.Checked && localManifests.CheckedInputs.Length > 0;
        execute.Enabled = !session.IsBusy && session.Game is not null && session.Reference is not null;
        next.Enabled = session.CanAdvance;
        close.Enabled = !session.IsBusy || session.Operation == DesktopOperation.Network;
        TableLayoutPanel parsingActions = localSource.Checked ? localReferenceActions : manualReferenceActions;
        if (cancelParsing.Parent != parsingActions) parsingActions.Controls.Add(cancelParsing, 1, 0);
        cancelParsing.Visible = session.Operation == DesktopOperation.Parsing;
        cancelParsing.Enabled = session.Operation == DesktopOperation.Parsing && session.CanCancel;
        cancelNetwork.Enabled = session.Operation == DesktopOperation.Network && session.CanCancel;
        cancelScan.Visible = session.Operation == DesktopOperation.Scan;
        cancelScan.Enabled = session.Operation == DesktopOperation.Scan && session.CanCancel;
        execute.Text = L.Get("CompareAction");
        scanOptions.Visible = scanGameSource.Checked;
        gameJsonRow.Visible = importGameSource.Checked;
        heavy.Visible = scanGameSource.Checked;
        loadGamePreset.Visible = scanGameSource.Checked;
        readGame.Enabled = !session.IsBusy && (importGameSource.Checked || loadedIdentification is not null);
        readGame.Text = importGameSource.Checked ? L.Get("ImportAction") : L.Get("ScanAction");
        identificationResult.Text = (session.Identification ?? previewIdentification) is { } identification
            ? ResultText.DescribeIdentification(identification)
            : L.Get("IdentificationMissing");
        referenceResult.Text = session.Reference is { } reference
            ? L.Format("ReferenceSummary", reference.FileCount, reference.TotalSize) : L.Get("ReferenceResultMissing");
        comparisonResult.Render(session, IsNetworkSource ? networkManifests.CheckedNetworkMetadata : null);
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
    private static string[] StepTitles() => [L.Get("StepGame"), L.Get("StepReference"),
        L.Get("StepComparison"), L.Get("StepDifferences"), L.Get("StepFileOperation")];
    private static Label Note(string text)
        => new() { Text = text, AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 5, 0, 5) };
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

    private static TableLayoutPanel OperationActions(string name, Button cancel, params Control[] operations)
    {
        var row = new TableLayoutPanel { Name = name, ColumnCount = 2, RowCount = 1, AutoSize = true, Dock = DockStyle.Top };
        using var layoutBatch = new LayoutBatch(row);
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.Controls.Add(Buttons(operations), 0, 0);
        cancel.Anchor = AnchorStyles.Right;
        row.Controls.Add(cancel, 1, 0);
        return row;
    }
    private static Panel Page(Control input, Control result)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        using var layoutBatch = new LayoutBatch(panel);
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(input, 0, 0);
        panel.Controls.Add(result, 0, 1);
        return panel;
    }
}
