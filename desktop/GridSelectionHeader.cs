namespace HoyoFileClean.Desktop;

internal static class GridSelectionHeader
{
    internal static void Position(DataGridView grid, CheckBox checkbox)
    {
        if (grid.Columns.Count == 0) return;
        Rectangle header = grid.GetCellDisplayRectangle(0, -1, true);
        Size size = checkbox.GetPreferredSize(Size.Empty);
        Rectangle bounds = new(header.Left + (header.Width - size.Width) / 2,
            header.Top + (header.Height - size.Height) / 2, size.Width, size.Height);
        if (checkbox.Bounds != bounds) checkbox.Bounds = bounds;
        bool visible = grid.ColumnHeadersVisible && header.Width >= size.Width && header.Height >= size.Height;
        if (checkbox.Visible != visible) checkbox.Visible = visible;
    }
}
