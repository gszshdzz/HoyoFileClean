namespace HoyoFileClean.Desktop;

public sealed partial class MainForm
{
    private readonly HttpClient networkHttp = NetworkManifestClient.CreateHttpClient();
    private readonly NetworkManifestClient networkClient;
    private readonly Func<IReadOnlyList<NetworkService>> loadNetworkServices;
    private string? networkConfigurationError;
    private readonly ListBox networkGames = new() { Name = "NetworkGames", Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly ListBox networkRegions = new() { Name = "NetworkRegions", Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly RadioButton latestNetworkVersion = new() { Name = "LatestNetworkVersion", Text = L.Get("LatestVersionLabel"), AutoSize = true, Checked = true };
    private readonly RadioButton customNetworkVersion = new() { Name = "CustomNetworkVersion", Text = L.Get("CustomVersionLabel"), AutoSize = true };
    private readonly TextBox networkVersion = new() { Name = "NetworkVersion", Dock = DockStyle.Top, PlaceholderText = L.Get("VersionExample"), Enabled = false };
    private readonly Button queryNetwork = MakeButton(L.Get("Button_QueryNetwork"), "QueryNetwork");
    private readonly Button getNetwork = MakeButton(L.Get("Button_GetNetworkManifests"), "GetNetworkManifests");
    private readonly Button parseNetwork = MakeButton(L.Get("Button_ParseNetworkReference"), "ParseNetworkReference");
    private readonly Button cancelNetwork = MakeButton(L.Get("Button_CancelNetwork"), "CancelNetwork");
    private readonly ManifestTable networkManifests = new("NetworkManifests");
    private Panel networkReferencePage = null!;
    private sealed record NetworkRegion(NetworkService Service)
    {
        public override string ToString() => L.Get(Service.NameTextId);
    }

    private sealed record NetworkGame(RemoteGameVersion Version, string Label)
    {
        public override string ToString() => Label;
    }

    private Panel BuildNetworkReferencePage()
    {
        try
        {
            NetworkRegion[] regions = loadNetworkServices().Select(service =>
            {
                if (!L.Contains(service.NameTextId)) throw new CoreException(CoreErrorCode.NetworkConfigurationInvalid);
                return new NetworkRegion(service);
            }).ToArray();
            networkRegions.Items.AddRange(regions);
            if (networkRegions.Items.Count > 0) networkRegions.SelectedIndex = 0;
        }
        catch (CoreException error) when (error.Code == CoreErrorCode.NetworkConfigurationInvalid)
        {
            networkConfigurationError = DiagnosticText.ForDesktop(error);
        }
        var choices = new TableLayoutPanel { Name = "NetworkChoices", Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, RowCount = 2 };
        for (int i = 0; i < 3; i++) choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        choices.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        choices.RowStyles.Add(new RowStyle(SizeType.Absolute, networkGames.ItemHeight * Maintenance.NetworkChoiceVisibleRows + networkGames.Margin.Vertical + 4));
        void UpdateChoiceHeight()
        {
            int height = networkGames.ItemHeight * Maintenance.NetworkChoiceVisibleRows + networkGames.Margin.Vertical + networkGames.Height - networkGames.ClientSize.Height;
            if (choices.RowStyles[1].Height != height) choices.RowStyles[1].Height = height;
        }
        Shown += (_, _) => UpdateChoiceHeight();
        DpiChanged += (_, _) => UpdateChoiceHeight();
        networkGames.FontChanged += (_, _) => UpdateChoiceHeight();
        choices.Controls.Add(Note(L.Get("ServiceChoiceLabel")), 0, 0);
        choices.Controls.Add(Note(L.Get("GameChoiceLabel")), 1, 0);
        choices.Controls.Add(Note(L.Get("VersionChoiceLabel")), 2, 0);
        choices.Controls.Add(networkRegions, 0, 1);
        choices.Controls.Add(networkGames, 1, 1);
        var versions = Vertical();
        versions.Controls.Add(latestNetworkVersion);
        versions.Controls.Add(customNetworkVersion);
        versions.Controls.Add(networkVersion);
        choices.Controls.Add(versions, 2, 1);
        networkGames.SelectedIndexChanged += (_, _) =>
        {
            latestNetworkVersion.Text = networkGames.SelectedItem is NetworkGame game
                ? L.Format("CurrentVersionLabel", game.Version.Version) : L.Get("LatestVersionLabel");
            ClearNetworkFiles();
        };
        networkRegions.SelectedIndexChanged += (_, _) =>
        {
            InvalidateNetworkQuery();
        };
        latestNetworkVersion.CheckedChanged += (_, _) =>
        {
            networkVersion.Enabled = !session.IsBusy && customNetworkVersion.Checked;
            ClearNetworkFiles();
        };
        networkVersion.TextChanged += (_, _) => ClearNetworkFiles();
        networkManifests.CheckedFilesChanged += ReferenceChanged;
        queryNetwork.Click += async (_, _) => await QueryNetworkAsync();
        getNetwork.Click += async (_, _) => await GetNetworkManifestsAsync();
        parseNetwork.Click += async (_, _) => await FetchAndParseNetworkAsync();
        networkRegions.MouseDoubleClick += async (_, args) =>
        {
            if (args.Button == MouseButtons.Left && networkRegions.IndexFromPoint(args.Location) >= 0)
                await QueryNetworkAsync();
        };
        networkGames.MouseDoubleClick += async (_, args) =>
        {
            if (args.Button == MouseButtons.Left && networkGames.IndexFromPoint(args.Location) >= 0)
            {
                networkGames.SelectedIndex = networkGames.IndexFromPoint(args.Location);
                await GetNetworkManifestsAsync();
            }
        };
        cancelNetwork.Click += (_, _) =>
        {
            session.CancelOperation();
            cancelNetwork.Enabled = false;
            status.Text = L.Get("CancellingNetwork");
        };
        var input = Vertical();
        input.Controls.Add(choices);
        var actions = new TableLayoutPanel { Name = "NetworkActions", ColumnCount = 2, RowCount = 1, AutoSize = true, Dock = DockStyle.Top };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actions.Controls.Add(Buttons(queryNetwork, getNetwork, parseNetwork), 0, 0);
        cancelNetwork.Anchor = AnchorStyles.Right;
        actions.Controls.Add(cancelNetwork, 1, 0);
        input.Controls.Add(actions);
        return Page(input, networkManifests);
    }

    private void InvalidateNetworkQuery()
    {
        networkGames.Items.Clear();
        latestNetworkVersion.Text = L.Get("LatestVersionLabel");
        ClearNetworkFiles();
    }

    private void ClearNetworkFiles()
    {
        networkManifests.Rows.Clear();
        ReferenceChanged();
        if (session.Step == 0 && networkSource.Checked) status.Text = DescribeNetworkInput();
    }

    private string DescribeNetworkInput() => networkConfigurationError ?? (networkManifests.Rows.Count > 0
        ? L.Get("SelectPackagesHint")
        : networkGames.Items.Count > 0 && networkGames.SelectedItem is not NetworkGame ? L.Get("SelectGameHint")
        : networkGames.SelectedItem is not NetworkGame game ? L.Get("QueryServiceHint")
        : L.Format("GameVersionHint", game.Version.Biz, game.Version.Version));

    private async Task QueryNetworkAsync()
    {
        if (session.IsBusy || !networkSource.Checked || networkRegions.SelectedItem is not NetworkRegion region) return;
        InvalidateNetworkQuery();
        await RunWorkAsync(token => networkClient.GetGameVersionsAsync(region.Service, cancellationToken: token).GetAwaiter().GetResult(), result =>
        {
            foreach (RemoteGameVersion game in result)
            {
                string label = result.Count(item => item.Biz == game.Biz) > 1
                    ? L.Format("GameBranchLabel", game.Biz, game.Id) : game.Biz;
                networkGames.Items.Add(new NetworkGame(game, label));
            }
        }, L.Get("QueryingGames"), DesktopOperation.Network,
            () => networkGames.Items.Count == 0 ? L.Get("OfficialGameListEmpty") : DescribeNetworkInput());
    }

    private async Task GetNetworkManifestsAsync()
    {
        if (session.IsBusy || !networkSource.Checked || networkGames.SelectedItem is not NetworkGame game) return;
        string? version = customNetworkVersion.Checked ? networkVersion.Text.Trim() : null;
        if (version is not null && (!Version.TryParse(version, out var parsed) || parsed.Build < 0 || parsed.Revision >= 0))
        { status.Text = L.Get("ThreePartVersionRequired"); return; }
        ClearNetworkFiles();
        await RunWorkAsync(token => networkClient.GetCatalogAsync(game.Version, token, version).GetAwaiter().GetResult(), catalog =>
        {
            var inputs = catalog.Manifests.Select(manifest => new ReferenceInput(
                new LocalManifestFile(manifest.Category, manifest.Url.AbsoluteUri, manifest.DownloadSize, default), manifest));
            networkManifests.AddNetworkFiles(inputs, game.Version.Biz, catalog.Version);
        }, L.Get("QueryingManifests"), DesktopOperation.Network,
            () => L.Format("ManifestListFound", networkManifests.Rows.Count));
    }

    private async Task FetchAndParseNetworkAsync()
    {
        if (session.IsBusy || !networkSource.Checked) return;
        ReferenceInput[] inputs = networkManifests.CheckedInputs;
        if (inputs.Length == 0) return;
        ReferenceChanged();
        networkManifests.ResetResults();
        networkManifests.ShowResults(inputs.Select(input => (input, ReferenceState.Fetching, (int?)null)));
        await RunWorkAsync(token =>
        {
            var parser = new ReferenceParser((input, cancellation) =>
                networkClient.ReadAsync(input.Remote!, cancellation).GetAwaiter().GetResult());
            return parser.Parse(inputs, token);
        }, result =>
        {
            networkManifests.ShowResults(inputs.Select((input, index) => (input,
                result.Items[index].Error is null ? ReferenceState.Parsed : ReferenceState.Failed, result.Items[index].Entries)));
            session.AcceptReference(result.GetSnapshot());
            referenceOrigin = L.Get("SourceHeading") + string.Join("\r\n", inputs.Select(input => DiagnosticText.DisplaySource(input.File.FullPath))) + "\r\n\r\n";
        }, L.Get("FetchingManifests"), DesktopOperation.Network, () => L.Format("NetworkBatchParsed", inputs.Length), onFinished: () =>
        {
            networkManifests.ResetFetching();
        });
    }

}
