namespace HoyoFileClean.Desktop;

public enum DifferenceCategory { All, GameOnly, ReferenceOnly, Different, OutsideScope }

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
    public ComparisonResult? Comparison { get; private set; }
    public IReadOnlyList<DifferenceRow> Rows { get; private set; } = [];
    public DifferenceCategory Filter { get; private set; }
    public int Step { get; private set; }
    public bool IsBusy { get; private set; }
    public bool SelectionConfirmed { get; private set; }
    public int SelectedCount => selected.Count;
    public long SelectedSize => selected.Values.Sum(file => file.Size);
    public bool CanDemonstrateDeletion => !IsBusy && selected.Count > 0 && SelectionConfirmed && Comparison is not null;
    public bool CanAdvance => !IsBusy && Step switch
    {
        0 => Reference is not null,
        1 => Game is not null,
        2 => Comparison is not null,
        _ => false
    };

    public async Task RunAsync<T>(Func<T> work, Action<T> apply)
    {
        RequireIdle();
        IsBusy = true;
        try
        {
            T result = await Task.Run(work);
            IsBusy = false;
            apply(result);
        }
        finally { IsBusy = false; }
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
        RequireIdle();
        Reference = null;
        InvalidateComparison();
    }

    public void InvalidateGame()
    {
        RequireIdle();
        Game = null;
        InvalidateComparison();
    }

    public void InvalidateComparison()
    {
        RequireIdle();
        Comparison = null;
        Rows = [];
        rowsByPath.Clear();
        selected.Clear();
        SelectionConfirmed = false;
    }

    public void AcceptReference(FileListSnapshot reference)
    {
        RequireIdle();
        InvalidateComparison();
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

    public void AcceptGame(FileListSnapshot game, string root)
    {
        RequireIdle();
        ValidateGameInput(game, root);
        InvalidateComparison();
        Game = game;
    }

    public void AcceptComparison(ComparisonResult comparison)
    {
        RequireIdle();
        if (Game is null || Reference is null) throw new InvalidOperationException(L.Get("ComparisonRequiresInputs"));
        InvalidateComparison();
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
}
