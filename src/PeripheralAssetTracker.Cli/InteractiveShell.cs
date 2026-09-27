using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PeripheralAssetTracker.Application;
using PeripheralAssetTracker.Application.Models;
using PeripheralAssetTracker.Application.Services;
using PeripheralAssetTracker.Domain;
using PeripheralAssetTracker.Domain.Enums;

namespace PeripheralAssetTracker.Cli;

internal sealed class InteractiveShell(CliSession session, ILogger logger)
{
    private readonly Workflows _flows = new(session);
    private ConsoleUi Ui => session.Ui;

    public async Task<int> RunAsync()
    {
        while (true)
        {
            Ui.Title("PERIPHERAL ASSET TRACKER");
            Ui.Line($"Machine: {session.CurrentHostname}");
            Ui.Line();
            Ui.Line("[1] Scan connected devices   (escanear dispositivos conectados)");
            Ui.Line("[2] Registered devices       (dispositivos cadastrados)");
            Ui.Line("[3] Search                   (pesquisar)");
            Ui.Line("[4] Assign device            (atribuir dispositivo)");
            Ui.Line("[5] Transfer device          (transferir dispositivo)");
            Ui.Line("[6] Computers                (computadores)");
            Ui.Line("[7] Responsible people       (responsáveis)");
            Ui.Line("[8] Movement history         (histórico)");
            Ui.Line("[9] Export inventory         (exportar CSV)");
            Ui.Line("[0] Exit                     (sair)");

            var choice = Ui.Ask(">");
            if (choice is null or "0")
            {
                return 0;
            }

            await SafeRunAsync(choice switch
            {
                "1" => ScanMenuAsync,
                "2" => DevicesMenuAsync,
                "3" => SearchAsync,
                "4" => _flows.AssignInteractiveAsync,
                "5" => _flows.TransferInteractiveAsync,
                "6" => _flows.ShowComputersAsync,
                "7" => PeopleMenuAsync,
                "8" => HistoryMenuAsync,
                "9" => () => _flows.ExportAsync(ExportDelimiter.Default),
                _ => () =>
                {
                    Ui.Warning("Opção inválida.");
                    return Task.CompletedTask;
                }
            });
        }
    }

    /// <summary>Expected errors become friendly messages; unexpected ones are logged and summarized.</summary>
    private async Task SafeRunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (AssetTrackerException ex)
        {
            Ui.Error(ex.Message);
        }
        catch (DomainException ex)
        {
            Ui.Error(ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected failure in interactive operation");
            Ui.Error($"Ocorreu um erro inesperado. Os detalhes técnicos foram registrados em '{session.Paths.LogsDirectory}'.");
        }
    }

    private async Task ScanMenuAsync()
    {
        var includeBuiltIn = false;
        var details = false;
        while (true)
        {
            var report = await _flows.ScanAsync(includeBuiltIn, details);
            Ui.Line();
            Ui.Line("[nº] cadastrar dispositivo NOT REGISTERED  [A] incluir/ocultar integrados  [D] detalhes técnicos  [Enter] voltar");
            var answer = Ui.Ask(">");
            if (string.IsNullOrEmpty(answer))
            {
                return;
            }

            if (answer.Equals("A", StringComparison.OrdinalIgnoreCase))
            {
                includeBuiltIn = !includeBuiltIn;
                continue;
            }

            if (answer.Equals("D", StringComparison.OrdinalIgnoreCase))
            {
                details = !details;
                continue;
            }

            if (int.TryParse(answer, out var index) && index >= 1 && index <= report.Items.Count)
            {
                await SafeRunAsync(() => _flows.RegisterInteractiveAsync(report.Items[index - 1]));
                continue;
            }

            Ui.Warning("Opção inválida.");
        }
    }

