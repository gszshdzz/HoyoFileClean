using System.ComponentModel;

namespace HoyoFileClean.Desktop;

internal sealed class ManifestTable : DataGridView
{
    public event Action? CheckedFilesChanged;
    internal event Action<ReferenceInput, FileListSnapshot, string>? DetailsRequested;
    private bool updating;
    private readonly bool groupByModifiedHour;
    private bool[] hourBands = [];
    private readonly CheckBox selectAll = new()
    {
        Name = "SelectAllManifests", AccessibleName = L.Get("SelectAllManifests"),
        ThreeState = true, AutoCheck = false, AutoSize = true, Enabled = false
    };

    public ManifestTable(string name, bool groupByModifiedHour = false)
    {
        this.groupByModifiedHour = groupByModifiedHour;
        DoubleBuffered = true;
        Name = name;
        Dock = DockStyle.Fill;
        AllowUserToAddRows = AllowUserToDeleteRows = false;
        AllowUserToResizeRows = false;
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        AutoGenerateColumns = false;
        RowHeadersVisible = false;
        SelectionMode = DataGridViewSelectionMode.CellSelect;
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Checked", HeaderText = "", Width = 35, AutoSizeMode = DataGridViewAutoSizeColumnMode.None
        });
        AddColumn("Name", L.Get("ManifestColumnName"), typeof(string), 8);
        AddColumn("Modified", L.Get("ManifestColumnModified"), typeof(DateTime), 7);
        AddColumn("Size", L.Get("ManifestColumnSize"), typeof(long), 3);
        AddColumn("State", L.Get("ManifestColumnState"), typeof(string), 3);
        AddColumn("Game", L.Get("ManifestColumnGame"), typeof(string), 3);
        AddColumn("Version", L.Get("ManifestColumnVersion"), typeof(string), 2);
        AddColumn("Package", L.Get("ManifestColumnPackage"), typeof(string), 4);
        AddColumn("Entries", L.Get("ManifestColumnEntryCount"), typeof(int), 3);
        Controls.Add(selectAll);
        selectAll.Click += (_, _) => ToggleAll();
        Layout += (_, _) => PositionSelectAll();
        Scroll += (_, _) => PositionSelectAll();
        ColumnWidthChanged += (_, _) => PositionSelectAll();
        ColumnHeadersHeightChanged += (_, _) => PositionSelectAll();
        RowsAdded += (_, _) => { if (!updating) { RefreshSelectAll(); RebuildHourBands(); } };
        RowsRemoved += (_, _) => { if (!updating) { RefreshSelectAll(); RebuildHourBands(); CheckedFilesChanged?.Invoke(); } };
        Sorted += (_, _) => { if (!updating) RebuildHourBands(); };
        CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (IsCurrentCellDirty && CurrentCell is DataGridViewCheckBoxCell)
                CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        CellValueChanged += (_, args) =>
        {
            if (!updating && args.RowIndex >= 0 && args.ColumnIndex == 0)
            {
                RefreshSelectAll();
                CheckedFilesChanged?.Invoke();
            }
        };
        CellFormatting += (_, args) =>
        {
            if (args.RowIndex >= 0 && args.RowIndex < hourBands.Length && hourBands[args.RowIndex]
                && !SystemInformation.HighContrast)
                args.CellStyle!.BackColor = HourBandColor(args.CellStyle.BackColor, args.CellStyle.ForeColor);
            if (args.ColumnIndex == 3 && args.Value is long bytes)
            {
                args.Value = FormatSize(bytes);
                args.FormattingApplied = true;
                return;
            }
            if (args.ColumnIndex != 2 || args.Value is not DateTime utc) return;
            args.Value = utc.ToLocalTime().ToString(L.Get("ManifestModifiedFormat"));
            args.FormattingApplied = true;
        };
        SelectionChanged += (_, _) => { if (!updating) ClearSelection(); };
        CellDoubleClick += (_, args) => OpenDetails(args.RowIndex, args.ColumnIndex);
        Sort(Columns[groupByModifiedHour ? "Modified" : "Name"]!,
            groupByModifiedHour ? ListSortDirection.Descending : ListSortDirection.Ascending);
    }

    private void RebuildHourBands()
    {
        hourBands = [];
        if (!groupByModifiedHour || SortedColumn?.Name != "Modified")
        {
            Invalidate();
            return;
        }
        hourBands = new bool[Rows.Count];
        long? previousHour = null;
        bool band = false;
        for (int i = 0; i < Rows.Count; i++)
        {
            if (Rows[i].Cells["Modified"].Value is not DateTime utc) continue;
            long hour = utc.ToLocalTime().Ticks / TimeSpan.TicksPerHour;
            if (previousHour is not null && previousHour != hour) band = !band;
            hourBands[i] = band;
            previousHour = hour;
        }
        Invalidate();
    }

    internal static Color HourBandColor(Color background, Color foreground)
        => Color.FromArgb((background.R * 9 + foreground.R) / 10,
            (background.G * 9 + foreground.G) / 10,
            (background.B * 9 + foreground.B) / 10);

    private void PositionSelectAll()
    {
        if (Columns.Count == 0) return;
        Rectangle header = GetCellDisplayRectangle(0, -1, true);
        Size size = selectAll.GetPreferredSize(Size.Empty);
        Rectangle bounds = new(header.Left + (header.Width - size.Width) / 2,
            header.Top + (header.Height - size.Height) / 2, size.Width, size.Height);
        if (selectAll.Bounds != bounds) selectAll.Bounds = bounds;
        bool visible = ColumnHeadersVisible && header.Width >= size.Width && header.Height >= size.Height;
        if (selectAll.Visible != visible) selectAll.Visible = visible;
    }

    private void RefreshSelectAll()
    {
        int count = Rows.Cast<DataGridViewRow>().Count(row => row.Cells[0].Value is true);
        selectAll.Enabled = !ReadOnly && Rows.Count > 0;
        selectAll.CheckState = count == 0 ? CheckState.Unchecked
            : count == Rows.Count ? CheckState.Checked : CheckState.Indeterminate;
    }

    internal void ToggleAll()
    {
        if (ReadOnly || Rows.Count == 0) return;
        EndEdit();
        bool value = selectAll.CheckState != CheckState.Checked;
        updating = true;
        try
        {
            foreach (DataGridViewRow row in Rows) row.Cells[0].Value = value;
        }
        finally { updating = false; }
        RefreshSelectAll();
        CheckedFilesChanged?.Invoke();
    }

    internal void SetBusy(bool busy)
    {
        if (ReadOnly == busy) return;
        ReadOnly = busy;
        if (!busy)
            foreach (DataGridViewColumn column in Columns) column.ReadOnly = column.Index != 0;
        RefreshSelectAll();
    }

    internal static string FormatSize(long bytes, bool detailedGigabytes = false)
    {
        decimal value = bytes / 1024m;
        string[] units = [L.Get("UnitKB"), L.Get("UnitMB"), L.Get("UnitGB"),
            L.Get("UnitTB"), L.Get("UnitPB"), L.Get("UnitEB")];
        int unitIndex = 0;
        while (value >= (detailedGigabytes && unitIndex == 2 ? 999.95m : 999.5m)
            && unitIndex < units.Length - 1) { value /= 1024m; unitIndex++; }
        int decimals = detailedGigabytes && unitIndex == 2 && value >= 99.95m ? 1
            : value >= 99.95m ? 0 : value >= 9.995m ? 1 : 2;
        return Math.Round(value, decimals, MidpointRounding.AwayFromZero)
            .ToString(decimals switch { 0 => "0", 1 => "0.#", _ => "0.##" }, System.Globalization.CultureInfo.InvariantCulture) + units[unitIndex];
    }

    private DataGridViewColumn AddColumn(string name, string title, Type type, float weight)
    {
        var column = new DataGridViewTextBoxColumn
        {
            Name = name, HeaderText = title, ValueType = type, ReadOnly = true,
            FillWeight = weight, MinimumWidth = 45, SortMode = DataGridViewColumnSortMode.Automatic
        };
        Columns.Add(column);
        return column;
    }

    internal void AddFiles(IEnumerable<LocalManifestFile> files, bool check = false)
    {
        LocalManifestFile[] complete = files.ToArray();
        var known = Rows.Cast<DataGridViewRow>().Select(row => Data(row).Input.File.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        DataGridViewRow[] added = complete.Where(file => known.Add(file.FullPath)).Select(file =>
        {
            var row = new DataGridViewRow { Tag = new ManifestRow(new ReferenceInput(file)) };
            row.CreateCells(this, check, file.Name, file.LastWriteTimeUtc, file.Size, StateText(ReferenceState.Unparsed), null!, null!, null!, null!);
            return row;
        }).ToArray();
        if (added.Length == 0) return;
        ChangeRows(() =>
        {
            Rows.AddRange(added);
            Sort(SortedColumn!, SortOrder == SortOrder.Descending ? ListSortDirection.Descending : ListSortDirection.Ascending);
        });
    }

    internal ReferenceInput[] CheckedInputs => Rows.Cast<DataGridViewRow>()
        .Where(row => row.Cells[0].Value is true).Select(row => Data(row).Input).ToArray();

    internal void RemoveCheckedFiles()
    {
        if (ReadOnly) return;
        DataGridViewRow[] removed = Rows.Cast<DataGridViewRow>().Where(row => row.Cells[0].Value is true).ToArray();
        if (removed.Length == 0) return;
        ChangeRows(() =>
        {
            foreach (DataGridViewRow row in removed) Rows.Remove(row);
        });
    }

    private void ChangeRows(Action change)
    {
        SuspendLayout();
        updating = true;
        try { change(); ClearSelection(); }
        finally
        {
            updating = false;
            RefreshSelectAll();
            RebuildHourBands();
            ResumeLayout();
        }
        CheckedFilesChanged?.Invoke();
    }

    internal void AddNetworkFiles(IEnumerable<ReferenceInput> inputs, string game, string version)
    {
        DataGridViewRow[] rows = inputs.Select(input =>
        {
            var row = new DataGridViewRow { Tag = new ManifestRow(input) { State = ReferenceState.Unfetched } };
            row.CreateCells(this, false, input.Remote!.Category, L.Get("UnavailableValue"),
                input.Remote.DownloadSize, StateText(ReferenceState.Unfetched), game, version, input.Remote.MatchingField, null!);
            return row;
        }).ToArray();
        ChangeRows(() =>
        {
            Rows.AddRange(rows);
            Sort(Columns["Name"]!, ListSortDirection.Ascending);
        });
    }

    internal void ResetResults()
    {
        foreach (DataGridViewRow row in Rows)
        {
            ManifestRow data = Data(row);
            SetState(row, data.Input.Remote is null ? ReferenceState.Unparsed : ReferenceState.Unfetched);
            row.Cells["Entries"].Value = null;
            data.Snapshot = null;
            if (data.Input.Remote is null) SetMetadata(row, null);
        }
    }

    internal void ShowResults(IEnumerable<(ReferenceInput Input, ReferenceState State, int? Entries)> results)
    {
        var rows = Rows.Cast<DataGridViewRow>().ToDictionary(row => Data(row).Input);
        SuspendLayout();
        try
        {
            foreach (var result in results)
            {
                DataGridViewRow row = rows[result.Input];
                SetState(row, result.State);
                row.Cells["Entries"].Value = result.Entries;
                Data(row).Snapshot = null;
                if (result.Input.Remote is null) SetMetadata(row, null);
            }
        }
        finally { ResumeLayout(); }
    }

    internal void ResetFetching()
    {
        foreach (DataGridViewRow row in Rows)
            if (Data(row).State is ReferenceState.Fetching) SetState(row, ReferenceState.Unfetched);
    }

    internal void ShowParsedResults(ReferenceInput[] inputs, IReadOnlyList<ReferenceParseItem> items)
    {
        ShowResults(inputs.Select((input, index) => (input,
            items[index].Error is null ? ReferenceState.Parsed : ReferenceState.Failed, items[index].Entries)));
        ShowMetadata(inputs.Select((input, index) => (input, items[index].Metadata)));
        var rows = Rows.Cast<DataGridViewRow>().ToDictionary(row => Data(row).Input);
        for (int index = 0; index < inputs.Length; index++)
            if (items[index].Error is null) Data(rows[inputs[index]]).Snapshot = items[index].Snapshot;
    }

    internal void OpenDetails(int rowIndex, int columnIndex)
    {
        if (ReadOnly || rowIndex < 0 || rowIndex >= Rows.Count || columnIndex <= 0) return;
        DataGridViewRow row = Rows[rowIndex];
        ManifestRow data = Data(row);
        if (data.State is not ReferenceState.Parsed || data.Snapshot is not { } snapshot) return;
        string information = string.Join("      ", new[] { "Game", "Version", "Package" }.Select(column =>
            Columns[column]!.HeaderText + ": " + row.Cells[column].Value));
        DetailsRequested?.Invoke(data.Input, snapshot, information);
    }

    internal void ShowMetadata(IEnumerable<(ReferenceInput Input, IReadOnlyDictionary<string, ManifestMetadataField>? Metadata)> results)
    {
        var rows = Rows.Cast<DataGridViewRow>().ToDictionary(row => Data(row).Input);
        foreach (var result in results)
            if (result.Input.Remote is null) SetMetadata(rows[result.Input], result.Metadata);
    }

    private static void SetMetadata(DataGridViewRow row, IReadOnlyDictionary<string, ManifestMetadataField>? metadata)
    {
        foreach (var (column, field) in new[] { ("Game", "game"), ("Version", "version"), ("Package", "package") })
        {
            ManifestMetadataField? result = field == "game"
                ? metadata?.GetValueOrDefault("biz") ?? metadata?.GetValueOrDefault("game")
                : metadata?.GetValueOrDefault(field);
            bool attempted = Data(row).State is ReferenceState.Parsed or ReferenceState.Failed or ReferenceState.Cancelled;
            string? display = result?.Value;
            if (result is { Multiple: true, Conflict: false } && display is null)
                display = string.Join(L.Get("ListSeparator"), result.Matches.Select(match => match.Value).Distinct(StringComparer.Ordinal));
            row.Cells[column].Value = result is null ? (attempted ? L.Get("UnavailableValue") : null)
                : result.Conflict ? L.Get("MetadataConflict") : display;
            row.Cells[column].ToolTipText = result is null ? "" : string.Join(Environment.NewLine,
                result.Matches.Select(match => $"{match.Id}: {match.Value}"));
            if (field == "game" && metadata is not null)
            {
                ManifestMetadataField? biz = metadata.GetValueOrDefault("biz");
                row.Cells[column].ToolTipText += Environment.NewLine + L.Get("ManifestOfficialBizLabel") + ": "
                    + (biz is null ? L.Get("UnavailableValue") : biz.Conflict ? L.Get("MetadataConflict") : biz.Value);
                if (biz is not null) row.Cells[column].ToolTipText += Environment.NewLine
                    + string.Join(Environment.NewLine, biz.Matches.Select(match => $"{match.Id}: {match.Value}"));
            }
        }
    }

    private static void SetState(DataGridViewRow row, ReferenceState state)
    {
        Data(row).State = state;
        row.Cells["State"].Value = StateText(state);
        // Keep data numeric/null; the native null-display style owns presentation and repainting.
        row.Cells["Entries"].Style.NullValue = state is ReferenceState.Failed or ReferenceState.Cancelled
            ? L.Get("UnavailableValue") : "";
    }

    private static ManifestRow Data(DataGridViewRow row) => (ManifestRow)row.Tag!;

    private sealed class ManifestRow(ReferenceInput input)
    {
        internal ReferenceInput Input { get; } = input;
        internal ReferenceState State { get; set; }
        internal FileListSnapshot? Snapshot { get; set; }
    }

    internal static string StateText(ReferenceState state) => L.Get(state switch
    {
        ReferenceState.Unparsed => "StateUnparsed",
        ReferenceState.Unfetched => "StateUnfetched",
        ReferenceState.Pending => "StatePending",
        ReferenceState.Fetching => "StateFetching",
        ReferenceState.Parsed => "StateParsed",
        ReferenceState.Failed => "StateFailed",
        ReferenceState.Cancelled => "StateCancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    });
}

internal enum ReferenceState { Unparsed, Unfetched, Pending, Fetching, Parsed, Failed, Cancelled }
