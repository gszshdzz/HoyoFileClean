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
    private TableLayoutPanel networkChoices = null!;
    private readonly Label automaticNetworkInformation = new() { Name = "AutomaticNetworkInformation", AutoSize = true, Dock = DockStyle.Top };
    private bool IsNetworkSource => automaticSource.Checked || networkSource.Checked;
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
        networkChoices = choices;
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
        parseNetwork.Click += async (_, _) =>
        {
            if (automaticSource.Checked) await FetchAutomaticNetworkAsync();
            else await FetchAndParseNetworkAsync();
        };
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
        input.Controls.Add(automaticNetworkInformation);
        input.Controls.Add(choices);
        input.Controls.Add(OperationActions("NetworkActions", cancelNetwork, queryNetwork, getNetwork, parseNetwork));
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
        if (session.Step == 1 && IsNetworkSource) status.Text = DescribeNetworkInput();
    }

    private string DescribeNetworkInput() => networkConfigurationError ?? (automaticSource.Checked ? L.Get("AutomaticNetworkHint") : networkManifests.Rows.Count > 0
        ? L.Get("SelectPackagesHint")
        : networkGames.Items.Count > 0 && networkGames.SelectedItem is not NetworkGame ? L.Get("SelectGameHint")
        : networkGames.SelectedItem is not NetworkGame game ? L.Get("QueryServiceHint")
        : L.Format("GameVersionHint", game.Version.Biz, game.Version.Version));

    private string DescribeAutomaticNetworkInformation()
    {
        string Values(string field) => session.Identification?.Information.GetValueOrDefault(field) is { Values.Count: > 0 } info
            ? string.Join(L.Get("ListSeparator"), info.Values.Select(value => value.RawValue ?? L.Get("UnavailableValue")))
            : L.Get("UnavailableValue");
        return L.Format("AutomaticNetworkInformation", Values(ResultText.IdentityField(session.Identification?.Information)),
            Values("version"), Values("package"));
    }

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
        if (version is not null && !NetworkManifestClient.IsValidVersion(version))
        { status.Text = L.Get("ThreePartVersionRequired"); return; }
        ClearNetworkFiles();
        await RunWorkAsync(token => networkClient.GetCatalogAsync(game.Version, token, version).GetAwaiter().GetResult(), catalog =>
        {
            AddNetworkCatalog(catalog, game.Version.Biz);
        }, L.Get("QueryingManifests"), DesktopOperation.Network,
            () => L.Format("ManifestListFound", networkManifests.Rows.Count));
    }

    private async Task FetchAutomaticNetworkAsync()
    {
        if (session.IsBusy || !automaticSource.Checked) return;
        if (GameIdentifier.HasIncompleteRules(session.Identification))
        {
            status.Text = L.Get("AutomaticNetworkRulesIncomplete");
            return;
        }
        ClearNetworkFiles();
        string? Value(string field) => session.Identification?.Information.GetValueOrDefault(field)?.Values.SingleOrDefault()?.RawValue;
        string? biz = Value("biz"), version = Value("version");
        string[] packages = session.Identification?.Information.GetValueOrDefault("package")?.Values
            .Select(value => value.RawValue).OfType<string>().Distinct(StringComparer.Ordinal).ToArray() ?? [];
        if (biz is null || !NetworkManifestClient.IsValidVersion(version) || packages.Length == 0)
        {
            status.Text = L.Get("AutomaticNetworkIdentificationRequired");
            return;
        }
        bool ready = false;
        NetworkService[] services = networkRegions.Items.Cast<NetworkRegion>().Select(region => region.Service).ToArray();
        await RunWorkAsync(token =>
        {
            var matches = new List<(NetworkService Service, RemoteGame Game)>();
            foreach (NetworkService service in services)
                matches.AddRange(networkClient.GetGamesAsync(service, token).GetAwaiter().GetResult()
                    .Where(game => game.Biz == biz).Select(game => (service, game)));
            if (matches.Count == 0) throw new CoreException(CoreErrorCode.ManifestChannelUnavailable);
            if (matches.Count != 1) throw new CoreException(CoreErrorCode.ManifestGameAmbiguous);
            return networkClient.GetCatalogAsync(matches[0].Service, biz, token, version, matches[0].Game.Id).GetAwaiter().GetResult();
        }, catalog =>
        {
            AddNetworkCatalog(catalog, biz);
            if (packages.Any(package => !catalog.Manifests.Any(manifest => manifest.MatchingField == package)))
                throw new CoreException(CoreErrorCode.ManifestPackageUnavailable);
            networkManifests.CheckPackages(packages);
            ready = true;
        }, L.Get("QueryingManifests"), DesktopOperation.Network);
        if (ready && !IsDisposed && !session.CloseRequested) await FetchAndParseNetworkAsync();
    }

    private void AddNetworkCatalog(RemoteManifestCatalog catalog, string biz)
    {
        var inputs = catalog.Manifests.Select(manifest => new ReferenceInput(
            new LocalManifestFile(manifest.Category, manifest.Url.AbsoluteUri, manifest.DownloadSize, default), manifest));
        networkManifests.AddNetworkFiles(inputs, biz, catalog.Version);
    }

    private async Task FetchAndParseNetworkAsync()
    {
        if (session.IsBusy || !IsNetworkSource) return;
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
            networkManifests.ShowParsedResults(inputs, result.Items);
            session.AcceptReference(result.GetSnapshot());
        }, L.Get("FetchingManifests"), DesktopOperation.Network, () => L.Format("NetworkBatchParsed", inputs.Length), onFinished: () =>
        {
            networkManifests.ResetFetching();
        });
    }

}
