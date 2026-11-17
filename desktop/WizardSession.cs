namespace HoyoFileClean.Desktop;

public enum DifferenceCategory { GameOnly, ReferenceOnly, Different, OutsideScope, Equal }

internal enum DesktopOperation { Work, Parsing, Network, Scan }

public sealed record DifferenceRow(string Path, ManifestFile? Game, ManifestFile? Reference,
    DifferenceCategory Category)
{
    public bool CanSelect => Game is not null && Category != DifferenceCategory.OutsideScope;
}

// UI-independent state. Neither paths in a report nor a checked tree node authorize file operations.
public sealed class WizardSession
{
    private static readonly IReadOnlyList<DifferenceCategory> defaultFilters = Enum.GetValues<DifferenceCategory>()
        .Where(category => category != DifferenceCategory.Equal).ToList().AsReadOnly();
    private readonly Dictionary<string, ManifestFile> selected = new(StringComparer.OrdinalIgnoreCase);
    private long selectedSize;
    internal event Action<string, bool>? SelectionChanged;
    private readonly Dictionary<string, DifferenceRow> rowsByPath = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> resultHiddenPaths = new(StringComparer.OrdinalIgnoreCase);
    public FileListSnapshot? Reference { get; private set; }
    public FileListSnapshot? Game { get; private set; }
    public GameIdentification? Identification { get; private set; }
    public ComparisonResult? Comparison { get; private set; }
    public IReadOnlyList<DifferenceRow> Rows { get; private set; } = [];
    public IReadOnlyList<DifferenceCategory> Filters { get; private set; } = defaultFilters;
    public bool ShowHiddenResults { get; private set; }
    public bool IsResultHidden(string path) => resultHiddenPaths.Contains(path);
    public IEnumerable<DifferenceRow> DisplayRows => Rows.Where(row => ShowHiddenResults || !IsResultHidden(row.Path));
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
    public long SelectedSize => selectedSize;
    public bool CanDemonstrateDeletion => !IsBusy && selected.Count > 0 && SelectionConfirmed && Comparison is not null;
    public bool CanAdvance => !IsBusy && Step switch
    {
        0 => Game is not null,
        1 => Reference is not null,
        2 => Comparison is not null,
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
        resultHiddenPaths.Clear();
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
        foreach (ManifestFile file in selected.Values.ToArray()) SetSelected(file, false);
        SelectionConfirmed = false;
        ShowHiddenResults = false;
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
        resultHiddenPaths = (restored is null ? [] : GameIdentifier.GetExclusionPreset(restored)?.ResultHiddenFiles ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
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
        Filters = defaultFilters;
        var differences = Rows.Select(row => row.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Rows = Rows.Concat(Game.Files.Values.Where(file => !differences.Contains(file.Path) && Reference.Files.ContainsKey(file.Path))
            .Select(file => new DifferenceRow(file.Path, file, Reference.Files[file.Path], DifferenceCategory.Equal)))
            .OrderBy(row => row.Path, StringComparer.Ordinal).ToArray();
        foreach (DifferenceRow row in Rows) rowsByPath.Add(row.Path, row);
    }

    public IEnumerable<DifferenceRow> VisibleRows => DisplayRows.Where(row => Filters.Contains(row.Category));

    public void SetShowHiddenResults(bool value)
    {
        RequireIdle();
        ShowHiddenResults = value;
        if (!value)
            foreach (string path in selected.Keys.Where(IsResultHidden).ToArray()) SetSelected(selected[path], false);
        SelectionConfirmed = false;
    }

    public void SetFilters(IEnumerable<DifferenceCategory> filters)
    {
        RequireIdle();
        Filters = filters.Distinct().ToList().AsReadOnly();
        var visible = VisibleRows.Where(row => row.CanSelect).Select(row => row.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string path in selected.Keys.Where(path => !visible.Contains(path)).ToArray())
        {
            SetSelected(selected[path], false);
        }
    }

    public bool IsSelected(string path) => selected.ContainsKey(path);

    public void Select(string path, bool value)
        => SelectRange([path], value);

    public void SelectRange(IEnumerable<string> paths, bool value)
    {
        RequireIdle();
        DifferenceRow[] rows = paths.Distinct(StringComparer.OrdinalIgnoreCase).Select(RequireSelectable).ToArray();
        foreach (DifferenceRow row in rows)
            SetSelected(row.Game!, value);
    }

    private void SetSelected(ManifestFile file, bool value)
    {
        bool changed = value ? selected.TryAdd(file.Path, file) : selected.Remove(file.Path);
        if (!changed) return;
        selectedSize += value ? file.Size : -file.Size;
        SelectionConfirmed = false;
        SelectionChanged?.Invoke(file.Path, value);
    }

    private DifferenceRow RequireSelectable(string path)
    {
        if (!rowsByPath.TryGetValue(path, out DifferenceRow? row) || (!ShowHiddenResults && IsResultHidden(path)) || !Filters.Contains(row.Category))
            throw new ArgumentException(L.Get("SelectionNotVisible"));
        if (!row.CanSelect) throw new ArgumentException(L.Get("SelectionRequiresScannedGameFile"));
        return row;
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
