namespace HoyoFileClean.Desktop;

public sealed partial class MainForm
{
    internal async Task ExecuteFileOperationAsync(FileOperationRequest request)
    {
        if (session.IsBusy || session.Step != 4 || session.Game is not { } game) return;
        // A stale UI event or a forged caller cannot expand the actual current selection.
        if (!request.Files.SequenceEqual(session.SelectedRows)
            || !string.Equals(request.Root, game.Sources[0].Scan!.Root, StringComparison.OrdinalIgnoreCase))
        {
            status.Text = L.Get("FileOperationOutsideSelection");
            return;
        }
        FileOperationResult? result = null;
        await RunWorkAsync(() => FileOperationExecutor.Execute(request, game), value => result = value,
            L.Get("FileOperationRunning"), () => result?.Error
                ?? L.Format("FileOperationFinished", result?.SucceededCount ?? 0, request.Files.Count));
        // Even an unexpected failure might have changed disk state. Never reuse that scan.
        if (result is null || result.ExecutionStarted) session.InvalidateGame();
        if (result is not null) fileOperations.ShowResult(request, result, game);
        differences.Render();
        RefreshView();
    }
}
