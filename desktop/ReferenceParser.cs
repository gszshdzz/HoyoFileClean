namespace HoyoFileClean.Desktop;

internal sealed record ReferenceParseItem(int? Entries, Exception? Error);
internal sealed record ReferenceParseBatch(ReferenceParseItem[] Items, FileListSnapshot? Snapshot, Exception? CombinedError);

// Workers merge each file immediately. Failed batches retain summaries, not parsed file lists.
internal sealed class ReferenceParser
{
    internal const int DefaultConcurrency = 1;
    internal const int MaximumConcurrency = 128;
    private readonly Func<string, CancellationToken, FileListSnapshot> read;

    public ReferenceParser() : this(ManifestConverter.Read) { }

    internal ReferenceParser(Func<string, CancellationToken, FileListSnapshot> read)
    {
        this.read = read;
    }

    public ReferenceParseBatch Parse(string[] paths, CancellationToken cancellationToken,
        int concurrency = DefaultConcurrency, IProgress<int>? progress = null)
    {
        if (concurrency is < 1 or > MaximumConcurrency) throw new ArgumentOutOfRangeException(nameof(concurrency));
        var items = new ReferenceParseItem[paths.Length];
        var gate = new object();
        FileComparer.SnapshotAccumulator? accumulator = new();
        Exception? combinedError = null;
        int completed = 0;
        try
        {
            Parallel.For(0, paths.Length, new ParallelOptions
            {
                MaxDegreeOfParallelism = concurrency, CancellationToken = cancellationToken
            }, index =>
            {
                try
                {
                    FileListSnapshot snapshot = read(paths[index], cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    items[index] = new ReferenceParseItem(snapshot.FileCount, null);
                    lock (gate)
                    {
                        if (accumulator is not null)
                        {
                            try { accumulator.Add(snapshot, index, cancellationToken); }
                            catch (CoreException error)
                            {
                                combinedError = error;
                                accumulator = null;
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Parallel.For recognizes cancellation only when it carries its own token.
                    throw new OperationCanceledException(cancellationToken);
                }
                catch (Exception error) when (error is IOException or HttpRequestException or CoreException or UnauthorizedAccessException
                    or ArgumentException or System.Security.SecurityException or NotSupportedException)
                {
                    items[index] = new ReferenceParseItem(null, error);
                    lock (gate) accumulator = null;
                }
                progress?.Report(Interlocked.Increment(ref completed));
            });
            cancellationToken.ThrowIfCancellationRequested();
            FileListSnapshot? combined = null;
            if (items.All(item => item.Error is null) && accumulator is not null)
            {
                try { combined = accumulator.Build(cancellationToken); }
                catch (CoreException error) { combinedError = error; }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new ReferenceParseBatch(items, combined, combinedError);
        }
        finally
        {
            accumulator = null;
        }
    }
}
