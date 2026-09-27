namespace PeripheralAssetTracker.Domain.Enums;

public enum MovementType
{
    /// <summary>First official assignment of a device that had no active assignment.</summary>
    Assignment,

    /// <summary>Official transfer from one active assignment to a new one.</summary>
    Transfer
}
