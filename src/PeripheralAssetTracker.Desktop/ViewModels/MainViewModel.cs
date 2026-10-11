using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using PeripheralAssetTracker.Application;
using PeripheralAssetTracker.Application.Models;
using PeripheralAssetTracker.Application.Services;
using PeripheralAssetTracker.Desktop.Mvvm;
using PeripheralAssetTracker.Desktop.Presentation;
using PeripheralAssetTracker.Desktop.Views;
using PeripheralAssetTracker.Domain;
using PeripheralAssetTracker.Domain.Enums;
using PeripheralAssetTracker.Infrastructure.Reports;

namespace PeripheralAssetTracker.Desktop.ViewModels;

internal enum AppPage
{
    Dashboard,
    Scan,
    Inventory,
    People,
    Computers,
    History,
    About
}

internal sealed record CategoryItem(string Label, string Glyph, int Count, double Percent);

internal sealed class MainViewModel : ObservableObject
{
    private const int MaxActivity = 200;
    private const string DemoWindowsVersion = "Windows (demonstração)";

    private readonly AppHost _host;
    private readonly DispatcherTimer _noticeTimer = new();
    private readonly DispatcherTimer _deviceChangeTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private ScanReport? _lastScan;
    private bool _pendingScan;

    private AppPage _currentPage = AppPage.Dashboard;
    private bool _isSidebarExpanded = true;
    private bool _isBusy;
    private string _busyText = string.Empty;
    private bool _isReady;
    private bool _isStarting = true;
    private string? _startupError;
    private string _hostname = "—";
    private string _windowsVersion = string.Empty;
    private string _lastScanText = "Ainda não verificado";
    private string? _noticeText;
    private Severity _noticeSeverity;
    private bool _isNoticeVisible;
    private InventoryOverview _overview = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    private bool _showBuiltIn;
    private bool _autoScan = true;
    private string _scanSummary = string.Empty;
    private string _searchText = string.Empty;
    private Option<DeviceType?> _selectedCategory = InventoryFilter.Categories[0];
    private Option<StatusFilter> _selectedStatus = InventoryFilter.Statuses[0];
    private string _inventoryCountText = string.Empty;
    private DeviceRow? _selectedDevice;

    public MainViewModel(AppHost host)
    {
        _host = host;
        _noticeTimer.Tick += (_, _) =>
        {
            _noticeTimer.Stop();
            IsNoticeVisible = false;
        };
        _deviceChangeTimer.Tick += async (_, _) =>
        {
            _deviceChangeTimer.Stop();
            await ScanAsync(userInitiated: false);
        };

        DevicesView = CollectionViewSource.GetDefaultView(Devices);
        DevicesView.Filter = o => o is DeviceRow row &&
                                  InventoryFilter.Matches(row, SearchText, SelectedCategory.Value, SelectedStatus.Value);
        ScanView = CollectionViewSource.GetDefaultView(ScanRows);
        ScanView.Filter = o => o is ScanRow row && (ShowBuiltIn || !row.IsBuiltIn);

        bool Idle(object? _) => IsReady && !IsBusy;
        ScanCommand = new AsyncCommand(_ => ScanAsync(userInitiated: true), Idle);
        ExportCommand = new AsyncCommand(_ => ExportAsync(), Idle);
        RegisterCommand = new AsyncCommand(p => RegisterAsync(p as ScanRow), p => Idle(p) && p is ScanRow { IsRegistered: false });
        ShowInInventoryCommand = new RelayCommand(p => ShowInInventory((p as ScanRow)?.RegisteredId), p => p is ScanRow { IsRegistered: true });
        AssignCommand = new AsyncCommand(_ => AssignAsync(), p => Idle(p) && SelectedDevice is { HasActiveAssignment: false });
        TransferCommand = new AsyncCommand(_ => TransferAsync(), p => Idle(p) && SelectedDevice is { HasActiveAssignment: true });
        AddPersonCommand = new AsyncCommand(_ => AddPersonAsync(), Idle);
        CopyFingerprintCommand = new RelayCommand(_ => CopyFingerprint(), _ => SelectedDevice is not null);
        OpenDataFolderCommand = new RelayCommand(_ => OpenFolder(_host.Paths.RootDirectory));
        RetryCommand = new AsyncCommand(_ => StartAsync(), _ => !IsBusy && !IsReady);
        DismissNoticeCommand = new RelayCommand(_ => IsNoticeVisible = false);
        ClearFiltersCommand = new RelayCommand(_ => ClearFilters());
        NavigateCommand = new RelayCommand(p =>
        {
            if (p is string name && Enum.TryParse<AppPage>(name, out var page))
            {
                CurrentPage = page;
            }
        });
    }

