using System.Text.Json;
using HoyoFileClean.Desktop;
using Xunit;
using L = HoyoFileClean.Presentation.Text;

namespace HoyoFileClean.Desktop.Tests;

public sealed class ManifestDetailsTests
{
    [Theory]
    [InlineData(1000)]
    [InlineData(100000)]
    public void LargeDetailsResizeKeepsPaintingAndLayoutWorkBounded(int count) => NativeTestHost.OnSta(() =>
    {
        var sample = Sample();
        string path = Path.Combine(sample.Directory, "large.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            fileCount = count, totalSize = count,
            files = Enumerable.Range(0, count).Select(index => new { path = $"file-{index:D6}", size = 1 }).ToArray()
        }));
        var snapshot = ManifestConverter.Read(path, TestContext.Current.CancellationToken);
        using var dialog = new ManifestDetailsForm(sample.Input, snapshot, "游戏: hk4e_cn      版本: 7.2.0      包体: game      包类型: 发行版")
        { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false };
        dialog.Show();
        var grid = NativeTestHost.Find<DataGridView>(dialog, "ManifestContents");
        int layouts = 0, values = 0;
        grid.Layout += (_, _) => layouts++;
        grid.CellValueNeeded += (_, _) => values++;
        using var bitmap = new Bitmap(1100, 800);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 30; i++)
        {
            dialog.ClientSize = new Size(900 + i % 2 * 20, 640 + i % 2 * 20);
            grid.DrawToBitmap(bitmap, grid.ClientRectangle);
        }
        string measurement = $"details rows={count}; resize: {watch.ElapsedMilliseconds}ms, layouts={layouts}, values={values}";
        Console.WriteLine(measurement);
        Assert.True(layouts <= 30 * 10, measurement);
        Assert.InRange(values, 1, 30 * grid.Columns.Count * (grid.Height / grid.RowTemplate.Height + 2) * 4);
        Assert.True((bool)typeof(Control).GetProperty("DoubleBuffered",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(grid)!, measurement);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParsingCancelLivesAtRightOfCurrentPageActionsNotBottomNavigation(bool local) => NativeTestHost.OnSta(() =>
    {
        var sample = Sample();
        using var entered = new ManualResetEventSlim();
        var parser = new ReferenceParser((_, token) =>
        {
            entered.Set();
            if (!token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            token.ThrowIfCancellationRequested();
            return sample.Snapshot;
        });
        var session = NativeTestHost.ReferenceSession();
        using var form = new MainForm(session, parser)
        {
            StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false
        };
        form.Show();
        NativeTestHost.Find<RadioButton>(form, local ? "LocalSource" : "ManualSource").Checked = true;
        NativeTestHost.Find<ManifestTable>(form, local ? "LocalManifests" : "Manifests").AddFiles([sample.Input.File], check: true);
        Control navigation = NativeTestHost.Find<Button>(form, "Next").Parent!;
        string[] before = navigation.Controls.Cast<Control>().Select(control => control.Name).ToArray();
        NativeTestHost.Find<Button>(form, local ? "ParseLocalReference" : "ParseManualReference").PerformClick();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        Button cancel = NativeTestHost.Find<Button>(form, "CancelParsing");
        Assert.True(cancel.Visible && cancel.Enabled);
        var row = Assert.IsType<TableLayoutPanel>(cancel.Parent);
        Assert.Equal(local ? "LocalReferenceActions" : "ManualReferenceActions", row.Name);
        Assert.Equal(new TableLayoutPanelCellPosition(1, 0), row.GetPositionFromControl(cancel));
        Assert.NotSame(navigation, row);
        Assert.True(row.ClientSize.Width - cancel.Right <= cancel.Margin.Right + 1);
        Assert.Equal(before, navigation.Controls.Cast<Control>().Select(control => control.Name));
        cancel.PerformClick();
        NativeTestHost.WaitUntil(() => !session.IsBusy);
        Assert.Null(session.Reference);
    });

    private static (ReferenceInput Input, FileListSnapshot Snapshot, string Directory) Sample()
    {
        string directory = Path.Combine(TestTemporaryFiles.Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "sample.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            fileCount = 1, totalSize = 1, files = new[] { new { path = "YuanShen.exe", size = 1 } }
        }));
        return (new ReferenceInput(new LocalManifestFile("sample.json", path, 0, default)),
            ManifestConverter.Read(path, TestContext.Current.CancellationToken), directory);
    }

    [Theory]
    [InlineData("manual")]
    [InlineData("local")]
    [InlineData("network")]
    public void OnlySuccessfulRowsOpenDetailsAndResetOrRetryReleasesSnapshot(string source) => NativeTestHost.OnSta(() =>
    {
        var sample = Sample();
        using var table = new ManifestTable("table", groupByModifiedHour: source == "local");
        if (source == "network")
        {
            var remote = new RemoteManifest("official-package", "official-category", new Uri("https://example.invalid/manifest"),
                1, 1, "unused", false);
            table.AddNetworkFiles([sample.Input with { Remote = remote }], "official-biz", "7.1.0");
            table.Rows[0].Cells[0].Value = true;
        }
        else table.AddFiles([sample.Input.File], check: true);
        ReferenceInput input = Assert.Single(table.CheckedInputs);
        int opened = 0;
        table.DetailsRequested += (actualInput, actualSnapshot, information) =>
        {
            opened++;
            Assert.Same(input, actualInput);
            Assert.Same(sample.Snapshot, actualSnapshot);
            Assert.DoesNotContain("\n", information);
            Assert.DoesNotContain("\r", information);
            Assert.Contains(source == "network" ? "official-biz" : "hk4e_cn", information);
            if (source != "network") Assert.Contains("hk4e_cn", information);
            Assert.Contains(source == "network" ? "official-package" : "game", information);
        };
        table.OpenDetails(0, 1);
        Assert.Equal(0, opened);
        var item = new ReferenceParseItem(1, null, sample.Snapshot);
        table.ShowParsedResults([input], [item]);
        table.OpenDetails(-1, 1);
        table.OpenDetails(0, 0);
        table.SetBusy(true);
        table.OpenDetails(0, 1);
        Assert.Equal(0, opened);
        table.SetBusy(false);
        table.OpenDetails(0, 1);
        Assert.Equal(1, opened);
        table.ShowResults([(input, ReferenceState.Pending, null)]);
        table.OpenDetails(0, 1);
        Assert.Equal(1, opened);
        table.ShowResults([(input, ReferenceState.Parsed, 1)]);
        table.OpenDetails(0, 1);
        Assert.Equal(1, opened);
        table.ShowResults([(input, ReferenceState.Failed, null)]);
        Assert.Equal(L.Get("UnavailableValue"), table.Rows[0].Cells["Entries"].FormattedValue);
        table.OpenDetails(0, 1);
        Assert.Equal(1, opened);
        table.ShowParsedResults([input], [item]);
        table.ResetResults();
        Assert.Null(table.Rows[0].Cells["Entries"].Value);
        table.OpenDetails(0, 1);
        Assert.Equal(1, opened);
        table.ShowResults([(input, ReferenceState.Parsed, 1)]);
        table.OpenDetails(0, 1);
        Assert.Equal(1, opened);
    });

    [Fact]
    public void DialogShowsContentsExportsLoadedSnapshotAndCloses() => NativeTestHost.OnSta(() =>
    {
        var sample = Sample();
        string output = Path.Combine(sample.Directory, "export.json");
        int selections = 0;
        using var dialog = new ManifestDetailsForm(sample.Input, sample.Snapshot, "游戏: hk4e_cn      版本: 7.2.0      包体: game", _ =>
        {
            selections++;
            return output;
        });
        dialog.StartPosition = FormStartPosition.Manual;
        dialog.Location = new Point(-20000, -20000);
        dialog.Show();
        var information = NativeTestHost.Find<Label>(dialog, "ManifestInformation");
        Assert.Equal("游戏: hk4e_cn      版本: 7.2.0      包体: game      文件数: 1 个      覆盖大小: 1B      MD5: 0/1", information.Text);
        Assert.DoesNotContain(sample.Input.File.FullPath, information.Text);
        Assert.Equal(BorderStyle.None, information.BorderStyle);
        var grid = NativeTestHost.Find<DataGridView>(dialog, "ManifestContents");
        Assert.True(grid.ReadOnly);
        Assert.True(grid.VirtualMode);
        Assert.Equal(1, grid.RowCount);
        Assert.Equal("YuanShen.exe", grid.Rows[0].Cells[0].Value);
        Assert.Equal(1L, grid.Rows[0].Cells[1].Value);
        var export = NativeTestHost.Find<Button>(dialog, "ExportManifestJson");
        using (var held = new FileStream(sample.Input.File.FullPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            export.PerformClick();
            NativeTestHost.WaitUntil(() => export.Enabled);
        }
        Assert.Equal(1, selections);
        Assert.True(File.Exists(output));
        FileListSnapshot exported = ManifestConverter.Read(output, TestContext.Current.CancellationToken);
        Assert.Equal(sample.Snapshot.Files.Values, exported.Files.Values);
        byte[] before = File.ReadAllBytes(output);
        export.PerformClick();
        NativeTestHost.WaitUntil(() => export.Enabled);
        Assert.Equal(L.Get("CoreErrorCode_OutputExists"), NativeTestHost.Find<Label>(dialog, "ExportStatus").Text);
        Assert.Equal(before, File.ReadAllBytes(output));
        NativeTestHost.Find<Button>(dialog, "CloseManifestDetails").PerformClick();
        Assert.False(dialog.Visible);
    });

    [Fact]
    public void DialogDoesNotPreselectInformationAndContentsCannotRemainSelected() => NativeTestHost.OnSta(() =>
    {
        var sample = Sample();
        using var dialog = new ManifestDetailsForm(sample.Input, sample.Snapshot, "game: hk4e", _ => null)
        {
            StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000)
        };
        dialog.Show();
        Application.DoEvents();
        var information = NativeTestHost.Find<Label>(dialog, "ManifestInformation");
        Assert.False(information.Focused);
        var grid = NativeTestHost.Find<DataGridView>(dialog, "ManifestContents");
        Assert.Empty(grid.SelectedCells.Cast<DataGridViewCell>());
        grid.Rows[0].Cells[0].Selected = true;
        Assert.Empty(grid.SelectedCells.Cast<DataGridViewCell>());
        grid.SelectAll();
        Assert.Empty(grid.SelectedCells.Cast<DataGridViewCell>());
        Assert.True(grid.Enabled);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SummaryWrapsAtMinimumWidthAndLeavesSpaceForContents(bool useSegoeUi) => NativeTestHost.OnSta(() =>
    {
        var sample = Sample();
        using var font = new Font("Segoe UI", 9);
        using var dialog = new ManifestDetailsForm(sample.Input, sample.Snapshot,
            "游戏: hk4e_cn      版本: 7.2.0      包体: game; zh-cn; en-us; ja-jp; ko-kr", _ => null)
        {
            StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000)
        };
        if (useSegoeUi) dialog.Font = font;
        dialog.Show();
        var header = NativeTestHost.Find<Label>(dialog, "ManifestInformation");
        int singleLineWidth = header.GetPreferredSize(Size.Empty).Width
            + header.Margin.Horizontal + header.Parent!.Padding.Horizontal;
        dialog.ClientSize = new Size(Math.Max(dialog.ClientSize.Width, singleLineWidth), dialog.ClientSize.Height);
        Application.DoEvents();
        Assert.True(header.GetPreferredSize(Size.Empty).Width <= header.Width, "宽窗口应容纳完整摘要的一行文字。");
        int wideHeight = header.Height;
        dialog.Size = dialog.MinimumSize;
        Application.DoEvents();
        var grid = NativeTestHost.Find<DataGridView>(dialog, "ManifestContents");
        Assert.True(header.GetPreferredSize(Size.Empty).Width > header.Width, "多包摘要必须超出窄窗口宽度，才能验证自动换行。");
        Assert.True(header.Height > wideHeight,
            $"font={header.Font.Name}, wideHeight={wideHeight}, narrowHeight={header.Height}, width={header.Width}, preferredWidth={header.GetPreferredSize(Size.Empty).Width}");
        Assert.True(header.Height >= header.GetPreferredSize(new Size(header.Width, 0)).Height);
        Assert.True(header.Bottom <= grid.Top);
        Assert.True(grid.Height > dialog.ClientSize.Height / 2);
    });

    [Theory]
    [InlineData(0L, "0B")]
    [InlineData(1861222L, "1.77MB")]
    [InlineData(1073741824L, "1GB")]
    [InlineData(132499741081L, "123GB")]
    [InlineData(1073634449817L, "0.976TB")]
    [InlineData(1099511627776L, "1TB")]
    public void DetailSizesUseTheSameThreeSignificantDigitsAsOtherPages(long bytes, string expected)
        => Assert.Equal(expected, HoyoFileClean.Presentation.FileSizeText.Format(bytes));

    [Fact]
    public void CancellingFileSelectionDoesNotExport() => NativeTestHost.OnSta(() =>
    {
        var sample = Sample();
        int selections = 0;
        using var dialog = new ManifestDetailsForm(sample.Input, sample.Snapshot, "", _ => { selections++; return null; });
        dialog.StartPosition = FormStartPosition.Manual;
        dialog.Location = new Point(-20000, -20000);
        dialog.Show();
        NativeTestHost.Find<Button>(dialog, "ExportManifestJson").PerformClick();
        Assert.Equal(1, selections);
        Assert.Empty(NativeTestHost.Find<Label>(dialog, "ExportStatus").Text);
        Assert.Single(Directory.GetFiles(sample.Directory));
    });
}
