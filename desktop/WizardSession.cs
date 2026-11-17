namespace HoyoFileClean.Desktop;

public enum DifferenceCategory { All, GameOnly, ReferenceOnly, Different, OutsideScope }

internal enum DesktopOperation { Work, Parsing, Network, Scan }

public sealed record DifferenceRow(string Path, ManifestFile? Game, ManifestFile? Reference,
    DifferenceCategory Category)
{
    public bool CanSelect => Game is not null && Category != DifferenceCategory.OutsideScope;
}

// UI-independent state. Neither paths in a report nor a checked tree node authorize file operations.
public sealed class WizardSession
{
    private readonly Dictionary<string, ManifestFile> selected = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DifferenceRow> rowsByPath = new(StringComparer.OrdinalIgnoreCase);
    public FileListSnapshot? Reference { get; private set; }
    public FileListSnapshot? Game { get; private set; }
    public GameIdentification? Identification { get; private set; }
    public ComparisonResult? Comparison { get; private set; }
    public IReadOnlyList<DifferenceRow> Rows { get; private set; } = [];
    public DifferenceCategory Filter { get; private set; }
    public int Step { get; private set; }
    public bool IsBusy { get; private set; }
    private CancellationTokenSource? cancellation;
    private bool applyingResult;
    internal DesktopOperation? Operation { get; private set; }
    internal bool IsCancellationRequested => cancellation?.IsCancellationRequested == true;
    internal bool CanCancel => IsBusy && cancellation is not null && !IsCancellationRequested;
    internal bool CloseRequested { get; private set; }
    public bool SelectionConfirmed { get; private set; }
    public int SelectedCount => selected.Count;
    public long SelectedSize => selected.Values.Sum(file => file.Size);
    public IEnumerable<DifferenceRow> SelectedRows => Rows.Where(row => selected.ContainsKey(row.Path));
    public bool CanDemonstrateDeletion => !IsBusy && selected.Count > 0 && SelectionConfirmed && Comparison is not null;
    public bool CanAdvance => !IsBusy && Step switch
    {
        0 => Game is not null,
        1 => Reference is not null,
        2 => Comparison is not null,
        3 => Comparison is not null,
        _ => false
    };

    public Task RunAsync<T>(Func<T> work, Action<T> apply)
        => RunAsync(_ => work(), apply, DesktopOperation.Work);

    internal async Task RunAsync<T>(Func<CancellationToken, T> work, Action<T> apply,
        DesktopOperation operation, Action? onFinished = null)
    {
        RequireIdle();
        Operation = operation;
        CloseRequested = false;
        using var source = operation == DesktopOperation.Work ? null : new CancellationTokenSource();
        cancellation = source;
        CancellationToken token = source?.Token ?? CancellationToken.None;
        IsBusy = true;
        try
        {
            T result = await Task.Run(() => work(token));
            token.ThrowIfCancellationRequested();
            applyingResult = true;
            try { apply(result); }
            finally { applyingResult = false; }
        }
        finally
        {
            try { onFinished?.Invoke(); }
            finally
            {
                IsBusy = false;
                cancellation = null;
                Operation = null;
            }
        }
    }

    internal void CancelOperation()
    {
        if (CanCancel) cancellation!.Cancel();
    }

    // Only network operations support cancel-and-close. Other work must finish first.
    internal bool RequestClose()
    {
        if (!IsBusy) return true;
        if (Operation == DesktopOperation.Network)
        {
            CloseRequested = true;
            CancelOperation();
        }
        return false;
    }

    public void Back()
    {
        RequireIdle();
        if (Step > 0) Step--;
    }

    public void Next()
    {
        if (!CanAdvance) throw new InvalidOperationException(L.Get("AdvanceRequiresResult"));
        Step++;
    }

    public void InvalidateReference()
    {
        RequireResultApplicationOrIdle();
        Reference = null;
        InvalidateComparison();
    }

    public void InvalidateGame()
    {
        RequireResultApplicationOrIdle();
        Game = null;
        Identification = null;
        InvalidateComparison();
    }

    public void InvalidateComparison()
    {
        RequireResultApplicationOrIdle();
        ClearComparison();
    }

