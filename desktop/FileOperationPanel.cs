namespace HoyoFileClean.Desktop;

public enum FileOperationKind { Recycle, Delete, Move }

// A UI-confirmed request, not authorization to access the filesystem without validation.
public sealed record FileOperationRequest(FileOperationKind Kind, string Root,
    IReadOnlyList<DifferenceRow> Files, string? Destination);

public sealed class FileOperationPanel : UserControl
{
    private readonly WizardSession session;
    private DifferenceRow[] displayedRows = [];
    private string displayedRoot = "";
    private FileOperationResult? operationResult;
    private FileListSnapshot? resultSnapshot;
    private readonly ProgressBar operationProgress = new() { Name = "FileOperationProgress", Dock = DockStyle.Fill,
        Style = ProgressBarStyle.Marquee, Visible = false };
    private readonly Label summary = new() { Name = "FileOperationSummary", AutoSize = true, Dock = DockStyle.Fill };
    private readonly DataGridView files = new() { Name = "FileOperationFiles", Dock = DockStyle.Fill, ReadOnly = true,
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, MultiSelect = false,
        AllowUserToResizeRows = false, SelectionMode = DataGridViewSelectionMode.CellSelect,
        VirtualMode = true };
    private readonly RadioButton recycle = new() { Name = "RecycleFiles", Text = L.Get("FileOperationRecycle"), Checked = true, AutoSize = true };
    private readonly RadioButton delete = new() { Name = "DeleteFiles", Text = L.Get("FileOperationDelete"), AutoSize = true };
    private readonly RadioButton move = new() { Name = "MoveFiles", Text = L.Get("FileOperationMove"), AutoSize = true };
    private readonly TextBox destination = new() { Name = "MoveDestination", Width = 480 };
    private readonly FlowLayoutPanel address = new() { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
    private readonly Button execute = new() { Name = "ConfirmFileOperation", Text = L.Get("FileOperationConfirmAction"), AutoSize = true };
    private readonly Func<string, bool> confirm;
    private readonly Action<string> inputError;
    public event Action<FileOperationRequest>? OperationConfirmed;

    public FileOperationPanel(WizardSession session, Func<string, bool>? confirm = null, Action<string>? inputError = null)
    {
        this.session = session;
        this.confirm = confirm ?? (message => MessageBox.Show(this, message, L.Get("FileOperationConfirmTitle"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes);
        this.inputError = inputError ?? (message => MessageBox.Show(this, message, L.Get("FileOperationInvalidInputTitle")));
        Name = "FileOperationPanel";
        Dock = DockStyle.Fill;
        AddColumn("Category", L.Get("FileOperationCategoryColumn"), 100, 12);
        AddColumn("Size", L.Get("FileOperationSizeColumn"), 110, 12);
        FileSizeCells.Configure(files);
        AddColumn("SizeDifference", L.Get("FileOperationSizeComparisonColumn"), 80, 8);
        AddColumn("HashDifference", L.Get("FileOperationHashComparisonColumn"), 100, 10);
        AddColumn("State", L.Get("FileOperationStateColumn"), 80, 8);
        AddColumn("Message", L.Get("FileOperationMessageColumn"), 120, 15);
        AddColumn("Path", L.Get("FileOperationPathColumn"), 240, 35);
        files.CellValueNeeded += (_, args) =>
        {
            if (args.RowIndex < 0 || args.RowIndex >= displayedRows.Length) return;
            DifferenceRow row = displayedRows[args.RowIndex];
            args.Value = args.ColumnIndex switch
            {
                0 => DifferenceText.CategoryName(row.Category),
                1 => row.Game!.Size,
                2 => L.Get(row.Reference is null ? "FileOperationNoCounterpart" :
                    row.Game!.Size == row.Reference.Size ? "FileOperationEqual" : "FileOperationUnequal"),
                3 => L.Get(row.Reference is null ? "FileOperationNoCounterpart" :
                    row.Game!.Hash is null || row.Reference.Hash is null ? "FileOperationHashUnchecked" :
                        string.Equals(row.Game.Hash, row.Reference.Hash, StringComparison.OrdinalIgnoreCase) ? "FileOperationEqual" : "FileOperationUnequal"),
                4 => L.Get(operationResult is null ? "FileOperationPending" : operationResult.Files[args.RowIndex].State switch
                {
                    FileOperationState.Succeeded => "FileOperationSucceeded",
                    FileOperationState.Failed => "FileOperationFailed",
                    _ => "FileOperationNotProcessed"
                }),
                5 => operationResult?.Files[args.RowIndex].Message ?? "",
                6 => FullPath(row),
                _ => null
            };
        };
        // Like ManifestTable, this is a display list, not another selection surface.
        files.SelectionChanged += (_, _) => files.ClearSelection();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.Percent, 100));
        for (int i = 0; i < 4; i++) layout.RowStyles.Add(new(SizeType.AutoSize));
        var options = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        options.Controls.AddRange([recycle, delete, move]);
        var browse = new Button { Text = L.Get("Button_BrowseGame"), AutoSize = true, Name = "BrowseMoveDestination" };
        address.Controls.AddRange([new Label { Text = L.Get("FileOperationDestinationLabel"), AutoSize = true }, destination, browse]);
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { Description = L.Get("FileOperationDestinationDialog") };
            if (dialog.ShowDialog(this) == DialogResult.OK) destination.Text = dialog.SelectedPath;
        };
        move.CheckedChanged += (_, _) => address.Visible = move.Checked;
        execute.Click += (_, _) => ConfirmOperation();
        layout.Controls.Add(summary, 0, 0);
        layout.Controls.Add(files, 0, 1);
        layout.Controls.Add(options, 0, 2);
        layout.Controls.Add(address, 0, 3);
        layout.Controls.Add(execute, 0, 4);
        layout.Controls.Add(operationProgress, 0, 5);
        Controls.Add(layout);
        Render();
    }

