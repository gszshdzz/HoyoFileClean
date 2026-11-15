namespace HoyoFileClean.Desktop;

public sealed partial class MainForm
{
    private async Task ExecuteAsync()
    {
        if (session.IsBusy) return;
        if (session.Step == 1 && !manualSource.Checked) return;
        switch (session.Step)
        {
            case 1: await ConvertAsync(); break;
            case 0:
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
        if (inputs.Length == 0) { status.Text = L.Get("SelectFilesToParse"); return; }
        session.InvalidateReference();
        referenceOrigin = reportOrigin = "";
        differences.Render();
        RefreshView();
        table.ShowResults(inputs.Select(input => (input, ReferenceState.Pending, (int?)null)));
        var reporter = new ParsingProgress();
        using var refreshProgress = new System.Windows.Forms.Timer { Interval = Maintenance.ParsingProgressIntervalMs };
        refreshProgress.Tick += (_, _) =>
        {
            if (session.Operation != DesktopOperation.Parsing || !session.IsBusy || session.IsCancellationRequested) return;
            int completed = reporter.Completed;
            if (completed <= progress.Value) return;
            progress.Value = Math.Clamp(Math.Max(progress.Value, completed), 0, inputs.Length);
            status.Text = progress.Value == inputs.Length
                ? L.Format("ParsingMergeProgress", inputs.Length, inputs.Length)
                : L.Format("ParsingFileProgress", progress.Value, inputs.Length);
        };
        refreshProgress.Start();
        ReferenceParser parser = referenceParser;
        await RunWorkAsync(token => parser.Parse(inputs,
            token, progress: reporter), result =>
        {
            progress.Value = inputs.Length;
            table.ShowResults(inputs.Select((input, i) => (input,
                result.Items[i].Error is null ? ReferenceState.Parsed : ReferenceState.Failed, result.Items[i].Entries)));
            session.AcceptReference(result.GetSnapshot());
            referenceOrigin = L.Get("SourceHeading") + string.Join("\r\n", inputs.Select(input => DiagnosticText.DisplaySource(input.File.FullPath))) + "\r\n\r\n";
        }, L.Format("ParsingStartProgress", inputs.Length), DesktopOperation.Parsing, totalFiles: inputs.Length,
        onFinished: () =>
        {
            refreshProgress.Stop();
            progress.Value = Math.Clamp(reporter.Completed, 0, inputs.Length);
            if (session.IsCancellationRequested)
                table.ShowResults(inputs.Select(input => (input, ReferenceState.Cancelled, (int?)null)));
        });
    }

    private async Task ScanAsync()
    {
        string root = gameRoot.Text.Trim();
        if (!Path.IsPathFullyQualified(root)) { status.Text = L.Get("GameRootRequired"); return; }
        bool hashes = heavy.Checked;
        string[] dirs = excludedDirectories.Items.Cast<string>().ToArray(), files = excludedFiles.Items.Cast<string>().ToArray();
        long generation = ++scanGeneration;
        var reporter = new Progress<ScanProgress>(value =>
        {
            if (generation != scanGeneration || session.Operation != DesktopOperation.Scan
                || !session.IsBusy || session.IsCancellationRequested) return;
            ShowScanProgress(value);
            status.Text = L.Format("GameHashProgress", value.CompletedFiles, value.TotalFiles, value.ProcessedBytes, value.TotalBytes);
        });
        session.InvalidateGame();
        RefreshView();
        scanOrigin = reportOrigin = "";
        ResetScanProgress();
        scanFileProgress.Style = scanByteProgress.Style = ProgressBarStyle.Marquee;
        scanProgressPanel.Visible = true;
        await RunWorkAsync(token =>
        {
            GameIdentification identification = GameIdentifier.Identify(root, token);
            FileListSnapshot scan = DirectoryScanner.Scan(root, dirs, files, hashes,
                value => ((IProgress<ScanProgress>)reporter).Report(value), token, identification);
            return (scan, identification);
        }, result =>
        {
            session.AcceptGame(result.scan, root, result.identification);
            ShowScanProgress(new ScanProgress(result.scan.FileCount, result.scan.FileCount, result.scan.TotalSize, result.scan.TotalSize));
            scanOrigin = L.Get("ActualScanOrigin");
        }, L.Get("IdentifyingBeforeScan"), DesktopOperation.Scan, onFinished: () =>
        {
            scanGeneration++;
            StopScanProgress();
        });
    }

    private long scanGeneration;

    private void StopScanProgress()
    {
        scanProgressPanel.Visible = false;
        scanFileProgress.Style = scanByteProgress.Style = ProgressBarStyle.Continuous;
        if (session.Game is not null) return;
        scanFileProgress.Value = scanByteProgress.Value = 0;
    }

    private async Task ImportGameAsync()
    {
        string root = gameRoot.Text.Trim(), path = gameJson.Text.Trim();
        if (!Path.IsPathFullyQualified(root)) { status.Text = L.Get("HistoricalRootRequired"); return; }
        if (path.Length == 0) { status.Text = L.Get("HistoricalJsonRequired"); return; }
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
            scanOrigin = L.Get("HistoricalScanOrigin");
        }, L.Get("HistoricalScanValidating"));
    }

    private async Task CompareAsync()
    {
        if (session.Game is not { } game || session.Reference is not { } reference) return;
        session.InvalidateComparison();
        reportOrigin = "";
        await RunWorkAsync(() => FileComparer.Compare(game, reference), result =>
        {
            session.AcceptComparison(result);
            reportOrigin = L.Get("MemoryComparisonOrigin");
        }, L.Get("ComparisonRunning"));
    }

    private async Task ImportReportAsync()
    {
        if (session.IsBusy || session.Step != 2 || session.Game is not { } game || session.Reference is not { } reference) return;
        using var dialog = new OpenFileDialog
        {
            Title = L.Get("ImportReportDialogTitle"),
            Filter = L.Get("JsonFileFilter"),
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string path = dialog.FileName;
        session.InvalidateComparison();
        reportOrigin = "";
        await RunWorkAsync(() => FileComparer.ImportReport(path, game, reference), result =>
        {
            session.AcceptComparison(result);
            reportOrigin = L.Get("ImportedReportOrigin") + path + "\r\n";
        }, L.Get("ReportValidating"));
    }

    private Task RunWorkAsync<T>(Func<T> work, Action<T> apply, string message,
        Func<string>? completedMessage = null, int? totalFiles = null, Action? onFinished = null)
        => RunWorkAsync(_ => work(), apply, message, DesktopOperation.Work, completedMessage, totalFiles, onFinished);

    private async Task RunWorkAsync<T>(Func<CancellationToken, T> work, Action<T> apply, string message,
        DesktopOperation kind, Func<string>? completedMessage = null, int? totalFiles = null, Action? onFinished = null)
    {
        CancellationToken cancellation = default;
        Task operation = session.RunAsync(token =>
        {
            cancellation = token;
            return work(token);
        }, apply, kind, onFinished);
        status.Text = message;
        progress.Value = 0;
        progress.Maximum = totalFiles ?? 100;
        progress.Style = totalFiles is null ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
        progress.Visible = session.Step != 0;
        differences.Render();
        RefreshView();
        string outcome;
        try
        {
            await operation;
            outcome = session.Step == 0 && session.Game is not null
                ? ResultText.DescribeGameStatus(session.Game, importGameSource.Checked)
                : completedMessage?.Invoke() ?? L.Get("StepCompleted");
        }
        catch (OperationCanceledException) when (kind == DesktopOperation.Network && cancellation.IsCancellationRequested)
        {
            outcome = L.Get("NetworkCancelled");
        }
        catch (OperationCanceledException) when (kind == DesktopOperation.Parsing && cancellation.IsCancellationRequested)
        {
            outcome = L.Get("ParsingCancelled");
        }
        catch (OperationCanceledException) when (kind == DesktopOperation.Scan && cancellation.IsCancellationRequested)
        {
            outcome = L.Get("ScanCancelled");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            outcome = L.Get("StepFailedPrefix") + DiagnosticText.ForDesktop(error);
        }
        finally
        {
            differences.Render();
            RefreshView();
            progress.Visible = false;
        }
        status.Text = outcome;
        if (session.CloseRequested) Close();
    }

    private static LocalManifestFile ReadReferenceMetadata(string path)
    {
        var file = new FileInfo(path);
        return new LocalManifestFile(file.Name, file.FullName, file.Length, file.LastWriteTimeUtc);
    }

    private bool CanAddManualFiles => !session.IsBusy && session.Step == 1 && manualSource.Checked;

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
        { status.Text = L.Get("AddFilesFailedPrefix") + DiagnosticText.ForDesktop(error); }
    }

}
