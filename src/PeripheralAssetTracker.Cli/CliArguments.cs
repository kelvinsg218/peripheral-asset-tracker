namespace PeripheralAssetTracker.Cli;

/// <summary>
/// Minimal argument parser: <c>pat [global options] [command] [positionals] [--options]</c>.
/// </summary>
internal sealed class CliArguments
{
    private static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase)
    {
        "all", "details", "unassigned", "low", "unexpected", "yes", "expected-current", "help"
    };

    private static readonly HashSet<string> ValueOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "data-dir", "fixture", "host", "type", "name", "person", "computer", "reason", "department",
        "delimiter", "dump"
    };

    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);

    public string? Command { get; private set; }
    public List<string> Positionals { get; } = [];

    public string? DataDirectory => Get("data-dir");
    public string? FixturePath => Get("fixture");
    public string? SimulatedHost => Get("host");

    public bool Has(string name) => _options.ContainsKey(name);

    public string? Get(string name) => _options.GetValueOrDefault(name);

    public static CliArguments Parse(IReadOnlyList<string> args)
    {
        var result = new CliArguments();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg is "-h" or "/?")
            {
                result._options["help"] = null;
                continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                var name = arg[2..];
                string? value = null;
                var equals = name.IndexOf('=');
                if (equals > 0)
                {
                    value = name[(equals + 1)..];
                    name = name[..equals];
                }

                if (Flags.Contains(name))
                {
                    result._options[name] = null;
                    continue;
                }

                if (!ValueOptions.Contains(name))
                {
                    throw new ArgumentException($"Opção desconhecida: --{name}");
                }

                if (value is null)
                {
                    if (i + 1 >= args.Count)
                    {
                        throw new ArgumentException($"A opção --{name} exige um valor.");
                    }

                    value = args[++i];
                }

                result._options[name] = value;
                continue;
            }

            if (result.Command is null)
            {
                result.Command = arg.ToLowerInvariant();
            }
            else
            {
                result.Positionals.Add(arg);
            }
        }

        return result;
    }
}