    // ===== Shell =====

    public AppPage CurrentPage
    {
        get => _currentPage;
        set
        {
            if (Set(ref _currentPage, value))
            {
                OnPropertyChanged(nameof(PageTitle));
                OnPropertyChanged(nameof(PageSubtitle));
            }
        }
    }

    public string PageTitle => CurrentPage switch
    {
        AppPage.Dashboard => "Painel",
        AppPage.Scan => "Verificar dispositivos",
        AppPage.Inventory => "Inventário de dispositivos",
        AppPage.People => "Responsáveis",
        AppPage.Computers => "Computadores",
        AppPage.History => "Histórico",
        _ => "Sobre e privacidade"
    };

    public string PageSubtitle => CurrentPage switch
    {
        AppPage.Dashboard => "Visão geral dos periféricos deste computador e do inventário.",
        AppPage.Scan => "Periféricos detectados agora pelo Windows. Os novos podem ser cadastrados no inventário.",
        AppPage.Inventory => "Periféricos cadastrados, com identificação técnica, responsável e última detecção.",
        AppPage.People => "Pessoas que podem receber dispositivos.",
        AppPage.Computers => "Computadores em que o aplicativo já foi executado com este banco de dados.",
        AppPage.History => "Atribuições, transferências e mudanças detectadas nesta sessão.",
        _ => "Como o aplicativo funciona e quais dados ele armazena."
    };

