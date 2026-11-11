namespace HoyoFileClean.Desktop;

public sealed partial class MainForm
{
    private readonly HttpClient networkHttp = NetworkManifestClient.CreateHttpClient();
    private readonly NetworkManifestClient networkClient;
    private readonly ListBox networkGames = new() { Name = "NetworkGames", Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly ListBox networkRegions = new() { Name = "NetworkRegions", Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly RadioButton latestNetworkVersion = new() { Name = "LatestNetworkVersion", Text = "最新版", AutoSize = true, Checked = true };
    private readonly RadioButton customNetworkVersion = new() { Name = "CustomNetworkVersion", Text = "自定义版本", AutoSize = true };
    private readonly TextBox networkVersion = new() { Name = "NetworkVersion", Dock = DockStyle.Top, PlaceholderText = "例如 7.1.0", Enabled = false };
    private readonly Button queryNetwork = MakeButton("查询", "QueryNetwork");
    private readonly Button getNetwork = MakeButton("获取", "GetNetworkManifests");
    private readonly Button parseNetwork = MakeButton("解析", "ParseNetworkReference");
    private readonly ManifestTable networkManifests = new("NetworkManifests");
    private readonly Label networkSummary = new() { Name = "NetworkSummary", AutoSize = true, Dock = DockStyle.Top,
        Text = "先选择游戏，再点击查询获取可用区服；不会自动联网。" };
    private Panel networkReferencePage = null!;
    private sealed record NetworkRegion(GenshinChannel Channel, string Name)
    {
        public override string ToString() => Name;
    }

    private Panel BuildNetworkReferencePage()
    {
        networkManifests.Columns["Game"]!.MinimumWidth = 100;
        networkManifests.Columns["Version"]!.MinimumWidth = 70;
        networkGames.Items.Add("原神");
        networkGames.SelectedIndex = 0;
        var choices = new TableLayoutPanel { Name = "NetworkChoices", Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, RowCount = 2 };
        for (int i = 0; i < 3; i++) choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        choices.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        choices.RowStyles.Add(new RowStyle(SizeType.Absolute, networkGames.ItemHeight * 6 + networkGames.Margin.Vertical + 4));
        void UpdateChoiceHeight()
        {
            int height = networkGames.ItemHeight * 6 + networkGames.Margin.Vertical + networkGames.Height - networkGames.ClientSize.Height;
            if (choices.RowStyles[1].Height != height) choices.RowStyles[1].Height = height;
        }
        Shown += (_, _) => UpdateChoiceHeight();
        DpiChanged += (_, _) => UpdateChoiceHeight();
        networkGames.FontChanged += (_, _) => UpdateChoiceHeight();
        choices.Controls.Add(Note("游戏（单选）"), 0, 0);
        choices.Controls.Add(Note("区服（单选，查询后可选）"), 1, 0);
        choices.Controls.Add(Note("版本（单选）"), 2, 0);
        choices.Controls.Add(networkGames, 0, 1);
        choices.Controls.Add(networkRegions, 1, 1);
        var versions = Vertical();
        versions.Controls.Add(latestNetworkVersion);
        versions.Controls.Add(customNetworkVersion);
        versions.Controls.Add(networkVersion);
        choices.Controls.Add(versions, 2, 1);
        networkGames.SelectedIndexChanged += (_, _) =>
        {
            networkRegions.Items.Clear();
            ClearNetworkFiles();
        };
        networkRegions.SelectedIndexChanged += (_, _) => ClearNetworkFiles();
        latestNetworkVersion.CheckedChanged += (_, _) =>
        {
            networkVersion.Enabled = !session.IsBusy && customNetworkVersion.Checked;
            ClearNetworkFiles();
        };
        networkVersion.TextChanged += (_, _) => ClearNetworkFiles();
        networkManifests.CheckedFilesChanged += ReferenceChanged;
        queryNetwork.Click += async (_, _) => await QueryNetworkAsync();
        getNetwork.Click += async (_, _) => await GetNetworkManifestsAsync();
        parseNetwork.Click += async (_, _) => await ConvertAsync();
        var input = Vertical();
        input.Controls.Add(choices);
        input.Controls.Add(Buttons(queryNetwork, getNetwork, parseNetwork));
        input.Controls.Add(networkSummary);
        return Page(input, networkManifests);
    }

    private void ClearNetworkFiles()
    {
        networkManifests.Rows.Clear();
        networkSummary.Text = "选择已变更，请点击获取；自定义版本是否可用以官方返回为准。";
        ReferenceChanged();
    }

    private async Task QueryNetworkAsync()
    {
        if (session.IsBusy || !networkSource.Checked || networkGames.SelectedIndex < 0) return;
        networkRegions.Items.Clear();
        ClearNetworkFiles();
        await RunWorkAsync(() =>
        {
            var available = new List<NetworkRegion>();
            var failures = new List<string>();
            foreach (var (channel, name) in new[] { (GenshinChannel.China, "官服"), (GenshinChannel.Global, "国际服") })
            {
                try
                {
                    IReadOnlyList<RemoteGame> games = networkClient.GetGamesAsync(channel).GetAwaiter().GetResult();
                    if (games.Any(game => game.Game == "hk4e")) available.Add(new NetworkRegion(channel, name));
                }
                catch (Exception error) when (error is CoreException or HttpRequestException or OperationCanceledException or IOException)
                { failures.Add(name + "：" + DescribeDesktopError(error)); }
            }
            return (available, failures);
        }, result =>
        {
            networkRegions.Items.AddRange(result.available.Cast<object>().ToArray());
            if (networkRegions.Items.Count > 0) networkRegions.SelectedIndex = 0;
            networkSummary.Text = $"查询到 {result.available.Count} 个区服。" + string.Join("；", result.failures)
                + " 其他渠道暂未确认，不提供官服替代结果。";
        }, "正在查询官方区服信息…", "查询完成，请选择区服与版本，再点击获取。");
    }

    private async Task GetNetworkManifestsAsync()
    {
        if (session.IsBusy || !networkSource.Checked || networkRegions.SelectedItem is not NetworkRegion region) return;
        string? version = customNetworkVersion.Checked ? networkVersion.Text.Trim() : null;
        if (version is not null && (!Version.TryParse(version, out var parsed) || parsed.Build < 0 || parsed.Revision >= 0))
        { status.Text = "请输入三段版本号，例如 7.1.0。"; return; }
        ClearNetworkFiles();
        await RunWorkAsync(() =>
        {
            // Refresh even 'latest' at the explicit Get action, never silently reuse a stale version.
            RemoteManifestCatalog catalog = networkClient.GetCatalogAsync(region.Channel, version: version).GetAwaiter().GetResult();
            var files = new List<ReferenceInput>();
            foreach (RemoteManifest manifest in catalog.Manifests)
            {
                byte[] bytes = networkClient.FetchAsync(manifest).GetAwaiter().GetResult();
                var file = new LocalManifestFile(manifest.MatchingField + " — " + manifest.Category,
                    manifest.Url.AbsoluteUri, bytes.Length, default);
                files.Add(new ReferenceInput(file, manifest, bytes));
            }
            return (catalog, files);
        }, result =>
        {
            networkManifests.AddNetworkFiles(result.files, "原神-" + region.Name, result.catalog.Version);
            networkSummary.Text = $"已获取 {result.files.Count} 份 manifest，仅保留在内存。勾选后点击解析。";
        }, "正在从官方服务器获取 manifest…", "获取完成，尚未解析；请勾选要使用的清单。");
    }
}
