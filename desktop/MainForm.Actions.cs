namespace HoyoFileClean.Desktop;

public sealed partial class MainForm
{
    private async Task ExecuteAsync()
    {
        if (session.IsBusy) return;
        if (session.Step == 0 && !manualSource.Checked) return;
        switch (session.Step)
        {
            case 0: await ConvertAsync(); break;
            case 1:
                if (importGameSource.Checked) await ImportGameAsync();
                else await ScanAsync();
                break;
            case 2: await CompareAsync(); break;
        }
    }

    private async Task ConvertAsync()
    {
        if (session.IsBusy) return;
        if (networkSource.Checked) { await FetchAndParseNetworkAsync(); return; }
        ManifestTable table = networkSource.Checked ? networkManifests : localSource.Checked ? localManifests : manifests;
        ReferenceInput[] inputs = table.CheckedInputs;
        if (inputs.Length == 0) { status.Text = "请先勾选需要解析的文件。"; return; }
        session.InvalidateReference();
        referenceOrigin = reportOrigin = "";
        differences.Render();
        RefreshView();
        table.ShowResults(inputs.Select(input => (input, "待解析", (int?)null)));
        using var cancellation = new CancellationTokenSource();
        parsingCancellation = cancellation;
        var reporter = new ParsingProgress();
        using var refreshProgress = new System.Windows.Forms.Timer { Interval = 100 };
        refreshProgress.Tick += (_, _) =>
        {
            if (parsingCancellation != cancellation || !session.IsBusy || cancellation.IsCancellationRequested) return;
            int completed = reporter.Completed;
            if (completed <= progress.Value) return;
            progress.Value = Math.Clamp(Math.Max(progress.Value, completed), 0, inputs.Length);
            status.Text = progress.Value == inputs.Length
                ? $"文件已处理 {inputs.Length:N0}/{inputs.Length:N0}，正在合并校验…"
                : $"正在解析：已处理 {progress.Value:N0}/{inputs.Length:N0} 份文件…";
        };
        refreshProgress.Start();
        ReferenceParser parser = referenceParser;
        await RunWorkAsync(() => parser.Parse(inputs.Select(input => input.File.FullPath).ToArray(),
            cancellation.Token, progress: reporter), result =>
        {
            cancellation.Token.ThrowIfCancellationRequested();
            progress.Value = inputs.Length;
            table.ShowResults(inputs.Select((input, i) => (input,
                result.Items[i].Error is null ? "已解析" : "失败", result.Items[i].Entries)));
            if (result.Items.Any(item => item.Error is not null))
                throw new InvalidDataException("部分文件解析失败，请取消勾选失败项或修正输入后重新解析。",
                    result.Items.First(item => item.Error is not null).Error);
            if (result.CombinedError is not null)
                throw new InvalidDataException("所选清单存在同侧冲突或无法共同载入，请检查选择。", result.CombinedError);
            session.AcceptReference(result.Snapshot!);
            referenceOrigin = "来源：\r\n" + string.Join("\r\n", inputs.Select(input => ResultText.DisplaySource(input.File.FullPath))) + "\r\n\r\n";
        }, $"正在解析：已处理 0/{inputs.Length:N0} 份文件…", totalFiles: inputs.Length,
        onFinished: () =>
        {
            refreshProgress.Stop();
            progress.Value = Math.Clamp(reporter.Completed, 0, inputs.Length);
            if (cancellation.IsCancellationRequested)
                table.ShowResults(inputs.Select(input => (input, "已取消", (int?)null)));
            parsingCancellation = null;
        });
    }

