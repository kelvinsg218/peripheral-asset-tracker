using PeripheralAssetTracker.Cli;
using PeripheralAssetTracker.Infrastructure.Platform;

namespace PeripheralAssetTracker.Tests.Platform;

public class PlatformAndCliTests
{
    [Fact]
    public void Windows_11_is_detected_from_build_even_when_product_name_says_10() =>
        // Real registry values observed on the development machine.
        Assert.Equal("Windows 11 Home Single Language 25H2 (build 26200.9457)",
            WindowsMachineInfoProvider.FormatWindowsVersion("Windows 10 Home Single Language", "25H2", "26200", 9457));

    [Fact]
    public void Windows_10_is_kept_for_old_builds() =>
        Assert.Equal("Windows 10 Pro 22H2 (build 19045.1)",
            WindowsMachineInfoProvider.FormatWindowsVersion("Windows 10 Pro", "22H2", "19045", 1));

    [Fact]
    public void Missing_windows_data_is_unavailable() =>
        Assert.Null(WindowsMachineInfoProvider.FormatWindowsVersion(null, null, null, null));

    [Fact]
    public void Cli_arguments_are_parsed()
    {
        var args = CliArguments.Parse(["--data-dir", "x", "transfer", "12", "--person=3", "--reason", "Equipment relocation", "--yes"]);

        Assert.Equal("transfer", args.Command);
        Assert.Equal(["12"], args.Positionals);
        Assert.Equal("x", args.DataDirectory);
        Assert.Equal("3", args.Get("person"));
        Assert.Equal("Equipment relocation", args.Get("reason"));
        Assert.True(args.Has("yes"));
    }

    [Fact]
    public void Unknown_or_incomplete_options_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => CliArguments.Parse(["scan", "--bogus"]));
        Assert.Throws<ArgumentException>(() => CliArguments.Parse(["assign", "1", "--person"]));
    }

    [Theory]
    [InlineData(";", ';')]
    [InlineData(",", ',')]
    [InlineData("tab", '\t')]
    public void Export_delimiter_is_parsed(string input, char expected) =>
        Assert.Equal(expected, ExportDelimiter.Parse(input));
}
