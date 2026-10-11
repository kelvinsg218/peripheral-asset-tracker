using System.Windows;
using System.Windows.Controls;

namespace PeripheralAssetTracker.Desktop.Views;

internal static class ColumnFitter
{
    /// <summary>
    /// Hides the least important columns (first in <paramref name="dropOrder"/>) until the minimum widths of the
    /// visible columns fit the grid, and shows them again when there is room. Avoids a horizontal scrollbar.
    /// </summary>
    public static void Fit(DataGrid grid, params DataGridColumn[] dropOrder)
    {
        // Slack for the vertical scrollbar, borders and rounding.
        var available = grid.ActualWidth - SystemParameters.VerticalScrollBarWidth - 48;
        var required = grid.Columns.Sum(c => c.MinWidth);
        foreach (var column in dropOrder)
        {
            var hide = required > available;
            column.Visibility = hide ? Visibility.Collapsed : Visibility.Visible;
            if (hide)
            {
                required -= column.MinWidth;
            }
        }
    }
}