    private async Task DevicesMenuAsync()
    {
        Ui.Section("Filtros");
        Ui.Line("[1] Todos  [2] Mouse  [3] Keyboard  [4] Other  [5] Por responsável  [6] Por computador");
        Ui.Line("[7] Sem responsável  [8] Fingerprint LOW  [9] Unexpected computer");
        var choice = Ui.Ask("Filtro (Enter = todos):");
        if (choice is null)
        {
            return;
        }

        DeviceFilter filter;
        switch (choice)
        {
            case "" or "1":
                filter = DeviceFilter.All;
                break;
            case "2":
                filter = new DeviceFilter { Type = DeviceType.Mouse };
                break;
            case "3":
                filter = new DeviceFilter { Type = DeviceType.Keyboard };
                break;
            case "4":
                filter = new DeviceFilter { Type = DeviceType.Other };
                break;
            case "5":
                var person = await _flows.Prompts.AskPersonAsync("Responsável");
                if (person is null)
                {
                    return;
                }

                filter = new DeviceFilter { ResponsiblePersonId = person };
                break;
            case "6":
                var computer = await _flows.Prompts.AskComputerAsync("Computador");
                if (computer is null)
                {
                    return;
                }

                filter = new DeviceFilter { ComputerId = computer };
                break;
            case "7":
                filter = new DeviceFilter { OnlyUnassigned = true };
                break;
            case "8":
                filter = new DeviceFilter { OnlyLowConfidence = true };
                break;
            case "9":
                filter = new DeviceFilter { OnlyUnexpectedComputer = true };
                break;
            default:
                Ui.Warning("Filtro inválido.");
                return;
        }

        var devices = await session.Use(sp => sp.GetRequiredService<InventoryQueryService>().ListAsync(filter));
        Ui.Section("Dispositivos cadastrados");
        Views.DeviceTable(Ui, devices);
        await OfferDetailsAsync();
    }

    private async Task SearchAsync()
    {
        var term = Ui.Ask("Pesquisar (fingerprint, nome, responsável, hostname, VID, PID, serial, fabricante):");
        if (string.IsNullOrWhiteSpace(term))
        {
            return;
        }

        var results = await session.Use(sp => sp.GetRequiredService<InventoryQueryService>().SearchAsync(term));
        Ui.Section($"Resultados para \"{term}\"");
        Views.DeviceTable(Ui, results);
        await OfferDetailsAsync();
    }

    private async Task OfferDetailsAsync()
    {
        var device = await _flows.Prompts.AskDeviceAsync("Ver detalhes");
        if (device is not null)
        {
            Views.DeviceDetails(Ui, device);
        }
    }

    private async Task PeopleMenuAsync()
    {
        Ui.Section("Responsáveis");
        Ui.Line("[1] Listar  [2] Cadastrar  [3] Pesquisar  [4] Dispositivos de um responsável");
        switch (Ui.Ask(">"))
        {
            case "1":
                _flows.Prompts.PeopleTable(await session.Use(sp => sp.GetRequiredService<PeopleService>().ListAsync()));
                break;
            case "2":
                await _flows.Prompts.CreatePersonAsync();
                break;
            case "3":
                var term = Ui.Ask("Nome ou setor:");
                if (!string.IsNullOrWhiteSpace(term))
                {
                    _flows.Prompts.PeopleTable(await session.Use(sp => sp.GetRequiredService<PeopleService>().ListAsync(term)));
                }

                break;
            case "4":
                var personId = await _flows.Prompts.AskPersonAsync("Responsável");
                if (personId is not null)
                {
                    var devices = await session.Use(sp => sp.GetRequiredService<InventoryQueryService>()
                        .ListAsync(new DeviceFilter { ResponsiblePersonId = personId }));
                    Views.DeviceTable(Ui, devices);
                }

                break;
        }
    }

    private async Task HistoryMenuAsync()
    {
        var device = await _flows.Prompts.AskDeviceAsync("Histórico do dispositivo", "movimentações recentes");
        if (device is null)
        {
            await _flows.ShowRecentMovementsAsync();
            return;
        }

        await _flows.ShowHistoryAsync(device.Id);
    }
}
