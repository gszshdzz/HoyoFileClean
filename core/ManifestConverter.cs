using Google.Protobuf;
using System.Text.Json.Serialization;
using HoyoFileClean.Protos;
using ZstdSharp;

namespace HoyoFileClean;

public sealed record ConversionSummary(int FileCount, long TotalSize);

public sealed record ManifestFile(string Path, long Size,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Hash = null);

internal sealed record ManifestFileList(
    string HashAlgorithm,
    int FileCount,
    long TotalSize,
    IReadOnlyList<ManifestFile> Files);

public static class ManifestConverter
{
    private const int MaxCompressedSize = 64 * 1024 * 1024;
    private const int MaxDecodedSize = 128 * 1024 * 1024;

    public static ConversionSummary Convert(
        string inputPath,
        string outputPath,
        bool uncompressed = false)
    {
        if (File.Exists(outputPath) || Directory.Exists(outputPath))
        {
            throw new IOException($"输出已存在，不能覆盖：{outputPath}");
        }
        using var input = File.OpenRead(inputPath);
        if (!uncompressed && input.Length > MaxCompressedSize)
        {
            throw new InvalidDataException("Manifest 压缩输入大小超过 64 MiB，已停止转换。");
        }
        using var decoded = new MemoryStream();
        if (uncompressed)
        {
            CopyWithLimit(input, decoded);
        }
        else
        {
            try
            {
                using var decompressor = new DecompressionStream(input);
                CopyWithLimit(decompressor, decoded);
            }
            catch (Exception error) when (error is ZstdException or EndOfStreamException)
            {
                throw new InvalidDataException("无法解压 Manifest：zstd 数据损坏或不完整。", error);
            }
        }

        SophonManifestProto manifest;
        try
        {
            manifest = SophonManifestProto.Parser.ParseFrom(decoded.ToArray());
        }
        catch (InvalidProtocolBufferException error)
        {
            throw new InvalidDataException("无法解析 Manifest：protobuf 数据损坏或不完整。", error);
        }
        List<ManifestFile> files = manifest.Assets
            .Where(asset => asset.AssetType == 0)
            .Select(ReadFile)
            .ToList();
        if (files.Count == 0)
        {
            throw new InvalidDataException("Manifest 没有文件条目，不能生成文件列表。");
        }
        FilePathRules.ValidatePaths(files.Select(file => file.Path), "Manifest");
        long totalSize;
        try
        {
            totalSize = files.Sum(file => file.Size);
        }
        catch (OverflowException error)
        {
            throw new InvalidDataException("Manifest 文件总大小超过可表示范围，已停止转换。", error);
        }
        var list = new ManifestFileList("MD5", files.Count, totalSize, files);
        JsonFileOutput.WriteNew(outputPath, list);
        return new ConversionSummary(files.Count, totalSize);
    }

    private static void CopyWithLimit(Stream input, MemoryStream output)
    {
        byte[] buffer = new byte[81920];
        int count;
        while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
        {
            if (output.Length + count > MaxDecodedSize)
            {
                throw new InvalidDataException("Manifest 解压后大小超过 128 MiB，已停止转换。");
            }
            output.Write(buffer, 0, count);
        }
    }

    private static ManifestFile ReadFile(SophonManifestAssetProperty asset)
    {
        if (asset.AssetSize < 0 || asset.AssetHashMd5.Length != 32
            || asset.AssetHashMd5.Any(character => character is not
                (>= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F')))
        {
            throw new InvalidDataException($"Manifest 文件大小或 MD5 无效：{asset.AssetName}");
        }

        string path = FilePathRules.NormalizeRelative(asset.AssetName, "Manifest");
        return new ManifestFile(path, asset.AssetSize, asset.AssetHashMd5.ToLowerInvariant());
    }
}
