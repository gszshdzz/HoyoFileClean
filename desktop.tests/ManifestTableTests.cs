using HoyoFileClean.Desktop;
using Xunit;

namespace HoyoFileClean.Desktop.Tests;

[Collection("Windows Forms")]
public sealed class ManifestTableTests
{
    [Theory]
    [InlineData("main", "发行版")]
    [InlineData("predownload", "预下载")]
    [InlineData("future", "—")]
    public void NetworkBranchIsVisibleBeforeParsingAndSurvivesReset(string branch, string expected) => NativeTestHost.OnSta(() =>
    {
        using var table = new ManifestTable("network");
        var remote = new RemoteManifest("game", "本体", new Uri("https://example.com/manifest"), 1, 1,
            "900150983cd24fb0d6963f7d28e17f72", false) { Branch = branch };
        var input = new ReferenceInput(new LocalManifestFile("manifest", @"C:\manifest", 1, default), remote);
        table.AddNetworkFiles([input], "hk4e_cn", "1.0.0");
        Assert.Equal(table.Columns.Count - 1, table.Columns["Branch"]!.Index);
        Assert.Equal(table.Columns["Game"]!.FillWeight, table.Columns["Branch"]!.FillWeight);
        Assert.Equal(table.Columns["Entries"]!.FillWeight, table.Columns["Branch"]!.FillWeight);
        var row = Assert.Single(table.Rows.Cast<DataGridViewRow>());
        Assert.Equal(expected, row.Cells["Branch"].Value);
        Assert.Equal(false, row.Cells[0].Value);
        table.ShowResults([(input, ReferenceState.Pending, null)]);
        FileListSnapshot snapshot = NativeTestHost.ReferenceSession().Game!;
        table.ShowParsedResults([input], [new ReferenceParseItem(snapshot.FileCount, null, snapshot)]);
        Assert.Equal(expected, row.Cells["Branch"].Value);
        string? details = null;
        table.DetailsRequested += (_, _, information) => details = information;
        table.OpenDetails(row.Index, table.Columns["Branch"]!.Index);
        Assert.Contains("包类型: " + expected, details);
        table.ResetResults();
        Assert.Equal(ManifestTable.BranchText(branch), row.Cells["Branch"].Value);
    });

    [Fact]
    public void BatchAddAndRemoveNotifyOnceAndNoChangesDoNotNotify() => NativeTestHost.OnSta(() =>
    {
        using var table = new ManifestTable("batch");
        var files = Enumerable.Range(0, 100)
            .Select(index => new LocalManifestFile($"{index:D3}", $@"C:\manifests\{index}", index, default)).ToArray();
        int changes = 0;
        table.CheckedFilesChanged += () => changes++;
        table.AddFiles(files, check: true);
        Assert.Equal(1, changes);
        table.AddFiles(files, check: true);
        Assert.Equal(1, changes);
        table.SetBusy(true);
        table.RemoveCheckedFiles();
        Assert.Equal(100, table.Rows.Count);
        Assert.Equal(1, changes);
        table.SetBusy(false);
        table.RemoveCheckedFiles();
        Assert.Empty(table.Rows.Cast<DataGridViewRow>());
        Assert.Empty(table.CheckedInputs);
        Assert.Equal(2, changes);
        table.RemoveCheckedFiles();
        Assert.Equal(2, changes);
        Assert.False(NativeTestHost.Find<CheckBox>(table, "SelectAllManifests").Enabled);
    });

    [Fact]
    public void RemovingCheckedRowsKeepsUncheckedRowsAndInvalidatesReference() => NativeTestHost.OnSta(() =>
    {
        var session = NativeTestHost.ReferenceSession();
        using var form = new MainForm(session)
        {
            StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false
        };
        form.Show();
        NativeTestHost.Find<RadioButton>(form, "ManualSource").Checked = true;
        var table = NativeTestHost.Find<ManifestTable>(form, "Manifests");
        table.AddFiles([new LocalManifestFile("checked", @"C:\checked", 1, default)], check: true);
        table.AddFiles([new LocalManifestFile("retained", @"C:\retained", 1, default)]);
        session.AcceptReference(session.Game!);
        int changes = 0;
        table.CheckedFilesChanged += () => changes++;
        NativeTestHost.Find<Button>(form, "RemoveManifest").PerformClick();
        Assert.Equal(1, changes);
        Assert.Equal("retained", Assert.Single(table.Rows.Cast<DataGridViewRow>()).Cells["Name"].Value);
        Assert.Null(session.Reference);
        Assert.NotNull(session.Game);
        Assert.False(NativeTestHost.Find<Button>(form, "Next").Enabled);
    });
}
