using System.Text.Json;
using HoyoFileClean.Desktop;
using Xunit;
using static HoyoFileClean.Desktop.Tests.NativeTestHost;

namespace HoyoFileClean.Desktop.Tests;

[Collection("Windows Forms")]
public sealed class PatchReferenceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NextConfirmsOnlyTheSelectedParsedPatchReferences(bool accept) => OnSta(() =>
    {
        WizardSession session = ReferenceSession();
        int prompts = 0;
        string? message = null;
        using var form = new MainForm(session, confirmPatchReference: text => { prompts++; message = text; return accept; })
        { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false };
        form.Show();
        Find<RadioButton>(form, "ManualSource").Checked = true;
        var table = Find<ManifestTable>(form, "Manifests");
        FileListSnapshot patch = Read("selected-patch.json", true), unselected = Read("unselected-patch.json", true);
        table.AddFiles([new LocalManifestFile("selected", patch.Sources[0].Path, 1, default),
            new LocalManifestFile("unselected", unselected.Sources[0].Path, 1, default)], check: false);
        DataGridViewRow selected = table.Rows.Cast<DataGridViewRow>().Single(row => row.Cells["Name"].Value is "selected");
        selected.Cells[0].Value = true;
        Find<Button>(form, "ParseManualReference").PerformClick();
        WaitUntil(() => session.Reference is not null && !session.IsBusy);
        Assert.Equal("预下载", selected.Cells["Branch"].Value);
        Assert.Null(table.Rows.Cast<DataGridViewRow>().Single(row => row.Cells["Name"].Value is "unselected").Cells["Branch"].Value);
        using var detailsTable = new ManifestTable("details");
        detailsTable.AddFiles([new LocalManifestFile("selected", patch.Sources[0].Path, 1, default)], check: true);
        detailsTable.ShowParsedResults(detailsTable.CheckedInputs, [new ReferenceParseItem(patch.FileCount, null, patch)]);
        string? details = null;
        detailsTable.DetailsRequested += (_, _, information) => details = information;
        detailsTable.OpenDetails(0, detailsTable.Columns["Branch"]!.Index);
        Assert.Contains("包类型: 预下载", details);
        Find<Button>(form, "Next").PerformClick();
        Assert.Equal(1, prompts);
        Assert.Contains("selected-patch.json", message);
        Assert.DoesNotContain("unselected-patch.json", message);
        Assert.Contains("通常不应作为对比参考", message);
        Assert.Equal(accept ? 2 : 1, session.Step);
        if (accept) Find<Button>(form, "Back").PerformClick();
        Find<Button>(form, "Next").PerformClick();
        Assert.Equal(2, prompts);
        Assert.Equal(accept ? 2 : 1, session.Step);
        if (accept) Find<Button>(form, "Back").PerformClick();
        selected.Cells[0].Value = false;
        Assert.Null(session.Reference);
        Find<Button>(form, "Next").PerformClick();
        Assert.Equal(2, prompts);
        Assert.Equal(1, session.Step);
        FileListSnapshot normal = Read("normal.json", false);
        table.AddFiles([new LocalManifestFile("normal", normal.Sources[0].Path, 1, default)], check: true);
        Find<Button>(form, "ParseManualReference").PerformClick();
        WaitUntil(() => session.Reference is not null && !session.IsBusy);
        Assert.Equal("发行版", table.Rows.Cast<DataGridViewRow>().Single(row => row.Cells["Name"].Value is "normal").Cells["Branch"].Value);
        Find<Button>(form, "Next").PerformClick();
        Assert.Equal(2, prompts);
        Assert.Equal(2, session.Step);
    });

    [Fact]
    public void FailedOrUnparsedReferenceCannotPromptOrAdvance() => OnSta(() =>
    {
        WizardSession session = ReferenceSession();
        using var form = new MainForm(session, confirmPatchReference: _ => throw new Exception("Unexpected prompt"));
        Find<Button>(form, "Next").PerformClick();
        Assert.Equal(1, session.Step);
    });

    private static FileListSnapshot Read(string name, bool patch)
    {
        string directory = Path.Combine(TestTemporaryFiles.Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            fileCount = 1, totalSize = 1, files = new[] { new { path = "YuanShen.exe", size = 1 } },
            manifestKind = patch ? FileValues.SophonPatch : null
        }));
        return ManifestConverter.Read(path, TestContext.Current.CancellationToken);
    }
}
