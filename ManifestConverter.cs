using System.Text.Json;
using Google.Protobuf;
using HoyoFileClean.Protos;
using ZstdSharp;

namespace HoyoFileClean;

public sealed record ConversionSummary(int FileCount, long TotalSize);

public sealed record ManifestFile(string Path, long Size, string Hash);

internal sealed record ManifestFileList(
    string HashAlgorithm,
    int FileCount,
    long TotalSize,
    IReadOnlyList<ManifestFile> Files);

public static class ManifestConverter
{
    private const int MaxCompressedSize = 64 * 1024 * 1024;
    private const int MaxDecodedSize = 128 * 1024 * 1024;
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        "COM¹", "COM²", "COM³", "LPT¹", "LPT²", "LPT³"
    };

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
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ManifestFile file in files)
        {
            if (!paths.Add(file.Path))
            {
                throw new InvalidDataException($"Manifest 中存在重复文件路径：{file.Path}");
            }
        }
        foreach (ManifestFile file in files)
        {
            for (int separator = file.Path.IndexOf('/'); separator >= 0;
                separator = file.Path.IndexOf('/', separator + 1))
            {
                if (paths.Contains(file.Path[..separator]))
                {
                    throw new InvalidDataException($"Manifest 中的文件同时被用作父目录：{file.Path[..separator]}");
                }
            }
        }
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
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(list, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });

        string partialPath = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(outputPath))!, $"{Guid.NewGuid():N}.partial");
        using (var output = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write))
        {
            output.Write(json);
        }
        File.Move(partialPath, outputPath, overwrite: false);
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

        string path = asset.AssetName.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path)
            || path.Split('/').Any(segment => segment.Length == 0
                || segment is "." or ".."
                || segment.EndsWith('.') || segment.EndsWith(' ')
                || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || ReservedDeviceNames.Contains(segment.Split('.')[0])))
        {
            throw new InvalidDataException($"Manifest 文件路径不是安全的相对路径：{asset.AssetName}");
        }

        return new ManifestFile(path, asset.AssetSize, asset.AssetHashMd5.ToLowerInvariant());
    }
}
