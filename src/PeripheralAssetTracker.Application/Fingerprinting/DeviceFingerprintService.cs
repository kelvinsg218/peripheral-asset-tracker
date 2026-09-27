using System.Security.Cryptography;
using System.Text;
using PeripheralAssetTracker.Application.Discovery;
using PeripheralAssetTracker.Domain.Enums;

namespace PeripheralAssetTracker.Application.Fingerprinting;

public sealed record FingerprintResult(
    string Fingerprint,
    FingerprintConfidence Confidence,
    string Basis,
    string CanonicalForm);

/// <summary>
/// Builds the persistent DEVICE FINGERPRINT:
/// properties → normalization → selection of the best identifiers → deterministic canonical string → SHA-256.
///
/// Selection (first match wins):
///  1. Bluetooth address  → HIGH (classic) / MEDIUM (LE: may be a random static address).
///  2. USB serial number  → HIGH, or MEDIUM when the serial looks generic (e.g. "0000000000").
///  3. VID + PID          → LOW (shared by every unit of the model).
///  4. Hardware id        → LOW (devices without VID/PID, e.g. PS/2 or ACPI).
///  5. Name/manufacturer  → LOW (last resort).
///
/// Deliberately NOT part of the fingerprint: firmware revision (changes with updates), Windows
/// friendly names and driver manufacturer (localized: differ between pt-BR and en-US machines),
/// USB port / instance path (changes when plugged into another port or computer).
/// </summary>
public sealed class DeviceFingerprintService
{
    /// <summary>Version tag of the canonical format. Changing the algorithm requires a new version.</summary>
    public const string AlgorithmVersion = "v1";

    public FingerprintResult Compute(DiscoveredDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        var vid = DeviceIdentifierNormalizer.NormalizeHexId(device.VendorId);
        var pid = DeviceIdentifierNormalizer.NormalizeHexId(device.ProductId);
        var serial = DeviceIdentifierNormalizer.NormalizeSerial(device.SerialNumber);
        var btAddress = DeviceIdentifierNormalizer.NormalizeBluetoothAddress(device.BluetoothAddress);
        var bus = device.BusType.ToString().ToUpperInvariant();

        var components = new List<(string Key, string Value)> { ("bus", bus) };
        if (vid is not null)
        {
            components.Add(("vid", vid));
        }

        if (pid is not null)
        {
            components.Add(("pid", pid));
        }

        FingerprintConfidence confidence;
        string basis;

        if (btAddress is not null && device.BusType is BusType.Bluetooth or BusType.BluetoothLowEnergy)
        {
            components.Add(("btaddr", btAddress));
            if (device.BusType == BusType.Bluetooth)
            {
                confidence = FingerprintConfidence.High;
                basis = "Endereço Bluetooth (BD_ADDR)";
            }
            else
            {
                confidence = FingerprintConfidence.Medium;
                basis = "Endereço Bluetooth LE (pode ser endereço aleatório estático)";
            }
        }
        else if (serial is not null)
        {
            components.Add(("serial", serial));
            if (vid is null || pid is null)
            {
                AddBestHardwareId(device, components);
            }

            if (IsGenericSerial(serial))
            {
                confidence = FingerprintConfidence.Medium;
                basis = "Número de série com padrão genérico (pode se repetir entre unidades)";
            }
            else
            {
                confidence = vid is not null && pid is not null ? FingerprintConfidence.High : FingerprintConfidence.Medium;
                basis = confidence == FingerprintConfidence.High
                    ? "VID + PID + número de série USB"
                    : "Número de série sem VID/PID";
            }
        }
        else if (vid is not null && pid is not null)
        {
            confidence = FingerprintConfidence.Low;
            basis = "Somente VID + PID (modelo; unidades idênticas geram o mesmo fingerprint)";
        }
        else if (AddBestHardwareId(device, components))
        {
            confidence = FingerprintConfidence.Low;
            basis = "Hardware ID (modelo; sem identificador individual)";
        }
        else
        {
            var name = DeviceIdentifierNormalizer.NormalizeText(device.DeviceName);
            var manufacturer = DeviceIdentifierNormalizer.NormalizeText(device.Manufacturer);
            var pnp = DeviceIdentifierNormalizer.NormalizePnpId(device.PnpDeviceId);
            if (name is null && manufacturer is null && pnp is null)
            {
                throw new AssetTrackerException("O dispositivo não possui nenhuma propriedade utilizável para gerar fingerprint.");
            }

            if (name is not null)
            {
                components.Add(("name", name));
            }

            if (manufacturer is not null)
            {
                components.Add(("mfr", manufacturer));
            }

            if (name is null && manufacturer is null)
            {
                components.Add(("pnp", pnp!));
            }

            confidence = FingerprintConfidence.Low;
            basis = "Nome/fabricante (sem identificadores de hardware)";
        }

        var canonical = BuildCanonicalForm(components);
        return new FingerprintResult(Hash(canonical), confidence, basis, canonical);
    }

    /// <summary>
    /// Serials that are very likely shared between units: too short, a single repeated character,
    /// or a simple ascending sequence.
    /// </summary>
    public static bool IsGenericSerial(string normalizedSerial)
    {
        if (normalizedSerial.Length < 4 || normalizedSerial.Distinct().Count() == 1)
        {
            return true;
        }

        const string ascending = "0123456789ABCDEF";
        return normalizedSerial.Length >= 6 && ascending.Contains(normalizedSerial, StringComparison.Ordinal);
    }

    private static bool AddBestHardwareId(DiscoveredDevice device, List<(string Key, string Value)> components)
    {
        var hardwareId = DeviceIdentifierNormalizer.NormalizeHardwareIds(device.HardwareIds).FirstOrDefault();
        if (hardwareId is null)
        {
            return false;
        }

        components.Add(("hwid", DeviceIdentifierNormalizer.StripRevision(hardwareId)));
        return true;
    }

    private static string BuildCanonicalForm(IEnumerable<(string Key, string Value)> components) =>
        AlgorithmVersion + "|" + string.Join('|', components.Select(c => $"{c.Key}={Escape(c.Value)}"));

    // '|' and '=' are structural; escape them so distinct inputs can never collide textually.
    private static string Escape(string value) =>
        value.Replace("%", "%25").Replace("|", "%7C").Replace("=", "%3D");

    private static string Hash(string canonical) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
}
