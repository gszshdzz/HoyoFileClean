namespace HoyoFileClean.Desktop;

public sealed class DifferencePanel : UserControl
{
    private readonly WizardSession session;
    private readonly Action<string> locateFile;
    private readonly ResultGrid grid = new()
    {
        Name = "DifferenceGrid", Dock = DockStyle.Fill, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false,
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.CellSelect, BackgroundColor = SystemColors.Window,
        BorderStyle = BorderStyle.None, CellBorderStyle = DataGridViewCellBorderStyle.None, VirtualMode = true, ReadOnly = true
    };
    private readonly CheckBox showHidden = new() { Name = "ShowHiddenResults", Text = L.Get("ShowHiddenResults"), AutoSize = true };
    private readonly Label selection = new() { Name = "SelectionSummary", AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 8) };
    private readonly Dictionary<DifferenceCategory, Button> filters = [];
    private readonly CheckBox selectAll = new()
    {
        Name = "SelectAllResults", AccessibleName = L.Get("SelectAllResults"),
        ThreeState = true, AutoCheck = false, AutoSize = true, Enabled = false
    };
    private readonly HashSet<string> collapsed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Branch> fileBranches = new(StringComparer.OrdinalIgnoreCase);
    private Branch root = new("", "");
    private readonly List<DisplayRow> visible = [];
    private ComparisonResult? renderedComparison;
    private DifferenceCategory[] renderedFilters = [];
    private bool renderedHidden;
    private bool hasRendered;
    private bool rendering;

    public DifferencePanel(WizardSession session, Action<string>? locateFile = null)
    {
        this.session = session;
        this.locateFile = path => WindowsFileLocator.Locate(this, path, locateFile);
        session.SelectionChanged += OnSelectionChanged;
        grid.CheckBoxToggleRequested += ToggleRowSelection;
        Name = "DifferencePanel";
        Dock = DockStyle.Fill;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var top = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        foreach (DifferenceCategory category in Enum.GetValues<DifferenceCategory>())
        {
            var button = new Button { Name = "Filter" + category, AutoSize = true };
            filters.Add(category, button);
            top.Controls.Add(button);
            button.Click += (_, _) =>
            {
                if (session.IsBusy) return;
                session.SetFilters(session.Filters.Contains(category)
                    ? session.Filters.Where(value => value != category)
                    : session.Filters.Append(category));
                Render();
            };
        }
        top.Controls.Add(showHidden);
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "", Width = 32, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, ThreeState = true, ValueType = typeof(CheckState) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Game", HeaderText = L.Get("GameTreeHeading"), ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "", ReadOnly = true, Width = 38, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Reference", HeaderText = L.Get("ReferenceTreeHeading"), ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.Columns[2].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        grid.Controls.Add(selectAll);
        selectAll.Click += (_, _) =>
        {
            if (session.IsBusy || root.SelectableCount == 0) return;
            session.SelectRange(SelectablePaths(root), root.CheckState != CheckState.Checked);
            RefreshSelection();
        };
        grid.Layout += (_, _) => PositionSelectAll();
        grid.Scroll += (_, _) => PositionSelectAll();
        grid.ColumnWidthChanged += (_, _) => PositionSelectAll();
        grid.ColumnHeadersHeightChanged += (_, _) => PositionSelectAll();
        layout.Controls.Add(top, 0, 0);
        layout.Controls.Add(grid, 0, 1);
        layout.Controls.Add(selection, 0, 2);
        Controls.Add(layout);
        showHidden.CheckedChanged += (_, _) =>
        {
            if (rendering || session.IsBusy) return;
            session.SetShowHiddenResults(showHidden.Checked);
            Render();
        };
        // Clicks update session state directly; cells have no independent edit state.
        grid.CellValueNeeded += (_, args) =>
        {
            if (args.RowIndex < 0 || args.RowIndex >= visible.Count) return;
            DisplayRow item = visible[args.RowIndex];
            if (args.ColumnIndex == 0) args.Value = item.Directory?.CheckState ??
                (item.File is { CanSelect: true } row && session.IsSelected(row.Path) ? CheckState.Checked : CheckState.Unchecked);
            else if (item.Directory is { } directory)
            {
                string label = new string(' ', item.Depth * 4) + (collapsed.Contains(directory.Path) ? "▶ " : "▼ ") + directory.Name + "/";
                args.Value = args.ColumnIndex switch { 1 => directory.HasGame ? label : "", 3 => directory.HasReference ? label : "", _ => "" };
            }
            else if (item.File is { } file)
                args.Value = args.ColumnIndex switch { 1 => FileText(file.Game, item.Depth), 3 => FileText(file.Reference, item.Depth), _ => Status(file.Category) };
        };
        grid.CellValuePushed += (_, args) =>
        {
            if (rendering || args.ColumnIndex != 0) return;
            SetRowSelection(args.RowIndex, args.Value is true or CheckState.Checked);
        };
        grid.CellDoubleClick += (_, args) =>
        {
            if (args.ColumnIndex == 0) ToggleRowSelection(args.RowIndex);
            else if (!session.IsBusy && args.ColumnIndex == 1 && FileAtRow(args.RowIndex) is { Game: not null } row
                && session.Game?.Sources[0].Scan is { } scan)
                this.locateFile(Path.Combine(scan.Root, row.Path.Replace('/', Path.DirectorySeparatorChar)));
        };
        grid.SelectionChanged += (_, _) => grid.ClearSelection();
        grid.CellFormatting += (_, args) =>
        {
            Color color = FileAtRow(args.RowIndex)?.Category switch
            {
                DifferenceCategory.Different => Color.Firebrick,
                DifferenceCategory.GameOnly or DifferenceCategory.ReferenceOnly => Color.Purple,
                _ => SystemColors.WindowText
            };
            args.CellStyle.ForeColor = args.CellStyle.SelectionForeColor = color;
            args.CellStyle.BackColor = args.CellStyle.SelectionBackColor = SystemColors.Window;
        };
        grid.CellPainting += (_, args) =>
        {
            if (args.RowIndex < 0 || args.RowIndex >= visible.Count || args.ColumnIndex < 0) return;
            DataGridViewPaintParts parts = args.PaintParts & ~DataGridViewPaintParts.Focus & ~DataGridViewPaintParts.SelectionBackground;
            if (args.ColumnIndex == 0 && visible[args.RowIndex].Directory is null && FileAtRow(args.RowIndex) is not { CanSelect: true }) parts &= ~DataGridViewPaintParts.ContentForeground;
            args.Paint(args.ClipBounds, parts);
            args.Handled = true;
        };
        grid.CellToolTipTextNeeded += (_, args) =>
        {
            if (FileAtRow(args.RowIndex) is not { } row) return;
            args.ToolTipText = args.ColumnIndex switch
            {
                1 or 3 => row.Path,
                2 when row.Category == DifferenceCategory.Equal => L.Get(row.Game?.Hash is not null && row.Reference?.Hash is not null ? "ResultEqualHashHint" : "ResultEqualSizeHint"),
                2 => CategoryName(row.Category), _ => ""
            };
        };
        grid.CellClick += (_, args) =>
        {
            if (args.ColumnIndex == 0) { ToggleRowSelection(args.RowIndex); return; }
            if (session.IsBusy || args.RowIndex < 0 || args.ColumnIndex is not (1 or 3)) return;
            if (args.RowIndex >= visible.Count || visible[args.RowIndex].Directory is not { } directory) return;
            if (!collapsed.Add(directory.Path)) collapsed.Remove(directory.Path);
            RebuildRows();
            RefreshSelection();
        };
        Render();
    }

    public void Render()
    {
        if (!ReferenceEquals(renderedComparison, session.Comparison)) collapsed.Clear();
        if (!hasRendered || !ReferenceEquals(renderedComparison, session.Comparison) ||
            !renderedFilters.SequenceEqual(session.Filters) || renderedHidden != session.ShowHiddenResults)
        {
            BuildTree();
            RebuildRows();
            renderedComparison = session.Comparison;
            renderedFilters = session.Filters.ToArray();
            renderedHidden = session.ShowHiddenResults;
            hasRendered = true;
        }
        DifferenceRow[] displayRows = session.DisplayRows.ToArray();
        foreach ((DifferenceCategory category, Button button) in filters)
        {
            int count = displayRows.Count(row => row.Category == category);
            bool active = session.Filters.Contains(category);
            button.Text = (active ? "✓ " : "") + L.Format("ResultFilterCount", CategoryName(category), count);
            button.BackColor = active ? SystemColors.ControlLight : SystemColors.Control;
        }
        RefreshSelection();
    }

    private void BuildTree()
    {
        root = new Branch("", "");
        fileBranches.Clear();
        foreach (DifferenceRow row in session.VisibleRows)
        {
            string[] parts = row.Path.Split('/');
            Branch branch = root;
            bool selected = row.CanSelect && session.IsSelected(row.Path);
            branch.Include(row, selected);
            foreach (string part in parts[..^1])
            {
                if (!branch.Directories.TryGetValue(part, out Branch? child))
                {
                    string path = branch.Path.Length == 0 ? part : branch.Path + "/" + part;
                    branch.Directories.Add(part, child = new Branch(part, path, branch));
                }
                branch = child;
                branch.Include(row, selected);
            }
            branch.Files.Add(row);
            if (row.CanSelect) fileBranches.Add(row.Path, branch);
        }
    }

    private void RebuildRows()
    {
        rendering = true;
        grid.SuspendLayout();
        try
        {
            int first = grid.FirstDisplayedScrollingRowIndex;
            string? anchor = first >= 0 && first < visible.Count ? visible[first].Path : null;
            grid.CancelEdit();
            grid.CurrentCell = null;
            visible.Clear();
            AddChildren(root, 0);
            grid.RowCount = visible.Count;
            grid.ClearSelection();
            if (anchor is not null && visible.Count > 0)
            {
                int restored = visible.FindIndex(row => row.Path.Equals(anchor, StringComparison.OrdinalIgnoreCase));
                grid.FirstDisplayedScrollingRowIndex = restored >= 0 ? restored : Math.Min(first, visible.Count - 1);
            }
            grid.Invalidate();
        }
        finally { grid.ResumeLayout(); rendering = false; }
    }

    private void AddChildren(Branch branch, int depth)
    {
        foreach (Branch child in branch.Directories.Values)
        {
            visible.Add(new DisplayRow(null, child, depth));
            if (!collapsed.Contains(child.Path)) AddChildren(child, depth + 1);
        }
        foreach (DifferenceRow row in branch.Files)
            visible.Add(new DisplayRow(row, null, depth));
    }

    internal DifferenceRow? FileAtRow(int index) => index >= 0 && index < visible.Count ? visible[index].File : null;

    private void ToggleRowSelection(int index)
    {
        if (index < 0 || index >= visible.Count) return;
        DisplayRow row = visible[index];
        bool value = row.Directory is { } directory ? directory.CheckState != CheckState.Checked
            : row.File is { } file && !session.IsSelected(file.Path);
        SetRowSelection(index, value);
    }

    private void SetRowSelection(int index, bool value)
    {
        if (session.IsBusy || index < 0 || index >= visible.Count) return;
        DisplayRow row = visible[index];
        if (row.Directory is { SelectableCount: > 0 } directory) session.SelectRange(SelectablePaths(directory), value);
        else if (row.File is { CanSelect: true } file) session.Select(file.Path, value);
        else return;
        RefreshSelection();
    }

    private static IEnumerable<string> SelectablePaths(Branch branch) => branch.Files.Where(row => row.CanSelect).Select(row => row.Path)
        .Concat(branch.Directories.Values.SelectMany(SelectablePaths));

    private void OnSelectionChanged(string path, bool value)
    {
        if (!fileBranches.TryGetValue(path, out Branch? branch)) return;
        for (Branch? current = branch; current is not null; current = current.Parent)
            current.SelectedCount += value ? 1 : -1;
    }

    private void PositionSelectAll()
        => GridSelectionHeader.Position(grid, selectAll);

    private static string FileText(ManifestFile? file, int depth) => file is null ? "" :
        new string(' ', depth * 4) + file.Path.Split('/')[^1] + "  ·  " + FileSizeText.Format(file.Size);

    private static string Status(DifferenceCategory category) => category switch
    {
        DifferenceCategory.GameOnly => "←", DifferenceCategory.ReferenceOnly => "→",
        DifferenceCategory.Different => "≠", DifferenceCategory.Equal => "=", _ => "—"
    };

    private void RefreshSelection()
    {
        bool previous = rendering;
        rendering = true;
        try
        {
            showHidden.Checked = session.ShowHiddenResults;
            showHidden.Enabled = !session.IsBusy;
            foreach (Button button in filters.Values) button.Enabled = !session.IsBusy;
            selectAll.Enabled = !session.IsBusy && root.SelectableCount > 0;
            selectAll.CheckState = root.CheckState;
            grid.InvalidateColumn(0);
            selection.Text = L.Format("SelectionSummary", session.SelectedCount, FileSizeText.Format(session.SelectedSize));
        }
        finally { rendering = previous; }
    }

    private static string CategoryName(DifferenceCategory category) => DifferenceText.CategoryName(category);

    private sealed class ResultGrid : DataGridView
    {
        internal ResultGrid() => DoubleBuffered = true;
        internal event Action<int>? CheckBoxToggleRequested;

        protected override void OnKeyDown(KeyEventArgs args)
        {
            if (args.KeyCode == Keys.Space && args.Modifiers == Keys.None && CurrentCell?.ColumnIndex == 0)
            {
                CheckBoxToggleRequested?.Invoke(CurrentCell.RowIndex);
                args.SuppressKeyPress = true;
                return;
            }
            base.OnKeyDown(args);
        }

        protected override void OnKeyUp(KeyEventArgs args)
        {
            if (args.KeyCode == Keys.Space && args.Modifiers == Keys.None && CurrentCell?.ColumnIndex == 0)
            {
                args.Handled = true;
                return;
            }
            base.OnKeyUp(args);
        }
    }

    private sealed record DisplayRow(DifferenceRow? File, Branch? Directory, int Depth)
    {
        public string Path => File?.Path ?? Directory!.Path;
    }
    private sealed class Branch(string name, string path, Branch? parent = null)
    {
        public string Name { get; } = name;
        public string Path { get; } = path;
        public Branch? Parent { get; } = parent;
        public SortedDictionary<string, Branch> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<DifferenceRow> Files { get; } = [];
        public bool HasGame { get; set; }
        public bool HasReference { get; set; }
        public int SelectableCount { get; set; }
        public int SelectedCount { get; set; }
        public CheckState CheckState => SelectedCount == 0 ? CheckState.Unchecked
            : SelectedCount == SelectableCount ? CheckState.Checked : CheckState.Indeterminate;

        public void Include(DifferenceRow row, bool selected)
        {
            HasGame |= row.Game is not null;
            HasReference |= row.Reference is not null;
            if (row.CanSelect) SelectableCount++;
            if (selected) SelectedCount++;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) session.SelectionChanged -= OnSelectionChanged;
        base.Dispose(disposing);
    }
}
