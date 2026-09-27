using System.Text.Json;
using System.Text.Json.Serialization;

namespace PeripheralAssetTracker.Infrastructure.Discovery;

/// <summary>
/// Raw Windows PnP device node, exactly as reported by the Configuration Manager (CfgMgr32).
/// "InstanceId" is the Device Instance ID, which is also what WMI calls PNPDeviceID.
/// </summary>
public sealed record PnpNode
{
    public required string InstanceId { get; init; }
    public string? ParentInstanceId { get; init; }
    public string? ClassName { get; init; }
    public string? EnumeratorName { get; init; }
    public string? FriendlyName { get; init; }
    public string? Description { get; init; }
    public string? Manufacturer { get; init; }

    /// <summary>Product string reported by the bus (USB iProduct), not localized. Often null.</summary>
    public string? BusReportedDescription { get; init; }

    public string? ContainerId { get; init; }
    public string? Service { get; init; }
    public IReadOnlyList<string> HardwareIds { get; init; } = [];
}

/// <summary>A captured set of PnP nodes (used for diagnostics, fixtures and replay with --fixture).</summary>
public sealed record PnpSnapshot
{
    public DateTimeOffset CapturedAt { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<PnpNode> Nodes { get; init; } = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static PnpSnapshot FromJson(string json) =>
        JsonSerializer.Deserialize<PnpSnapshot>(json, JsonOptions)
        ?? throw new InvalidDataException("Invalid PnP snapshot file.");
}

public interface IPnpNodeSource
{
    /// <summary>All PnP nodes currently present on the machine.</summary>
    IReadOnlyList<PnpNode> GetPresentNodes();
}
