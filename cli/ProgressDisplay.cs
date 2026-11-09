namespace HoyoFileClean;

public static class ProgressDisplay
{
    public static string Format(ScanProgress value, int previousWidth, int columns)
    {
        string line = FormattableString.Invariant(
            $"MD5 {value.Percentage,6:F1}%  {value.CompletedFiles:N0}/{value.TotalFiles:N0}  {value.ProcessedBytes / 1048576.0:N1}/{value.TotalBytes / 1048576.0:N1} MiB");
        int limit = Math.Max(0, columns - 1);
        if (line.Length > limit)
        {
            line = line[..limit];
        }
        return "\r" + line.PadRight(Math.Min(Math.Max(previousWidth, 0), limit));
    }
}
