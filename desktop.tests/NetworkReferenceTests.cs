using System.Net;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using Google.Protobuf;
using HoyoFileClean.Desktop;
using HoyoFileClean.Protos;
using Xunit;

namespace HoyoFileClean.Desktop.Tests;

[Collection("Windows Forms")]
public sealed class NetworkReferenceTests
{
    private sealed class Handler : HttpMessageHandler
    {
        internal int Requests;
        internal bool Fail;
        internal bool FailChina;
        internal bool NoGame;
        internal bool ExtraGames;
        internal bool ExtraPackage;
        internal bool SharedManifestUrl;
        internal bool FailExtraPackage;
        internal bool ConflictingExtraPackage;
        internal bool CorruptManifest;
        internal string? BlockPath;
        internal bool BlockBody;
        internal readonly TaskCompletionSource Blocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Cancelled;
        internal bool BodyDisposed;
        internal readonly List<string> Versions = [];
        internal readonly List<string> Hosts = [];
        internal readonly List<string> Paths = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Requests);
            Hosts.Add(request.RequestUri!.Host);
            Paths.Add(request.RequestUri.AbsolutePath);
            token.ThrowIfCancellationRequested();
            if (request.RequestUri.AbsolutePath.EndsWith(BlockPath ?? "never-block"))
            {
                Blocked.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                finally { Cancelled = token.IsCancellationRequested; }
            }
            bool china = request.RequestUri!.Host == "hyp-api.mihoyo.com";
            if (FailChina && china) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = request };
            string biz = china ? "hk4e_cn" : "hk4e_global";
            byte[] bytes = ManifestBytes(3);
            object data;
            if (request.RequestUri.AbsolutePath.EndsWith("getGameBranches"))
                data = new { game_branches = (NoGame ? Array.Empty<(string Biz, string Id)>()
                    : ExtraGames ? new[] { (biz, "1"), ("future_cn", "2"), ("future_cn", "3") } : new[] { (biz, "1") })
                    .Select(item => new { game = new { biz = item.Item1, id = item.Item2 }, main = new
                    { tag = "7.2.0", branch = "main", password = "p", package_id = "pkg" + item.Item2 } }) };
            else if (request.RequestUri.AbsolutePath.EndsWith("getBuild"))
            {
                string version = Uri.UnescapeDataString(request.RequestUri.Query.Split("tag=")[1]);
                Versions.Add(version);
                data = new { tag = version, build_id = "build", manifests = (ExtraPackage ? new[] { "game", "future-voice" } : new[] { "game" }).Select(field =>
                {
                    byte[] content = ConflictingExtraPackage && field == "future-voice" ? ManifestBytes(4) : bytes;
                    return new
                    {
                        matching_field = field, category_name = "类别-" + field,
                        manifest = new { id = "manifest-" + (SharedManifestUrl ? "shared" : field), checksum = Convert.ToHexString(MD5.HashData(content)), compressed_size = content.Length, uncompressed_size = content.Length },
                        manifest_download = new { url_prefix = "https://autopatchcn.yuanshen.com/test", url_suffix = "", compression = 0, encryption = 0 }
                    };
                }) };
            }
            else
            {
                bool extra = request.RequestUri.AbsolutePath.EndsWith("manifest-future-voice");
                if (extra && ConflictingExtraPackage) bytes = ManifestBytes(4);
                return new HttpResponseMessage(Fail || extra && FailExtraPackage ? HttpStatusCode.NotFound : HttpStatusCode.OK)
                    { RequestMessage = request, Content = BlockBody ? new StreamContent(new StalledStream(this))
                        : new ByteArrayContent(CorruptManifest ? new byte[bytes.Length] : bytes) };
            }
            return new HttpResponseMessage(Fail ? HttpStatusCode.NotFound : HttpStatusCode.OK)
            { RequestMessage = request, Content = new StringContent(JsonSerializer.Serialize(new { retcode = 0, data })) };
        }

        private static byte[] ManifestBytes(long size) => new SophonManifestProto
        {
            Assets = { new SophonManifestAssetProperty
            { AssetName = "Data/file", AssetSize = size, AssetHashMd5 = "900150983cd24fb0d6963f7d28e17f72" } }
        }.ToByteArray();
    }

    private sealed class StalledStream(Handler handler) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            handler.Blocked.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); return 0; }
            finally { handler.Cancelled = token.IsCancellationRequested; }
        }
        protected override void Dispose(bool disposing) { handler.BodyDisposed = true; base.Dispose(disposing); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public void ExplicitQueryGetParseAndCustomVersionUseCommonTable() => OnSta(() =>
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var session = new WizardSession();
        using var form = new MainForm(session, networkClient: new NetworkManifestClient(http))
        { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false, Size = new Size(800, 650) };
        form.Show();
        Application.DoEvents();
        Assert.IsType<WindowsFormsSynchronizationContext>(SynchronizationContext.Current);
        Assert.Equal(0, handler.Requests);
        Assert.Empty(form.Controls.Find("LocalManifestSummary", true));
        Assert.Empty(form.Controls.Find("NetworkSummary", true));
        var outer = Assert.IsType<TableLayoutPanel>(form.Controls[0]);
        Assert.Equal(4, outer.RowCount);
        Assert.DoesNotContain(outer.Controls.Cast<Control>(), control => control is ProgressBar);
        var games = Find<ListBox>(form, "NetworkGames");
        var regions = Find<ListBox>(form, "NetworkRegions");
        Assert.Equal(SelectionMode.One, games.SelectionMode);
        Assert.Equal(SelectionMode.One, regions.SelectionMode);
        Assert.True(regions.Left < games.Left);
        Assert.Empty(games.Items);
        Assert.Equal(2, regions.Items.Count);
        Assert.True(games.ClientSize.Height / games.ItemHeight == 6,
            $"list={games.Bounds}; client={games.ClientSize}; item={games.ItemHeight}; parent={games.Parent!.Bounds}; rows={string.Join(',', ((TableLayoutPanel)games.Parent).GetRowHeights())}; font={games.Font.Size}");
        for (int i = 0; i < 21; i++) games.Items.Add("滚动验证-" + i);
        games.TopIndex = 15;
        Assert.True(games.TopIndex >= 15);
        Assert.Equal(21, games.Items.Count);
        games.TopIndex = 0;
        foreach (string mode in new[] { "LocalSource", "ManualSource" })
        {
            Find<RadioButton>(form, mode).Checked = true;
            string tableName = mode == "LocalSource" ? "LocalManifests" : "Manifests";
            string buttonName = mode == "LocalSource" ? "ParseLocalReference" : "ParseManualReference";
            var inputTable = Find<ManifestTable>(form, tableName);
            Button parse = Find<Button>(form, buttonName);
            Assert.False(parse.Enabled);
            inputTable.AddFiles([new LocalManifestFile("placeholder", "C:\\placeholder", 3, DateTime.UtcNow)], check: true);
            inputTable.ToggleAll();
            Assert.False(parse.Enabled);
            inputTable.ToggleAll();
            Assert.True(parse.Enabled);
            inputTable.Rows.Clear();
            inputTable.ToggleAll();
            // Source switching refreshes both the table and the derived button state.
            Find<RadioButton>(form, "NetworkSource").Checked = true;
            Find<RadioButton>(form, mode).Checked = true;
            Assert.False(parse.Enabled);
        }
        Find<RadioButton>(form, "ManualSource").Checked = true;
        Find<RadioButton>(form, "NetworkSource").Checked = true;
        Assert.Equal(0, handler.Requests);
        Assert.False(Find<Button>(form, "GetNetworkManifests").Enabled);
        ClickAndWait(form, session, "QueryNetwork");
        Assert.Equal(1, handler.Requests);
        Assert.Equal("hk4e_cn", Assert.Single(games.Items.Cast<object>()).ToString());
        Assert.Equal(-1, games.SelectedIndex);
        Assert.False(Find<Button>(form, "GetNetworkManifests").Enabled);
        games.SelectedIndex = 0;
        Assert.True(regions.Items.Count == 2,
            $"requests={handler.Requests}; game={games.SelectedIndex}; queryEnabled={Find<Button>(form, "QueryNetwork").Enabled}; status={Find<Label>(form, "Status").Text}");
        Assert.Empty(Find<DataGridView>(form, "NetworkManifests").Rows.Cast<DataGridViewRow>());
        Find<RadioButton>(form, "CustomNetworkVersion").Checked = true;
        var version = Find<TextBox>(form, "NetworkVersion");
        Assert.True(version.Enabled);
        Assert.True(version.Visible && version.Height >= 20);
        Assert.True(version.Width >= 120, $"version bounds={version.Bounds}; parent={version.Parent!.Bounds}");
        Assert.True(version.Parent!.ClientRectangle.Contains(version.Bounds), "自定义版本号输入框被父容器裁切");
        version.Text = "7.1.0";
        Assert.True(Find<Button>(form, "GetNetworkManifests").Enabled);
        Assert.Empty(handler.Versions);
        Assert.Equal(new[] { "hyp-api.mihoyo.com" }, handler.Hosts);
        int beforeGet = handler.Requests;
        ClickAndWait(form, session, "GetNetworkManifests");
        Assert.Equal(beforeGet + 1, handler.Requests);
        Assert.Equal(new[] { "api-takumi.mihoyo.com" }, handler.Hosts.Skip(beforeGet));
        Assert.Equal("7.1.0", handler.Versions.Last());
        var table = Find<DataGridView>(form, "NetworkManifests");
        Assert.True(table.Rows.Count > 0, Find<Label>(form, "Status").Text);
        DataGridViewRow row = Assert.Single(table.Rows.Cast<DataGridViewRow>());
        Assert.Equal("hk4e_cn", row.Cells["Game"].Value);
        Assert.Equal("game", row.Cells["Package"].Value);
        Assert.Equal("类别-game", row.Cells["Name"].Value);
        Assert.Equal("未获取", row.Cells["State"].Value);
        Assert.IsType<long>(row.Cells["Size"].Value);
        Assert.Equal("7.1.0", row.Cells["Version"].Value);
        Assert.Equal("—", row.Cells["Modified"].Value);
        Assert.Null(row.Cells["Entries"].Value);
        Assert.Null(session.Reference);
        Assert.False(Find<Button>(form, "ParseNetworkReference").Enabled);
        Assert.False((bool)row.Cells["Checked"].Value!);
        row.Cells["Checked"].Value = true;
        int beforeParse = handler.Requests;
        ClickAndWait(form, session, "ParseNetworkReference");
        Assert.Equal(beforeParse + 1, handler.Requests);
        Assert.Equal(1, session.Reference!.FileCount);
        Assert.True(Find<Button>(form, "Next").Enabled);
        Assert.Equal("已解析", row.Cells["State"].Value);
        Assert.DoesNotContain("勾选后", Find<Label>(form, "Status").Text);
        Find<Button>(form, "Next").PerformClick();
        Assert.False(Find<ProgressBar>(form, "OperationProgress").Visible);
        Assert.DoesNotContain("解析", Find<Label>(form, "Status").Text);
        Find<Button>(form, "Back").PerformClick();
        Assert.Contains("参考清单已就绪", Find<Label>(form, "Status").Text);
        table.Rows[0].Cells[0].Value = false;
        Assert.Contains("重新获取并解析", Find<Label>(form, "Status").Text);
        Assert.False(Find<Button>(form, "Next").Enabled);
        table.Rows[0].Cells[0].Value = true;
        Find<RadioButton>(form, "LocalSource").Checked = true;
        Assert.Null(session.Reference);
        Assert.Equal("未获取", row.Cells["State"].Value);
        Find<RadioButton>(form, "NetworkSource").Checked = true;
        Assert.Contains("重新获取并解析", Find<Label>(form, "Status").Text);
        Assert.DoesNotContain("目录已变更", Find<Label>(form, "Status").Text);
        ClickAndWait(form, session, "ParseNetworkReference");
        version.Text = "7.0.0";
        Assert.Empty(table.Rows.Cast<DataGridViewRow>());
        Assert.Null(session.Reference);
        Assert.False(Find<Button>(form, "Next").Enabled);
        Assert.Equal(beforeParse + 2, handler.Requests);
        Rectangle client = form.RectangleToScreen(form.ClientRectangle);
        foreach (string name in new[] { "NetworkGames", "NetworkRegions", "NetworkVersion", "QueryNetwork", "GetNetworkManifests", "ParseNetworkReference", "NetworkManifests" })
        {
            Control control = Find<Control>(form, name);
            Assert.True(client.Contains(control.RectangleToScreen(control.ClientRectangle)), name);
        }
        string screenshot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "opencode", "network-reference.png");
        using var image = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
        image.Save(screenshot);
        handler.Fail = true;
        ClickAndWait(form, session, "QueryNetwork");
        Assert.False(Find<Button>(form, "GetNetworkManifests").Enabled);
        ClickAndWait(form, session, "GetNetworkManifests");
        Assert.Empty(table.Rows.Cast<DataGridViewRow>());
        Assert.Null(session.Reference);
        Assert.Contains("失败", Find<Label>(form, "Status").Text);
        ClickAndWait(form, session, "QueryNetwork");
        Assert.Equal(2, regions.Items.Count);
        Assert.Empty(games.Items);
        Assert.False(Find<Button>(form, "GetNetworkManifests").Enabled);
        Assert.Contains("失败", Find<Label>(form, "Status").Text);
        Assert.DoesNotContain("再点击获取", Find<Label>(form, "Status").Text);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectedServiceFailureOrEmptyListNeverQueriesOtherService(bool noGame) => OnSta(() =>
    {
        using var handler = new Handler { FailChina = !noGame, NoGame = noGame };
        using var http = new HttpClient(handler);
        var session = new WizardSession();
        using var form = new MainForm(session, networkClient: new NetworkManifestClient(http))
        { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false };
        form.Show();
        ClickAndWait(form, session, "QueryNetwork");
        ListBox regions = Find<ListBox>(form, "NetworkRegions");
        string status = Find<Label>(form, "Status").Text;
        if (noGame)
        {
            Assert.Empty(Find<ListBox>(form, "NetworkGames").Items);
            Assert.Contains("官方未返回游戏列表", status);
            Assert.DoesNotContain("再点击获取", status);
            Assert.False(Find<Button>(form, "GetNetworkManifests").Enabled);
        }
        else
        {
            Assert.Equal(2, regions.Items.Count);
            Assert.Equal("国服", regions.SelectedItem!.ToString());
            Assert.Contains("官方网络请求失败", status);
            Assert.False(Find<Button>(form, "GetNetworkManifests").Enabled);
        }
        Assert.Equal(1, handler.Requests);
        Assert.Equal(new[] { "hyp-api.mihoyo.com" }, handler.Hosts);
        Assert.Empty(handler.Versions);
        Assert.False(Find<Button>(form, "Next").Enabled);
    });

    [Fact]
    public void SelectedRegionQueryAndRepeatedGetStayWithinTheirStages() => OnSta(() =>
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var session = new WizardSession();
        using var form = new MainForm(session, networkClient: new NetworkManifestClient(http))
        { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false };
        form.Show();
        var regions = Find<ListBox>(form, "NetworkRegions");
        Assert.Equal(2, regions.Items.Count);
        regions.SelectedIndex = 1;
        Assert.Equal(0, handler.Requests);
        ClickAndWait(form, session, "QueryNetwork");
        Assert.Equal(new[] { "sg-hyp-api.hoyoverse.com" }, handler.Hosts);
        Assert.Empty(handler.Versions);
        Assert.Equal(-1, Find<ListBox>(form, "NetworkGames").SelectedIndex);
        Find<ListBox>(form, "NetworkGames").SelectedIndex = 0;
        Assert.Contains("7.2.0", Find<RadioButton>(form, "LatestNetworkVersion").Text);
        Assert.Contains("7.2.0", Find<Label>(form, "Status").Text);
        ClickAndWait(form, session, "GetNetworkManifests");
        ClickAndWait(form, session, "GetNetworkManifests");
        Assert.Equal(3, handler.Requests);
        Assert.Equal(new[] { "sg-public-api.hoyoverse.com", "sg-public-api.hoyoverse.com" }, handler.Hosts.Skip(1));
        Assert.Equal(2, handler.Versions.Count);
        regions.SelectedIndex = 0;
        Assert.False(Find<Button>(form, "GetNetworkManifests").Enabled);
        Assert.Empty(Find<DataGridView>(form, "NetworkManifests").Rows.Cast<DataGridViewRow>());
        Assert.Equal(3, handler.Requests);
        Find<ListBox>(form, "NetworkGames").SelectedIndex = -1;
        Assert.Empty(Find<ListBox>(form, "NetworkGames").Items);
        Assert.False(Find<Button>(form, "GetNetworkManifests").Enabled);
    });

    [Theory]
    [InlineData("query", "cancel")]
    [InlineData("query", "window")]
    [InlineData("build", "cancel")]
    [InlineData("build", "button")]
    [InlineData("body", "cancel")]
    [InlineData("body", "window")]
    [InlineData("body", "button")]
    public void StalledNetworkCanBeCancelledOrClosed(string stage, string action) => OnSta(() =>
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var session = new WizardSession();
        using var form = new MainForm(session, networkClient: new NetworkManifestClient(http))
        { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false };
        form.Show();
        if (stage != "query")
        {
            ClickAndWait(form, session, "QueryNetwork");
            Find<ListBox>(form, "NetworkGames").SelectedIndex = 0;
        }
        if (stage == "body")
        {
            ClickAndWait(form, session, "GetNetworkManifests");
            Find<DataGridView>(form, "NetworkManifests").Rows[0].Cells["Checked"].Value = true;
        }
        handler.BlockPath = stage == "query" ? "getGameBranches" : stage == "build" ? "getBuild" : null;
        handler.BlockBody = stage == "body";
        string operationButton = stage == "query" ? "QueryNetwork" : stage == "build" ? "GetNetworkManifests" : "ParseNetworkReference";
        Find<Button>(form, operationButton).PerformClick();
        WaitUntil(() => handler.Blocked.Task.IsCompleted);
        Assert.True(session.IsBusy);
        Assert.True(Find<Button>(form, "CloseWizard").Enabled);
        var cancel = Find<Button>(form, "CancelNetwork");
        Assert.True(cancel.Visible && cancel.Enabled);
        Assert.Equal("取消网络操作", cancel.Text);
        Assert.False(Find<Button>(form, "CancelParsing").Visible);
        Assert.Equal("NetworkActions", cancel.Parent!.Name);
        Assert.True(cancel.Left > Find<Button>(form, "QueryNetwork").Parent!.Right);
        if (action == "cancel") cancel.PerformClick();
        else if (action == "button") Find<Button>(form, "CloseWizard").PerformClick();
        else form.Close();
        WaitUntil(() => !session.IsBusy && (action == "cancel" ? !Find<ProgressBar>(form, "OperationProgress").Visible : form.IsDisposed));
        Assert.True(handler.Cancelled);
        if (stage == "body") Assert.True(handler.BodyDisposed);
        Assert.Null(session.Reference);
        if (stage == "query") Assert.Equal(1, handler.Requests);
        if (action == "cancel")
        {
            Assert.False(form.IsDisposed);
            if (stage == "body")
                Assert.Equal("未获取", Assert.Single(Find<DataGridView>(form, "NetworkManifests").Rows.Cast<DataGridViewRow>()).Cells["State"].Value);
            else Assert.Empty(Find<DataGridView>(form, "NetworkManifests").Rows.Cast<DataGridViewRow>());
            Assert.Contains("已取消网络操作", Find<Label>(form, "Status").Text);
            handler.BlockPath = null;
            handler.BlockBody = false;
            ClickAndWait(form, session, operationButton);
            Assert.Equal(stage != "query", Find<Button>(form, "GetNetworkManifests").Enabled);
        }
        else Assert.True(form.IsDisposed);
    });

    private static void WaitUntil(Func<bool> condition)
    {
        long start = Environment.TickCount64;
        while (!condition() && Environment.TickCount64 - start < 5000) { Application.DoEvents(); Thread.Yield(); }
        Assert.True(condition());
    }

    [Fact]
    public void ArbitraryConfiguredServiceIsUsedDirectlyAndGameRemainsUnselected() => OnSta(() =>
    {
        using var handler = new Handler { ExtraGames = true };
        using var http = new HttpClient(handler);
        var service = new NetworkService("future", "ServiceGlobal", new Uri("https://future.example/getGameBranches"),
            new Uri("https://build.future.example/getBuild"), "launcher&new");
        var session = new WizardSession();
        using var form = new MainForm(session, networkClient: new NetworkManifestClient(http), loadNetworkServices: () => [service])
        { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false };
        form.Show();
        Assert.Equal(0, handler.Requests);
        ClickAndWait(form, session, "QueryNetwork");
        Assert.Equal(new[] { "future.example" }, handler.Hosts);
        ListBox games = Find<ListBox>(form, "NetworkGames");
        Assert.Equal(-1, games.SelectedIndex);
        Assert.False(Find<Button>(form, "GetNetworkManifests").Enabled);
        Assert.Contains("不会自动选择游戏", Find<Label>(form, "Status").Text);
        games.SelectedIndex = 2;
        ClickAndWait(form, session, "GetNetworkManifests");
        Assert.Equal(new[] { "future.example", "build.future.example" }, handler.Hosts);
        Assert.Empty(Find<ManifestTable>(form, "NetworkManifests").CheckedInputs);
        ClickAndWait(form, session, "QueryNetwork");
        Assert.Equal(-1, games.SelectedIndex);
        Assert.False(Find<Button>(form, "GetNetworkManifests").Enabled);
    });

    [Fact]
    public void ConfigurationFailureLeavesOfflineSourcesAvailable() => OnSta(() =>
    {
        using var form = new MainForm(new WizardSession(), loadNetworkServices: () =>
            throw new CoreException(CoreErrorCode.NetworkConfigurationInvalid))
        { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false };
        form.Show();
        Assert.Empty(Find<ListBox>(form, "NetworkRegions").Items);
        Assert.False(Find<Button>(form, "QueryNetwork").Enabled);
        Assert.Contains("网络服务配置缺失或无效", Find<Label>(form, "Status").Text);
        foreach (string source in new[] { "LocalSource", "ManualSource" })
        {
            var option = Find<RadioButton>(form, source);
            Assert.True(option.Enabled);
            option.Checked = true;
            Assert.True(option.Checked);
        }
    });

    [Fact]
    public void DoubleClickQueriesOnlyItsStageAndEmptySpaceDoesNothing() => OnSta(() =>
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var session = new WizardSession();
        using var form = new MainForm(session, networkClient: new NetworkManifestClient(http))
        { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false };
        form.Show();
        var regions = Find<ListBox>(form, "NetworkRegions");
        var games = Find<ListBox>(form, "NetworkGames");
        void DoubleClick(ListBox list, Point location)
        {
            typeof(Control).GetMethod("OnMouseDoubleClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(list, [new MouseEventArgs(MouseButtons.Left, 2, location.X, location.Y, 0)]);
            WaitUntil(() => !session.IsBusy && !Find<ProgressBar>(form, "OperationProgress").Visible);
        }
        DoubleClick(games, new Point(3, 3));
        Assert.Equal(0, handler.Requests);
        DoubleClick(regions, new Point(3, 3));
        Assert.Equal(1, handler.Requests);
        Assert.Equal(-1, games.SelectedIndex);
        DoubleClick(games, new Point(3, 3));
        Assert.Equal(2, handler.Requests);
        Assert.Single(Find<DataGridView>(form, "NetworkManifests").Rows.Cast<DataGridViewRow>());
        Assert.Empty(Find<ManifestTable>(form, "NetworkManifests").CheckedInputs);
        DoubleClick(games, new Point(3, games.ClientSize.Height - 2));
        Assert.Equal(2, handler.Requests);
        var cancel = Find<Button>(form, "CancelNetwork");
        Assert.False(cancel.Enabled);
        Assert.NotEqual(cancel.Parent, Find<Button>(form, "CloseWizard").Parent);
    });

    [Fact]
    public void DynamicGamesAndSelectedPackagesNeedOnlyExplicitStageRequests() => OnSta(() =>
    {
        using var handler = new Handler { ExtraGames = true, ExtraPackage = true };
        using var http = new HttpClient(handler);
        var session = new WizardSession();
        using var form = new MainForm(session, networkClient: new NetworkManifestClient(http))
        { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false };
        form.Show();
        ClickAndWait(form, session, "QueryNetwork");
        var games = Find<ListBox>(form, "NetworkGames");
        var table = Find<ManifestTable>(form, "NetworkManifests");
        Assert.Equal(new[] { "hk4e_cn", "future_cn (2)", "future_cn (3)" }, games.Items.Cast<object>().Select(item => item.ToString()));
        Assert.Empty(table.Rows.Cast<DataGridViewRow>());
        games.SelectedIndex = 2;
        Find<RadioButton>(form, "CustomNetworkVersion").Checked = true;
        Find<TextBox>(form, "NetworkVersion").Text = "7.1.0";
        Assert.Equal(1, handler.Requests);
        ClickAndWait(form, session, "GetNetworkManifests");
        Assert.Equal(2, handler.Requests);
        Assert.Equal(2, table.Rows.Count);
        Assert.Empty(table.CheckedInputs);
        Assert.False(Find<Button>(form, "ParseNetworkReference").Enabled);
        Assert.All(table.Rows.Cast<DataGridViewRow>(), row =>
        {
            Assert.Equal("future_cn", row.Cells["Game"].Value);
            Assert.Equal("7.1.0", row.Cells["Version"].Value);
            Assert.Equal("未获取", row.Cells["State"].Value);
            Assert.Equal("—", row.Cells["Modified"].Value);
            Assert.Null(row.Cells["Entries"].Value);
        });
        foreach (DataGridViewRow row in table.Rows) row.Cells["Checked"].Value = (string?)row.Cells["Package"].Value == "future-voice";
        Assert.Equal(2, handler.Requests);
        ClickAndWait(form, session, "ParseNetworkReference");
        Assert.Equal(3, handler.Requests);
        Assert.EndsWith("manifest-future-voice", handler.Paths.Last());
        Assert.DoesNotContain(handler.Paths, path => path.EndsWith("manifest-game"));
        Assert.Equal(1, session.Reference!.FileCount);
        foreach (DataGridViewRow row in table.Rows)
        {
            bool selected = (string?)row.Cells["Package"].Value == "future-voice";
            Assert.Equal(selected ? "已解析" : "未获取", row.Cells["State"].Value);
            Assert.Equal(selected ? 1 : (int?)null, row.Cells["Entries"].Value);
        }
        Find<TextBox>(form, "NetworkVersion").Text = "7.0.0";
        Assert.Empty(table.Rows.Cast<DataGridViewRow>());
        Assert.Null(session.Reference);
        Assert.Equal(3, handler.Requests);
        games.SelectedIndex = 1;
        Assert.Equal(3, handler.Requests);
        foreach (string name in new[] { "NetworkManifests", "LocalManifests", "Manifests" })
        {
            var columns = Find<ManifestTable>(form, name).Columns;
            Assert.Equal(new[] { "Checked", "Name", "Modified", "Size", "State", "Game", "Version", "Package", "Entries" },
                columns.Cast<DataGridViewColumn>().Select(column => column.Name));
            Assert.Equal(new float[] { 8, 7, 3, 3, 3, 2, 4, 3 },
                columns.Cast<DataGridViewColumn>().Skip(1).Select(column => column.FillWeight));
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectedPackagesKeepTheirIdentityEvenWhenDownloadUrlsMatch(bool sharedUrl) => OnSta(() =>
    {
        using var handler = new Handler { ExtraPackage = true, SharedManifestUrl = sharedUrl };
        using var http = new HttpClient(handler);
        var session = new WizardSession();
        using var form = new MainForm(session, networkClient: new NetworkManifestClient(http))
        { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false };
        form.Show();
        ClickAndWait(form, session, "QueryNetwork");
        Find<ListBox>(form, "NetworkGames").SelectedIndex = 0;
        ClickAndWait(form, session, "GetNetworkManifests");
        var table = Find<ManifestTable>(form, "NetworkManifests");
        Assert.Equal(2, table.Rows.Count);
        Assert.Empty(table.CheckedInputs);
        foreach (DataGridViewRow row in table.Rows) row.Cells["Checked"].Value = true;
        ReferenceInput[] inputs = table.CheckedInputs;
        Assert.Equal(2, inputs.Select(input => input.Remote!.MatchingField).Distinct().Count());
        Assert.Equal(sharedUrl ? 1 : 2, inputs.Select(input => input.Remote!.Url).Distinct().Count());
        ClickAndWait(form, session, "ParseNetworkReference");
        Assert.Equal(4, handler.Requests);
        Assert.NotNull(session.Reference);
        Assert.Equal(1, session.Reference.FileCount);
        Assert.Equal(2, session.Reference.Sources.Count);
        Assert.All(table.Rows.Cast<DataGridViewRow>(), row =>
        {
            Assert.Equal("已解析", row.Cells["State"].Value);
            Assert.Equal(1, row.Cells["Entries"].Value);
        });
        Assert.True(Find<Button>(form, "Next").Enabled);
        table.Rows[0].Cells["Checked"].Value = false;
        Assert.Null(session.Reference);
        Assert.False(Find<Button>(form, "Next").Enabled);
    });

    [Fact]
    public void CorruptDownloadedManifestFailsWithoutPublishingReference() => OnSta(() =>
    {
        using var handler = new Handler { CorruptManifest = true };
        using var http = new HttpClient(handler);
        var session = new WizardSession();
        using var form = new MainForm(session, networkClient: new NetworkManifestClient(http))
        { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false };
        form.Show();
        ClickAndWait(form, session, "QueryNetwork");
        Find<ListBox>(form, "NetworkGames").SelectedIndex = 0;
        ClickAndWait(form, session, "GetNetworkManifests");
        Assert.Contains("请勾选", Find<Label>(form, "Status").Text);
        Find<DataGridView>(form, "NetworkManifests").Rows[0].Cells["Checked"].Value = true;
        int requests = handler.Requests;
        ClickAndWait(form, session, "ParseNetworkReference");
        Assert.Equal(requests + 1, handler.Requests);
        Assert.Equal("失败", Assert.Single(Find<DataGridView>(form, "NetworkManifests").Rows.Cast<DataGridViewRow>()).Cells["State"].Value);
        Assert.Null(session.Reference);
        Assert.Contains("处理失败", Find<Label>(form, "Status").Text);
        Assert.DoesNotContain("勾选后解析", Find<Label>(form, "Status").Text);
        Assert.False(Find<Button>(form, "Next").Enabled);
        Assert.False(Find<ProgressBar>(form, "OperationProgress").Visible);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedMultiPackageBatchCannotAdvanceAndCanRecover(bool conflict) => OnSta(() =>
    {
        using var handler = new Handler
        { ExtraPackage = true, FailExtraPackage = !conflict, ConflictingExtraPackage = conflict };
        using var http = new HttpClient(handler);
        var session = new WizardSession();
        using var form = new MainForm(session, networkClient: new NetworkManifestClient(http))
        { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false };
        form.Show();
        ClickAndWait(form, session, "QueryNetwork");
        Find<ListBox>(form, "NetworkGames").SelectedIndex = 0;
        ClickAndWait(form, session, "GetNetworkManifests");
        var table = Find<ManifestTable>(form, "NetworkManifests");
        foreach (DataGridViewRow row in table.Rows) row.Cells["Checked"].Value = true;
        ClickAndWait(form, session, "ParseNetworkReference");
        Assert.Equal(4, handler.Requests);
        Assert.Null(session.Reference);
        Assert.False(Find<Button>(form, "Next").Enabled);
        Assert.Contains("部分文件无效或所选清单存在同侧冲突", Find<Label>(form, "Status").Text);
        Assert.All(table.Rows.Cast<DataGridViewRow>(), row =>
        {
            bool parsed = conflict || (string?)row.Cells["Package"].Value == "game";
            Assert.Equal(parsed ? "已解析" : "失败", row.Cells["State"].Value);
            Assert.Equal(parsed ? 1 : (int?)null, row.Cells["Entries"].Value);
        });
        Assert.False(Find<ProgressBar>(form, "OperationProgress").Visible);
        handler.FailExtraPackage = handler.ConflictingExtraPackage = false;
        ClickAndWait(form, session, "GetNetworkManifests");
        Assert.Empty(table.CheckedInputs);
        foreach (DataGridViewRow row in table.Rows) row.Cells["Checked"].Value = true;
        ClickAndWait(form, session, "ParseNetworkReference");
        Assert.Equal(7, handler.Requests);
        Assert.NotNull(session.Reference);
        Assert.Equal(1, session.Reference.FileCount);
        Assert.True(Find<Button>(form, "Next").Enabled);
        Assert.All(table.Rows.Cast<DataGridViewRow>(), row => Assert.Equal("已解析", row.Cells["State"].Value));
    });

    [Fact]
    public void SourceDisplayRemovesUrlSecretsWithoutChangingIdentity()
    {
        const string source = "https://autopatchcn.yuanshen.com/test/manifest?signature=private-value#fragment";
        Assert.Equal("https://autopatchcn.yuanshen.com/test/manifest", HoyoFileClean.Presentation.DiagnosticText.DisplaySource(source));
        Assert.Equal(@"C:\manifests\local", HoyoFileClean.Presentation.DiagnosticText.DisplaySource(@"C:\manifests\local"));
    }

    private static void ClickAndWait(MainForm form, WizardSession session, string name)
    {
        Find<Button>(form, name).PerformClick();
        long start = Environment.TickCount64;
        while ((session.IsBusy || Find<ProgressBar>(form, "OperationProgress").Visible)
            && Environment.TickCount64 - start < 10000) { Application.DoEvents(); Thread.Yield(); }
        Application.DoEvents();
        Assert.False(session.IsBusy);
    }

    private static T Find<T>(Control parent, string name) where T : Control => (T)Assert.Single(parent.Controls.Find(name, true));
    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var host = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                    Location = new Point(-20000, -20000), Size = new Size(1, 1) };
                host.Shown += (_, _) => host.BeginInvoke(() =>
                {
                    try { action(); }
                    catch (Exception error) { failure = error; }
                    finally { host.Close(); }
                });
                Application.Run(host);
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
