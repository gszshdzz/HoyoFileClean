namespace HoyoFileClean.Desktop;

public sealed partial class MainForm
{
    internal static string DefaultManifestDirectory => Path.Combine(
        new[] { Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) }
            .Concat(Maintenance.ManifestDirectorySegments).ToArray());

    private readonly TextBox localManifestDirectory = new() { Name = "LocalManifestDirectory", Anchor = AnchorStyles.Left | AnchorStyles.Right, Text = DefaultManifestDirectory };
    private readonly Button getLocalManifests = MakeButton(L.Get("Button_GetLocalManifests"), "GetLocalManifests");
    private readonly ManifestTable localManifests = new("LocalManifests", groupByModifiedHour: true);
    private readonly Button parseLocalReference = MakeButton(L.Get("Button_Execute"), "ParseLocalReference");
    private Panel localReferencePage = null!;

    internal static FolderBrowserDialog CreateManifestDirectoryDialog() => new()
    {
        Description = L.Get("SelectManifestDirectoryDialogTitle"), UseDescriptionForTitle = true,
        SelectedPath = DefaultManifestDirectory, ShowNewFolderButton = false
    };

    private Panel BuildLocalReferencePage()
    {
        localManifestDirectory.AllowDrop = true;
        localManifestDirectory.DragEnter += (_, args) =>
            args.Effect = DroppedManifestDirectory(args) is not null ? DragDropEffects.Copy : DragDropEffects.None;
        localManifestDirectory.DragDrop += (_, args) =>
        {
            if (DroppedManifestDirectory(args) is { } directory) localManifestDirectory.Text = directory;
        };
        Button browse = MakeButton(L.Get("Button_BrowseGame"), "BrowseLocalManifestDirectory");
        browse.Click += (_, _) =>
        {
            using var dialog = CreateManifestDirectoryDialog();
            if (dialog.ShowDialog(this) == DialogResult.OK) localManifestDirectory.Text = dialog.SelectedPath;
        };
        localManifestDirectory.TextChanged += (_, _) =>
        {
            localManifests.Rows.Clear();
            ReferenceChanged();
            status.Text = L.Get("ManifestDirectoryChanged");
        };
        getLocalManifests.Click += async (_, _) => await GetLocalManifestsAsync();
        var pathRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.Controls.Add(localManifestDirectory, 0, 0);
        pathRow.Controls.Add(browse, 1, 0);
        localManifests.CheckedFilesChanged += ReferenceChanged;
        parseLocalReference.Click += async (_, _) => await ConvertAsync();
        var input = Vertical();
        input.Controls.Add(pathRow);
        localReferenceActions = OperationActions("LocalReferenceActions", cancelParsing, getLocalManifests, parseLocalReference);
        input.Controls.Add(localReferenceActions);
        input.Controls.Add(Note(L.Get("LocalManifestHint")));
        return Page(input, localManifests);
    }

    private string? DroppedManifestDirectory(DragEventArgs args)
    {
        if (session.IsBusy || session.Step != 1 || !localSource.Checked
            || (args.AllowedEffect & DragDropEffects.Copy) == 0) return null;
        if (args.Data is ITypedDataObject data && data.TryGetData(DataFormats.FileDrop, out string[]? paths)
            && paths is { Length: 1 } && !string.IsNullOrWhiteSpace(paths[0])
            && Path.IsPathFullyQualified(paths[0]) && Directory.Exists(paths[0]))
            return paths[0];
        return null;
    }

    private async Task GetLocalManifestsAsync()
    {
        if (session.IsBusy || !localSource.Checked) return;
        string directory = localManifestDirectory.Text.Trim();
        localManifests.Rows.Clear();
        session.InvalidateReference();
        reportOrigin = "";
        await RunWorkAsync(() =>
        {
            if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException(L.Get("ManifestDirectoryRequired"));
            return ManifestDiscovery.Find(directory);
        }, files =>
        {
            localManifests.AddFiles(files);
        }, L.Get("GettingLocalManifests"), () => L.Format("LocalManifestsFound", localManifests.Rows.Count));
    }
}
