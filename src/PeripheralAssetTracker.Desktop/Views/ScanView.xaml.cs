using System.Windows;
using System.Windows.Controls;

namespace PeripheralAssetTracker.Desktop.Views;

public partial class ScanView : UserControl
{
    public ScanView() => InitializeComponent();

    private void ScanGrid_SizeChanged(object sender, SizeChangedEventArgs e) =>
        ColumnFitter.Fit(ScanGrid, ModelColumn, ConfidenceColumn, ManufacturerColumn, BusColumn);
}
