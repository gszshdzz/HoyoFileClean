using Google.Protobuf;
using HoyoFileClean.Protos;
using ZstdSharp;

namespace HoyoFileClean;

public sealed record ConversionSummary(int FileCount, long TotalSize);

public static class ManifestConverter
{

    public static ConversionSummary Convert(
        string inputPath,
        string outputPath)
    {
        if (File.Exists(outputPath) || Directory.Exists(outputPath))
        {
            throw new CoreException(CoreErrorCode.OutputExists, outputPath);
        }
        FileListSnapshot snapshot = Read(inputPath, out bool isJson, CancellationToken.None);
        FileListSource source = snapshot.Sources[0];
        JsonFileOutput.WriteNew(outputPath, new FileListDocument(snapshot.FileCount, snapshot.TotalSize,
            snapshot.Files.Values.ToArray(), source.HashAlgorithm, source.Scan));
        return new ConversionSummary(snapshot.FileCount, snapshot.TotalSize);
    }

    public static FileListSnapshot Read(string inputPath, CancellationToken cancellationToken = default)
        => Read(inputPath, out _, cancellationToken);

    private static FileListSnapshot Read(string inputPath, out bool isJson, CancellationToken cancellationToken)
    {
        isJson = false;
        cancellationToken.ThrowIfCancellationRequested();
        using var input = File.OpenRead(inputPath);
        return Read(input, Path.GetFullPath(inputPath), out isJson, cancellationToken);
    }

    internal static FileListSnapshot Read(Stream input, string inputPath, out bool isJson,
        CancellationToken cancellationToken, long? decodedSize = null, string? decodedMd5 = null,
        bool? expectedCompression = null)
    {
        isJson = false;
        Span<byte> header = stackalloc byte[4];
        int headerLength = input.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        input.Position = 0;
        bool compressed = headerLength == 4
            && (header.SequenceEqual(new byte[] { 0x28, 0xb5, 0x2f, 0xfd })
                || (header[0] is >= 0x50 and <= 0x5f && header[1] == 0x2a && header[2] == 0x4d && header[3] == 0x18));
        if (expectedCompression is not null && compressed != expectedCompression)
            throw new CoreException(CoreErrorCode.ManifestDownloadMismatch, inputPath);
        if (compressed && input.Length > Maintenance.CompressedLimit)
        {
            throw new CoreException(CoreErrorCode.ManifestCompressedSizeLimit, inputPath);
        }
        if (!compressed)
        {
            return ReadDecoded(input, inputPath, out isJson, cancellationToken, () =>
            {
                input.Position = 0;
                return new PrefixReadStream(Stream.Null, input);
            }, decodedSize, decodedMd5, allowJson: expectedCompression is null);
        }
        try
        {
            using var decompressor = new DecompressionStream(input);
            return ReadDecoded(decompressor, inputPath, out isJson, cancellationToken, () =>
            {
                input.Position = 0;
                return new DecompressionStream(input);
            }, decodedSize, decodedMd5, allowJson: expectedCompression is null);
        }
        catch (Exception error) when (error is ZstdException or EndOfStreamException)
        {
            throw new CoreException(CoreErrorCode.ManifestCompressionInvalid, inputPath, innerException: error);
        }
    }

    private static FileListSnapshot ReadDecoded(Stream decoded, string inputPath, out bool isJson,
        CancellationToken cancellationToken, Func<Stream> replayDecoded,
        long? decodedSize = null, string? decodedMd5 = null, bool allowJson = true)
    {
        using var limited = new ManifestReadStream(decoded, Maintenance.DecodedLimit, cancellationToken, decodedSize, decodedMd5);
        try { return ReadContent(limited, inputPath, out isJson, cancellationToken, replayDecoded, allowJson); }
        finally
        {
            // Preserve the original decode-limit/invalid-compression precedence even if parsing
            // rejects an early byte. Draining does not retain any decoded content.
            limited.CopyTo(Stream.Null);
            limited.ValidateContent();
        }
    }

    private static FileListSnapshot ReadContent(Stream limited, string inputPath, out bool isJson,
        CancellationToken cancellationToken, Func<Stream> replayDecoded, bool allowJson)
    {
        isJson = false;
        // Probe only format-leading bytes. Replay them for protobuf; strip only a JSON BOM.
        using var prefix = new MemoryStream();
        int first = limited.ReadByte();
        if (first >= 0) prefix.WriteByte((byte)first);
        if (first == 0xef)
        {
            for (int i = 0; i < 2; i++)
            {
                int value = limited.ReadByte();
                if (value >= 0) prefix.WriteByte((byte)value);
            }
            if (prefix.Length == 3 && prefix.GetBuffer().AsSpan(0, 3).SequenceEqual(new byte[] { 0xef, 0xbb, 0xbf }))
            {
                first = limited.ReadByte();
                if (first >= 0) prefix.WriteByte((byte)first);
            }
        }
        bool prefixOverflow = false;
        while (first is 0x20 or 0x09 or 0x0a or 0x0d)
        {
            first = limited.ReadByte();
            if (first >= 0)
            {
                if (prefix.Length < Maintenance.FormatProbeLimit) prefix.WriteByte((byte)first);
                else prefixOverflow = true;
            }
        }
        int skip = prefix.Length >= 3 && prefix.GetBuffer().AsSpan(0, 3)
            .SequenceEqual(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
        if (first == (byte)'{')
        {
            if (!allowJson) throw new CoreException(CoreErrorCode.ManifestNetworkUnsupported, inputPath);
            isJson = true;
            // JSON does not need its leading whitespace replayed.
            if (prefixOverflow)
            {
                prefix.SetLength(0);
                prefix.WriteByte((byte)'{');
                prefix.Position = 0;
            }
            else prefix.Position = skip;
            return FileComparer.ReadInputs([inputPath],
                _ => new PrefixReadStream(prefix, limited), cancellationToken);
        }

        if (prefixOverflow)
        {
            // Leading whitespace can also be valid protobuf bytes. Rare long probes are
            // reread from the same open file rather than cached or silently discarded.
            limited.CopyTo(Stream.Null);
            using var decodedAgain = replayDecoded();
            using var boundedAgain = new ManifestReadStream(decodedAgain, Maintenance.DecodedLimit, cancellationToken);
            try { return ReadProtobuf(boundedAgain, inputPath, cancellationToken); }
            finally { boundedAgain.CopyTo(Stream.Null); }
        }
        prefix.Position = 0;
        using var replay = new PrefixReadStream(prefix, limited);
        return ReadProtobuf(replay, inputPath, cancellationToken);
    }

    private static FileListSnapshot ReadProtobuf(Stream replay, string inputPath, CancellationToken cancellationToken)
    {
        SophonFileManifest manifest;
        try
        {
            manifest = SophonFileManifest.Parser.WithDiscardUnknownFields(true).ParseFrom(replay);
        }
        catch (InvalidProtocolBufferException error)
        {
            throw new CoreException(CoreErrorCode.ManifestEncodingInvalid, inputPath, innerException: error);
        }
        var files = new List<ManifestFile>();
        foreach (SophonFileAsset asset in manifest.Assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (asset.AssetType == OfficialProtocol.FileAsset) files.Add(ReadFile(asset));
        }
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
        return FileComparer.FromFiles(inputPath, files, totalSize, FileValues.Md5, cancellationToken: cancellationToken);
    }

    private static ManifestFile ReadFile(SophonFileAsset asset)
        => FileEntryRules.Normalize(new ManifestFile(asset.AssetName, asset.AssetSize, asset.AssetHashMd5),
            hasHashes: true, metadataPath: asset.AssetName);
}
