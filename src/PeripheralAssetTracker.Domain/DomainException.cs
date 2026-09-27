namespace PeripheralAssetTracker.Domain;

/// <summary>
/// Raised when a business rule or invariant is violated. The message is user-facing.
/// </summary>
public sealed class DomainException(string message) : Exception(message);
