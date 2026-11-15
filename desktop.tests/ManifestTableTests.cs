using HoyoFileClean.Desktop;
using Xunit;

namespace HoyoFileClean.Desktop.Tests;

[Collection("Windows Forms")]
public sealed class ManifestTableTests
{
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
