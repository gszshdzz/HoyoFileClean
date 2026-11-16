namespace HoyoFileClean.Desktop;

internal sealed class ComparisonPanel : UserControl
{
    private readonly Dictionary<string, Label> values = [];
    private readonly ToolTip tips = new();

    internal ComparisonPanel()
    {
        Name = "ComparisonResult";
        Dock = DockStyle.Fill;
        AutoScroll = true;
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4 };
        foreach (float width in new[] { 25f, 25f, 25f, 25f })
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
        var scope = new TableLayoutPanel { Name = "ComparisonScope", Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        scope.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddHeading(scope, "ComparisonScopeHeading", 0, 1);
        foreach (var (field, text) in new[]
        {
            ("ExcludedDirectories", L.Get("ComparisonExcludedDirectoriesLabel")),
            ("ExcludedFiles", L.Get("ComparisonExcludedFilesLabel")),
            ("SkippedLinks", L.Get("ComparisonSkippedLinksLabel"))
        })
        {
            scope.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            scope.Controls.Add(new Label { Text = text, AutoSize = true, Dock = DockStyle.Top,
                Margin = new Padding(3, 6, 3, 3) }, 0, scope.RowCount++);
            var value = new Label { Name = "ComparisonGame" + field, AutoSize = true, Dock = DockStyle.Top, UseMnemonic = false,
                Margin = new Padding(3, 3, 9, 3) };
            scope.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            scope.Controls.Add(value, 0, scope.RowCount++);
            values.Add("Game" + field, value);
        }
        scope.SizeChanged += (_, _) =>
        {
            foreach (Control label in scope.Controls)
                label.MaximumSize = new Size(Math.Max(1, scope.ClientSize.Width - label.Margin.Horizontal), 0);
        };
        var inputs = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4 };
        inputs.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        inputs.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        AddHeading(inputs, "ComparisonGameHeading", 0, 2);
        AddHeading(inputs, "ComparisonManifestHeading", 2, 2);
        (string Field, string Label)[] fields =
        [
            ("Game", L.Get("ComparisonGameLabel")), ("Version", L.Get("ComparisonVersionLabel")),
            ("Location", L.Get("ComparisonRootLabel")), ("Files", L.Get("ComparisonFilesLabel")),
            ("Size", L.Get("ComparisonSizeLabel")), ("Md5", L.Get("ComparisonMd5Label"))
        ];
        for (int row = 0; row < fields.Length; row++)
        {
            inputs.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var (field, label) = fields[row];
            AddInput(inputs, "Game", field, label, 0, row + 1);
            AddInput(inputs, "Reference", field,
                field == "Location" ? L.Get("ComparisonPackagesLabel") : label, 2, row + 1);
        }
        var output = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        output.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddHeading(output, "ComparisonOutputHeading", 0, 1);
        foreach (string field in new[] { "OnlyGame", "OnlyReference", "Different", "Matched" })
        {
            Label value = Value("Comparison" + field);
            output.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            output.Controls.Add(value, 0, output.RowCount++);
            values.Add(field, value);
        }
        layout.Controls.Add(scope, 0, 0);
        layout.Controls.Add(inputs, 1, 0);
        layout.SetColumnSpan(inputs, 2);
        layout.Controls.Add(output, 3, 0);
        Controls.Add(layout);
    }

    internal void Render(WizardSession session, IReadOnlyList<IReadOnlyDictionary<string, ManifestMetadataField>>? referenceMetadata = null)
    {
        ShowInput("Game", session.Game);
        ShowInput("Reference", session.Reference);
        Set("GameGame", Identification(session.Identification, ResultText.IdentityField(session.Identification?.Information)));
        Set("GameVersion", Identification(session.Identification, "version"));
        var metadata = referenceMetadata is { Count: > 0 } && session.Reference is not null ? referenceMetadata
            : session.Reference?.Sources.Select(source => source.Metadata).OfType<IReadOnlyDictionary<string, ManifestMetadataField>>().ToArray() ?? [];
        Set("ReferenceGame", Metadata(metadata, "game"));
        Set("ReferenceVersion", Metadata(metadata, "version"));
        Set("ReferenceLocation", Metadata(metadata, "package"));
        ScanScope? scan = session.Game?.Sources[0].Scan;
        Set("GameLocation", scan?.Root ?? L.Get("UnavailableValue"));
        Set("GameExcludedDirectories", Paths(scan?.ExcludedDirectories));
        Set("GameExcludedFiles", Paths(scan?.ExcludedFiles));
        Set("GameSkippedLinks", Paths(scan?.SkippedLinks));
        ComparisonSummary? summary = session.Comparison?.Summary;
        string Count(int? count) => count?.ToString("N0") ?? L.Get("UnavailableValue");
        Set("OnlyGame", L.Format("ComparisonOnlyGame", Count(summary?.OnlyA)));
        Set("OnlyReference", L.Format("ComparisonOnlyReference", Count(summary?.OnlyB)));
        Set("Different", L.Format("ComparisonDifferent", Count(summary?.Different)));
        bool incompleteHashes = session.Game is { } game && session.Reference is { } reference
            && (game.HashedFileCount < game.FileCount || reference.HashedFileCount < reference.FileCount);
        string matched = summary is null ? L.Format("ComparisonMatchedPending", L.Get("UnavailableValue"))
            : summary.MatchedWithHash > 0 && incompleteHashes
                ? L.Format("ComparisonMatchedMixed", summary.MatchedWithHash, summary.MatchedSizeOnly)
                : incompleteHashes
                    ? L.Format("ComparisonMatchedSize", summary.MatchedSizeOnly)
                    : L.Format("ComparisonMatchedMd5", summary.MatchedWithHash);
        Set("Matched", matched);
    }

    private void ShowInput(string side, FileListSnapshot? snapshot)
    {
        Set(side + "Files", snapshot?.FileCount.ToString("N0") ?? L.Get("UnavailableValue"));
        Set(side + "Size", snapshot is null ? L.Get("UnavailableValue") : ManifestTable.FormatSize(snapshot.TotalSize));
        Set(side + "Md5", snapshot is null ? L.Get("UnavailableValue") : $"{snapshot.HashedFileCount:N0}/{snapshot.FileCount:N0}");
    }

    private void AddInput(TableLayoutPanel table, string side, string field, string label, int column, int row)
    {
        table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Right,
            Margin = new Padding(3, 3, 3, 3) }, column, row);
        Label value = Value("Comparison" + side + field);
        table.Controls.Add(value, column + 1, row);
        values.Add(side + field, value);
    }

    private static Label Value(string name)
    {
        var label = new Label { Name = name, Dock = DockStyle.Fill, AutoEllipsis = true, UseMnemonic = false,
            TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(3, 3, 9, 3) };
        void UpdateHeight()
        {
            label.MinimumSize = new Size(0, label.Font.Height + 2);
            label.Height = label.MinimumSize.Height;
        }
        label.FontChanged += (_, _) => UpdateHeight();
        UpdateHeight();
        return label;
    }

    private static void AddHeading(TableLayoutPanel table, string text, int column, int span)
    {
        var heading = new Label { Text = L.Get(text), AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(3, 6, 0, 12) };
        table.Controls.Add(heading, column, 0);
        table.SetColumnSpan(heading, span);
        table.RowCount = 1;
        if (table.RowStyles.Count == 0) table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    }

    private void Set(string name, string text)
    {
        values[name].Text = text;
        tips.SetToolTip(values[name], text);
    }

    private static string Identification(GameIdentification? identification, string field)
        => Join(identification?.Information.GetValueOrDefault(field)?.Values.Select(value => value.RawValue)
            .OfType<string>() ?? []);

    private static string Metadata(IEnumerable<IReadOnlyDictionary<string, ManifestMetadataField>> metadata, string field)
    {
        ManifestMetadataField[] fields = metadata.Select(source => source.GetValueOrDefault(
            field == "game" ? ResultText.IdentityField(source) : field)).OfType<ManifestMetadataField>().ToArray();
        string[] candidates = fields.SelectMany(item => item.Matches).Select(item => item.Value).Distinct(StringComparer.Ordinal).ToArray();
        string text = Join(candidates);
        return fields.Any(item => item.Conflict)
            ? L.Format("ComparisonMetadataConflict", text) : text;
    }

    private static string Paths(IReadOnlyList<string>? paths) => paths is null ? L.Get("UnavailableValue")
        : paths.Count == 0 ? L.Get("NoneLabel") : string.Join(Environment.NewLine, paths);

    private static string Join(IEnumerable<string> items) => string.Join("; ", items.Distinct(StringComparer.Ordinal))
        is { Length: > 0 } text ? text : L.Get("UnavailableValue");

    protected override void Dispose(bool disposing)
    {
        if (disposing) tips.Dispose();
        base.Dispose(disposing);
    }
}