    private async Task ScanAsync()
    {
        string root = gameRoot.Text.Trim();
        if (!Path.IsPathFullyQualified(root)) { status.Text = "请输入或浏览选择游戏目录的完整路径。"; return; }
        bool hashes = heavy.Checked;
        string[] dirs = excludedDirectories.Items.Cast<string>().ToArray(), files = excludedFiles.Items.Cast<string>().ToArray();
        var reporter = new Progress<ScanProgress>(value =>
        {
            if (!session.IsBusy) return;
            ShowScanProgress(value);
            status.Text = $"正在 MD5 重扫：{value.CompletedFiles:N0}/{value.TotalFiles:N0} 个文件；{value.ProcessedBytes:N0}/{value.TotalBytes:N0} 字节。";
        });
        session.InvalidateGame();
        scanOrigin = reportOrigin = "";
        ResetScanProgress();
        scanFileProgress.Style = scanByteProgress.Style = ProgressBarStyle.Marquee;
        scanFileStatus.Text = "文件数量：正在枚举，总数尚未知…";
        scanByteStatus.Text = "文件大小：正在枚举，总大小尚未知…";
        await RunWorkAsync(() =>
        {
            return DirectoryScanner.Scan(root, dirs, files, hashes,
                value => ((IProgress<ScanProgress>)reporter).Report(value));
        }, result =>
        {
            session.AcceptGame(result, root);
            ShowScanProgress(new ScanProgress(result.FileCount, result.FileCount, result.TotalSize, result.TotalSize));
            scanOrigin = "本次实际扫描；结果仅保留在内存。\r\n";
        }, hashes ? "正在枚举文件，随后计算 MD5…" : "正在轻扫（只读取路径与大小）…", onFinished: StopScanProgress);
    }

    private void StopScanProgress()
    {
        scanFileProgress.Style = scanByteProgress.Style = ProgressBarStyle.Continuous;
        if (session.Game is not null) return;
        scanFileProgress.Value = scanByteProgress.Value = 0;
        scanFileStatus.Text = "文件数量：未完成，结果不可用";
        scanByteStatus.Text = "文件大小：未完成，结果不可用";
    }

    private async Task ImportGameAsync()
    {
        string root = gameRoot.Text.Trim(), path = gameJson.Text.Trim();
        if (!Path.IsPathFullyQualified(root)) { status.Text = "请先指定游戏目录的完整路径，以核对历史记录。"; return; }
        if (path.Length == 0) { status.Text = "请选择或拖入扫描 JSON。"; return; }
        session.InvalidateGame();
        scanOrigin = reportOrigin = "";
        ResetScanProgress();
        await RunWorkAsync(() =>
        {
            FileListSnapshot result = FileComparer.ReadInputs([path]);
            WizardSession.ValidateGameInput(result, root);
            return result;
        }, result =>
        {
            session.AcceptGame(result, root);
            scanFileStatus.Text = $"文件数量：历史记录 {result.FileCount:N0} 个（未扫描）";
            scanByteStatus.Text = $"文件大小：历史记录 {result.TotalSize:N0} 字节（未读取）";
            scanOrigin = "已导入历史扫描 JSON；未重新扫描，也不证明目录当前状态未变化。模式与范围来自记录。\r\n";
        }, "正在校验扫描 JSON 与指定目录…");
    }

    private async Task CompareAsync()
    {
        if (session.Game is not { } game || session.Reference is not { } reference) return;
        session.InvalidateComparison();
        reportOrigin = "";
        await RunWorkAsync(() => FileComparer.Compare(game, reference), result =>
        {
            session.AcceptComparison(result);
            reportOrigin = "比较结果仅保留在内存。\r\n";
        }, "正在比较两侧清单…");
    }

