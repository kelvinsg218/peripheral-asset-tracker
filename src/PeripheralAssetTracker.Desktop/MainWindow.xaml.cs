using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using PeripheralAssetTracker.Desktop.ViewModels;
using PeripheralAssetTracker.Desktop.Views;

namespace PeripheralAssetTracker.Desktop;

public partial class MainWindow : Window
{
    private const int WmDeviceChange = 0x0219;
    private const int DbtDevNodesChanged = 0x0007;
    private const double CompactWidth = 1060;

    private readonly MainViewModel _viewModel;

    internal MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        var area = SystemParameters.WorkArea;
        Width = Math.Min(1500, area.Width * 0.92);
        Height = Math.Min(920, area.Height * 0.92);
        DataContext = _viewModel = viewModel;
        Loaded += async (_, _) => await _viewModel.StartAsync();
        SizeChanged += (_, _) => UpdateLayoutMode();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DarkTitleBar.Apply(this);

        // Windows broadcasts WM_DEVICECHANGE to top-level windows when a device is plugged in or removed.
        // It is only handled while this window is open: there is no background monitoring.
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmDeviceChange && wParam.ToInt64() == DbtDevNodesChanged)
        {
            _viewModel.OnDevicesChanged();
        }

        return IntPtr.Zero;
    }

    private void UpdateLayoutMode()
    {
        var expanded = ActualWidth >= CompactWidth;
        _viewModel.IsSidebarExpanded = expanded;
        SidebarColumn.Width = new GridLength(expanded ? 240 : 68);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.CurrentPage) || !SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        ((TranslateTransform)PageHost.RenderTransform).BeginAnimation(
            TranslateTransform.YProperty, new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
    }
}
