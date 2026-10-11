using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using PeripheralAssetTracker.Desktop.ViewModels;

namespace PeripheralAssetTracker.Desktop;

internal static class AppInfo
{
    public const string Name = "Peripheral Asset Tracker";

    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "1.0.0";
}

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private AppHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _host?.Logger.LogError(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };

        // One instance per user: two windows writing to the same database would only confuse the user.
        var mutex = new Mutex(initiallyOwned: true, @"Local\PeripheralAssetTracker.Desktop", out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            MessageBox.Show("O Peripheral Asset Tracker já está aberto.\n\nProcure a janela na barra de tarefas.",
                AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(0);
            return;
        }

        _singleInstance = mutex;

        try
        {
            _host = AppHost.Create(StartupOptions.Parse(e.Args));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Não foi possível criar a pasta de dados do aplicativo.\n\n{ex.Message}",
                AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var window = new MainWindow(new MainViewModel(_host));
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            // Lets a running operation finish (bounded) and flushes the log file.
            Task.Run(() => _host.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(5));
        }

        if (_singleInstance is not null)
        {
            _singleInstance.ReleaseMutex();
            _singleInstance.Dispose();
        }

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _host?.Logger.LogCritical(e.Exception, "Unhandled UI exception");
        var logs = _host?.Paths.LogsDirectory;
        MessageBox.Show(
            "Ocorreu um erro inesperado. O aplicativo continuará aberto, mas talvez seja necessário reiniciá-lo." +
            (logs is null ? string.Empty : $"\n\nOs detalhes técnicos foram registrados em:\n{logs}"),
            AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
