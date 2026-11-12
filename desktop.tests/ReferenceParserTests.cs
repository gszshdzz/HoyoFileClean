using System.Text.Json;
using HoyoFileClean.Desktop;
using Xunit;

namespace HoyoFileClean.Desktop.Tests;

public sealed class ReferenceParserTests
{
    private static ReferenceInput[] Inputs(params string[] paths)
        => paths.Select(path => new ReferenceInput(new LocalManifestFile(path, path, 0, default))).ToArray();

    private static FileListSnapshot Sample()
    {
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "opencode", "HoyoFileClean.Desktop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "sample.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            fileCount = 1, totalSize = 3, files = new[] { new { path = "file", size = 3 } }
        }));
        return ManifestConverter.Read(path, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WorkersAreParallelButBoundedAndResultsKeepInputOrder()
    {
        FileListSnapshot snapshot = Sample();
        using var entered = new CountdownEvent(2);
        using var release = new ManualResetEventSlim();
        int active = 0, maximum = 0, started = 0;
        var parser = new ReferenceParser((input, token) =>
        {
            int current = Interlocked.Increment(ref active);
            int old;
            do { old = Volatile.Read(ref maximum); }
            while (current > old && Interlocked.CompareExchange(ref maximum, current, old) != old);
            try
            {
                if (Interlocked.Increment(ref started) <= 2) entered.Signal();
                if (!release.Wait(TimeSpan.FromSeconds(10), token)) throw new TimeoutException();
                if (input.File.FullPath == "bad") throw new IOException("invalid input");
                return snapshot;
            }
            finally { Interlocked.Decrement(ref active); }
        });
        CancellationToken token = TestContext.Current.CancellationToken;
        Task<ReferenceParseBatch> work = Task.Run(() => parser.Parse(Inputs("first", "second", "bad", "last"), token, 2), token);
        bool concurrent = entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        release.Set();
        ReferenceParseBatch result = await work;
        Assert.True(concurrent, "两个工作线程没有同时进入解析");
        Assert.Equal(2, maximum);
        Assert.Equal(0, active);
        Assert.Equal(new int?[] { 1, 1, null, 1 }, result.Items.Select(item => item.Entries));
        Assert.IsType<IOException>(result.Items[2].Error);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public async Task CancellationWaitsForWorkersAndDoesNotPublishOrStartQueuedFiles()
    {
        using var cancellation = new CancellationTokenSource();
        using var entered = new CountdownEvent(2);
        int calls = 0, active = 0;
        var parser = new ReferenceParser((_, token) =>
        {
            Interlocked.Increment(ref calls);
            Interlocked.Increment(ref active);
            entered.Signal();
            try
            {
                if (!token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10))) throw new TimeoutException();
                token.ThrowIfCancellationRequested();
                throw new InvalidOperationException();
            }
            finally { Interlocked.Decrement(ref active); }
        });
        Task<ReferenceParseBatch> work = Task.Run(() => parser.Parse(Inputs(Enumerable.Range(0, 100).Select(i => i.ToString()).ToArray()), cancellation.Token, 2), TestContext.Current.CancellationToken);
        bool concurrent = entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await work);
        Assert.True(concurrent);
        Assert.Equal(2, calls);
        Assert.Equal(0, active);
    }

    [Fact]
    public void SampledProgressCoalescesWorkerUpdatesWithoutPostingUiMessages()
    {
        var progress = new ParsingProgress();
        Parallel.For(0, 10000, new ParallelOptions { CancellationToken = TestContext.Current.CancellationToken },
            index => progress.Report(index + 1));
        progress.Report(1);
        Assert.Equal(10000, progress.Completed);
    }

    [Fact]
    public void ProgressCountsSuccessfulAndFailedFilesAndAcceptsConfiguredConcurrency()
    {
        FileListSnapshot snapshot = Sample();
        var parser = new ReferenceParser((input, _) => input.File.FullPath == "bad" ? throw new IOException("invalid") : snapshot);
        foreach (int concurrency in new[] { 1, 4, 16, 128 })
        {
            var counts = new System.Collections.Concurrent.ConcurrentQueue<int>();
            var reporter = new InlineProgress(counts.Enqueue);
            ReferenceParseBatch result = parser.Parse(Inputs("a", "bad", "b"), TestContext.Current.CancellationToken, concurrency, reporter);
            Assert.Equal(new[] { 1, 2, 3 }, counts.Order());
            Assert.Equal(new int?[] { 1, null, 1 }, result.Items.Select(item => item.Entries));
            Assert.Null(result.Snapshot);
        }
        Assert.Equal(1, ReferenceParser.DefaultConcurrency);
        foreach (int invalid in new[] { 0, -1, 129 })
            Assert.Throws<ArgumentOutOfRangeException>(() => parser.Parse(Inputs("a"), TestContext.Current.CancellationToken, invalid));
    }

    [Fact]
    public void CancellationDoesNotCountInterruptedFileAsCompleted()
    {
        FileListSnapshot snapshot = Sample();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var counts = new List<int>();
        int calls = 0;
        var parser = new ReferenceParser((_, token) =>
        {
            if (Interlocked.Increment(ref calls) == 2)
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            }
            return snapshot;
        });
        Assert.ThrowsAny<OperationCanceledException>(() => parser.Parse(Inputs("a", "b", "c"), cancellation.Token,
            1, new InlineProgress(counts.Add)));
        Assert.Equal(new[] { 1 }, counts);
        Assert.Equal(2, calls);
    }

    private sealed class InlineProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }

    [Fact]
    public void SuccessfulRepeatedBatchesDoNotAccumulateInputs()
    {
        FileListSnapshot snapshot = Sample();
        var parser = new ReferenceParser((_, _) => snapshot);
        Assert.Equal(2, parser.Parse(Inputs("a", "b"), TestContext.Current.CancellationToken).Snapshot!.Sources.Count);
        Assert.Single(parser.Parse(Inputs("c"), TestContext.Current.CancellationToken).Snapshot!.Sources);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => parser.Parse(Inputs("d"), cancelled.Token));
        Assert.Single(parser.Parse(Inputs("e"), TestContext.Current.CancellationToken).Snapshot!.Sources);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HourBandFollowsCurrentLightOrDarkCellPalette(bool dark)
    {
        Color background = dark ? Color.FromArgb(30, 32, 34) : Color.FromArgb(250, 248, 246);
        Color foreground = dark ? Color.FromArgb(230, 232, 234) : Color.FromArgb(20, 22, 24);
        Color band = ManifestTable.HourBandColor(background, foreground);
        Assert.True(Math.Abs(band.R - background.R) >= 20);
        Assert.True(dark ? band.R > background.R : band.R < background.R);
        Assert.True(Math.Abs(foreground.R - band.R) >= 170);
    }
}
