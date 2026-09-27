using System.Globalization;
using System.Text.RegularExpressions;
using PeripheralAssetTracker.Application.Discovery;
using PeripheralAssetTracker.Application.Fingerprinting;
using PeripheralAssetTracker.Domain.Enums;

namespace PeripheralAssetTracker.Infrastructure.Discovery;

/// <summary>
/// Turns raw Windows PnP nodes into PHYSICAL devices. Pure logic (no Windows API) so it is fully testable.
///
/// Windows exposes one peripheral through several nodes, e.g. a USB mouse:
///   USB\VID_046D&amp;PID_C084\SERIAL          (physical USB device)
///   USB\VID_046D&amp;PID_C084&amp;MI_00\...   (interface 0)  →  HID\VID_046D&amp;PID_C084&amp;MI_00\... (mouse collection)
///   USB\VID_046D&amp;PID_C084&amp;MI_01\...   (interface 1)  →  HID\...&amp;MI_01&amp;COL01..COL0n (keyboard/consumer/vendor collections)
///
/// Strategy:
///  1. "Function" nodes = HID top-level collections (enumerator HID) and any node of class Mouse/Keyboard.
///  2. From each function, climb parents while the node is a HID collection or a USB interface (MI_xx):
///     the node where the climb stops is the PHYSICAL node (USB device, Bluetooth device, ACPI/I2C device).
///  3. Group by the physical node's ContainerId (Windows' own notion of "one physical box") when it is a
///     real container; otherwise by the physical node itself. The special container
///     {00000000-0000-0000-FFFF-FFFFFFFFFFFF} means "part of the computer" → built-in device.
///  4. Device type = function of the lowest interface/collection that is a mouse or keyboard; when both
///     exist (gaming mice with macro keys, combo wireless receivers) a note asks the technician to confirm.
/// Virtual devices (RDP, Hyper-V, root-enumerated) are ignored.
/// </summary>
public static partial class HidDeviceGrouper
{
    public const string LocalMachineContainerId = "{00000000-0000-0000-FFFF-FFFFFFFFFFFF}";

    private static readonly string[] VirtualEnumerators = ["ROOT", "SWD", "TERMINPUT_BUS", "VMBUS", "UMB"];