    /// <summary>False on narrow windows: the sidebar shows icons only.</summary>
    public bool IsSidebarExpanded
    {
        get => _isSidebarExpanded;
        set => Set(ref _isSidebarExpanded, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => Set(ref _isBusy, value);
    }

    public string BusyText
    {
        get => _busyText;
        private set => Set(ref _busyText, value);
    }

    public bool IsReady
    {
        get => _isReady;
        private set => Set(ref _isReady, value);
    }

    public bool IsStarting
    {
        get => _isStarting;
        private set => Set(ref _isStarting, value);
    }

    public string? StartupError
    {
        get => _startupError;
        private set
        {
            if (Set(ref _startupError, value))
            {
                OnPropertyChanged(nameof(HasStartupError));
            }
        }
    }

    public bool HasStartupError => StartupError is not null;

    public string Hostname
    {
        get => _hostname;
        private set => Set(ref _hostname, value);
    }

    public string WindowsVersion
    {
        get => _windowsVersion;
        private set => Set(ref _windowsVersion, value);
    }

    public string LastScanText
    {
        get => _lastScanText;
        private set => Set(ref _lastScanText, value);
    }

    public string DataDirectory => _host.Paths.RootDirectory;
    public string DatabasePath => _host.Paths.DatabasePath;
    public string LogsDirectory => _host.Paths.LogsDirectory;
    public string VersionText => $"Versão {AppInfo.Version}";
    public bool IsSimulation => _host.Options.IsSimulation;

    public string SimulationText =>
        "Modo de demonstração: " +
        (_host.Options.FixturePath is not null ? "dispositivos lidos de um arquivo de exemplo" : "dispositivos reais") +
        (_host.Options.SimulatedHost is not null ? $" e computador simulado \"{_host.Options.SimulatedHost}\"." : ".");

    public string? NoticeText
    {
        get => _noticeText;
        private set => Set(ref _noticeText, value);
    }

    public Severity NoticeSeverity
    {
        get => _noticeSeverity;
        private set => Set(ref _noticeSeverity, value);
    }

    public bool IsNoticeVisible
    {
        get => _isNoticeVisible;
        private set => Set(ref _isNoticeVisible, value);
    }

    // ===== Dashboard =====

    public InventoryOverview Overview
    {
        get => _overview;
        private set => Set(ref _overview, value);
    }

    public ObservableCollection<AlertItem> Alerts { get; } = [];
    public ObservableCollection<ActivityItem> Activity { get; } = [];
    public ObservableCollection<CategoryItem> Categories { get; } = [];

    // ===== Scan =====

    public ObservableCollection<ScanRow> ScanRows { get; } = [];
    public ICollectionView ScanView { get; }

    public bool ShowBuiltIn
    {
        get => _showBuiltIn;
        set
        {
            if (Set(ref _showBuiltIn, value))
            {
                ScanView.Refresh();
                UpdateScanSummary();
            }
        }
    }

    public bool AutoScan
    {
        get => _autoScan;
        set => Set(ref _autoScan, value);
    }

    public string ScanSummary
    {
        get => _scanSummary;
        private set => Set(ref _scanSummary, value);
    }

    // ===== Inventory =====

    public ObservableCollection<DeviceRow> Devices { get; } = [];
    public ICollectionView DevicesView { get; }
    public ObservableCollection<HistoryRow> SelectedHistory { get; } = [];
    public IReadOnlyList<Option<DeviceType?>> CategoryOptions => InventoryFilter.Categories;
    public IReadOnlyList<Option<StatusFilter>> StatusOptions => InventoryFilter.Statuses;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value))
            {
                RefreshInventoryView();
            }
        }
    }

    public Option<DeviceType?> SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (Set(ref _selectedCategory, value ?? InventoryFilter.Categories[0]))
            {
                RefreshInventoryView();
            }
        }
    }

    public Option<StatusFilter> SelectedStatus
    {
        get => _selectedStatus;
        set
        {
            if (Set(ref _selectedStatus, value ?? InventoryFilter.Statuses[0]))
            {
                RefreshInventoryView();
            }
        }
    }

    public string InventoryCountText
    {
        get => _inventoryCountText;
        private set => Set(ref _inventoryCountText, value);
    }

    public DeviceRow? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (Set(ref _selectedDevice, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                CommandManager.InvalidateRequerySuggested();
                _ = LoadHistoryAsync(value);
            }
        }
    }

    public bool HasSelection => SelectedDevice is not null;

    // ===== People / computers / movements =====

    public ObservableCollection<PersonRow> People { get; } = [];
    public ObservableCollection<ComputerRow> Computers { get; } = [];
    public ObservableCollection<MovementRow> Movements { get; } = [];

    // ===== Commands =====

    public ICommand NavigateCommand { get; }
    public ICommand ScanCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand RegisterCommand { get; }
    public ICommand ShowInInventoryCommand { get; }
    public ICommand AssignCommand { get; }
    public ICommand TransferCommand { get; }
    public ICommand AddPersonCommand { get; }
    public ICommand CopyFingerprintCommand { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand RetryCommand { get; }
    public ICommand DismissNoticeCommand { get; }
    public ICommand ClearFiltersCommand { get; }

    // ===== Startup and scan =====

    public async Task StartAsync()
    {
        StartupError = null;
        IsStarting = true;
        SetBusy("Preparando o inventário…");
        try
        {
            await _host.InitializeAsync();
            Hostname = _host.Hostname;
            // A simulated computer is not this machine: do not show this machine's real Windows version.
            WindowsVersion = _host.Options.SimulatedHost is not null
                ? DemoWindowsVersion
                : _host.WindowsVersion ?? "Versão do Windows indisponível";
            IsReady = true;
        }
        catch (Exception ex)
        {
            _host.Logger.LogError(ex, "Desktop startup failed");
            StartupError = ex is AssetTrackerException or DomainException
                ? ex.Message
                : $"Não foi possível abrir o banco de dados local em \"{_host.Paths.DataDirectory}\". " +
                  $"Os detalhes técnicos foram registrados em \"{_host.Paths.LogsDirectory}\".";
            return;
        }
        finally
        {
            ClearBusy();
            IsStarting = false;
        }

        await ScanAsync(userInitiated: false);
    }

    /// <summary>Called by the window when Windows reports that devices were added or removed.</summary>
    public void OnDevicesChanged()
    {
        if (!AutoScan || !IsReady)
        {
            return;
        }

        // Plugging one device fires several notifications: wait until they settle.
        _deviceChangeTimer.Stop();
        _deviceChangeTimer.Start();
    }

    private async Task ScanAsync(bool userInitiated)
    {
        if (!IsReady)
        {
            return;
        }

        if (IsBusy)
        {
            _pendingScan = true;
            return;
        }

        SetBusy("Verificando dispositivos…");
        try
        {
            // Built-in parts are always scanned so a registered laptop keyboard is never reported as disconnected;
            // the "show built-in" switch only filters the list.
            var report = await _host.Run(sp => sp.GetRequiredService<ScanService>().ScanAsync(_host.ComputerId, includeBuiltIn: true));
            var previous = _lastScan;
            _lastScan = report;
            RecordChanges(previous, report);
            await ReloadCoreAsync();

            if (userInitiated)
            {
                Notify(Severity.Ok, $"Verificação concluída: {Labels.Count(Peripherals(report), "periférico detectado", "periféricos detectados")}.");
            }
        }
        catch (Exception ex)
        {
            HandleError(ex, "Não foi possível verificar os dispositivos.");
        }
        finally
        {
            ClearBusy();
        }

        if (_pendingScan)
        {
            _pendingScan = false;
            _deviceChangeTimer.Start();
        }
    }

    private void RecordChanges(ScanReport? previous, ScanReport current)
    {
        if (previous is null)
        {
            AddActivity(Severity.Info, $"Verificação inicial: {Labels.Count(Peripherals(current), "periférico detectado", "periféricos detectados")}.");
            return;
        }

        foreach (var change in InventoryOverviewCalculator.CompareScans(previous, current))
        {
            var row = new ScanRow(change.Item);
            AddActivity(
                change.Connected ? Severity.Ok : Severity.Warning,
                change.Connected
                    ? $"Conectado: {row.Name} ({(row.IsRegistered ? "conhecido" : "novo, não cadastrado")})"
                    : $"Desconectado: {row.Name}");
        }
    }

    private static int Peripherals(ScanReport report) => report.Items.Count(i => !i.Discovered.IsBuiltIn);

    private async Task ReloadAsync()
    {
        SetBusy("Atualizando…");
        try
        {
            await ReloadCoreAsync();
        }
        catch (Exception ex)
        {
            HandleError(ex, "Não foi possível carregar o inventário.");
        }
        finally
        {
            ClearBusy();
        }
    }

    private async Task ReloadCoreAsync()
    {
        var (summaries, people, computers, movements) = await _host.Run(async sp => (
            await sp.GetRequiredService<InventoryQueryService>().ListAsync(DeviceFilter.All),
            await sp.GetRequiredService<PeopleService>().ListAsync(),
            await sp.GetRequiredService<ComputerService>().ListAsync(),
            await sp.GetRequiredService<HistoryService>().GetRecentMovementsAsync(200)));

        var scanned = _lastScan is not null;
        var rows = summaries
            .Select(s => new DeviceRow(s, InventoryOverviewCalculator.EvaluatePresence(s, _lastScan, _host.ComputerId), scanned))
            .ToList();

        var selectedId = SelectedDevice?.Id;
        Replace(Devices, rows);
        RefreshInventoryView();
        SelectedDevice = Devices.FirstOrDefault(d => d.Id == selectedId);

        Overview = InventoryOverviewCalculator.Calculate(summaries, _lastScan, _host.ComputerId, DateTime.UtcNow);
        Replace(Alerts, AlertBuilder.Build(Overview, rows, _lastScan));
        var total = Math.Max(1, Overview.Total);
        Replace(Categories,
        [
            new CategoryItem("Mouse", "", Overview.Mice, 100.0 * Overview.Mice / total),
            new CategoryItem("Teclado", "", Overview.Keyboards, 100.0 * Overview.Keyboards / total),
            new CategoryItem("Outros", "", Overview.Others, 100.0 * Overview.Others / total)
        ]);

        Replace(ScanRows, _lastScan?.Items.Select(i => new ScanRow(i)) ?? []);
        UpdateScanSummary();
        Replace(People, people.Select(PersonRow.Create));
        Replace(Computers, computers
            .Select(c => ComputerRow.Create(c, _host.ComputerId))
            .Select(c => c.IsCurrent && _host.Options.SimulatedHost is not null ? c with { WindowsVersion = DemoWindowsVersion } : c));
        Replace(Movements, movements.Select(MovementRow.Create));
        LastScanText = _lastScan is null
            ? "Ainda não verificado"
            : $"Hoje às {Labels.Time(_lastScan.ScannedAt)}";
    }

    private void UpdateScanSummary()
    {
        if (_lastScan is null)
        {
            ScanSummary = "Nenhuma verificação realizada.";
            return;
        }

        var visible = ScanRows.Where(r => ShowBuiltIn || !r.IsBuiltIn).ToList();
        var hidden = ScanRows.Count - visible.Count;
        ScanSummary =
            $"{Labels.Count(visible.Count, "dispositivo detectado", "dispositivos detectados")} · " +
            $"{Labels.Count(visible.Count(r => r.IsRegistered), "conhecido", "conhecidos")} · " +
            $"{Labels.Count(visible.Count(r => !r.IsRegistered), "novo", "novos")}" +
            (hidden > 0 ? $" · {Labels.Count(hidden, "integrado oculto", "integrados ocultos")}" : string.Empty) +
            $" · verificado às {Labels.Time(_lastScan.ScannedAt)}";
    }

    // ===== Inventory =====

    private void RefreshInventoryView()
    {
        DevicesView.Refresh();
        var shown = DevicesView.Cast<object>().Count();
        InventoryCountText = shown == Devices.Count
            ? Labels.Count(Devices.Count, "dispositivo", "dispositivos")
            : $"{shown} de {Labels.Count(Devices.Count, "dispositivo", "dispositivos")}";
    }

    private void ClearFilters()
    {
        _searchText = string.Empty;
        _selectedCategory = InventoryFilter.Categories[0];
        _selectedStatus = InventoryFilter.Statuses[0];
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(SelectedCategory));
        OnPropertyChanged(nameof(SelectedStatus));
        RefreshInventoryView();
    }

    private void ShowInInventory(int? deviceId)
    {
        if (deviceId is null)
        {
            return;
        }

        ClearFilters();
        CurrentPage = AppPage.Inventory;
        SelectedDevice = Devices.FirstOrDefault(d => d.Id == deviceId);
    }

    private async Task LoadHistoryAsync(DeviceRow? row)
    {
        SelectedHistory.Clear();
        if (row is null)
        {
            return;
        }

        try
        {
            var history = await _host.Run(sp => sp.GetRequiredService<HistoryService>().GetDeviceHistoryAsync(row.Id));
            if (SelectedDevice?.Id != row.Id)
            {
                return;
            }

            Replace(SelectedHistory, history.Entries.Reverse().Select(HistoryRow.Create));
        }
        catch (Exception ex)
        {
            _host.Logger.LogWarning(ex, "Could not load the history of device {DeviceId}", row.Id);
        }
    }

    private async Task ExportAsync()
    {
        var rows = DevicesView.Cast<DeviceRow>().ToList();
        var dialog = new SaveFileDialog
        {
            Title = "Exportar relatório do inventário",
            Filter = "Planilha CSV (*.csv)|*.csv",
            FileName = $"inventario_{DateTime.Now:yyyy-MM-dd_HHmmss}.csv",
            InitialDirectory = _host.Paths.ReportsDirectory,
            AddExtension = true,
            DefaultExt = ".csv"
        };
        if (dialog.ShowDialog(System.Windows.Application.Current.MainWindow) != true)
        {
            return;
        }

        try
        {
            // The user's regional settings (not the UI language) decide the separator Excel expects.
            var csv = CsvReport.Build(rows, CsvReport.DelimiterFor(CultureInfo.CurrentCulture));
            await File.WriteAllTextAsync(dialog.FileName, csv, ReportFileWriter.Utf8WithBom);
            _host.Logger.LogInformation("Desktop inventory report exported with {Count} device(s)", rows.Count);
            Notify(Severity.Ok, $"Relatório exportado com {Labels.Count(rows.Count, "dispositivo", "dispositivos")}: {Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notify(Severity.Error, $"Não foi possível salvar o relatório: {ex.Message}");
        }
    }

    private async Task RegisterAsync(ScanRow? row)
    {
        if (row is null || row.IsRegistered)
        {
            return;
        }

        var dialog = new AssignmentDialog(AssignmentMode.Register, row.Name, People, Computers, CreatePersonAsync)
        {
            DeviceType = row.Item.Discovered.SuggestedType,
            ComputerId = _host.ComputerId,
            Details = $"Confiança da identificação: {row.ConfidenceLabel}. Base: {row.Basis}"
        };
        if (!ShowDialog(dialog))
        {
            return;
        }

        // Dialog values are read here, on the UI thread; the operation itself runs on a worker thread.
        var request = new RegisterDeviceRequest(dialog.DeviceType, dialog.FriendlyName, dialog.PersonId, dialog.ComputerId);
        var ok = await RunOperationAsync("Cadastrando dispositivo…", () => _host.Run(sp =>
            sp.GetRequiredService<DeviceRegistrationService>().RegisterAsync(row.Item.Discovered, request, _host.ComputerId)),
            "Dispositivo cadastrado no inventário.");
        if (ok)
        {
            AddActivity(Severity.Ok, $"Cadastrado: {request.FriendlyName}");
            await ScanAsync(userInitiated: false);
        }
    }

    private async Task AssignAsync()
    {
        var device = SelectedDevice;
        if (device is null)
        {
            return;
        }

        var dialog = new AssignmentDialog(AssignmentMode.Assign, device.Name, People, Computers, CreatePersonAsync)
        {
            ComputerId = device.Summary.LastSeenComputerId
        };
        if (!ShowDialog(dialog))
        {
            return;
        }

        var (personId, computerId) = (dialog.PersonId, dialog.ComputerId);
        if (await RunOperationAsync("Registrando atribuição…", () => _host.Run(sp =>
                sp.GetRequiredService<AssignmentService>().AssignAsync(device.Id, personId, computerId)),
                "Atribuição registrada."))
        {
            AddActivity(Severity.Info, $"Atribuído: {device.Name}");
            await ReloadAsync();
        }
    }

    private async Task TransferAsync()
    {
        var device = SelectedDevice;
        if (device is null)
        {
            return;
        }

        var dialog = new AssignmentDialog(AssignmentMode.Transfer, device.Name, People, Computers, CreatePersonAsync)
        {
            PersonId = device.Summary.ResponsiblePersonId,
            ComputerId = device.Summary.ExpectedComputerId,
            Details = $"Atual: {MovementRow.Describe(device.Summary.ResponsiblePersonName, device.Summary.ExpectedComputerHostname)}."
        };
        if (!ShowDialog(dialog))
        {
            return;
        }

        var request = new TransferRequest(device.Id, dialog.PersonId, dialog.ComputerId, dialog.Reason);
        if (await RunOperationAsync("Registrando transferência…", () => _host.Run(sp =>
                sp.GetRequiredService<AssignmentService>().TransferAsync(request)),
                "Transferência registrada."))
        {
            AddActivity(Severity.Info, $"Transferido: {device.Name}");
            await ReloadAsync();
        }
    }

    // ===== People =====

    private async Task AddPersonAsync()
    {
        if (await CreatePersonAsync(System.Windows.Application.Current.MainWindow) is { } person)
        {
            Notify(Severity.Ok, $"Responsável cadastrado: {person.Name}");
        }
    }

    /// <summary>Shows the person dialog and saves the person. Also used from the assignment dialog.</summary>
    private async Task<PersonRow?> CreatePersonAsync(Window owner)
    {
        var dialog = new PersonDialog { Owner = owner };
        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        try
        {
            var (name, department) = (dialog.PersonName, dialog.Department);
            var (createdId, all) = await _host.Run(async sp =>
            {
                var people = sp.GetRequiredService<PeopleService>();
                var person = await people.CreateAsync(name, department);
                return (person.Id, await people.ListAsync());
            });
            Replace(People, all.Select(PersonRow.Create));
            return People.FirstOrDefault(p => p.Id == createdId);
        }
        catch (Exception ex)
        {
            HandleError(ex, "Não foi possível cadastrar o responsável.");
            return null;
        }
    }

    // ===== Helpers =====

    private void CopyFingerprint()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        try
        {
            Clipboard.SetText(SelectedDevice.Fingerprint);
            Notify(Severity.Info, "Fingerprint copiado para a área de transferência.");
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            Notify(Severity.Warning, "A área de transferência está em uso por outro programa. Tente novamente.");
        }
    }

    private void OpenFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Notify(Severity.Error, $"Não foi possível abrir a pasta: {ex.Message}");
        }
    }

    private static bool ShowDialog(Window dialog)
    {
        dialog.Owner = System.Windows.Application.Current.MainWindow;
        return dialog.ShowDialog() == true;
    }

    private async Task<bool> RunOperationAsync(string busyText, Func<Task> operation, string success)
    {
        SetBusy(busyText);
        try
        {
            await operation();
            Notify(Severity.Ok, success);
            return true;
        }
        catch (Exception ex)
        {
            HandleError(ex, "A operação não pôde ser concluída.");
            return false;
        }
        finally
        {
            ClearBusy();
        }
    }

    private void HandleError(Exception ex, string fallback)
    {
        if (ex is AssetTrackerException or DomainException)
        {
            if (ex.InnerException is not null)
            {
                _host.Logger.LogWarning(ex, "Operation failed");
            }

            Notify(Severity.Error, ex.Message);
            return;
        }

        _host.Logger.LogError(ex, "Unexpected failure");
        Notify(Severity.Error, $"{fallback} Os detalhes técnicos foram registrados em \"{_host.Paths.LogsDirectory}\".");
    }

    private void Notify(Severity severity, string text)
    {
        NoticeSeverity = severity;
        NoticeText = text;
        IsNoticeVisible = true;
        _noticeTimer.Stop();
        _noticeTimer.Interval = TimeSpan.FromSeconds(severity == Severity.Error ? 10 : 5);
        _noticeTimer.Start();
    }

    private void AddActivity(Severity severity, string text)
    {
        Activity.Insert(0, new ActivityItem(Labels.Time(DateTime.UtcNow), text, severity));
        while (Activity.Count > MaxActivity)
        {
            Activity.RemoveAt(Activity.Count - 1);
        }
    }

    private void SetBusy(string text)
    {
        BusyText = text;
        IsBusy = true;
        CommandManager.InvalidateRequerySuggested();
    }

    private void ClearBusy()
    {
        IsBusy = false;
        CommandManager.InvalidateRequerySuggested();
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}
