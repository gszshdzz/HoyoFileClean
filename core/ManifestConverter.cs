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
            throw new CoreException(CoreErrorCode.OutputExists, outputPath);
        }
        using var input = File.OpenRead(inputPath);
        if (!uncompressed && input.Length > MaxCompressedSize)
        {
            throw new CoreException(CoreErrorCode.ManifestCompressedSizeLimit, inputPath);
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
                throw new CoreException(CoreErrorCode.ManifestCompressionInvalid, inputPath, innerException: error);
            }
        }

        SophonManifestProto manifest;
        try
        {
            manifest = SophonManifestProto.Parser.ParseFrom(decoded.ToArray());
        }
        catch (InvalidProtocolBufferException error)
        {
            throw new CoreException(CoreErrorCode.ManifestEncodingInvalid, inputPath, innerException: error);
        }
        List<ManifestFile> files = manifest.Assets
            .Where(asset => asset.AssetType == 0)
            .Select(ReadFile)
            .ToList();
        if (files.Count == 0)
        {
            throw new CoreException(CoreErrorCode.ManifestEmpty, inputPath);
        }
        FilePathRules.ValidatePaths(files.Select(file => file.Path), inputPath);
        long totalSize;
        try
        {
            totalSize = files.Sum(file => file.Size);
        }
        catch (OverflowException error)
        {
            throw new CoreException(CoreErrorCode.TotalSizeOverflow, inputPath, innerException: error);
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
                throw new CoreException(CoreErrorCode.ManifestDecodedSizeLimit);
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
            throw new CoreException(CoreErrorCode.FileMetadataInvalid, asset.AssetName);
        }

        string path = FilePathRules.NormalizeRelative(asset.AssetName);
        return new ManifestFile(path, asset.AssetSize, asset.AssetHashMd5.ToLowerInvariant());
    }
}