    public static DeviceDiscoveryResult Group(IReadOnlyCollection<PnpNode> nodes)
    {
        var byId = nodes
            .GroupBy(n => n.InstanceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var ignoredVirtual = 0;
        var entries = new List<(PnpNode Function, PnpNode Physical, HidFunction Kind)>();

        foreach (var node in byId.Values.Where(IsFunctionNode))
        {
            var physical = FindPhysicalNode(node, byId);
            if (IsVirtual(node) || IsVirtual(physical))
            {
                ignoredVirtual++;
                continue;
            }

            entries.Add((node, physical, Classify(node)));
        }

        var devices = entries
            .GroupBy(e => GroupKey(e.Physical))
            .Select(g => BuildDevice(g.ToList()))
            .OrderBy(d => d.IsBuiltIn)
            .ThenBy(d => d.SuggestedType)
            .ThenBy(d => d.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.PnpDeviceId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new DeviceDiscoveryResult(devices, entries.Count, ignoredVirtual, []);
    }

    public static bool IsFunctionNode(PnpNode node)
    {
        var cls = node.ClassName;
        if (string.Equals(cls, "Mouse", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(cls, "Keyboard", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(cls, "HIDClass", StringComparison.OrdinalIgnoreCase) && Enumerator(node) == "HID";
    }

    internal static PnpNode FindPhysicalNode(PnpNode node, IReadOnlyDictionary<string, PnpNode> byId)
    {
        var current = node;
        for (var depth = 0; depth < 16; depth++)
        {
            var enumerator = Enumerator(current);
            var isTransportLayer = enumerator == "HID" || (enumerator == "USB" && IsUsbInterface(current.InstanceId));
            if (!isTransportLayer ||
                current.ParentInstanceId is null ||
                !byId.TryGetValue(current.ParentInstanceId, out var parent))
            {
                return current;
            }

            current = parent;
        }

        return current;
    }

    internal static HidFunction Classify(PnpNode node)
    {
        if (string.Equals(node.ClassName, "Mouse", StringComparison.OrdinalIgnoreCase))
        {
            return HidFunction.Mouse;
        }

        if (string.Equals(node.ClassName, "Keyboard", StringComparison.OrdinalIgnoreCase))
        {
            return HidFunction.Keyboard;
        }

        foreach (var id in node.HardwareIds)
        {
            var upper = id.ToUpperInvariant();
            if (upper.Contains("HID_DEVICE_SYSTEM_MOUSE"))
            {
                return HidFunction.Mouse;
            }

            if (upper.Contains("HID_DEVICE_SYSTEM_KEYBOARD"))
            {
                return HidFunction.Keyboard;
            }

            var match = UsageRegex().Match(upper);
            if (!match.Success)
            {
                continue;
            }

            var page = int.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var usage = int.Parse(match.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return (page, usage) switch
            {
                (0x01, 0x01 or 0x02) => HidFunction.Mouse,
                (0x01, 0x06 or 0x07) => HidFunction.Keyboard,
                (0x01, 0x04 or 0x05 or 0x08) => HidFunction.GameController,
                (0x01, 0x80) => HidFunction.SystemControl,
                (0x0C, _) => HidFunction.ConsumerControl,
                (0x0D, _) => HidFunction.Digitizer,
                (0xF1D0, _) => HidFunction.Fido,
                (>= 0xFF00, _) => HidFunction.VendorDefined,
                _ => HidFunction.Other
            };
        }

        return HidFunction.Other;
    }

    private static DiscoveredDevice BuildDevice(List<(PnpNode Function, PnpNode Physical, HidFunction Kind)> group)
    {
        var physicalNodes = group.Select(e => e.Physical).DistinctBy(p => p.InstanceId, StringComparer.OrdinalIgnoreCase).ToList();
        var primary = physicalNodes
            .OrderByDescending(p => UsbSerialFromInstanceId(p.InstanceId) is not null)
            .ThenByDescending(p => Enumerator(p) is "USB" or "BTHENUM" or "BTHLEDEVICE" or "BTHLE")
            .ThenBy(p => p.InstanceId, StringComparer.OrdinalIgnoreCase)
            .First();

        var orderedFunctions = group
            .OrderBy(e => InterfaceNumber(e.Function.InstanceId) ?? int.MaxValue)
            .ThenBy(e => CollectionNumber(e.Function.InstanceId) ?? int.MaxValue)
            .ThenBy(e => e.Function.InstanceId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var kinds = orderedFunctions.Select(e => e.Kind).ToList();
        var primaryKind = kinds.FirstOrDefault(k => k is HidFunction.Mouse or HidFunction.Keyboard, HidFunction.Other);
        var type = primaryKind switch
        {
            HidFunction.Mouse => DeviceType.Mouse,
            HidFunction.Keyboard => DeviceType.Keyboard,
            _ => DeviceType.Other
        };

        var bus = BusOf(primary);
        var vendorId = ParseVendorId(primary.InstanceId)
                       ?? group.SelectMany(e => e.Function.HardwareIds.Prepend(e.Function.InstanceId)).Select(ParseVendorId).FirstOrDefault(v => v is not null);
        var productId = ParseProductId(primary.InstanceId)
                        ?? group.SelectMany(e => e.Function.HardwareIds.Prepend(e.Function.InstanceId)).Select(ParseProductId).FirstOrDefault(v => v is not null);
        var serial = UsbSerialFromInstanceId(primary.InstanceId);
        var btAddress = bus is BusType.Bluetooth or BusType.BluetoothLowEnergy ? BluetoothAddressFromInstanceId(primary.InstanceId) : null;

        var containerId = NormalizeContainer(primary.ContainerId);
        var isBuiltIn = containerId == LocalMachineContainerId || bus == BusType.Internal;

        var mainFunctionNode = orderedFunctions.FirstOrDefault(e => e.Kind == primaryKind).Function ?? orderedFunctions[0].Function;
        var name = FirstText(
            primary.BusReportedDescription,
            bus is BusType.Bluetooth or BusType.BluetoothLowEnergy ? primary.FriendlyName : null,
            mainFunctionNode.FriendlyName,
            mainFunctionNode.Description,
            primary.FriendlyName,
            primary.Description);

        var manufacturer = KnownUsbVendors.TryGetName(vendorId) ?? DriverManufacturer(primary, vendorId);

        var notes = new List<string>();
        var distinctKinds = kinds.Distinct().ToList();
        if (distinctKinds.Contains(HidFunction.Mouse) && distinctKinds.Contains(HidFunction.Keyboard))
        {
            notes.Add("Expõe funções de mouse E teclado (comum em mouses com teclas programáveis e em receptores " +
                      "sem fio combinados). Confirme o tipo no cadastro.");
        }

        if (LooksLikeWirelessReceiver(name, vendorId, productId))
        {
            notes.Add("Parece ser um receptor sem fio: a identidade técnica é a do receptor USB, não a do mouse/teclado pareado.");
        }

        if (bus == BusType.Usb && serial is null)
        {
            notes.Add("Sem número de série USB: unidades idênticas deste modelo não podem ser diferenciadas.");
        }

        if (bus == BusType.Internal && distinctKinds.Contains(HidFunction.Keyboard) && Enumerator(primary) == "ACPI")
        {
            notes.Add("Teclado enumerado via ACPI/PS2: pode ser o teclado integrado do notebook ou um teclado PS/2 externo.");
        }

        if (isBuiltIn && distinctKinds.Contains(HidFunction.Mouse) && distinctKinds.Contains(HidFunction.Digitizer))
        {
            notes.Add("Mouse + digitalizador integrados: provavelmente o touchpad do notebook.");
        }

        if (type == DeviceType.Other && distinctKinds.All(k => k is HidFunction.VendorDefined or HidFunction.Other))
        {
            notes.Add("Somente coleções HID genéricas/definidas pelo fabricante.");
        }

        return new DiscoveredDevice
        {
            SuggestedType = type,
            BusType = bus,
            DeviceName = name,
            Manufacturer = manufacturer,
            VendorId = vendorId,
            ProductId = productId,
            SerialNumber = serial,
            BluetoothAddress = btAddress,
            PnpDeviceId = primary.InstanceId,
            HardwareIds = primary.HardwareIds.Count > 0 ? primary.HardwareIds : mainFunctionNode.HardwareIds,
            InterfaceInstanceIds = orderedFunctions.Select(e => e.Function.InstanceId).ToList(),
            Functions = distinctKinds.Select(k => k.ToString()).ToList(),
            ContainerId = containerId,
            IsBuiltIn = isBuiltIn,
            Notes = notes
        };
    }

    private static string GroupKey(PnpNode physical)
    {
        var container = NormalizeContainer(physical.ContainerId);
        return container is not null && container != LocalMachineContainerId
            ? "container:" + container
            : "node:" + physical.InstanceId.ToUpperInvariant();
    }

    internal static BusType BusOf(PnpNode physical) => Enumerator(physical) switch
    {
        "USB" => BusType.Usb,
        "BTHENUM" => BusType.Bluetooth,
        "BTHLEDEVICE" or "BTHLE" => BusType.BluetoothLowEnergy,
        "ACPI" or "I2C" or "SPB" => BusType.Internal,
        _ => BusType.Unknown
    };

    internal static string Enumerator(PnpNode node) =>
        (node.EnumeratorName ?? node.InstanceId.Split('\\')[0]).ToUpperInvariant();

    internal static bool IsVirtual(PnpNode node)
    {
        var enumerator = Enumerator(node);
        var id = node.InstanceId.ToUpperInvariant();
        return VirtualEnumerators.Contains(enumerator) || id.Contains("RDP_") || id.Contains("TERMINPUT") ||
               id.Contains("VMBUS");
    }

    private static bool IsUsbInterface(string instanceId) =>
        instanceId.Split('\\') is [_, var deviceId, ..] && deviceId.Contains("&MI_", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Windows uses the USB iSerialNumber as the last segment of the device instance id when the device
    /// reports one. Otherwise the segment is generated from the port location and always contains '&amp;'.
    /// </summary>
    internal static string? UsbSerialFromInstanceId(string instanceId)
    {
        var parts = instanceId.Split('\\');
        if (parts.Length != 3 || !parts[0].Equals("USB", StringComparison.OrdinalIgnoreCase) ||
            parts[1].Contains("&MI_", StringComparison.OrdinalIgnoreCase) || !parts[1].Contains("VID_", StringComparison.OrdinalIgnoreCase) ||
            parts[2].Contains('&') || string.IsNullOrWhiteSpace(parts[2]))
        {
            return null;
        }

        return DeviceIdentifierNormalizer.NormalizeSerial(parts[2]);
    }

    internal static string? BluetoothAddressFromInstanceId(string instanceId)
    {
        var parts = instanceId.ToUpperInvariant().Split('\\');
        if (parts.Length < 2)
        {
            return null;
        }

        var match = BtAddressInDeviceIdRegex().Match(parts[1]);
        if (!match.Success && parts.Length >= 3)
        {
            match = BtAddressInInstanceRegex().Match(parts[2]);
        }

        return match.Success ? DeviceIdentifierNormalizer.NormalizeBluetoothAddress(match.Groups[1].Value) : null;
    }

    internal static string? ParseVendorId(string? id)
    {
        if (id is null)
        {
            return null;
        }

        var match = VendorIdRegex().Match(id.ToUpperInvariant());
        return match.Success ? match.Groups[2].Value : null;
    }

    internal static string? ParseProductId(string? id)
    {
        if (id is null)
        {
            return null;
        }

        var match = ProductIdRegex().Match(id.ToUpperInvariant());
        return match.Success ? match.Groups[1].Value : null;
    }

    private static int? InterfaceNumber(string instanceId)
    {
        var match = InterfaceRegex().Match(instanceId.ToUpperInvariant());
        return match.Success ? int.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) : null;
    }

    private static int? CollectionNumber(string instanceId)
    {
        var match = CollectionRegex().Match(instanceId.ToUpperInvariant());
        return match.Success ? int.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) : null;
    }

    private static string? NormalizeContainer(string? containerId)
    {
        if (string.IsNullOrWhiteSpace(containerId))
        {
            return null;
        }

        var trimmed = containerId.Trim().Trim('{', '}');
        return Guid.TryParse(trimmed, out var guid) ? "{" + guid.ToString().ToUpperInvariant() + "}" : null;
    }

    private static string? DriverManufacturer(PnpNode primary, string? vendorId)
    {
        var manufacturer = DeviceIdentifierNormalizer.NormalizeManufacturer(primary.Manufacturer);
        // "Microsoft" is the provider of the in-box HID drivers; it only identifies real Microsoft hardware.
        if (manufacturer is not null && manufacturer.Equals("Microsoft", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(vendorId, "045E", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return manufacturer;
    }

    private static bool LooksLikeWirelessReceiver(string? name, string? vendorId, string? productId)
    {
        if (name is not null && ReceiverNameRegex().IsMatch(name))
        {
            return true;
        }

        // Logitech Unifying / Nano / Bolt / Lightspeed receivers.
        return string.Equals(vendorId, "046D", StringComparison.OrdinalIgnoreCase) &&
               productId is "C52B" or "C52F" or "C531" or "C534" or "C539" or "C53A" or "C53F" or "C541" or "C545" or "C547" or "C548";
    }

    private static string? FirstText(params string?[] values) =>
        values.Select(DeviceIdentifierNormalizer.CleanText).FirstOrDefault(v => v is not null);

    [GeneratedRegex(@"HID_DEVICE_UP:([0-9A-F]{4})_U:([0-9A-F]{4})", RegexOptions.CultureInvariant)]
    private static partial Regex UsageRegex();

    [GeneratedRegex(@"VID[_&]([0-9A-F]{2,4})?([0-9A-F]{4})(?![0-9A-F])", RegexOptions.CultureInvariant)]
    private static partial Regex VendorIdRegex();

    [GeneratedRegex(@"PID[_&]([0-9A-F]{4})(?![0-9A-F])", RegexOptions.CultureInvariant)]
    private static partial Regex ProductIdRegex();

    [GeneratedRegex(@"&MI_([0-9A-F]{2})", RegexOptions.CultureInvariant)]
    private static partial Regex InterfaceRegex();

    [GeneratedRegex(@"&COL([0-9A-F]{2})", RegexOptions.CultureInvariant)]
    private static partial Regex CollectionRegex();

    // BTHLEDEVICE\{uuid}_DEV_VID&..._PID&..._REV&..._AABBCCDDEEFF  /  BTHLE\DEV_AABBCCDDEEFF  /  BTHENUM\DEV_AABBCCDDEEFF
    [GeneratedRegex(@"(?:^DEV_|_)([0-9A-F]{12})$", RegexOptions.CultureInvariant)]
    private static partial Regex BtAddressInDeviceIdRegex();

    // BTHENUM\{uuid}_VID&..._PID&...\7&1234&0&AABBCCDDEEFF_C00000000
    [GeneratedRegex(@"(?:^|&)([0-9A-F]{12})_C[0-9A-F]+$", RegexOptions.CultureInvariant)]
    private static partial Regex BtAddressInInstanceRegex();

    [GeneratedRegex(@"receiver|receptor|dongle|unifying|lightspeed|\bbolt\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReceiverNameRegex();
}

public enum HidFunction
{
    Mouse,
    Keyboard,
    ConsumerControl,
    SystemControl,
    GameController,
    Digitizer,
    Fido,
    VendorDefined,
    Other
}
