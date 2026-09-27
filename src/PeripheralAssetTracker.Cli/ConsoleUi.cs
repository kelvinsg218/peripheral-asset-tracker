using System.Globalization;

namespace PeripheralAssetTracker.Cli;

/// <summary>Console input/output helpers. Works with redirected stdin (automation, tests).</summary>
internal sealed class ConsoleUi(TextReader input, TextWriter output)
{
    public const string Unavailable = "Indisponível";
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public TextWriter Out => output;

    public void Line(string text = "") => output.WriteLine(text);

    public void Title(string text)
    {
        var bar = new string('=', 56);
        output.WriteLine();
        output.WriteLine(bar);
        output.WriteLine(text);
        output.WriteLine(bar);
    }

    public void Section(string text)
    {
        output.WriteLine();
        output.WriteLine("--- " + text + " ---");
    }

    public void Field(string label, string? value, int width = 22) =>
        output.WriteLine((label + ":").PadRight(width) + " " + (string.IsNullOrWhiteSpace(value) ? Unavailable : value));

    public void Success(string text) => WriteColored("OK: " + text, ConsoleColor.Green);

    public void Warning(string text) => WriteColored("ATENÇÃO: " + text, ConsoleColor.Yellow);

    public void Error(string text) => WriteColored("ERRO: " + text, ConsoleColor.Red);

    public void Info(string text) => output.WriteLine(text);

    /// <summary>Reads a line; returns null at end of input (so automated runs never hang).</summary>
    public string? Ask(string prompt)
    {
        output.Write(prompt + " ");
        var line = input.ReadLine();
        if (line is null)
        {
            output.WriteLine();
            return null;
        }

        return line.Trim();
    }

    public bool Confirm(string prompt)
    {
        while (true)
        {
            var answer = Ask(prompt + " [S/N]");
            switch (answer?.ToUpperInvariant())
            {
                case null:
                    return false;
                case "S" or "SIM" or "Y" or "YES":
                    return true;
                case "N" or "NAO" or "NÃO" or "NO" or "":
                    return false;
            }
        }
    }

    public static string FormatDate(DateTime? utc) =>
        utc is null ? Unavailable : utc.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm", PtBr);

    public static string FormatDay(DateTime utc) => utc.ToLocalTime().ToString("dd/MM/yyyy", PtBr);

    public static string OrUnavailable(string? value) => string.IsNullOrWhiteSpace(value) ? Unavailable : value;

    /// <summary>Simple fixed-width table (truncates long cells).</summary>
    public void Table(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows, IReadOnlyList<int> maxWidths)
    {
        var data = rows.Select(r => r.Select(c => c ?? "-").ToList()).ToList();
        var widths = headers.Select((h, i) =>
            Math.Min(maxWidths[i], Math.Max(h.Length, data.Count == 0 ? 0 : data.Max(r => r[i].Length)))).ToList();

        output.WriteLine(string.Join("  ", headers.Select((h, i) => Cell(h, widths[i]))));
        output.WriteLine(string.Join("  ", widths.Select(w => new string('-', w))));
        foreach (var row in data)
        {
            output.WriteLine(string.Join("  ", row.Select((c, i) => Cell(c, widths[i]))).TrimEnd());
        }

        if (data.Count == 0)
        {
            output.WriteLine("(nenhum registro)");
        }
    }

    private static string Cell(string value, int width) =>
        value.Length > width ? value[..Math.Max(0, width - 1)] + "…" : value.PadRight(width);

    private void WriteColored(string text, ConsoleColor color)
    {
        var redirected = Console.IsOutputRedirected || !ReferenceEquals(output, Console.Out);
        if (!redirected)
        {
            Console.ForegroundColor = color;
        }

        output.WriteLine(text);
        if (!redirected)
        {
            Console.ResetColor();
        }
    }
}
