namespace HoyoFileClean.Desktop;

internal static class FileSizeCells
{
    internal static void Configure(DataGridView table)
    {
        int column = table.Columns["Size"]!.Index;
        table.Columns[column].ValueType = typeof(long);
        table.CellFormatting += (_, args) =>
        {
            if (args.ColumnIndex != column || args.Value is not long bytes) return;
            args.Value = FileSizeText.Format(bytes);
            args.FormattingApplied = true;
        };
    }
}
