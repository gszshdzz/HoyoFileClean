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
        FileListSnapshot snapshot = Read(inputPath, CancellationToken.None);
        Export(snapshot, outputPath);
        return new ConversionSummary(snapshot.FileCount, snapshot.TotalSize);
    }

    public static FileListSnapshot Read(string inputPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var input = File.OpenRead(inputPath);
        return Read(input, Path.GetFullPath(inputPath), cancellationToken);
    }

    public static void Export(FileListSnapshot snapshot, string outputPath)
    {
        if (snapshot.Sources.Count != 1) throw new ArgumentException("Export requires one source manifest.", nameof(snapshot));
        if (File.Exists(outputPath) || Directory.Exists(outputPath))
            throw new CoreException(CoreErrorCode.OutputExists, outputPath);
        FileListSource source = snapshot.Sources[0];
        JsonFileOutput.WriteNew(outputPath, new FileListDocument(snapshot.FileCount, snapshot.TotalSize,
            snapshot.Files.Values.ToArray(), source.HashAlgorithm, source.Scan,
            source.Metadata is { Count: > 0 } ? source.Metadata : null, source.ManifestKind));
    }

    internal static FileListSnapshot Read(Stream input, string inputPath,
        CancellationToken cancellationToken, long? decodedSize = null, string? decodedMd5 = null,
        bool? expectedCompression = null)
    {
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
            return ReadDecoded(input, inputPath, cancellationToken, () =>
            {
                input.Position = 0;
                return new PrefixReadStream(Stream.Null, input);
            }, decodedSize, decodedMd5, allowLocalFormats: expectedCompression is null);
        }
        try
        {
            using var decompressor = new DecompressionStream(input);
            return ReadDecoded(decompressor, inputPath, cancellationToken, () =>
            {
                input.Position = 0;
                return new DecompressionStream(input);
            }, decodedSize, decodedMd5, allowLocalFormats: expectedCompression is null);
        }
        catch (Exception error) when (error is ZstdException or EndOfStreamException)
        {
            throw new CoreException(CoreErrorCode.ManifestCompressionInvalid, inputPath, innerException: error);
        }
    }

    private static FileListSnapshot ReadDecoded(Stream decoded, string inputPath,
        CancellationToken cancellationToken, Func<Stream> replayDecoded,
        long? decodedSize = null, string? decodedMd5 = null, bool allowLocalFormats = true)
    {
        using var limited = new ManifestReadStream(decoded, Maintenance.DecodedLimit, cancellationToken, decodedSize, decodedMd5);
        try { return ReadContent(limited, inputPath, cancellationToken, replayDecoded, allowLocalFormats); }
        finally
        {
            // Preserve the original decode-limit/invalid-compression precedence even if parsing
            // rejects an early byte. Draining does not retain any decoded content.
            limited.CopyTo(Stream.Null);
            limited.ValidateContent();
        }
    }

    private static FileListSnapshot ReadContent(Stream limited, string inputPath,
        CancellationToken cancellationToken, Func<Stream> replayDecoded, bool allowLocalFormats)
    {
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
            if (!allowLocalFormats) throw new CoreException(CoreErrorCode.ManifestNetworkUnsupported, inputPath);
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
            try { return ReadProtobuf(boundedAgain, inputPath, cancellationToken, replayDecoded, allowLocalFormats); }
            finally { boundedAgain.CopyTo(Stream.Null); }
        }
        prefix.Position = 0;
        using var replay = new PrefixReadStream(prefix, limited);
        return ReadProtobuf(replay, inputPath, cancellationToken, replayDecoded, allowLocalFormats);
    }

    private static FileListSnapshot ReadProtobuf(Stream replay, string inputPath, CancellationToken cancellationToken,
        Func<Stream> replayDecoded, bool allowPatch)
    {
        SophonFileManifest manifest;
        SophonPatchFileManifest? patch = null;
        try
        {
            manifest = SophonFileManifest.Parser.WithDiscardUnknownFields(true).ParseFrom(replay);
            if (manifest.Assets.Count == 0) throw new CoreException(CoreErrorCode.ManifestEmpty, inputPath);
            if (allowPatch)
            {
                // Only unknown local inputs need both projections. Official network input
                // already has a build-format contract; never re-decode it as a patch.
                using var decodedAgain = replayDecoded();
                using var boundedAgain = new ManifestReadStream(decodedAgain, Maintenance.DecodedLimit, cancellationToken);
                patch = SophonPatchFileManifest.Parser.WithDiscardUnknownFields(true).ParseFrom(boundedAgain);
            }
        }
        catch (InvalidProtocolBufferException error)
        {
            throw new CoreException(CoreErrorCode.ManifestEncodingInvalid, inputPath, innerException: error);
        }
        var files = new List<ManifestFile>();
        bool? patchFormat = null;
        if (patch is not null && manifest.Assets.Count != patch.Assets.Count)
            throw new CoreException(CoreErrorCode.ManifestEncodingInvalid, inputPath);
        for (int index = 0; index < manifest.Assets.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SophonFileAsset asset = manifest.Assets[index];
            SophonPatchFileAsset? patchAsset = patch?.Assets[index];
            bool buildFields = asset.HasAssetType || asset.HasAssetSize || asset.HasAssetHashMd5;
            bool patchFields = patchAsset is not null && (patchAsset.HasAssetSize || patchAsset.HasAssetHashMd5);
            if (buildFields && patchFields || patchFormat is not null && patchFormat != patchFields)
                throw new CoreException(CoreErrorCode.ManifestEncodingInvalid, inputPath);
            if (!buildFields && !patchFields)
                throw new CoreException(allowPatch ? CoreErrorCode.FileMetadataInvalid : CoreErrorCode.ManifestNetworkUnsupported, inputPath);
            patchFormat = patchFields;
            if (patchFields)
            {
                files.Add(ReadFile(patchAsset!.AssetName, patchAsset.AssetSize, patchAsset.AssetHashMd5));
            }
            else if (asset.AssetType == OfficialProtocol.FileAsset) files.Add(ReadFile(asset));
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
        return FileComparer.FromFiles(inputPath, files, totalSize, FileValues.Md5, cancellationToken: cancellationToken,
            manifestKind: patchFormat == true ? FileValues.SophonPatch : null);
    }

    private static ManifestFile ReadFile(SophonFileAsset asset)
        => ReadFile(asset.AssetName, asset.AssetSize, asset.AssetHashMd5);

    private static ManifestFile ReadFile(string path, long size, string md5)
        => FileEntryRules.Normalize(new ManifestFile(path, size, md5), hasHashes: true, metadataPath: path);
}
