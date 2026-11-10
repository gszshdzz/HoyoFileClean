using System.Text;
using System.Text.Json;
using Xunit;

namespace HoyoFileClean.Tests;

public sealed class ManifestJsonTests
{
    private readonly string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Temp", "opencode", "HoyoFileClean.Tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonIsDetectedByContentAndPreservesHashesAndScope(bool hashes)
    {
        Directory.CreateDirectory(directory);
        string input = Path.Combine(directory, "no-extension"), output = Path.Combine(directory, "output.json");
        string json = JsonSerializer.Serialize(new
        {
            hashAlgorithm = hashes ? "MD5" : null, fileCount = 1, totalSize = 3,
            files = new[] { new { path = "file", size = 3, hash = hashes ? "900150983cd24fb0d6963f7d28e17f72" : null } },
            scan = new { root = @"Z:\not-accessed", mode = hashes ? "heavy" : "light",
                excludedDirectories = new[] { "Cache" }, excludedFiles = Array.Empty<string>(), skippedLinks = new[] { "link" } }
        });
        File.WriteAllText(input, " \r\n" + json, new UTF8Encoding(true));
        byte[] before = File.ReadAllBytes(input);
        Assert.Equal(new ConversionSummary(1, 3), ManifestConverter.Convert(input, output));
        FileListSnapshot snapshot = FileComparer.ReadInputs([output]);
        Assert.Equal(hashes ? 1 : 0, snapshot.HashedFileCount);
        Assert.Equal("Cache", Assert.Single(snapshot.Sources[0].Scan!.ExcludedDirectories));
        Assert.Equal("link", Assert.Single(snapshot.Sources[0].Scan!.SkippedLinks));
        Assert.Equal(before, File.ReadAllBytes(input));
        Assert.Empty(Directory.EnumerateFiles(directory, "*.partial"));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("{\"fileCount\":1,\"totalSize\":1,\"files\":[{\"path\":\"../escape\",\"size\":1}]}")]
    [InlineData("{\"fileCount\":2,\"totalSize\":1,\"files\":[{\"path\":\"file\",\"size\":1}]}")]
    public void InvalidJsonNeverPublishes(string json)
    {
        Directory.CreateDirectory(directory);
        string input = Path.Combine(directory, "input"), output = Path.Combine(directory, "output.json");
        File.WriteAllText(input, json);
        Assert.Throws<CoreException>(() => ManifestConverter.Convert(input, output));
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void LongWhitespaceJsonPrefixUsesBoundedProbeMemory()
    {
        Directory.CreateDirectory(directory);
        string input = Path.Combine(directory, "long-prefix");
        File.WriteAllText(input, new string(' ', 4 * 1024 * 1024)
            + "{\"fileCount\":1,\"totalSize\":3,\"files\":[{\"path\":\"file\",\"size\":3}]}");
        string warmup = Path.Combine(directory, "warmup");
        File.WriteAllText(warmup, "{\"fileCount\":0,\"totalSize\":0,\"files\":[]}");
        _ = ManifestConverter.Read(warmup, TestContext.Current.CancellationToken);
        long before = GC.GetAllocatedBytesForCurrentThread();
        FileListSnapshot snapshot = ManifestConverter.Read(input, TestContext.Current.CancellationToken);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(3, snapshot.Files["file"].Size);
        Assert.True(allocated < 1024 * 1024, $"long-prefix allocated={allocated}");
    }

    [Fact]
    public void LongWhitespaceProtobufPrefixIsReplayedRatherThanDropped()
    {
        Directory.CreateDirectory(directory);
        string input = Path.Combine(directory, "protobuf-prefix");
        byte[] asset = [0x0a, 0x0a, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20];
        File.WriteAllBytes(input, Enumerable.Range(0, 500).SelectMany(_ => asset).ToArray());
        CoreException error = Assert.Throws<CoreException>(() => ManifestConverter.Read(input, TestContext.Current.CancellationToken));
        Assert.Equal(CoreErrorCode.FileMetadataInvalid, error.Code);
    }

    [Fact]
    public void IncrementalSnapshotsPreserveInputOrderAndCannotMutatePublishedResults()
    {
        Directory.CreateDirectory(directory);
        FileListSnapshot Read(string name, string spelling)
        {
            string input = Path.Combine(directory, name);
            File.WriteAllText(input, JsonSerializer.Serialize(new
            {
                fileCount = 1, totalSize = 3, files = new[] { new { path = spelling, size = 3 } }
            }));
            return ManifestConverter.Read(input, TestContext.Current.CancellationToken);
        }
        FileListSnapshot first = Read("first", "File"), last = Read("last", "FILE");
        var accumulator = new FileComparer.SnapshotAccumulator();
        accumulator.Add(last, 1, TestContext.Current.CancellationToken);
        accumulator.Add(first, 0, TestContext.Current.CancellationToken);
        FileListSnapshot combined = accumulator.Build(TestContext.Current.CancellationToken);
        Assert.Equal(first.Sources.Concat(last.Sources), combined.Sources);
        Assert.Equal("File", Assert.Single(combined.Files.Keys));
        Assert.Equal("File", Assert.Single(combined.Files.Values).Path);
        Assert.Throws<InvalidOperationException>(() => accumulator.Add(last, 2, TestContext.Current.CancellationToken));
        Assert.Throws<InvalidOperationException>(() => accumulator.Build(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void UnusedChunkDetailsDoNotCreatePerChunkObjectsDuringConversion()
    {
        Directory.CreateDirectory(directory);
        var manifest = new Protos.SophonManifestProto();
        var asset = new Protos.SophonManifestAssetProperty
        {
            AssetName = "file", AssetSize = 3, AssetHashMd5 = "900150983cd24fb0d6963f7d28e17f72"
        };
        for (int i = 0; i < 20000; i++) asset.AssetChunks.Add(new Protos.SophonManifestAssetChunk
        {
            ChunkName = new string('a', 120), ChunkDecompressedHashMd5 = new string('b', 32), ChunkSize = 3
        });
        manifest.Assets.Add(asset);
        string input = Path.Combine(directory, "chunks");
        byte[] encoded = Google.Protobuf.MessageExtensions.ToByteArray(manifest);
        File.WriteAllBytes(input, encoded);
        // Warm the parser and validators before measuring synchronous allocation.
        string warmup = Path.Combine(directory, "warmup");
        File.WriteAllText(warmup, "{\"fileCount\":0,\"totalSize\":0,\"files\":[]}");
        _ = ManifestConverter.Read(warmup, TestContext.Current.CancellationToken);
        long before = GC.GetAllocatedBytesForCurrentThread();
        FileListSnapshot result = ManifestConverter.Read(input, TestContext.Current.CancellationToken);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine($"CHUNKS encoded={encoded.Length}, allocated={allocated}");
        Assert.Equal(3, result.Files["file"].Size);
        Assert.True(allocated < 1024 * 1024,
            $"encoded={encoded.Length}, allocated={allocated}; unused chunk allocation grew with chunk objects");
    }

    [Fact]
    public void CancelledReadDoesNotOpenInputOrPublishCombinedSnapshot()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => ManifestConverter.Read(
            Path.Combine(directory, "missing"), cancellation.Token));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "input");
        File.WriteAllText(path, "{\"fileCount\":0,\"totalSize\":0,\"files\":[]}");
        FileListSnapshot snapshot = ManifestConverter.Read(path, TestContext.Current.CancellationToken);
        Assert.ThrowsAny<OperationCanceledException>(() => FileComparer.Combine([snapshot], cancellation.Token));
    }

    [Fact]
    public void CombinedSnapshotsPreserveSourcesAndRejectConflicts()
    {
        Directory.CreateDirectory(directory);
        FileListSnapshot Read(string name, string path, int size)
        {
            string input = Path.Combine(directory, name);
            File.WriteAllText(input, JsonSerializer.Serialize(new
            {
                fileCount = 1, totalSize = size, files = new[] { new { path, size } }
            }));
            return ManifestConverter.Read(input, TestContext.Current.CancellationToken);
        }
        FileListSnapshot a = Read("a", "file", 3), b = Read("b", "FILE", 3);
        FileListSnapshot combined = FileComparer.Combine([a, b], TestContext.Current.CancellationToken);
        Assert.Equal(1, combined.FileCount);
        Assert.Equal(new[] { Path.Combine(directory, "a"), Path.Combine(directory, "b") },
            combined.Sources.Select(source => source.Path));
        Assert.Equal(combined.Sources, FileComparer.Combine([combined], TestContext.Current.CancellationToken).Sources);
        Assert.Equal(CoreErrorCode.InputConflict, Assert.Throws<CoreException>(() =>
            FileComparer.Combine([a, Read("conflict", "file", 4)], TestContext.Current.CancellationToken)).Code);
        Assert.Throws<CoreException>(() => FileComparer.Combine([a, Read("child", "file/child", 1)], TestContext.Current.CancellationToken));
        Assert.Equal(CoreErrorCode.InputsRequired,
            Assert.Throws<CoreException>(() => FileComparer.Combine([], TestContext.Current.CancellationToken)).Code);
    }
}
