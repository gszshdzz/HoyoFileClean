namespace HoyoFileClean.Desktop;

internal static class DifferenceText
{
    internal static string CategoryName(DifferenceCategory category) => category switch
    {
        DifferenceCategory.GameOnly => L.Get("CategoryGameOnly"),
        DifferenceCategory.ReferenceOnly => L.Get("CategoryReferenceOnly"),
        DifferenceCategory.Different => L.Get("CategoryDifferent"),
        _ => L.Get("CategoryOutsideScope")
    };
}
