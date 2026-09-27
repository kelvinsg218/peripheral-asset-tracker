using Microsoft.Extensions.DependencyInjection;
using PeripheralAssetTracker.Application;
using PeripheralAssetTracker.Application.Abstractions;
using PeripheralAssetTracker.Application.Models;
using PeripheralAssetTracker.Application.Services;
using PeripheralAssetTracker.Domain.Enums;
using PeripheralAssetTracker.Infrastructure.Discovery;

namespace PeripheralAssetTracker.Cli;

/// <summary>Direct (non-interactive) commands for automation and tests.</summary>
internal sealed class CommandRunner(CliSession session, CliArguments args)
{
    private readonly Workflows _flows = new(session);
    private ConsoleUi Ui => session.Ui;

    public Task<int> RunAsync() => args.Command switch
    {
        "scan" => ScanAsync(),
        "register" => RegisterAsync(),
        "devices" => DevicesAsync(),
        "device" => DeviceAsync(),
        "search" => SearchAsync(),
        "people" => PeopleAsync(),
        "assign" => AssignAsync(),
        "transfer" => TransferAsync(),
        "computers" => Done(_flows.ShowComputersAsync()),
        "history" => HistoryAsync(),
        "export" => Done(_flows.ExportAsync(ExportDelimiter.Parse(args.Get("delimiter")))),
        "discover" => DiscoverAsync(),
        _ => throw new ArgumentException($"Comando desconhecido: '{args.Command}'. Use 'pat help'.")
    };

    private static async Task<int> Done(Task task)
    {
        await task;
        return 0;
    }

    private async Task<int> ScanAsync()
    {
        await _flows.ScanAsync(args.Has("all"), args.Has("details"));
        return 0;
    }

    /// <summary>register &lt;scan index | fingerprint prefix&gt; [--type] [--name] [--person] [--computer | --expected-current]</summary>
    private async Task<int> RegisterAsync()
    {
        var reference = Positional(0, "Informe o índice do scan ou o prefixo do fingerprint.");
        var report = await session.Use(sp => sp.GetRequiredService<ScanService>().ScanAsync(session.CurrentComputerId, args.Has("all")));

        ScanItem item;
        if (int.TryParse(reference, out var index) && index >= 1 && index <= report.Items.Count)
        {
            item = report.Items[index - 1];
        }
        else
        {
            var matches = report.Items
                .Where(i => i.Fingerprint.Fingerprint.StartsWith(reference.ToUpperInvariant(), StringComparison.Ordinal))
                .ToList();
            item = matches.Count == 1
                ? matches[0]
                : throw new AssetTrackerException(matches.Count == 0
                    ? "Nenhum dispositivo conectado corresponde a essa referência."
                    : "A referência corresponde a mais de um dispositivo conectado.");
        }

        if (item.IsRegistered)
        {
            throw new AssetTrackerException($"Este dispositivo já está cadastrado (#{item.Registered!.Id}).");
        }

        var type = args.Get("type") is { } t ? ParseType(t) : item.Discovered.SuggestedType;
        var personId = await PersonOptionAsync();
        var computerId = args.Has("expected-current") ? session.CurrentComputerId : await ComputerOptionAsync();

        var device = await session.Use(sp => sp.GetRequiredService<DeviceRegistrationService>().RegisterAsync(
            item.Discovered, new RegisterDeviceRequest(type, args.Get("name"), personId, computerId), session.CurrentComputerId));
        Ui.Success($"Dispositivo cadastrado como #{device.Id} ({device.DeviceName}) — confiança {Views.Confidence(device.FingerprintConfidence)}" +
                   (device.ActiveAssignment is null ? " — UNASSIGNED" : string.Empty));
        return 0;
    }

    private async Task<int> DevicesAsync()
    {
        var filter = new DeviceFilter
        {
            Type = args.Get("type") is { } t ? ParseType(t) : null,
            ResponsiblePersonId = await PersonOptionAsync(),
            ComputerId = await ComputerOptionAsync(createIfMissing: false),
            OnlyUnassigned = args.Has("unassigned"),
            OnlyLowConfidence = args.Has("low"),
            OnlyUnexpectedComputer = args.Has("unexpected")
        };
        var devices = await session.Use(sp => sp.GetRequiredService<InventoryQueryService>().ListAsync(filter));
        Views.DeviceTable(Ui, devices);
        return 0;
    }

