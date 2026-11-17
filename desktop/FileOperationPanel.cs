namespace HoyoFileClean.Desktop;

public enum FileOperationKind { Recycle, Delete, Move }

// A UI-confirmed request, not authorization to access the filesystem without validation.
public sealed record FileOperationRequest(FileOperationKind Kind, string Root,
    IReadOnlyList<DifferenceRow> Files, string? Destination);

public sealed class FileOperationPanel : UserControl
{
    private readonly WizardSession session;
    private DifferenceRow[] displayedRows = [];
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
    public event Action<FileOperationRequest>? OperationConfirmed;

    public FileOperationPanel(WizardSession session, Func<string, bool>? confirm = null)
    {
        this.session = session;
        this.confirm = confirm ?? (message => MessageBox.Show(this, message, L.Get("FileOperationConfirmTitle"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes);
        Name = "FileOperationPanel";
        Dock = DockStyle.Fill;
        AddColumn("Category", L.Get("FileOperationCategoryColumn"), 100, 12);
        AddColumn("Size", L.Get("FileOperationSizeColumn"), 110, 12);
        files.Columns["Size"]!.ValueType = typeof(long);
        files.Columns["Size"]!.DefaultCellStyle.Format = "N0";
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
                4 => L.Get("FileOperationPending"),
                5 => "",
                6 => FullPath(row),
                _ => null
            };
        };
        // Like ManifestTable, this is a display list, not another selection surface.
        files.SelectionChanged += (_, _) => files.ClearSelection();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++) layout.RowStyles.Add(new(SizeType.AutoSize));
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
        Controls.Add(layout);
        Render();
    }

    public void Render()
    {
        summary.Text = L.Format("SelectionSummary", session.SelectedCount, session.SelectedSize) +
            L.Get("FileOperationPreviewHint");
        displayedRows = session.SelectedRows.ToArray();
        files.RowCount = displayedRows.Length;
        files.Invalidate();
        files.ClearSelection();
        recycle.Enabled = delete.Enabled = move.Enabled = destination.Enabled = !session.IsBusy;
        address.Visible = move.Checked;
        address.Enabled = !session.IsBusy;
        execute.Enabled = !session.IsBusy && session.SelectedCount > 0;
    }

    private string FullPath(DifferenceRow row) => Path.Combine(session.Game!.Sources[0].Scan!.Root,
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
        if (kind == FileOperationKind.Move && !Path.IsPathFullyQualified(target))
        {
            MessageBox.Show(this, L.Get("FileOperationDestinationRequired"), L.Get("FileOperationInvalidInputTitle"));
            return;
        }
        DifferenceRow[] rows = session.SelectedRows.ToArray();
        int sharedCount = rows.Count(row => row.Category != DifferenceCategory.GameOnly);
        if (sharedCount > 0 && !confirm(L.Format("FileOperationRiskConfirmation", sharedCount))) return;
        string action = L.Get(kind == FileOperationKind.Move ? "FileOperationMove" :
            kind == FileOperationKind.Delete ? "FileOperationDelete" : "FileOperationRecycleAction");
        string message = L.Format("FileOperationConfirmation", action, session.SelectedCount, session.SelectedSize,
            kind == FileOperationKind.Move ? L.Format("FileOperationTargetConfirmation", target) : "",
            kind == FileOperationKind.Delete ? L.Get("FileOperationPermanentWarning") : "");
        if (!confirm(message)) return;
        OperationConfirmed?.Invoke(new FileOperationRequest(kind, session.Game!.Sources[0].Scan!.Root,
            Array.AsReadOnly(rows), kind == FileOperationKind.Move ? target : null));
    }
}
