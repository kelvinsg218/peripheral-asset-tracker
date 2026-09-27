namespace PeripheralAssetTracker.Application;

/// <summary>
/// Expected failure of a use case (not found, invalid input, conflict...). The message is user-facing
/// and is shown by the UI without a stack trace.
/// </summary>
public class AssetTrackerException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class NotFoundException(string message) : AssetTrackerException(message);
