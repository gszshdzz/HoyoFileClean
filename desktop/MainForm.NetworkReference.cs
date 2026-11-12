namespace HoyoFileClean.Desktop;

public sealed partial class MainForm
{
    private readonly HttpClient networkHttp = NetworkManifestClient.CreateHttpClient();
    private readonly NetworkManifestClient networkClient;
    private readonly Func<IReadOnlyList<NetworkService>> loadNetworkServices;
    private string? networkConfigurationError;
    private readonly ListBox networkGames = new() { Name = "NetworkGames", Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly ListBox networkRegions = new() { Name = "NetworkRegions", Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly RadioButton latestNetworkVersion = new() { Name = "LatestNetworkVersion", Text = "最新版", AutoSize = true, Checked = true };
    private readonly RadioButton customNetworkVersion = new() { Name = "CustomNetworkVersion", Text = "自定义版本", AutoSize = true };
    private readonly TextBox networkVersion = new() { Name = "NetworkVersion", Dock = DockStyle.Top, PlaceholderText = "例如 7.1.0", Enabled = false };
    private readonly Button queryNetwork = MakeButton("查询游戏列表", "QueryNetwork");
    private readonly Button getNetwork = MakeButton("查询 manifest 列表", "GetNetworkManifests");
    private readonly Button parseNetwork = MakeButton("获取 manifest 并解析", "ParseNetworkReference");
    private readonly Button cancelNetwork = MakeButton("取消网络操作", "CancelNetwork");
    private readonly ManifestTable networkManifests = new("NetworkManifests");
    private Panel networkReferencePage = null!;
    private CancellationTokenSource? networkCancellation;
    private bool closeAfterNetwork;
    private sealed record NetworkRegion(GenshinChannel Channel, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record NetworkGame(RemoteGameVersion Version, string Label)
    {
        public override string ToString() => Label;
    }

    private Panel BuildNetworkReferencePage()
    {
        try
        {
            networkRegions.Items.AddRange(loadNetworkServices().Select(service => (object)new NetworkRegion(service.Channel, service.Name)).ToArray());
            if (networkRegions.Items.Count > 0) networkRegions.SelectedIndex = 0;
        }
        catch (CoreException error) when (error.Code == CoreErrorCode.NetworkConfigurationInvalid)
        {
            networkConfigurationError = DescribeDesktopError(error);
        }
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
        choices.Controls.Add(Note("区服（查询入口，单选）"), 0, 0);
        choices.Controls.Add(Note("游戏（查询后可选）"), 1, 0);
        choices.Controls.Add(Note("版本（单选）"), 2, 0);
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
                ? $"最新版（{game.Version.Version}）" : "最新版";
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
                await GetNetworkManifestsAsync();
        };
        cancelNetwork.Click += (_, _) =>
        {
            networkCancellation?.Cancel();
            cancelNetwork.Enabled = false;
            status.Text = "正在取消网络操作…";
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
        latestNetworkVersion.Text = "最新版";
        ClearNetworkFiles();
    }

    private void ClearNetworkFiles()
    {
        networkManifests.Rows.Clear();
        ReferenceChanged();
        if (session.Step == 0 && networkSource.Checked) status.Text = DescribeNetworkInput();
    }

    private string DescribeNetworkInput() => networkConfigurationError ?? (networkManifests.Rows.Count > 0
        ? "请勾选所需包体并获取、解析；改选后需重新获取并解析。"
        : networkGames.SelectedItem is not NetworkGame game ? "请选择区服入口，再查询游戏列表；不会自动联网。"
        : $"{game.Version.Biz} 当前版本 {game.Version.Version}；选择游戏与版本后查询 manifest 列表。");

    private async Task QueryNetworkAsync()
    {
        if (session.IsBusy || !networkSource.Checked || networkRegions.SelectedItem is not NetworkRegion region) return;
        InvalidateNetworkQuery();
        await RunNetworkWorkAsync(token => networkClient.GetGameVersionsAsync(region.Channel, cancellationToken: token).GetAwaiter().GetResult(), result =>
        {
            foreach (RemoteGameVersion game in result)
            {
                string label = result.Count(item => item.Biz == game.Biz) > 1
                    ? $"{game.Biz} ({game.Id})" : game.Biz;
                networkGames.Items.Add(new NetworkGame(game, label));
            }
            if (networkGames.Items.Count > 0) networkGames.SelectedIndex = 0;
        }, "正在查询所选入口的游戏列表与当前版本…",
            () => networkGames.Items.Count == 0 ? "官方未返回游戏列表。" : DescribeNetworkInput());
    }

    private async Task GetNetworkManifestsAsync()
    {
        if (session.IsBusy || !networkSource.Checked || networkGames.SelectedItem is not NetworkGame game) return;
        string? version = customNetworkVersion.Checked ? networkVersion.Text.Trim() : null;
        if (version is not null && (!Version.TryParse(version, out var parsed) || parsed.Build < 0 || parsed.Revision >= 0))
        { status.Text = "请输入三段版本号，例如 7.1.0。"; return; }
        ClearNetworkFiles();
        await RunNetworkWorkAsync(token => networkClient.GetCatalogAsync(game.Version, token, version).GetAwaiter().GetResult(), catalog =>
        {
            var inputs = catalog.Manifests.Select(manifest => new ReferenceInput(
                new LocalManifestFile(manifest.Category, manifest.Url.AbsoluteUri, manifest.DownloadSize, default), manifest));
            networkManifests.AddNetworkFiles(inputs, game.Version.Biz, catalog.Version);
        }, "正在查询所选游戏与版本的 manifest 列表…",
            () => $"已查询 {networkManifests.Rows.Count} 份 manifest，请勾选所需包体后获取并解析。");
    }

    private async Task FetchAndParseNetworkAsync()
    {
        if (session.IsBusy || !networkSource.Checked) return;
        ReferenceInput[] inputs = networkManifests.CheckedInputs;
        if (inputs.Length == 0) return;
        ReferenceChanged();
        networkManifests.ResetResults();
        networkManifests.ShowResults(inputs.Select(input => (input, "获取并解析中", (int?)null)));
        await RunNetworkWorkAsync(token =>
        {
            var parser = new ReferenceParser((path, cancellation) =>
            {
                ReferenceInput input = inputs.Single(item => item.File.FullPath == path);
                return networkClient.ReadAsync(input.Remote!, cancellation).GetAwaiter().GetResult();
            });
            return parser.Parse(inputs.Select(input => input.File.FullPath).ToArray(), token);
        }, result =>
        {
            networkManifests.ShowResults(inputs.Select((input, index) => (input,
                result.Items[index].Error is null ? "已解析" : "失败", result.Items[index].Entries)));
            if (result.Items.Any(item => item.Error is not null))
                throw new InvalidDataException("部分 manifest 获取或解析失败，本批结果不可用。", result.Items.First(item => item.Error is not null).Error);
            if (result.CombinedError is not null)
                throw new InvalidDataException("所选 manifest 存在同侧冲突，未启用结果。", result.CombinedError);
            session.AcceptReference(result.Snapshot!);
            referenceOrigin = "来源：\r\n" + string.Join("\r\n", inputs.Select(input => ResultText.DisplaySource(input.File.FullPath))) + "\r\n\r\n";
        }, "正在获取所选 manifest 并解析…", () => $"已获取并解析 {inputs.Length} 份 manifest。", onFinished: () =>
        {
            foreach (DataGridViewRow row in networkManifests.Rows)
                if ((string?)row.Cells["State"].Value == "获取并解析中") row.Cells["State"].Value = "未获取";
        });
    }

    private async Task RunNetworkWorkAsync<T>(Func<CancellationToken, T> work, Action<T> apply,
        string message, Func<string> completedMessage, Action? onFinished = null)
    {
        using var cancellation = new CancellationTokenSource();
        networkCancellation = cancellation;
        await RunWorkAsync(() => work(cancellation.Token), result =>
        {
            cancellation.Token.ThrowIfCancellationRequested();
            apply(result);
        }, message, completedMessage, onFinished: () => { onFinished?.Invoke(); networkCancellation = null; });
        if (closeAfterNetwork) Close();
    }
}
