using System.Windows;
using System.Windows.Controls;

namespace PeripheralAssetTracker.Desktop.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateLayoutMode();
    }

    /// <summary>Wide: 6 indicators per row and two columns. Narrow: 2-3 indicators per row and stacked panels.</summary>
    private void UpdateLayoutMode()
    {
        var width = ActualWidth;
        Kpis.Columns = width >= 1400 ? 6 : width >= 700 ? 3 : 2;

        var stacked = width < 900;
        SideColumn.Width = stacked ? new GridLength(0) : new GridLength(360);
        Grid.SetColumn(SidePanel, stacked ? 0 : 1);
        Grid.SetRow(SidePanel, stacked ? 1 : 0);
        SidePanel.Margin = stacked ? new Thickness(0, 12, 0, 0) : new Thickness(12, 0, 0, 0);
    }
}
