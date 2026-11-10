namespace HoyoFileClean.Desktop;

public sealed partial class MainForm
{
    internal static string DefaultManifestDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "miHoYo", "HYP", "1_1", "modules", "sophon", "manifest");

    private readonly TextBox localManifestDirectory = new() { Name = "LocalManifestDirectory", Anchor = AnchorStyles.Left | AnchorStyles.Right, Text = DefaultManifestDirectory };
    private readonly Button getLocalManifests = MakeButton("获取", "GetLocalManifests");
    private readonly ManifestTable localManifests = new("LocalManifests", groupByModifiedHour: true);
    private readonly Button parseLocalReference = MakeButton("解析", "ParseLocalReference");
    private Panel localReferencePage = null!;

    internal static FolderBrowserDialog CreateManifestDirectoryDialog() => new()
    {
        Description = "选择启动器保存清单的目录", UseDescriptionForTitle = true,
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
        Button browse = MakeButton("浏览…", "BrowseLocalManifestDirectory");
        browse.Click += (_, _) =>
        {
            using var dialog = CreateManifestDirectoryDialog();
            if (dialog.ShowDialog(this) == DialogResult.OK) localManifestDirectory.Text = dialog.SelectedPath;
        };
        localManifestDirectory.TextChanged += (_, _) =>
        {
            localManifests.Rows.Clear();
            ReferenceChanged();
            status.Text = "目录已变更，请点击获取。";
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
        input.Controls.Add(Buttons(getLocalManifests, parseLocalReference));
        input.Controls.Add(Note("只获取当前目录的候选清单文件，不解析内容、不扫描子目录；链接文件不列入。默认按修改时间从新到旧显示。"));
        return Page(input, localManifests);
    }

    private string? DroppedManifestDirectory(DragEventArgs args)
    {
        if (session.IsBusy || session.Step != 0 || !localSource.Checked
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
        referenceOrigin = reportOrigin = "";
        await RunWorkAsync(() =>
        {
            if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("请输入清单目录的完整路径。");
            return ManifestDiscovery.Find(directory);
        }, files =>
        {
            localManifests.AddFiles(files);
        }, "正在获取本地清单文件…", () => $"已获取 {localManifests.Rows.Count:N0} 份候选清单，尚未解析。请勾选后解析。");
    }
}
