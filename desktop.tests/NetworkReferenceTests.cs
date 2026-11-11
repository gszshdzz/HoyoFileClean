using System.Net;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using Google.Protobuf;
using HoyoFileClean.Desktop;
using HoyoFileClean.Protos;
using Xunit;

namespace HoyoFileClean.Desktop.Tests;

public sealed class NetworkReferenceTests
{
    private sealed class Handler : HttpMessageHandler
    {
        internal int Requests;
        internal bool Fail;
        internal readonly List<string> Versions = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Requests);
            token.ThrowIfCancellationRequested();
            string biz = request.RequestUri!.Host == "hyp-api.mihoyo.com" ? "hk4e_cn" : "hk4e_global";
            byte[] bytes = new SophonManifestProto { Assets = { new SophonManifestAssetProperty
            { AssetName = "Data/file", AssetSize = 3, AssetHashMd5 = "900150983cd24fb0d6963f7d28e17f72" } } }.ToByteArray();
            object data;
            if (request.RequestUri.AbsolutePath.EndsWith("getGameBranches"))
                data = new { game_branches = new[] { new { game = new { biz }, main = new
                { tag = "7.2.0", branch = "main", password = "p", package_id = "pkg" } } } };
            else if (request.RequestUri.AbsolutePath.EndsWith("getBuild"))
            {
                string version = Uri.UnescapeDataString(request.RequestUri.Query.Split("tag=")[1]);
                Versions.Add(version);
                data = new { tag = version, build_id = "build", manifests = new[] { new
                {
                    matching_field = "game", category_name = "本体",
                    manifest = new { id = "manifest", checksum = Convert.ToHexString(MD5.HashData(bytes)), compressed_size = bytes.Length, uncompressed_size = bytes.Length },
                    manifest_download = new { url_prefix = "https://autopatchcn.yuanshen.com/test", url_suffix = "", compression = 0, encryption = 0 }
                } } };
            }
            else return Task.FromResult(new HttpResponseMessage(Fail ? HttpStatusCode.NotFound : HttpStatusCode.OK)
                { RequestMessage = request, Content = new ByteArrayContent(bytes) });
            return Task.FromResult(new HttpResponseMessage(Fail ? HttpStatusCode.NotFound : HttpStatusCode.OK)
            { RequestMessage = request, Content = new StringContent(JsonSerializer.Serialize(new { retcode = 0, data })) });
        }
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
        var games = Find<ListBox>(form, "NetworkGames");
        var regions = Find<ListBox>(form, "NetworkRegions");
        Assert.Equal(SelectionMode.One, games.SelectionMode);
        Assert.Equal(SelectionMode.One, regions.SelectionMode);
        Assert.True(games.Left < regions.Left);
        Assert.True(games.ClientSize.Height / games.ItemHeight == 6,
            $"list={games.Bounds}; client={games.ClientSize}; item={games.ItemHeight}; parent={games.Parent!.Bounds}; rows={string.Join(',', ((TableLayoutPanel)games.Parent).GetRowHeights())}; font={games.Font.Size}");
        for (int i = 0; i < 20; i++) games.Items.Add("滚动验证-" + i);
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
        Assert.Equal(2, handler.Requests);
        Assert.True(regions.Items.Count == 2,
            $"requests={handler.Requests}; game={games.SelectedIndex}; queryEnabled={Find<Button>(form, "QueryNetwork").Enabled}; summary={Find<Label>(form, "NetworkSummary").Text}; status={Find<Label>(form, "Status").Text}");
        Assert.Empty(Find<DataGridView>(form, "NetworkManifests").Rows.Cast<DataGridViewRow>());
        Find<RadioButton>(form, "CustomNetworkVersion").Checked = true;
        var version = Find<TextBox>(form, "NetworkVersion");
        Assert.True(version.Enabled);
        Assert.True(version.Visible && version.Height >= 20);
        Assert.True(version.Width >= 120, $"version bounds={version.Bounds}; parent={version.Parent!.Bounds}");
        Assert.True(version.Parent!.ClientRectangle.Contains(version.Bounds), "自定义版本号输入框被父容器裁切");
        version.Text = "7.1.0";
        int beforeGet = handler.Requests;
        ClickAndWait(form, session, "GetNetworkManifests");
        Assert.Equal(beforeGet + 3, handler.Requests);
        Assert.Equal("7.1.0", handler.Versions.Last());
        var table = Find<DataGridView>(form, "NetworkManifests");
        Assert.True(table.Rows.Count > 0, $"summary={Find<Label>(form, "NetworkSummary").Text}; status={Find<Label>(form, "Status").Text}");
        DataGridViewRow row = Assert.Single(table.Rows.Cast<DataGridViewRow>());
        Assert.Equal("原神-官服", row.Cells["Game"].Value);
        Assert.Equal("7.1.0", row.Cells["Version"].Value);
        Assert.Equal("—", row.Cells["Modified"].Value);
        Assert.Null(row.Cells["Entries"].Value);
        Assert.Null(session.Reference);
        int beforeParse = handler.Requests;
        ClickAndWait(form, session, "ParseNetworkReference");
        Assert.Equal(beforeParse, handler.Requests);
        Assert.Equal(1, session.Reference!.FileCount);
        Assert.True(Find<Button>(form, "Next").Enabled);
        Assert.Equal("已解析", row.Cells["State"].Value);
        Find<RadioButton>(form, "LocalSource").Checked = true;
        Assert.Null(session.Reference);
        Assert.Equal("未解析", row.Cells["State"].Value);
        Find<RadioButton>(form, "NetworkSource").Checked = true;
        ClickAndWait(form, session, "ParseNetworkReference");
        version.Text = "7.0.0";
        Assert.Empty(table.Rows.Cast<DataGridViewRow>());
        Assert.Null(session.Reference);
        Assert.False(Find<Button>(form, "Next").Enabled);
        Assert.Equal(beforeParse, handler.Requests);
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
        ClickAndWait(form, session, "GetNetworkManifests");
        Assert.Empty(table.Rows.Cast<DataGridViewRow>());
        Assert.Null(session.Reference);
        Assert.Contains("失败", Find<Label>(form, "Status").Text);
    });

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
