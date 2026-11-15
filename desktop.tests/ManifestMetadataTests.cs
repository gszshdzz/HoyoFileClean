using System.Text.Json;
using HoyoFileClean.Desktop;
using Xunit;
using L = HoyoFileClean.Presentation.Text;

namespace HoyoFileClean.Desktop.Tests;

public sealed class ManifestMetadataTests
{
    [Theory]
    [InlineData("StarRail.exe", "崩坏：星穹铁道")]
    [InlineData("ZenlessZoneZero.exe", "绝区零")]
    [InlineData("BH3.exe", "崩坏 3")]
    public void KnownGameNameRemainsVisibleWithoutCertainOfficialBiz(string executable, string name) => NativeTestHost.OnSta(() =>
    {
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "opencode", "HoyoFileClean.Desktop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "sample.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            fileCount = 1, totalSize = 1, files = new[] { new { path = executable, size = 1 } }
        }));
        FileListSnapshot snapshot = ManifestConverter.Read(path, TestContext.Current.CancellationToken);
        Assert.False(snapshot.Sources[0].Metadata!.ContainsKey("biz"));
        using var table = new ManifestTable("test");
        table.AddFiles([new LocalManifestFile("sample", path, 1, default)], check: true);
        ReferenceInput input = Assert.Single(table.CheckedInputs);
        table.ShowParsedResults([input], [new ReferenceParseItem(1, null, snapshot.Sources[0].Metadata, snapshot)]);
        Assert.Equal(name, table.Rows[0].Cells["Game"].Value);
        Assert.Equal("game", table.Rows[0].Cells["Package"].Value);
        Assert.Contains(L.Get("ManifestOfficialBizLabel") + ": " + L.Get("UnavailableValue"), table.Rows[0].Cells["Game"].ToolTipText);
    });

    [Fact]
    public void FailureInvalidatesEntryCountEvenWhenItsUnderlyingValueStaysNull() => NativeTestHost.OnSta(() =>
    {
        using var form = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000),
            ShowInTaskbar = false, ClientSize = new Size(1100, 400) };
        using var table = new ManifestTable("test");
        form.Controls.Add(table);
        table.AddFiles([new LocalManifestFile("input", "input", 1, default)], check: true);
        ReferenceInput input = Assert.Single(table.CheckedInputs);
        form.Show();
        table.ShowResults([(input, ReferenceState.Pending, null)]);
        table.Update();
        Application.DoEvents();
        Rectangle entries = table.GetCellDisplayRectangle(table.Columns["Entries"]!.Index, 0, true);
        Assert.True(entries.Width > 0 && entries.Height > 0);
        entries.Inflate(-2, -2);
        bool invalidated = false;
        table.Invalidated += (_, args) => invalidated |= args.InvalidRect.IntersectsWith(entries);
        foreach (ReferenceState state in new[] { ReferenceState.Failed, ReferenceState.Pending, ReferenceState.Cancelled })
        {
            invalidated = false;
            table.ShowResults([(input, state, null)]);
            Assert.Null(table.Rows[0].Cells["Entries"].Value);
            Assert.True(invalidated, $"{state} 时条目数仍为 null，但显示依赖状态变化，必须主动使条目数单元格失效重绘。");
            table.Update();
        }
        invalidated = false;
        table.ResetResults();
        Assert.True(invalidated, "重置时应主动重绘，不能残留失败时的横线。");
    });

    [Fact]
    public void OfflineTableShowsResultAndConflictCandidatesAndClearsStaleValues() => NativeTestHost.OnSta(() =>
    {
        using var table = new ManifestTable("test");
        table.AddFiles([new LocalManifestFile("input", "input", 1, default)], check: true);
        ReferenceInput input = Assert.Single(table.CheckedInputs);
        foreach (string column in new[] { "Game", "Version", "Package", "Entries" }) Assert.Null(table.Rows[0].Cells[column].Value);
        table.ShowResults([(input, ReferenceState.Parsed, 1)]);
        var metadata = new Dictionary<string, ManifestMetadataField>
        {
            ["game"] = new([new("game_hk4e", "hk4e")]),
            ["version"] = new([new("v1", "1.0.0"), new("v2", "2.0.0")])
        };
        table.ShowMetadata([(input, metadata)]);
        DataGridViewRow row = table.Rows[0];
        Assert.Equal("原神", row.Cells["Game"].Value);
        Assert.Equal(L.Get("MetadataConflict"), row.Cells["Version"].Value);
        Assert.Contains("v1: 1.0.0", row.Cells["Version"].ToolTipText);
        Assert.Contains("v2: 2.0.0", row.Cells["Version"].ToolTipText);
        Assert.Equal(L.Get("UnavailableValue"), row.Cells["Package"].Value);
        table.ResetResults();
        foreach (string column in new[] { "Game", "Version", "Package" })
        {
            Assert.Null(row.Cells[column].Value);
            Assert.Empty(row.Cells[column].ToolTipText);
        }
        table.ShowMetadata([(input, metadata)]);
        table.ShowMetadata([(input, null)]);
        Assert.Null(row.Cells["Game"].Value);
    });

    [Fact]
    public void BatchKeepsPerFileMetadataEvenWhenCombinedFilesConflict()
    {
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "opencode", "HoyoFileClean.Desktop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        ReferenceInput Write(string name, long size)
        {
            string path = Path.Combine(directory, name);
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                fileCount = 1, totalSize = size, files = new[] { new { path = "YuanShen.exe", size } }
            }));
            return new ReferenceInput(new LocalManifestFile(name, path, 0, default));
        }
        ReferenceParseBatch batch = new ReferenceParser().Parse([Write("a.json", 1), Write("b.json", 2)],
            TestContext.Current.CancellationToken);
        Assert.All(batch.Items, item =>
        {
            Assert.Null(item.Error);
            Assert.Equal("hk4e", item.Metadata!["game"].Value);
            Assert.NotNull(item.Snapshot);
        });
        Assert.Throws<InvalidDataException>(() => batch.GetSnapshot());
    }
}
