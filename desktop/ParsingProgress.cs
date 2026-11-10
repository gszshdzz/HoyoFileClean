namespace HoyoFileClean.Desktop;

// Workers only publish a counter. The UI samples it; no per-file UI messages are queued.
internal sealed class ParsingProgress : IProgress<int>
{
    private int completed;
    public int Completed => Volatile.Read(ref completed);

    public void Report(int value)
    {
        int current;
        do
        {
            current = Volatile.Read(ref completed);
            if (value <= current) return;
        } while (Interlocked.CompareExchange(ref completed, value, current) != current);
    }
}