    private void ClearComparison()
    {
        Comparison = null;
        Rows = [];
        rowsByPath.Clear();
        selected.Clear();
        SelectionConfirmed = false;
    }

    public void AcceptReference(FileListSnapshot reference)
    {
        RequireResultApplicationOrIdle();
        ClearComparison();
        Reference = reference;
    }

    public static void ValidateGameInput(FileListSnapshot game, string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
            throw new ArgumentException(L.Get("ImportRequiresRoot"));
        if (game.Sources.Count != 1 || game.Sources[0].Scan is not { } scan)
            throw new InvalidDataException(L.Get("ImportRequiresScanDocument"));
        if (!Path.IsPathFullyQualified(scan.Root) || !string.Equals(
            DirectoryScanner.NormalizeRootPath(root), DirectoryScanner.NormalizeRootPath(scan.Root),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(L.Get("ImportedRootMismatch"));
    }

    public void AcceptGame(FileListSnapshot game, string root, GameIdentification? identification = null)
    {
        RequireResultApplicationOrIdle();
        ValidateGameInput(game, root);
        GameIdentification? restored = identification ?? (game.Sources[0].Scan?.GameDetections is { } recorded
            ? GameIdentifier.FromDetections(recorded) : null);
        ClearComparison();
        Game = game;
        Identification = restored;
    }

    public void AcceptComparison(ComparisonResult comparison)
    {
        RequireResultApplicationOrIdle();
        if (Game is null || Reference is null) throw new InvalidOperationException(L.Get("ComparisonRequiresInputs"));
        ClearComparison();
        Comparison = comparison;
        Rows = comparison.OnlyA.Select(file => new DifferenceRow(file.Path, file, null, DifferenceCategory.GameOnly))
            .Concat(comparison.OnlyB.Select(file => new DifferenceRow(file.Path, null, file, DifferenceCategory.ReferenceOnly)))
            .Concat(comparison.Different.Select(item => new DifferenceRow(item.A.Path, item.A, item.B, DifferenceCategory.Different)))
            .Concat(comparison.OutsideScope.Select(item => new DifferenceRow(item.File.Path,
                item.PresentIn == FileValues.SideA ? item.File : null, item.PresentIn == FileValues.SideB ? item.File : null, DifferenceCategory.OutsideScope)))
            .OrderBy(row => row.Path, StringComparer.Ordinal).ToArray();
        Filter = DifferenceCategory.All;
        foreach (DifferenceRow row in Rows) rowsByPath.Add(row.Path, row);
    }

    public IEnumerable<DifferenceRow> VisibleRows => Rows.Where(row => Filter == DifferenceCategory.All || row.Category == Filter);

    public void SetFilter(DifferenceCategory filter)
    {
        RequireIdle();
        Filter = filter;
        var visible = VisibleRows.Where(row => row.CanSelect).Select(row => row.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string path in selected.Keys.Where(path => !visible.Contains(path)).ToArray())
        {
            selected.Remove(path);
            SelectionConfirmed = false;
        }
    }

    public bool IsSelected(string path) => selected.ContainsKey(path);

    public void Select(string path, bool value)
    {
        RequireIdle();
        if (!rowsByPath.TryGetValue(path, out DifferenceRow? row) || (Filter != DifferenceCategory.All && row.Category != Filter))
            throw new ArgumentException(L.Get("SelectionNotVisible"));
        if (!row.CanSelect) throw new ArgumentException(L.Get("SelectionRequiresScannedGameFile"));
        bool changed = value ? selected.TryAdd(row.Path, row.Game!) : selected.Remove(row.Path);
        if (changed) SelectionConfirmed = false;
    }

    public void ConfirmSelection(bool value)
    {
        RequireIdle();
        SelectionConfirmed = value && selected.Count > 0 && Comparison is not null;
    }

    private void RequireIdle()
    {
        if (IsBusy) throw new InvalidOperationException(L.Get("OperationBusy"));
    }

    private void RequireResultApplicationOrIdle()
    {
        // Publishing controls can synchronously invalidate dependent results. This does
        // not unlock navigation or another operation, and cleanup has no such permission.
        if (!applyingResult) RequireIdle();
    }
}
