using Microsoft.Extensions.DependencyInjection;
using PeripheralAssetTracker.Application.Models;
using PeripheralAssetTracker.Application.Services;
using PeripheralAssetTracker.Domain.Enums;

namespace PeripheralAssetTracker.Cli;

/// <summary>Interactive selection of people, computers, devices and device types.</summary>
internal sealed class Prompts(CliSession session)
{
    private ConsoleUi Ui => session.Ui;

    /// <summary>Marker returned when the user chose to keep the current value.</summary>
    public const int Keep = -1;

    public DeviceType? AskDeviceType(DeviceType suggested)
    {
        while (true)
        {
            var answer = Ui.Ask($"Tipo [M]ouse, [T]eclado, [O]utro (Enter = {Views.TypeLabel(suggested)}):");
            switch (answer?.ToUpperInvariant())
            {
                case null:
                    return null;
                case "":
                    return suggested;
                case "M" or "MOUSE":
                    return DeviceType.Mouse;
                case "T" or "K" or "TECLADO" or "KEYBOARD":
                    return DeviceType.Keyboard;
                case "O" or "OUTRO" or "OTHER":
                    return DeviceType.Other;
                default:
                    Ui.Warning("Opção inválida.");
                    break;
            }
        }
    }

    /// <summary>
    /// Asks for a responsible person: #id, part of the name, 'n' to create one, Enter for none/keep.
    /// Returns null for none, <see cref="Keep"/> for keep (when allowed), otherwise the person id.
    /// </summary>
    public async Task<int?> AskPersonAsync(string label, bool allowKeep = false)
    {
        var emptyMeaning = allowKeep ? "manter atual" : "nenhum";
        while (true)
        {
            var answer = Ui.Ask($"{label} (#id, parte do nome, 'n' = novo, Enter = {emptyMeaning}):");
            if (answer is null)
            {
                return allowKeep ? Keep : null;
            }

            if (answer.Length == 0)
            {
                return allowKeep ? Keep : null;
            }

            if (answer.Equals("n", StringComparison.OrdinalIgnoreCase))
            {
                var created = await CreatePersonAsync();
                if (created is not null)
                {
                    return created;
                }

                continue;
            }

            var people = await session.Use(sp => sp.GetRequiredService<PeopleService>().ListAsync());
            var trimmed = answer.TrimStart('#');
            if (int.TryParse(trimmed, out var id))
            {
                if (people.Any(p => p.Id == id))
                {
                    return id;
                }

                Ui.Warning($"Responsável #{id} não encontrado.");
                continue;
            }

            var matches = people.Where(p => TextSearch.Contains(p.Name, answer)).ToList();
            switch (matches.Count)
            {
                case 0:
                    Ui.Warning("Nenhum responsável encontrado. Use 'n' para cadastrar.");
                    break;
                case 1:
                    Ui.Info($"Selecionado: {matches[0].Name} (#{matches[0].Id})");
                    return matches[0].Id;
                default:
                    PeopleTable(matches);
                    Ui.Info("Mais de um resultado: informe o #id.");
                    break;
            }
        }
    }

    public async Task<int?> CreatePersonAsync()
    {
        var name = Ui.Ask("Nome do responsável:");
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var department = Ui.Ask("Setor (opcional):");
        var person = await session.Use(sp => sp.GetRequiredService<PeopleService>().CreateAsync(name, department));
        Ui.Success($"Responsável cadastrado: {person.Name} (#{person.Id})");
        return person.Id;
    }

    /// <summary>Asks for a computer by hostname ('.' = this computer). Same return convention as people.</summary>
    public async Task<int?> AskComputerAsync(string label, bool allowKeep = false)
    {
        var emptyMeaning = allowKeep ? "manter atual" : "nenhum";
        while (true)
        {
            var answer = Ui.Ask($"{label} (hostname, '.' = {session.CurrentHostname}, Enter = {emptyMeaning}):");
            if (string.IsNullOrEmpty(answer))
            {
                return allowKeep ? Keep : null;
            }

            if (answer == ".")
            {
                return session.CurrentComputerId;
            }

            var existing = await session.Use(sp => sp.GetRequiredService<ComputerService>().FindAsync(answer));
            if (existing is not null)
            {
                return existing.Id;
            }

            if (Ui.Confirm($"Computador '{answer.ToUpperInvariant()}' ainda não é conhecido. Cadastrar?"))
            {
                var created = await session.Use(sp => sp.GetRequiredService<ComputerService>().GetOrCreateAsync(answer));
                return created.Id;
            }
        }
    }

    public async Task<DeviceSummary?> AskDeviceAsync(string label, string emptyMeaning = "voltar")
    {
        var answer = Ui.Ask($"{label} (#id ou prefixo do fingerprint, Enter = {emptyMeaning}):");
        if (string.IsNullOrEmpty(answer))
        {
            return null;
        }

        return await session.Use(sp => sp.GetRequiredService<InventoryQueryService>().ResolveAsync(answer));
    }

    public void PeopleTable(IReadOnlyList<PersonSummary> people) =>
        Ui.Table(
            ["#", "Nome", "Setor", "Dispositivos ativos", "Cadastrado em"],
            people.Select(p => (IReadOnlyList<string?>)
                [p.Id.ToString(), p.Name, p.Department, p.ActiveDeviceCount.ToString(), ConsoleUi.FormatDate(p.CreatedAt)]),
            [5, 40, 25, 19, 16]);
}
