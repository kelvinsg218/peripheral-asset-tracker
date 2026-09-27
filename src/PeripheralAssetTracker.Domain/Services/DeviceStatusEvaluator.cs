using PeripheralAssetTracker.Domain.Entities;
using PeripheralAssetTracker.Domain.Enums;

namespace PeripheralAssetTracker.Domain.Services;

/// <summary>
/// Computes the conditions of a device. Expected vs Observed lives here:
/// EXPECTED = computer of the active assignment; OBSERVED = computer where it was detected.
/// A mismatch only raises UNEXPECTED_COMPUTER — it never changes the assignment.
/// </summary>
public static class DeviceStatusEvaluator
{
    public static DeviceConditions EvaluateUnregistered(FingerprintConfidence confidence) =>
        DeviceConditions.NotRegistered | LowConfidenceFlag(confidence);

    public static DeviceConditions Evaluate(
        FingerprintConfidence confidence, Assignment? activeAssignment, int? observedComputerId)
    {
        var conditions = LowConfidenceFlag(confidence);

        if (activeAssignment?.ResponsiblePersonId is null)
        {
            conditions |= DeviceConditions.Unassigned;
        }

        if (IsUnexpectedComputer(activeAssignment?.ExpectedComputerId, observedComputerId))
        {
            conditions |= DeviceConditions.UnexpectedComputer;
        }

        return conditions;
    }

    /// <summary>Unexpected only when both sides are known and differ.</summary>
    public static bool IsUnexpectedComputer(int? expectedComputerId, int? observedComputerId) =>
        expectedComputerId is not null && observedComputerId is not null && expectedComputerId != observedComputerId;

    private static DeviceConditions LowConfidenceFlag(FingerprintConfidence confidence) =>
        confidence == FingerprintConfidence.Low ? DeviceConditions.LowConfidence : DeviceConditions.None;
}
