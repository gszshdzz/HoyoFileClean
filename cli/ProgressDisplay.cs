namespace HoyoFileClean;

public static class ProgressDisplay
{
    public static string Format(ScanProgress value, int previousWidth, int columns)
    {
        string line = L.FormatInvariant("HashProgress", value.Percentage, value.CompletedFiles,
            value.TotalFiles, value.ProcessedBytes / 1048576.0, value.TotalBytes / 1048576.0);
        int limit = Math.Max(0, columns - 1);
        if (line.Length > limit)
        {
            line = line[..limit];
        }
        return "\r" + line.PadRight(Math.Min(Math.Max(previousWidth, 0), limit));
    }
}
