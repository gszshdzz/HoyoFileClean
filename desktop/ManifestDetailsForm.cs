namespace HoyoFileClean.Desktop;

internal sealed class ManifestDetailsForm : Form
{
    private readonly FileListSnapshot snapshot;
    private readonly Button export = new() { Name = "ExportManifestJson", Text = L.Get("ExportManifestJson"), AutoSize = true };
    private readonly Button close = new() { Name = "CloseManifestDetails", Text = L.Get("Button_CloseWizard"), AutoSize = true, DialogResult = DialogResult.Cancel };
    private readonly Label status = new() { Name = "ExportStatus", AutoSize = true, Dock = DockStyle.Fill };
    private readonly Func<IWin32Window, string?> chooseOutput;
    private bool exporting;

    internal ManifestDetailsForm(ReferenceInput input, FileListSnapshot snapshot, string information,
        Func<IWin32Window, string?>? chooseOutput = null)
    {
        this.snapshot = snapshot;
        this.chooseOutput = chooseOutput ?? ChooseOutput;
        Name = "ManifestDetails";
        Text = L.Get("ManifestDetailsTitle");
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(640, 440);
        ClientSize = new Size(900, 640);
        ManifestFile[] files = snapshot.Files.Values.ToArray();
        var content = new DataGridView
        {
            Name = "ManifestContents", Dock = DockStyle.Fill, ReadOnly = true, VirtualMode = true,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
            RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        foreach (var (name, label, weight) in new[] { ("Path", "ManifestFilePath", 6f), ("Size", "ManifestFileBytes", 2f), ("Hash", "ManifestFileHash", 4f) })
            content.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = L.Get(label), FillWeight = weight,
                SortMode = DataGridViewColumnSortMode.NotSortable });
        content.CellValueNeeded += (_, args) =>
        {
            ManifestFile file = files[args.RowIndex];
            args.Value = args.ColumnIndex switch { 0 => file.Path, 1 => file.Size, _ => file.Hash ?? L.Get("UnavailableValue") };
        };
        content.RowCount = files.Length;
        var header = new TextBox
        {
            Name = "ManifestInformation", Multiline = true, ReadOnly = true, Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Vertical,
            Text = input.File.Name + Environment.NewLine + information + Environment.NewLine
                + ResultText.Describe(snapshot, L.Get("ManifestDetailsTitle"))
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        buttons.Controls.AddRange([close, export]);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 70));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(content, 0, 1);
        layout.Controls.Add(status, 0, 2);
        layout.Controls.Add(buttons, 0, 3);
        Controls.Add(layout);
        CancelButton = close;
        close.Click += (_, _) => Close();
        export.Click += async (_, _) => await ExportAsync();
        FormClosing += (_, args) => args.Cancel = exporting;
    }

    private string? ChooseOutput(IWin32Window owner)
    {
        using var dialog = new SaveFileDialog
        {
            Title = L.Get("ExportManifestJson"), Filter = L.Get("JsonFileFilter"),
            DefaultExt = "json", AddExtension = true, FileName = "manifest.json", OverwritePrompt = false
        };
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
    }

    private async Task ExportAsync()
    {
        if (exporting || chooseOutput(this) is not { } path) return;
        exporting = true;
        export.Enabled = close.Enabled = false;
        status.Text = L.Get("ExportingManifestJson");
        try
        {
            await Task.Run(() => ManifestConverter.Export(snapshot, path));
            status.Text = L.Format("SavedPath", path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CoreException
            or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            status.Text = DiagnosticText.ForCli(error);
        }
        finally
        {
            exporting = false;
            export.Enabled = close.Enabled = true;
        }
    }
}
