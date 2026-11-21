using Google.Protobuf;
using HoyoFileClean.Protos;
using System.Text.Json;
using Xunit;
using ZstdSharp;

namespace HoyoFileClean.Tests;

public sealed class PreDownloadManifestTests
{
    private const string Md5 = "900150983cd24fb0d6963f7d28e17f72";
    private readonly string directory = Path.Combine(TestTemporaryFiles.Root, Guid.NewGuid().ToString("N"));

    // refs/ is intentionally untracked. Run this local evidence explicitly;
    // ordinary regression must remain runnable in a fresh checkout without samples.
    [Theory(Explicit = true)]
    [InlineData("333-hk4epre-802e8aa24e4335c9f7e88a018345593d", 179, 14535287384L, "zh-cn")]
    [InlineData("333-hk4epre-ab3b0aa890ee764aa13207343c7b154e", 2795, 135833870380L, "game")]
    public void RealSamplesKeepAllTargetsAndIdentifyExistingGameAndPackage(string name, int count, long size, string package)
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "refs"))) root = root.Parent;
        Assert.NotNull(root);
        FileListSnapshot snapshot = ManifestConverter.Read(Path.Combine(root.FullName, "refs", name), TestContext.Current.CancellationToken);
        Assert.Equal(count, snapshot.FileCount);
        Assert.Equal(size, snapshot.TotalSize);
        FileListSource source = Assert.Single(snapshot.Sources);
        Assert.Equal(FileValues.SophonPatch, source.ManifestKind);
        Assert.Equal("hk4e", source.Metadata!["game"].Value);
        Assert.Equal("hk4e_cn", source.Metadata["biz"].Value);
        Assert.Equal(package, source.Metadata["package"].Value);
        Assert.False(source.Metadata.ContainsKey("version"));
        if (package == "zh-cn")
            Assert.Equal(new ManifestFile("YuanShen_Data/StreamingAssets/AudioAssets/Chinese/2078.pck", 97841751,
                "07a45f0be7233b649d02fa235f8f9565"), snapshot.Files["YuanShen_Data/StreamingAssets/AudioAssets/Chinese/2078.pck"]);
        else
        {
            Assert.Equal(new ManifestFile("YuanShen_Data/StreamingAssets/AssetBundles/blocks/00/09501945.blk", 50481510,
                "bf1cb76052d36e82c9177d53cc54c894"), snapshot.Files["YuanShen_Data/StreamingAssets/AssetBundles/blocks/00/09501945.blk"]);
            Assert.False(snapshot.Files.ContainsKey("YuanShen_Data/StreamingAssets/AssetBundles/blocks/00/11790361.blk"));
            Assert.False(snapshot.Files.ContainsKey("YuanShen_Data/StreamingAssets/AssetBundles/blocks/00/01420412.blk"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PatchSupportsCompressionZeroSizeAndExportImportWithoutLosingWarning(bool compressed)
    {
        var patch = new SophonPatchFileManifest
        {
            Assets = { new SophonPatchFileAsset { AssetName = "YuanShen.exe", AssetHashMd5 = Md5 } }
        };
        string input = Write(patch.ToByteArray(), compressed);
        FileListSnapshot snapshot = ManifestConverter.Read(input, TestContext.Current.CancellationToken);
        Assert.Equal(0, Assert.Single(snapshot.Files.Values).Size);
        string output = Path.Combine(directory, "export.json");
        ManifestConverter.Export(snapshot, output);
        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(output));
        Assert.Equal(FileValues.SophonPatch, json.RootElement.GetProperty("manifestKind").GetString());
        FileListSnapshot imported = ManifestConverter.Read(output, TestContext.Current.CancellationToken);
        Assert.Equal(FileValues.SophonPatch, Assert.Single(imported.Sources).ManifestKind);
        Assert.Equal(snapshot.Files.Values, imported.Files.Values);
        Assert.Equal("hk4e_cn", imported.Sources[0].Metadata!["biz"].Value);
        Assert.All(FileComparer.Combine([snapshot, imported], TestContext.Current.CancellationToken).Sources,
            source => Assert.Equal(FileValues.SophonPatch, source.ManifestKind));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PatchWithJsonLikeBinaryPrefixKeepsFileMetadata(bool compressed)
    {
        string path = new string('a', 82);
        var patch = new SophonPatchFileManifest
        {
            Assets = { new SophonPatchFileAsset { AssetName = path, AssetSize = 9817920, AssetHashMd5 = Md5 } }
        };
        byte[] bytes = patch.ToByteArray();
        Assert.Equal(new byte[] { 0x0a, 0x7b, 0x0a, 0x52 }, bytes[..4]);

        FileListSnapshot snapshot = ManifestConverter.Read(Write(bytes, compressed), TestContext.Current.CancellationToken);

        Assert.Equal(new ManifestFile(path, 9817920, Md5), Assert.Single(snapshot.Files.Values));
        Assert.Equal(1, snapshot.FileCount);
        Assert.Equal(9817920, snapshot.TotalSize);
        Assert.Equal(FileValues.SophonPatch, Assert.Single(snapshot.Sources).ManifestKind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonLikeBinaryPrefixDoesNotBypassHashValidation(bool compressed)
    {
        var patch = new SophonPatchFileManifest
        {
            Assets = { new SophonPatchFileAsset
            {
                AssetName = new string('a', 82), AssetSize = 9817920, AssetHashMd5 = new string('z', 32)
            } }
        };
        string input = Write(patch.ToByteArray(), compressed), output = Path.Combine(directory, "export.json");

        Assert.Equal(CoreErrorCode.FileMetadataInvalid,
            Assert.Throws<CoreException>(() => ManifestConverter.Convert(input, output)).Code);
        Assert.False(File.Exists(output));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidJsonRetainsJsonErrorAfterBinaryRetry(bool compressed)
    {
        string input = Write(System.Text.Encoding.UTF8.GetBytes("{invalid"), compressed);
        string output = Path.Combine(directory, "export.json");

        Assert.Equal(CoreErrorCode.ListJsonInvalid,
            Assert.Throws<CoreException>(() => ManifestConverter.Convert(input, output)).Code);
        Assert.False(File.Exists(output));
    }

    [Theory]
    [InlineData("negative", CoreErrorCode.FileMetadataInvalid)]
    [InlineData("hash", CoreErrorCode.FileMetadataInvalid)]
    [InlineData("path", CoreErrorCode.RelativePathInvalid)]
    [InlineData("duplicate", CoreErrorCode.DuplicatePath)]
    [InlineData("parent", CoreErrorCode.FileDirectoryConflict)]
    [InlineData("overflow", CoreErrorCode.TotalSizeOverflow)]
    [InlineData("mixed", CoreErrorCode.ManifestEncodingInvalid)]
    [InlineData("ambiguous", CoreErrorCode.ManifestEncodingInvalid)]
    [InlineData("truncated", CoreErrorCode.ManifestEncodingInvalid)]
    public void PatchRejectsInvalidAndMixedInputsWithoutPublishing(string scenario, CoreErrorCode expected)
    {
        var patch = new SophonPatchFileManifest();
        patch.Assets.Add(new SophonPatchFileAsset
        {
            AssetName = scenario == "path" ? "../outside" : "Data/file",
            AssetSize = scenario == "negative" ? -1 : scenario == "overflow" ? long.MaxValue : 3,
            AssetHashMd5 = scenario == "hash" ? "invalid" : Md5
        });
        if (scenario is "duplicate" or "parent" or "overflow")
            patch.Assets.Add(new SophonPatchFileAsset { AssetName = scenario == "duplicate" ? "DATA/FILE"
                : scenario == "parent" ? "Data/file/child" : "Data/extra", AssetSize = 1, AssetHashMd5 = Md5 });
        byte[] encoded = patch.ToByteArray();
        if (scenario == "mixed")
            encoded = encoded.Concat(new SophonManifestProto { Assets = {
                new SophonManifestAssetProperty { AssetName = "other", AssetSize = 1, AssetHashMd5 = Md5 } } }.ToByteArray()).ToArray();
        if (scenario == "ambiguous")
        {
            byte[] asset = patch.Assets[0].ToByteArray().Concat(new byte[] { 0x20, 0x01 }).ToArray();
            encoded = new byte[] { 0x0a, (byte)asset.Length }.Concat(asset).ToArray();
        }
        if (scenario == "truncated") encoded = encoded[..^1];
        string input = Write(encoded, false), output = Path.Combine(directory, "export.json");
        Assert.Equal(expected, Assert.Throws<CoreException>(() => ManifestConverter.Convert(input, output)).Code);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void MissingBuildHashDoesNotGetReinterpretedAsPatch()
    {
        string input = Write(new SophonManifestProto { Assets = {
            new SophonManifestAssetProperty { AssetName = "file", AssetSize = 3 } } }.ToByteArray(), false);
        Assert.Equal(CoreErrorCode.FileMetadataInvalid, Assert.Throws<CoreException>(() =>
            ManifestConverter.Read(input, TestContext.Current.CancellationToken)).Code);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void OnlyUnknownLocalInputRequiresASecondRead(bool local, int rewinds)
    {
        using var stream = new RewindCountingStream(new SophonManifestProto { Assets = {
            new SophonManifestAssetProperty { AssetName = "file", AssetSize = 3, AssetHashMd5 = Md5 } } }.ToByteArray());
        FileListSnapshot result = ManifestConverter.Read(stream, "sample", TestContext.Current.CancellationToken,
            expectedCompression: local ? null : false);
        Assert.Equal(1, result.FileCount);
        Assert.Null(result.Sources[0].ManifestKind);
        Assert.Equal(rewinds, stream.Rewinds);
    }

    [Fact]
    public void OfficialBuildEntryDoesNotAcceptPatchInput()
    {
        using var stream = new MemoryStream(new SophonPatchFileManifest { Assets = {
            new SophonPatchFileAsset { AssetName = "file", AssetSize = 3, AssetHashMd5 = Md5 } } }.ToByteArray(), writable: false);
        Assert.Equal(CoreErrorCode.ManifestNetworkUnsupported, Assert.Throws<CoreException>(() =>
            ManifestConverter.Read(stream, "sample", TestContext.Current.CancellationToken, expectedCompression: false)).Code);
    }

    private sealed class RewindCountingStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        internal int Rewinds { get; private set; }
        public override long Position
        {
            get => base.Position;
            set
            {
                if (value == 0 && base.Position > 0) Rewinds++;
                base.Position = value;
            }
        }
    }

    private string Write(byte[] bytes, bool compressed)
    {
        Directory.CreateDirectory(directory);
        if (compressed)
        {
            using var compressor = new Compressor();
            bytes = compressor.Wrap(bytes).ToArray();
        }
        string path = Path.Combine(directory, "manifest");
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
