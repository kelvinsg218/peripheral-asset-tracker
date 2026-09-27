using System.Net;
using System.Runtime.Versioning;
using Microsoft.Win32;
using PeripheralAssetTracker.Application.Abstractions;

namespace PeripheralAssetTracker.Infrastructure.Platform;

/// <summary>Real host name and Windows version of the current machine.</summary>
public sealed class WindowsMachineInfoProvider : IMachineInfoProvider
{
    /// <summary>First build number of Windows 11.</summary>
    public const int FirstWindows11Build = 22000;

    public string GetHostname()
    {
        try
        {
            var dnsName = Dns.GetHostName();
            if (!string.IsNullOrWhiteSpace(dnsName))
            {
                return dnsName;
            }
        }
        catch (System.Net.Sockets.SocketException)
        {
            // Fall back to the NetBIOS name below.
        }

        return Environment.MachineName;
    }

    public string? GetWindowsVersion()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Environment.OSVersion.VersionString;
        }

        try
        {
            return ReadWindowsVersion();
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return Environment.OSVersion.VersionString;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadWindowsVersion()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        if (key is null)
        {
            return Environment.OSVersion.VersionString;
        }

        var productName = key.GetValue("ProductName") as string;
        var displayVersion = key.GetValue("DisplayVersion") as string;
        var currentBuild = key.GetValue("CurrentBuild") as string;
        var ubr = key.GetValue("UBR") is int u ? u : (int?)null;
        return FormatWindowsVersion(productName, displayVersion, currentBuild, ubr);
    }

    /// <summary>
    /// Windows 11 still reports "Windows 10 ..." in ProductName; the build number is authoritative.
    /// </summary>
    public static string? FormatWindowsVersion(string? productName, string? displayVersion, string? currentBuild, int? ubr)
    {
        var name = productName?.Trim();
        if (name is null && currentBuild is null)
        {
            return null;
        }

        if (name is not null && int.TryParse(currentBuild, out var build) && build >= FirstWindows11Build &&
            name.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase))
        {
            name = "Windows 11" + name["Windows 10".Length..];
        }

        var parts = new List<string> { name ?? "Windows" };
        if (!string.IsNullOrWhiteSpace(displayVersion))
        {
            parts.Add(displayVersion.Trim());
        }

        if (!string.IsNullOrWhiteSpace(currentBuild))
        {
            parts.Add(ubr is null ? $"(build {currentBuild})" : $"(build {currentBuild}.{ubr})");
        }

        return string.Join(' ', parts);
    }
}

/// <summary>
/// Simulates another computer name (--host option) to validate Expected vs Observed without a
/// second machine. The Windows version is still read from the real machine.
/// </summary>
public sealed class SimulatedMachineInfoProvider(string hostname, IMachineInfoProvider inner) : IMachineInfoProvider
{
    public string GetHostname() => hostname;

    public string? GetWindowsVersion() => inner.GetWindowsVersion();
}