    public void Render()
    {
        if (operationResult is not null && session.Game is not null
            && (!ReferenceEquals(resultSnapshot, session.Game) || !displayedRows.SequenceEqual(session.SelectedRows)))
            operationResult = null;
        if (operationResult is null)
        {
            displayedRows = session.SelectedRows.ToArray();
            displayedRoot = session.Game?.Sources[0].Scan?.Root ?? "";
        }
        summary.Text = session.IsBusy ? L.Get("FileOperationRunning") : operationResult is null
            ? L.Format("SelectionSummary", session.SelectedCount, FileSizeText.Format(session.SelectedSize)) + L.Get("FileOperationSafetyHint")
            : L.Format("FileOperationFinished", operationResult.SucceededCount, operationResult.Files.Count)
                + Environment.NewLine + (operationResult.Error ?? "")
                + (operationResult.ExecutionStarted ? L.Get("FileOperationRescanRequired") : "");
        files.RowCount = displayedRows.Length;
        files.Invalidate();
        files.ClearSelection();
        recycle.Enabled = delete.Enabled = move.Enabled = destination.Enabled = !session.IsBusy;
        address.Visible = move.Checked;
        address.Enabled = !session.IsBusy;
        execute.Enabled = !session.IsBusy && session.Game is not null && session.SelectedCount > 0;
        operationProgress.Visible = session.IsBusy;
    }

    internal void ShowResult(FileOperationRequest request, FileOperationResult result, FileListSnapshot snapshot)
    {
        displayedRows = request.Files.ToArray();
        displayedRoot = request.Root;
        resultSnapshot = snapshot;
        operationResult = result;
        Render();
    }

    private string FullPath(DifferenceRow row) => Path.Combine(displayedRoot,
        row.Path.Replace('/', Path.DirectorySeparatorChar));

    private void AddColumn(string name, string heading, int minimumWidth, float weight)
    {
        files.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = name, HeaderText = heading, MinimumWidth = minimumWidth, FillWeight = weight, ReadOnly = true
        });
    }

    internal void ConfirmOperation()
    {
        if (session.IsBusy || session.SelectedCount == 0) return;
        FileOperationKind kind = move.Checked ? FileOperationKind.Move : delete.Checked ? FileOperationKind.Delete : FileOperationKind.Recycle;
        string target = destination.Text.Trim();
        if (kind == FileOperationKind.Move)
        {
            try { target = FileOperationExecutor.ValidateMoveDestination(session.Game!.Sources[0].Scan!.Root, target); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException
                or ArgumentException or NotSupportedException or System.ComponentModel.Win32Exception or CoreException)
            {
                inputError(DiagnosticText.ForDesktop(error));
                return;
            }
        }
        DifferenceRow[] rows = session.SelectedRows.ToArray();
        var warnings = new List<string> { L.Get("FileOperationCommonWarning") };
        int sharedCount = rows.Count(row => row.Category != DifferenceCategory.GameOnly);
        if (sharedCount > 0) warnings.Add(L.Format("FileOperationRiskConfirmation", sharedCount));
        DifferenceRow[] hidden = rows.Where(row => session.IsResultHidden(row.Path)).ToArray();
        if (hidden.Length > 0) warnings.Add(L.Format("FileOperationHiddenWarning", hidden.Length,
            string.Join(L.Get("ListSeparator"), hidden.Take(PresentationOptions.FileOperationWarningExampleCount).Select(row => row.Path))));
        if (kind == FileOperationKind.Recycle) warnings.Add(L.Get("FileOperationRecycleWarning"));
        if (kind == FileOperationKind.Delete) warnings.Add(L.Get("FileOperationPermanentWarning"));
        if (!confirm(L.Format("FileOperationWarnings", string.Join(Environment.NewLine + Environment.NewLine, warnings)))) return;
        string action = L.Get(kind == FileOperationKind.Move ? "FileOperationMove" :
            kind == FileOperationKind.Delete ? "FileOperationDelete" : "FileOperationRecycleAction");
        string message = L.Format("FileOperationConfirmation", action, session.SelectedCount, FileSizeText.Format(session.SelectedSize),
            kind == FileOperationKind.Move ? L.Format("FileOperationTargetConfirmation", target) : "",
            kind == FileOperationKind.Delete ? L.Get("FileOperationPermanentWarning") : "");
        if (!confirm(message)) return;
        OperationConfirmed?.Invoke(new FileOperationRequest(kind, session.Game!.Sources[0].Scan!.Root,
            Array.AsReadOnly(rows), kind == FileOperationKind.Move ? target : null));
    }
}
