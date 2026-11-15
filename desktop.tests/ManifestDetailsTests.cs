using System.Text.Json;
using HoyoFileClean.Desktop;
using Xunit;
using L = HoyoFileClean.Presentation.Text;

namespace HoyoFileClean.Desktop.Tests;

public sealed class ManifestDetailsTests
{
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
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "opencode", "HoyoFileClean.Desktop.Tests", Guid.NewGuid().ToString("N"));
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
            Assert.Contains(source == "network" ? "official-biz" : "原神", information);
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
        Assert.Null(table.Rows[0].Cells["Entries"].Tag);
        table.OpenDetails(0, 1);
        Assert.Equal(1, opened);
        table.ShowResults([(input, ReferenceState.Failed, null)]);
        Assert.Equal(L.Get("UnavailableValue"), table.Rows[0].Cells["Entries"].FormattedValue);
        table.OpenDetails(0, 1);
        Assert.Equal(1, opened);
        table.ShowParsedResults([input], [item]);
        table.ResetResults();
        Assert.Null(table.Rows[0].Cells["Entries"].Tag);
        Assert.Null(table.Rows[0].Cells["Entries"].Value);
        table.OpenDetails(0, 1);
        Assert.Equal(1, opened);
    });

    [Fact]
    public void DialogShowsContentsExportsLoadedSnapshotAndCloses() => NativeTestHost.OnSta(() =>
    {
        var sample = Sample();
        string output = Path.Combine(sample.Directory, "export.json");
        int selections = 0;
        using var dialog = new ManifestDetailsForm(sample.Input, sample.Snapshot, "game: hk4e\r\npackage: game", _ =>
        {
            selections++;
            return output;
        });
        dialog.StartPosition = FormStartPosition.Manual;
        dialog.Location = new Point(-20000, -20000);
        dialog.Show();
        var information = NativeTestHost.Find<TextBox>(dialog, "ManifestInformation");
        Assert.Contains("hk4e", information.Text);
        Assert.Contains(sample.Input.File.FullPath, information.Text);
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
