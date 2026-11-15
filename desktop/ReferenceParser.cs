namespace HoyoFileClean.Desktop;

internal sealed record ReferenceParseItem(int? Entries, Exception? Error,
    IReadOnlyDictionary<string, ManifestMetadataField>? Metadata = null, FileListSnapshot? Snapshot = null);

internal sealed class ReferenceParseBatch(ReferenceParseItem[] items, FileListSnapshot? snapshot, Exception? combinedError)
{
    public IReadOnlyList<ReferenceParseItem> Items { get; } = Array.AsReadOnly(items);

    // A batch can retain row diagnostics without exposing a usable partial snapshot.
    public FileListSnapshot GetSnapshot() => snapshot ?? throw new InvalidDataException(null,
        Items.FirstOrDefault(item => item.Error is not null)?.Error ?? combinedError);
}

// Retain successful per-file snapshots for details/export, even when batch merging fails.
internal sealed class ReferenceParser
{
    internal const int DefaultConcurrency = Maintenance.DefaultParsingConcurrency;
    internal const int MaximumConcurrency = Maintenance.MaximumParsingConcurrency;
    private readonly Func<ReferenceInput, CancellationToken, FileListSnapshot> read;

    public ReferenceParser() : this((input, token) => ManifestConverter.Read(input.File.FullPath, token)) { }

    internal ReferenceParser(Func<ReferenceInput, CancellationToken, FileListSnapshot> read)
    {
        this.read = read;
    }

    public ReferenceParseBatch Parse(ReferenceInput[] inputs, CancellationToken cancellationToken,
        int concurrency = DefaultConcurrency, IProgress<int>? progress = null)
    {
        if (concurrency is < 1 or > MaximumConcurrency) throw new ArgumentOutOfRangeException(nameof(concurrency));
        var items = new ReferenceParseItem[inputs.Length];
        var gate = new object();
        FileComparer.SnapshotAccumulator? accumulator = new();
        Exception? combinedError = null;
        int completed = 0;
        try
        {
            Parallel.For(0, inputs.Length, new ParallelOptions
            {
                MaxDegreeOfParallelism = concurrency, CancellationToken = cancellationToken
            }, index =>
            {
                try
                {
                    FileListSnapshot snapshot = read(inputs[index], cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    items[index] = new ReferenceParseItem(snapshot.FileCount, null, snapshot.Sources[0].Metadata, snapshot);
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