    private async Task ImportReportAsync()
    {
        if (session.IsBusy || session.Step != 2 || session.Game is not { } game || session.Reference is not { } reference) return;
        using var dialog = new OpenFileDialog
        {
            Title = "导入比较报告并核对当前两侧",
            Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string path = dialog.FileName;
        session.InvalidateComparison();
        reportOrigin = "";
        await RunWorkAsync(() => FileComparer.ImportReport(path, game, reference), result =>
        {
            session.AcceptComparison(result);
            reportOrigin = "已核对身份和内容的导入报告：" + path + "\r\n";
        }, "正在核对报告身份，并用当前清单复核内容…");
    }

    private async Task RunWorkAsync<T>(Func<T> work, Action<T> apply, string message,
        Func<string>? completedMessage = null, int? totalFiles = null, Action? onFinished = null)
    {
        Task operation = session.RunAsync(work, apply);
        status.Text = message;
        progress.Value = 0;
        progress.Maximum = totalFiles ?? 100;
        progress.Style = totalFiles is null ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
        progress.Visible = session.Step != 1;
        differences.Render();
        RefreshView();
        string outcome;
        try
        {
            await operation;
            outcome = session.Step == 1 && session.Game is not null
                ? ResultText.DescribeGameStatus(session.Game, importGameSource.Checked)
                : completedMessage?.Invoke() ?? "处理完成，请核对本步摘要后点击下一步。";
        }
        catch (OperationCanceledException) when (networkCancellation is { IsCancellationRequested: true })
        {
            outcome = "已取消网络操作，本批结果已丢弃。";
        }
        catch (OperationCanceledException) when (parsingCancellation is { IsCancellationRequested: true })
        {
            outcome = "已取消解析，本批结果已丢弃，请重新解析。";
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            outcome = "处理失败，未启用本步结果：" + DescribeDesktopError(error);
        }
        finally
        {
            onFinished?.Invoke();
            differences.Render();
            RefreshView();
            progress.Visible = false;
        }
        status.Text = outcome;
    }

    private static LocalManifestFile ReadReferenceMetadata(string path)
    {
        var file = new FileInfo(path);
        return new LocalManifestFile(file.Name, file.FullName, file.Length, file.LastWriteTimeUtc);
    }

    private bool CanAddManualFiles => !session.IsBusy && session.Step == 0 && manualSource.Checked;

    private void ManualFilesDragEnter(DragEventArgs args)
    {
        args.Effect = DroppedManualFiles(args) is not null ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void ManualFilesDragDrop(DragEventArgs args)
    {
        if (DroppedManualFiles(args) is { } paths) AddManualFiles(paths);
    }

    private string[]? DroppedManualFiles(DragEventArgs args)
    {
        if (!CanAddManualFiles || (args.AllowedEffect & DragDropEffects.Copy) == 0) return null;
        if (args.Data is ITypedDataObject data && data.TryGetData(DataFormats.FileDrop, out string[]? paths)
            && paths is { Length: > 0 } && paths.All(path => !string.IsNullOrWhiteSpace(path)
                && Path.IsPathFullyQualified(path) && File.Exists(path))) return paths;
        return null;
    }

    private void AddManualFiles(IEnumerable<string> paths)
    {
        if (!CanAddManualFiles) return;
        try
        {
            // AddFiles materializes metadata first: a directory/missing file rejects the batch,
            // never recursively imports a folder or leaves a partially changed table.
            manifests.AddFiles(paths.Select(ReadReferenceMetadata), check: true);
            ReferenceChanged();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or System.Security.SecurityException)
        { status.Text = "无法添加文件：" + DescribeDesktopError(error); }
    }

    internal static string DescribeDesktopError(Exception error) => error switch
    {
        CoreException { Code: CoreErrorCode.InputConflict } => "所选清单存在同侧冲突，请检查选择。",
        CoreException { Code: CoreErrorCode.OutputInsideScanRoot } => "程序临时目录不能位于扫描目录内，请调整程序位置或扫描范围。",
        CoreException { Code: CoreErrorCode.ManifestChannelUnavailable or CoreErrorCode.ManifestVersionUnavailable } => "官方未提供所选区服／版本的清单，未改用最新版或其他区服。",
        CoreException { Code: CoreErrorCode.ManifestDownloadMismatch } => "清单长度、压缩方式或 MD5 校验失败，请重新获取。",
        CoreException { Code: CoreErrorCode.ManifestNetworkSizeLimit } => "网络清单或元数据超过大小上限。",
        CoreException { Code: CoreErrorCode.NetworkConfigurationInvalid } => "网络服务配置缺失或无效，请检查 config/network-services.json；本地和手动输入仍可使用。",
        CoreException { Code: CoreErrorCode.ManifestNetworkMetadataInvalid or CoreErrorCode.ManifestNetworkUnsupported } => "官方清单元数据、地址或格式不受支持。",
        CoreException => "清单或路径无效，请检查所选文件。",
        FileNotFoundException => "所选文件不存在，请重新获取或添加。",
        DirectoryNotFoundException => "目录不存在，请检查目录。",
        UnauthorizedAccessException or System.Security.SecurityException => "没有访问权限，请检查程序及输入目录权限。",
        InvalidDataException => "部分文件无效或所选清单存在同侧冲突，请检查表格与选择。",
        HttpRequestException => "官方网络请求失败，请检查网络或稍后重试。",
        OperationCanceledException => "网络请求已取消或超时，请重试。",
        IOException => "文件读写失败，请检查目录、链接、文件占用及剩余空间。",
        ArgumentException or NotSupportedException => "目录或参数无效，请检查输入。",
        _ => "操作未完成，请检查输入后重试。"
    };

}
