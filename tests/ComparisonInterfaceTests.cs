using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace HoyoFileClean.Tests;

public sealed class ComparisonInterfaceTests
{
    private readonly string directory = Path.Combine(TestTemporaryFiles.Root, Guid.NewGuid().ToString("N"));

    [Fact]
    public void DirectCoreCallsAndCliCompatibleWriterProduceIdenticalReports()
    {
        string a = Write("a", new ManifestFile("file", 3));
        string b = Write("b", new ManifestFile("file", 4));
        ComparisonResult direct = FileComparer.Compare(FileComparer.ReadInputs([a]), FileComparer.ReadInputs([b]));
        string first = Path.Combine(directory, "first.json"), second = Path.Combine(directory, "second.json");
        FileComparer.WriteReport(first, direct);
        Assert.Equal(direct.Summary, FileComparer.Compare([a], [b], second));
        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
        Assert.Single(direct.Different);
        Assert.False(direct.Different[0].HashCompared);
    }

    [Fact]
    public void SnapshotDoesNotReopenInputsAfterValidation()
    {
        string input = Write("input", new ManifestFile("file", 3));
        FileListSnapshot snapshot = FileComparer.ReadInputs([input]);
        File.Move(input, input + ".moved");
        Assert.Equal(1, FileComparer.Compare(snapshot, snapshot).Summary.MatchedSizeOnly);
        Assert.Equal(3, snapshot.TotalSize);
    }

    [Fact]
    public void ReportImportChecksContentNotJustInputPaths()
    {
        string a = Write("a", new ManifestFile("game-only", 3));
        string b = Write("b", new ManifestFile("reference-only", 4));
        FileListSnapshot left = FileComparer.ReadInputs([a]), right = FileComparer.ReadInputs([b]);
        string report = Path.Combine(directory, "report.json");
        FileComparer.Compare([a], [b], report);
        Assert.Equal(1, FileComparer.ImportReport(report, left, right).Summary.OnlyA);
        JsonObject json = JsonNode.Parse(File.ReadAllText(report))!.AsObject();
        json["onlyA"]![0]!["size"] = 100;
        File.WriteAllText(report, json.ToJsonString());
        Assert.Equal(CoreErrorCode.ReportContentMismatch, Assert.Throws<CoreException>(() => FileComparer.ImportReport(report, left, right)).Code);
    }

    [Fact]
    public void ReportForSamePathButChangedInputContentIsRejected()
    {
        string a = Write("a", new ManifestFile("file", 3)), b = Write("b", new ManifestFile("file", 3));
        string report = Path.Combine(directory, "report.json");
        FileComparer.Compare([a], [b], report);
        Write("a", new ManifestFile("file", 4));
        Assert.Equal(CoreErrorCode.ReportContentMismatch, Assert.Throws<CoreException>(() => FileComparer.ImportReport(report,
            FileComparer.ReadInputs([a]), FileComparer.ReadInputs([b]))).Code);
    }

    [Fact]
    public void ReversedSidesOrForgedPathsCannotAuthorizeImport()
    {
        string a = Write("a", new ManifestFile("a", 3)), b = Write("b", new ManifestFile("b", 3));
        string report = Path.Combine(directory, "report.json");
        FileComparer.Compare([a], [b], report);
        FileListSnapshot left = FileComparer.ReadInputs([a]), right = FileComparer.ReadInputs([b]);
        Assert.Equal(CoreErrorCode.ReportInputsMismatch, Assert.Throws<CoreException>(() => FileComparer.ImportReport(report, right, left)).Code);
        JsonObject json = JsonNode.Parse(File.ReadAllText(report))!.AsObject();
        json["aInputs"]![0] = @"Z:\never-open-this-input.json";
        json["onlyA"]![0]!["path"] = "../escape";
        File.WriteAllText(report, json.ToJsonString());
        Assert.Equal(CoreErrorCode.ReportInputsMismatch, Assert.Throws<CoreException>(() => FileComparer.ImportReport(report, left, right)).Code);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"aInputs\":null}")]
    [InlineData("{\"aInputs\":[],\"aInputs\":[]}")]
    public void InvalidReportsAreRejected(string json)
    {
        string input = Write("input", new ManifestFile("file", 3));
        var snapshot = FileComparer.ReadInputs([input]);
        string report = Path.Combine(directory, "report.json");
        File.WriteAllText(report, json);
        Assert.Equal(CoreErrorCode.ReportJsonInvalid, Assert.Throws<CoreException>(() => FileComparer.ImportReport(report, snapshot, snapshot)).Code);
    }

    [Fact]
    public void ReadSideChecksAggregateSizeOverflow()
    {
        string a = Write("a", new ManifestFile("a", long.MaxValue)), b = Write("b", new ManifestFile("b", 1));
        Assert.Equal(CoreErrorCode.TotalSizeOverflow, Assert.Throws<CoreException>(() => FileComparer.ReadInputs([a, b])).Code);
    }

    [Fact]
    public void WriteReportCannotOverwriteEitherInput()
    {
        string a = Write("a", new ManifestFile("a", 3));
        byte[] before = File.ReadAllBytes(a);
        var side = FileComparer.ReadInputs([a]);
        Assert.Throws<IOException>(() => FileComparer.WriteReport(a, FileComparer.Compare(side, side)));
        Assert.Equal(before, File.ReadAllBytes(a));
    }

    private string Write(string name, params ManifestFile[] files)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { fileCount = files.Length, totalSize = files.Sum(file => file.Size), files },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        return path;
    }
}
