namespace HoyoFileClean.Presentation;

public static class FileSizeText
{
    public static string Format(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        decimal value = bytes;
        int unit = 0;
        decimal nextUnitThreshold = (decimal)Math.Pow(10, PresentationOptions.FileSizeSignificantDigits) - 0.5m;
        while (value >= (unit == 0 ? PresentationOptions.FileSizeUnitBase : nextUnitThreshold)
            && unit < PresentationOptions.FileSizeUnitTextIds.Count - 1)
        {
            value /= PresentationOptions.FileSizeUnitBase;
            unit++;
        }
        int decimals = unit == 0 ? 0 : Math.Max(0,
            PresentationOptions.FileSizeSignificantDigits - 1 - (int)Math.Floor(Math.Log10((double)value)));
        decimal rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
        return rounded.ToString(decimals == 0 ? "0" : "0." + new string('#', decimals),
            System.Globalization.CultureInfo.InvariantCulture) + Text.Get(PresentationOptions.FileSizeUnitTextIds[unit]);
    }
}
