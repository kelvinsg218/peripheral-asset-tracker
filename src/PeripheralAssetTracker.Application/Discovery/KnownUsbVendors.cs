namespace PeripheralAssetTracker.Application.Discovery;

/// <summary>
/// Small table of USB vendor ids (assigned by USB-IF) commonly found on mice and keyboards.
/// Windows does not expose the USB manufacturer string as a device property: the "Manufacturer"
/// property is the DRIVER provider (often a generic, localized text such as
/// "(Dispositivos padrão do sistema)"). This table is used instead; unknown VIDs stay unavailable.
/// </summary>
public static class KnownUsbVendors
{
    private static readonly Dictionary<string, string> Vendors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["03F0"] = "HP",
        ["0409"] = "NEC",
        ["041E"] = "Creative",
        ["0458"] = "KYE Systems (Genius)",
        ["045E"] = "Microsoft",
        ["0461"] = "Primax",
        ["046A"] = "Cherry",
        ["046D"] = "Logitech",
        ["04B4"] = "Cypress",
        ["04CA"] = "Lite-On",
        ["04D9"] = "Holtek",
        ["04F2"] = "Chicony",
        ["04F3"] = "Elan",
        ["056E"] = "Elecom",
        ["05AC"] = "Apple",
        ["062A"] = "MosArt",
        ["0951"] = "Kingston (HyperX)",
        ["093A"] = "PixArt",
        ["0B05"] = "ASUS",
        ["1038"] = "SteelSeries",
        ["1044"] = "Chu Yuen",
        ["1189"] = "Acer",
        ["12D1"] = "Huawei",
        ["1532"] = "Razer",
        ["17EF"] = "Lenovo",
        ["1A2C"] = "China Resource Semico",
        ["1B1C"] = "Corsair",
        ["1BCF"] = "Sunplus",
        ["1C4F"] = "SiGma Micro",
        ["1D57"] = "Xenta",
        ["1EA7"] = "SHARKOON",
        ["24AE"] = "Rapoo",
        ["258A"] = "SINO WEALTH",
        ["25A7"] = "Areson",
        ["2717"] = "Xiaomi",
        ["3151"] = "YICHIP",
        ["3434"] = "Keychron",
        ["413C"] = "Dell",
        ["04E8"] = "Samsung",
        ["0E8F"] = "GreenAsia",
        ["1BF0"] = "Multilaser",
        ["2B89"] = "C3Tech"
    };

    public static string? TryGetName(string? vendorId) =>
        vendorId is not null && Vendors.TryGetValue(vendorId, out var name) ? name : null;
}
