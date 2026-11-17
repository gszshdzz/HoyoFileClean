using System.Text.Json;
using Google.Protobuf;
using HoyoFileClean.Protos;
using Xunit;

namespace HoyoFileClean.Tests;

public sealed class FileEntryInterfaceTests
{
    private const string Hash = "900150983CD24FB0D6963F7D28E17F72";

    [Theory]
    [InlineData("valid")]
    [InlineData("negative")]
    [InlineData("hash")]
    [InlineData("path")]
    [InlineData("duplicate")]
    [InlineData("parent")]
    [InlineData("overflow")]
    public void JsonAndProtobufEnforceTheSameFileSemantics(string scenario)
    {
        string directory = Path.Combine(TestTemporaryFiles.Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        ManifestFile[] files = scenario switch
        {
            "negative" => [new("Data/file", -1, Hash)],
            "hash" => [new("Data/file", 3, "invalid")],
            "path" => [new("../escape", 3, Hash)],
            "duplicate" => [new("Data/file", 3, Hash), new("DATA/FILE", 3, Hash)],
            "parent" => [new("Data", 3, Hash), new("Data/file", 3, Hash)],
            "overflow" => [new("Data/a", long.MaxValue, Hash), new("Data/b", 1, Hash)],
            _ => [new(@"Data\File", 3, Hash)]
        };
        string json = Path.Combine(directory, "files.json"), protobuf = Path.Combine(directory, "manifest");
        long total = scenario == "overflow" ? long.MaxValue : files.Sum(file => file.Size);
        File.WriteAllText(json, JsonSerializer.Serialize(new
        {
            fileCount = files.Length, totalSize = total, hashAlgorithm = "MD5",
            files = files.Select(file => new { path = file.Path, size = file.Size, hash = file.Hash })
        }));
        var manifest = new SophonManifestProto();
        foreach (ManifestFile file in files)
            manifest.Assets.Add(new SophonManifestAssetProperty
            { AssetName = file.Path, AssetSize = file.Size, AssetHashMd5 = file.Hash! });
        File.WriteAllBytes(protobuf, manifest.ToByteArray());
        if (scenario == "valid")
        {
            FileListSnapshot fromJson = ManifestConverter.Read(json, TestContext.Current.CancellationToken);
            FileListSnapshot fromProtobuf = ManifestConverter.Read(protobuf, TestContext.Current.CancellationToken);
            ManifestFile expected = new("Data/File", 3, Hash.ToLowerInvariant());
            Assert.Equal(expected, Assert.Single(fromJson.Files.Values));
            Assert.Equal(expected, Assert.Single(fromProtobuf.Files.Values));
            return;
        }
        CoreErrorCode expectedCode = scenario switch
        {
            "negative" or "hash" => CoreErrorCode.FileMetadataInvalid,
            "path" => CoreErrorCode.RelativePathInvalid,
            "duplicate" => CoreErrorCode.DuplicatePath,
            "parent" => CoreErrorCode.FileDirectoryConflict,
            _ => CoreErrorCode.TotalSizeOverflow
        };
        foreach (string input in new[] { json, protobuf })
            Assert.Equal(expectedCode, Assert.Throws<CoreException>(() =>
                ManifestConverter.Read(input, TestContext.Current.CancellationToken)).Code);
    }
}
