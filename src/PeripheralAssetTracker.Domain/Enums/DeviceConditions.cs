namespace PeripheralAssetTracker.Domain.Enums;

/// <summary>
/// Conditions/alerts of a device. It is a flags set on purpose: a device can be
/// UNASSIGNED and LOW_CONFIDENCE at the same time and neither must hide the other.
/// No flag set means OK.
/// </summary>
[Flags]
public enum DeviceConditions
{
    None = 0,
    NotRegistered = 1 << 0,
    Unassigned = 1 << 1,
    UnexpectedComputer = 1 << 2,
    LowConfidence = 1 << 3,

    /// <summary>More than one connected unit produced the same fingerprint during a scan.</summary>
    AmbiguousFingerprint = 1 << 4
}

public static class DeviceConditionsExtensions
{
    private static readonly (DeviceConditions Flag, string Code)[] Codes =
    [
        (DeviceConditions.NotRegistered, "NOT_REGISTERED"),
        (DeviceConditions.Unassigned, "UNASSIGNED"),
        (DeviceConditions.UnexpectedComputer, "UNEXPECTED_COMPUTER"),
        (DeviceConditions.LowConfidence, "LOW_CONFIDENCE"),
        (DeviceConditions.AmbiguousFingerprint, "AMBIGUOUS_FINGERPRINT")
    ];

    /// <summary>Stable codes in a fixed order; ["OK"] when there is no condition.</summary>
    public static IReadOnlyList<string> ToCodes(this DeviceConditions conditions)
    {
        var codes = Codes.Where(c => conditions.HasFlag(c.Flag)).Select(c => c.Code).ToList();
        return codes.Count == 0 ? ["OK"] : codes;
    }
}
