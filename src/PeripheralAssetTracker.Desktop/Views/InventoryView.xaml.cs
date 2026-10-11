using System.Windows;
using System.Windows.Controls;

namespace PeripheralAssetTracker.Desktop.Views;

public partial class InventoryView : UserControl
{
    public InventoryView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateLayoutMode();
    }

    /// <summary>Details beside the list on wide windows, below it on narrow ones.</summary>
    private void UpdateLayoutMode()
    {
        var stacked = ActualWidth < 1000;
        DetailsColumn.Width = stacked ? new GridLength(0) : new GridLength(360);
        DetailsRow.Height = stacked ? new GridLength(2, GridUnitType.Star) : new GridLength(0);
        ListRow.Height = new GridLength(stacked ? 3 : 1, GridUnitType.Star);
        Grid.SetColumn(Details, stacked ? 0 : 1);
        Grid.SetRow(Details, stacked ? 1 : 0);
        Details.Margin = stacked ? new Thickness(0, 12, 0, 0) : new Thickness(12, 0, 0, 0);
    }

    private void DevicesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => DetailsScroll.ScrollToTop();

    // Everything hidden here is still shown in the details panel.
    private void DevicesGrid_SizeChanged(object sender, SizeChangedEventArgs e) =>
        ColumnFitter.Fit(DevicesGrid, ModelColumn, ManufacturerColumn, CategoryColumn, LastSeenColumn);
}