    private async Task<int> DeviceAsync()
    {
        var device = await session.Use(sp => sp.GetRequiredService<InventoryQueryService>()
            .ResolveAsync(Positional(0, "Informe o #id ou o prefixo do fingerprint.")));
        Views.DeviceDetails(Ui, device);
        return 0;
    }

    private async Task<int> SearchAsync()
    {
        var term = string.Join(' ', args.Positionals);
        if (string.IsNullOrWhiteSpace(term))
        {
            throw new AssetTrackerException("Informe o termo de pesquisa.");
        }

        var results = await session.Use(sp => sp.GetRequiredService<InventoryQueryService>().SearchAsync(term));
        Views.DeviceTable(Ui, results);
        return 0;
    }

    /// <summary>people [list] | people add &lt;name&gt; [--department] | people search &lt;term&gt; | people devices &lt;id&gt;</summary>
    private async Task<int> PeopleAsync()
    {
        var sub = args.Positionals.FirstOrDefault()?.ToLowerInvariant() ?? "list";
        var rest = string.Join(' ', args.Positionals.Skip(1));
        switch (sub)
        {
            case "list":
                _flows.Prompts.PeopleTable(await session.Use(sp => sp.GetRequiredService<PeopleService>().ListAsync()));
                return 0;
            case "add":
                var person = await session.Use(sp => sp.GetRequiredService<PeopleService>().CreateAsync(rest, args.Get("department")));
                Ui.Success($"Responsável cadastrado: {person.Name} (#{person.Id})");
                return 0;
            case "search":
                _flows.Prompts.PeopleTable(await session.Use(sp => sp.GetRequiredService<PeopleService>().ListAsync(rest)));
                return 0;
            case "devices":
                if (!int.TryParse(rest.TrimStart('#'), out var personId))
                {
                    throw new AssetTrackerException("Informe o #id do responsável.");
                }

                var devices = await session.Use(sp => sp.GetRequiredService<InventoryQueryService>()
                    .ListAsync(new DeviceFilter { ResponsiblePersonId = personId }));
                Views.DeviceTable(Ui, devices);
                return 0;
            default:
                throw new AssetTrackerException("Uso: people [list | add <nome> | search <termo> | devices <id>]");
        }
    }

    private async Task<int> AssignAsync()
    {
        var device = await ResolveDeviceAsync();
        var personId = await PersonOptionAsync();
        var computerId = await ComputerOptionAsync();
        await session.Use(sp => sp.GetRequiredService<AssignmentService>().AssignAsync(device.Id, personId, computerId));
        Ui.Success($"Dispositivo #{device.Id} atribuído.");
        return 0;
    }

    /// <summary>transfer &lt;device&gt; [--person id] [--computer host] --reason "..." [--yes]; omitted values are kept.</summary>
    private async Task<int> TransferAsync()
    {
        var device = await ResolveDeviceAsync();
        var newPerson = args.Has("person") ? await PersonOptionAsync() : device.ResponsiblePersonId;
        var newComputer = args.Has("computer") ? await ComputerOptionAsync() : device.ExpectedComputerId;
        var reason = args.Get("reason") ?? throw new AssetTrackerException("Informe o motivo com --reason \"...\".");

        Ui.Field("Device", $"{device.DeviceName} (#{device.Id})");
        Ui.Field("Current responsible", device.ResponsiblePersonName ?? "Nenhum");
        Ui.Field("Current computer", device.ExpectedComputerHostname ?? "Nenhum");
        var newPersonName = newPerson is null
            ? "Nenhum"
            : (await session.Use(sp => sp.GetRequiredService<PeopleService>().FindAsync(newPerson.Value)))?.Name;
        var newHostname = newComputer is null
            ? "Nenhum"
            : (await session.Use(sp => sp.GetRequiredService<ComputerService>().FindAsync(newComputer.Value.ToString())))?.Hostname;
        Ui.Field("New responsible", newPersonName);
        Ui.Field("New computer", newHostname);
        Ui.Field("Reason", reason);
        if (!args.Has("yes") && !Ui.Confirm("Confirm?"))
        {
            Ui.Info("Transferência cancelada.");
            return 1;
        }

        await session.Use(sp => sp.GetRequiredService<AssignmentService>()
            .TransferAsync(new TransferRequest(device.Id, newPerson, newComputer, reason)));
        Ui.Success("Transferência registrada.");
        return 0;
    }

    private async Task<int> HistoryAsync()
    {
        if (args.Positionals.Count == 0)
        {
            await _flows.ShowRecentMovementsAsync();
            return 0;
        }

        var device = await ResolveDeviceAsync();
        await _flows.ShowHistoryAsync(device.Id);
        return 0;
    }

