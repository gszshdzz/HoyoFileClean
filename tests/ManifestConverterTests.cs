using System.Diagnostics;
using System.Text.Json;
using Google.Protobuf;
using HoyoFileClean.Protos;
using Xunit;
using ZstdSharp;

namespace HoyoFileClean.Tests;

public sealed class ManifestConverterTests
{
    private const string ExampleMd5 = "900150983cd24fb0d6963f7d28e17f72";
    private readonly string directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Temp", "opencode", "HoyoFileClean.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void CompressedManifestProducesFileListWithWholeFileHash()
    {
        string input = CreateInput(new SophonManifestProto
        {
            Assets =
            {
                new SophonManifestAssetProperty { AssetName = "Data", AssetType = 1 },
                new SophonManifestAssetProperty
                {
                    AssetName = "Data/example.bin",
                    AssetSize = 3,
                    AssetHashMd5 = ExampleMd5,
                    AssetChunks =
                    {
                        new SophonManifestAssetChunk
                        {
                            ChunkName = "chunk-a",
                            ChunkDecompressedHashMd5 = "0cc175b9c0f1b6a831c399e269772661",
                            ChunkSize = 1,
                            ChunkSizeDecompressed = 1
                        },
                        new SophonManifestAssetChunk
                        {
                            ChunkName = "chunk-bc",
                            ChunkDecompressedHashMd5 = "5360af35bde9ebd8f01f492dc059593c",
                            ChunkOnFileOffset = 1,
                            ChunkSize = 2,
                            ChunkSizeDecompressed = 2
                        }
                    }
                }
            }
        });
        string output = Path.Combine(directory, "files.json");

        ConversionSummary summary = ManifestConverter.Convert(input, output);

        using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(output));
        JsonElement root = json.RootElement;
        JsonElement file = Assert.Single(root.GetProperty("files").EnumerateArray());
        Assert.Equal(
            (1, 3L, "MD5", "Data/example.bin", 3L, ExampleMd5),
            (summary.FileCount, summary.TotalSize,
                root.GetProperty("hashAlgorithm").GetString(),
                file.GetProperty("path").GetString(),
                file.GetProperty("size").GetInt64(),
                file.GetProperty("hash").GetString()));
        Assert.Equal(3, file.EnumerateObject().Count());
    }

    [Fact]
    public void UncompressedManifestIsDetectedAutomatically()
    {
        string input = CreateInput(new SophonManifestProto
        {
            Assets = { FileAsset("Data/example.bin", 3, ExampleMd5) }
        }, compressed: false);
        string output = Path.Combine(directory, "files.json");

        ConversionSummary summary = ManifestConverter.Convert(input, output);

        using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(output));
        Assert.Equal(
            (new ConversionSummary(1, 3), "Data/example.bin"),
            (summary, json.RootElement.GetProperty("files")[0].GetProperty("path").GetString()));
    }

    [Fact]
    public void EmptyManifestDoesNotPublishFileList()
    {
        string input = CreateInput(new SophonManifestProto());
        string output = Path.Combine(directory, "files.json");

        Assert.Equal(CoreErrorCode.ManifestEmpty, Assert.Throws<CoreException>(() => ManifestConverter.Convert(input, output)).Code);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void UnknownFieldBeforeAssetsDoesNotRequireAn0AHeader()
    {
        string input = CreateInput(new SophonManifestProto { Assets = { FileAsset("file", 3, ExampleMd5) } }, compressed: false);
        byte[] original = File.ReadAllBytes(input);
        File.WriteAllBytes(input, new byte[] { 0x10, 0x01 }.Concat(original).ToArray());
        Assert.Equal(new ConversionSummary(1, 3), ManifestConverter.Convert(input, Path.Combine(directory, "files.json")));
    }

    [Theory]
    [InlineData(0x50)]
    [InlineData(0x5f)]
    public void SkippableFrameBeforeCompressedManifestIsDetected(int magic)
    {
        string input = CreateInput(new SophonManifestProto { Assets = { FileAsset("file", 3, ExampleMd5) } });
        byte[] original = File.ReadAllBytes(input);
        File.WriteAllBytes(input, new byte[] { (byte)magic, 0x2a, 0x4d, 0x18, 3, 0, 0, 0, 1, 2, 3 }.Concat(original).ToArray());
        Assert.Equal(new ConversionSummary(1, 3), ManifestConverter.Convert(input, Path.Combine(directory, "files.json")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("28")]
    [InlineData("28B5")]
    [InlineData("28B52F")]
    [InlineData("28B52FFD")]
    [InlineData("502A4D18")]
    [InlineData("0AFF")]
    [InlineData("FFFFFFFF")]
    public void EmptyShortAndInvalidInputsNeverPublish(string hex)
    {
        Directory.CreateDirectory(directory);
        string input = Path.Combine(directory, "manifest"), output = Path.Combine(directory, "files.json");
        File.WriteAllBytes(input, System.Convert.FromHexString(hex));
        Assert.Throws<CoreException>(() => ManifestConverter.Convert(input, output));
        Assert.False(File.Exists(output));
        Assert.Empty(Directory.EnumerateFiles(directory, "*.partial"));
    }

    [Fact]
    public void DetectedCompressionStillEnforcesInputSizeLimit()
    {
        Directory.CreateDirectory(directory);
        string input = Path.Combine(directory, "manifest"), output = Path.Combine(directory, "files.json");
        using (var stream = File.Create(input))
        {
            stream.Write(new byte[] { 0x28, 0xb5, 0x2f, 0xfd });
            stream.SetLength(64L * 1024 * 1024 + 1);
        }
        Assert.Equal(CoreErrorCode.ManifestCompressedSizeLimit,
            Assert.Throws<CoreException>(() => ManifestConverter.Convert(input, output)).Code);
        Assert.False(File.Exists(output));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DamagedManifestDoesNotPublishFileList(bool compressed)
    {
        Directory.CreateDirectory(directory);
        string input = Path.Combine(directory, "manifest");
        File.WriteAllBytes(input, compressed ? [0x28, 0xb5, 0x2f, 0xfd, 0xff] : [0x0a, 0xff]);
        string output = Path.Combine(directory, "files.json");

        Assert.Equal(compressed ? CoreErrorCode.ManifestCompressionInvalid : CoreErrorCode.ManifestEncodingInvalid,
            Assert.Throws<CoreException>(() => ManifestConverter.Convert(input, output)).Code);
        Assert.False(File.Exists(output));
    }

    [Theory]
    [InlineData(-1, ExampleMd5)]
    [InlineData(3, "")]
    [InlineData(3, "not-an-md5")]
    [InlineData(3, "900150983cd24fb0d6963f7d28e17f7z")]
    public void InvalidFileMetadataDoesNotPublishFileList(long size, string md5)
    {
        string input = CreateInput(new SophonManifestProto
        {
            Assets = { FileAsset("Data/example.bin", size, md5) }
        });
        string output = Path.Combine(directory, "files.json");

        Assert.Equal(CoreErrorCode.FileMetadataInvalid, Assert.Throws<CoreException>(() => ManifestConverter.Convert(input, output)).Code);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void UnsafePathsDoNotPublishFileList()
    {
        string[] paths = ["", "../outside.bin", "/outside.bin", @"C:\outside.bin",
            "Data/../outside.bin", "Data/file.bin:stream", "Data//file.bin", "Data/file.bin.",
            "Data/NUL.txt", "Data/con.tar.gz", "Data/AUX/file.bin", "Data/PRN",
            "Data/COM9.bin", "Data/LPT1", "Data/com¹.txt", "Data/LPT³.bin"];
        foreach (string path in paths)
        {
            string input = CreateInput(new SophonManifestProto
            {
                Assets = { FileAsset(path, 3, ExampleMd5) }
            });
            string output = Path.Combine(directory, $"{Guid.NewGuid():N}.json");

            Assert.Equal(CoreErrorCode.RelativePathInvalid, Assert.Throws<CoreException>(() => ManifestConverter.Convert(input, output)).Code);
            Assert.False(File.Exists(output));
        }
    }

    [Fact]
    public void ExistingOutputIsNotOverwritten()
    {
        string input = CreateInput(new SophonManifestProto
        {
            Assets = { FileAsset("Data/example.bin", 3, ExampleMd5) }
        });
        string output = Path.Combine(directory, "files.json");
        File.WriteAllText(output, "keep-existing-content");

        Assert.Equal(CoreErrorCode.OutputExists, Assert.Throws<CoreException>(() => ManifestConverter.Convert(input, output)).Code);
        Assert.Equal("keep-existing-content", File.ReadAllText(output));
        Assert.Empty(Directory.EnumerateFiles(directory, "*.partial"));
    }

    [Fact]
    public void LongValidOutputFileNameCanBePublished()
    {
        string input = CreateInput(new SophonManifestProto
        {
            Assets = { FileAsset("Data/example.bin", 3, ExampleMd5) }
        });
        string output = Path.Combine(directory, new string('a', 215) + ".json");

        ConversionSummary summary = ManifestConverter.Convert(input, output);

        using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(output));
        Assert.Equal(new ConversionSummary(1, 3), summary);
        Assert.Equal("Data/example.bin", json.RootElement.GetProperty("files")[0].GetProperty("path").GetString());
        Assert.Empty(Directory.EnumerateFiles(directory, "*.partial"));
    }

    [Theory]
    [InlineData("Data/example.bin")]
    [InlineData(@"data\EXAMPLE.bin")]
    public void ConflictingPathsDoNotPublishFileList(string secondPath)
    {
        string input = CreateInput(new SophonManifestProto
        {
            Assets =
            {
                FileAsset("Data/example.bin", 3, ExampleMd5),
                FileAsset(secondPath, 4, "00000000000000000000000000000000")
            }
        });
        string output = Path.Combine(directory, "files.json");

        Assert.Equal(CoreErrorCode.DuplicatePath, Assert.Throws<CoreException>(() => ManifestConverter.Convert(input, output)).Code);
        Assert.False(File.Exists(output));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FileCannotAlsoBeAnotherFilesParentDirectory(bool parentFirst)
    {
        string[] paths = parentFirst
            ? ["Data/item", "data/item/file.bin"]
            : ["data/item/file.bin", "Data/item"];
        string input = CreateInput(new SophonManifestProto
        {
            Assets =
            {
                FileAsset(paths[0], 3, ExampleMd5),
                FileAsset(paths[1], 3, ExampleMd5)
            }
        });
        string output = Path.Combine(directory, "files.json");

        Assert.Equal(CoreErrorCode.FileDirectoryConflict, Assert.Throws<CoreException>(() => ManifestConverter.Convert(input, output)).Code);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void OverflowingTotalSizeReturnsCliErrorWithoutOutput()
    {
        string input = CreateInput(new SophonManifestProto
        {
            Assets =
            {
                FileAsset("Data/large.bin", long.MaxValue, ExampleMd5),
                FileAsset("Data/extra.bin", 1, ExampleMd5)
            }
        });
        string output = Path.Combine(directory, "files.json");
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "HoyoFileClean.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("convert");
        start.ArgumentList.Add(input);
        start.ArgumentList.Add(output);
        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("无法启动转换命令。");
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.Equal(1, process.ExitCode);
        Assert.Contains("总大小超过", error);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void ExcessiveDecompressedSizeIsRejectedBeforeParsing()
    {
        Directory.CreateDirectory(directory);
        string input = Path.Combine(directory, "manifest");
        using (var compressor = new Compressor())
        {
            File.WriteAllBytes(input, compressor.Wrap(new byte[128 * 1024 * 1024 + 1]).ToArray());
        }
        string output = Path.Combine(directory, "files.json");

        CoreException error = Assert.Throws<CoreException>(() =>
            ManifestConverter.Convert(input, output));

        Assert.Equal(CoreErrorCode.ManifestDecodedSizeLimit, error.Code);
        Assert.False(File.Exists(output));
    }

    private string CreateInput(SophonManifestProto manifest, bool compressed = true)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "manifest");
        byte[] bytes = manifest.ToByteArray();
        if (compressed)
        {
            using var compressor = new Compressor();
            bytes = compressor.Wrap(bytes).ToArray();
        }
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static SophonManifestAssetProperty FileAsset(string path, long size, string md5)
        => new() { AssetName = path, AssetSize = size, AssetHashMd5 = md5 };
}
