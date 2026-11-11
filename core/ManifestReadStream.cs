using System.Security.Cryptography;

namespace HoyoFileClean;

// Forward-only, non-owning wrappers: decoding stays bounded even when the parser skips fields.
internal abstract class ForwardReadStream : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int ReadByte()
    {
        Span<byte> value = stackalloc byte[1];
        return Read(value) == 0 ? -1 : value[0];
    }
}

internal sealed class ManifestReadStream(Stream input, long limit, CancellationToken cancellationToken,
    long? expectedSize = null, string? expectedMd5 = null) : ForwardReadStream
{
    private long consumed;
    private readonly IncrementalHash? hash = expectedMd5 is null ? null : IncrementalHash.CreateHash(HashAlgorithmName.MD5);

    public override int Read(Span<byte> buffer)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (buffer.IsEmpty) return 0;
        int count = input.Read(buffer[..(int)Math.Min(buffer.Length, limit - consumed + 1)]);
        consumed += count;
        if (consumed > limit) throw new CoreException(CoreErrorCode.ManifestDecodedSizeLimit);
        hash?.AppendData(buffer[..count]);
        return count;
    }

    internal void ValidateContent()
    {
        if (expectedSize is not null && consumed != expectedSize)
            throw new CoreException(CoreErrorCode.ManifestDownloadMismatch);
        if (hash is not null && !Convert.ToHexString(hash.GetHashAndReset()).Equals(expectedMd5, StringComparison.OrdinalIgnoreCase))
            throw new CoreException(CoreErrorCode.ManifestDownloadMismatch);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) hash?.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class PrefixReadStream(Stream prefix, Stream input) : ForwardReadStream
{
    public override int Read(Span<byte> buffer)
    {
        int count = prefix.Read(buffer);
        return count > 0 ? count : input.Read(buffer);
    }
}