    /// <summary>Diagnostics: grouped devices with all technical data; --dump saves the raw PnP snapshot.</summary>
    private async Task<int> DiscoverAsync()
    {
        if (args.Get("dump") is { } dumpPath)
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new AssetTrackerException("A captura de nós PnP só é suportada no Windows.");
            }

            var nodes = new CfgMgrPnpNodeSource().GetPresentNodes();
            var relevant = SelectRelevantNodes(nodes);
            var snapshot = new PnpSnapshot
            {
                CapturedAt = DateTimeOffset.Now,
                Description = $"PnP snapshot (HID/Mouse/Keyboard nodes and ancestors) from {session.CurrentHostname}",
                Nodes = relevant
            };
            await File.WriteAllTextAsync(dumpPath, snapshot.ToJson());
            Ui.Success($"{relevant.Count} nós PnP salvos em {Path.GetFullPath(dumpPath)} (contém seriais: trate como dado de inventário).");
            return 0;
        }

        var result = await session.Use(sp => sp.GetRequiredService<IDeviceDiscoveryService>().DiscoverAsync());
        var fingerprints = new Application.Fingerprinting.DeviceFingerprintService();
        Ui.Title($"DISCOVERY — {result.RawEntryCount} entradas HID/input → {result.Devices.Count} dispositivo(s) físico(s)");
        var i = 0;
        foreach (var device in result.Devices.Where(d => args.Has("all") || !d.IsBuiltIn))
        {
            var fp = fingerprints.Compute(device);
            var conditions = Domain.Services.DeviceStatusEvaluator.EvaluateUnregistered(fp.Confidence);
            Views.ScanItem(Ui, ++i, new ScanItem(device, fp, null, conditions & ~DeviceConditions.NotRegistered, 1), details: true);
        }

        return 0;
    }

    private static List<PnpNode> SelectRelevantNodes(IReadOnlyList<PnpNode> nodes)
    {
        var byId = nodes.GroupBy(n => n.InstanceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var selected = new Dictionary<string, PnpNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes.Where(HidDeviceGrouper.IsFunctionNode))
        {
            var current = node;
            for (var depth = 0; current is not null && depth < 8; depth++)
            {
                selected.TryAdd(current.InstanceId, current);
                current = current.ParentInstanceId is not null && byId.TryGetValue(current.ParentInstanceId, out var parent) ? parent : null;
            }
        }

        return selected.Values.OrderBy(n => n.InstanceId, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<DeviceSummary> ResolveDeviceAsync() =>
        await session.Use(sp => sp.GetRequiredService<InventoryQueryService>()
            .ResolveAsync(Positional(0, "Informe o #id ou o prefixo do fingerprint do dispositivo.")));

    private async Task<int?> PersonOptionAsync()
    {
        if (args.Get("person") is not { } value)
        {
            return null;
        }

        if (!int.TryParse(value.TrimStart('#'), out var id))
        {
            throw new AssetTrackerException("--person deve ser o #id do responsável (veja 'pat people').");
        }

        _ = await session.Use(sp => sp.GetRequiredService<PeopleService>().FindAsync(id))
            ?? throw new NotFoundException($"Responsável #{id} não encontrado.");
        return id;
    }

    private async Task<int?> ComputerOptionAsync(bool createIfMissing = true)
    {
        if (args.Get("computer") is not { } value)
        {
            return null;
        }

        if (value == ".")
        {
            return session.CurrentComputerId;
        }

        var computer = createIfMissing
            ? await session.Use(sp => sp.GetRequiredService<ComputerService>().GetOrCreateAsync(value))
            : await session.Use(sp => sp.GetRequiredService<ComputerService>().FindAsync(value))
              ?? throw new NotFoundException($"Computador '{value}' não encontrado.");
        return computer.Id;
    }

    private string Positional(int index, string error) =>
        args.Positionals.Count > index ? args.Positionals[index] : throw new AssetTrackerException(error);

    private static DeviceType ParseType(string value) => value.ToLowerInvariant() switch
    {
        "mouse" or "m" => DeviceType.Mouse,
        "keyboard" or "teclado" or "k" or "t" => DeviceType.Keyboard,
        "other" or "outro" or "o" => DeviceType.Other,
        _ => throw new AssetTrackerException("Tipo inválido. Use mouse, keyboard ou other.")
    };
}
