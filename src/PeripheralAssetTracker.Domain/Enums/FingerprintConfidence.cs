namespace PeripheralAssetTracker.Domain.Enums;

/// <summary>
/// How much evidence the fingerprint carries that it identifies ONE physical unit.
/// </summary>
public enum FingerprintConfidence
{
    /// <summary>Fingerprint relies mainly on properties shared by every unit of the same model (e.g. VID/PID).</summary>
    Low,

    /// <summary>Relatively strong combination, but without a guarantee of physical individualization.</summary>
    Medium,

    /// <summary>Based on a strong individual identifier (plausible USB serial number, classic Bluetooth address).</summary>
    High
}
