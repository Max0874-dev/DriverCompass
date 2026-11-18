using System.Text.RegularExpressions;
using DriverCompass.Core.Models;

namespace DriverCompass.Core.Parsing;

/// <summary>
/// Splits Windows hardware IDs (PCI\VEN_&amp;DEV_, USB\VID_&amp;PID_, HDAUDIO, ACPI, HID, MONITOR …)
/// into their parts. Pure function, no Windows dependency.
/// </summary>
public static partial class HardwareIdParser
{
    private static readonly Dictionary<string, HardwareBus> BusByEnumerator = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PCI"] = HardwareBus.Pci,
        ["USB"] = HardwareBus.Usb,
        ["HDAUDIO"] = HardwareBus.HdAudio,
        ["INTELAUDIO"] = HardwareBus.HdAudio,
        ["ACPI"] = HardwareBus.Acpi,
        ["*"] = HardwareBus.Acpi,
        ["HID"] = HardwareBus.Hid,
        ["BTHENUM"] = HardwareBus.Bluetooth,
        ["BTH"] = HardwareBus.Bluetooth,
        ["BTHLE"] = HardwareBus.Bluetooth,
        ["BTHLEDEVICE"] = HardwareBus.Bluetooth,
        ["BTHHFENUM"] = HardwareBus.Bluetooth,
        ["MONITOR"] = HardwareBus.Display,
        ["DISPLAY"] = HardwareBus.Display,
        ["SCSI"] = HardwareBus.Storage,
        ["IDE"] = HardwareBus.Storage,
        ["NVME"] = HardwareBus.Storage,
        ["STORAGE"] = HardwareBus.Storage,
        ["USBSTOR"] = HardwareBus.Storage,
        ["SD"] = HardwareBus.Storage,
        ["SDBUS"] = HardwareBus.Storage,
        ["ROOT"] = HardwareBus.Root,
        ["HTREE"] = HardwareBus.Root,
        ["SWD"] = HardwareBus.Software,
        ["SW"] = HardwareBus.Software,
        ["UMB"] = HardwareBus.Software,
    };

    /// <summary>Parses all IDs, skipping empty entries. Order is preserved.</summary>
    public static IReadOnlyList<HardwareId> ParseAll(IEnumerable<string>? ids) =>
        ids is null
            ? []
            : ids.Where(s => !string.IsNullOrWhiteSpace(s)).Select(Parse).ToList();

    public static HardwareId Parse(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var trimmed = raw.Trim();
        var upper = trimmed.ToUpperInvariant();

        string enumerator;
        string rest;
        if (upper.StartsWith('*'))
        {
            // Compatible ACPI IDs such as "*PNP0A08".
            enumerator = "*";
            rest = upper[1..];
        }
        else
        {
            var slash = upper.IndexOf('\\', StringComparison.Ordinal);
            if (slash < 0)
            {
                return new HardwareId { Raw = trimmed, Enumerator = upper, Bus = HardwareBus.Other };
            }

            enumerator = upper[..slash];
            rest = upper[(slash + 1)..];

            // Hardware IDs normally have exactly one backslash; if an instance ID slips in, ignore the instance part.
            var second = rest.IndexOf('\\', StringComparison.Ordinal);
            if (second >= 0)
            {
                rest = rest[..second];
            }
        }

        var bus = BusByEnumerator.GetValueOrDefault(enumerator, HardwareBus.Other);
        var id = new HardwareId { Raw = trimmed, Enumerator = enumerator, Bus = bus };
        var tokens = Tokenize(rest);

        return bus switch
        {
            HardwareBus.Pci => ParsePci(id, tokens),
            HardwareBus.Usb => ParseUsbLike(id, tokens, rest),
            HardwareBus.Hid => ParseUsbLike(id, tokens, rest),
            HardwareBus.Bluetooth => ParseBluetooth(id, rest),
            HardwareBus.HdAudio => ParseHdAudio(id, tokens),
            HardwareBus.Acpi => ParseAcpi(id, tokens, rest),
            HardwareBus.Display => ParseMonitor(id, rest),
            _ => id,
        };
    }

    private static Dictionary<string, string> Tokenize(string rest)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in rest.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var underscore = part.IndexOf('_', StringComparison.Ordinal);
            if (underscore <= 0 || underscore == part.Length - 1)
            {
                continue;
            }

            var key = part[..underscore];
            var value = part[(underscore + 1)..];
            result.TryAdd(key, value);
        }

        return result;
    }

    private static HardwareId ParsePci(HardwareId id, Dictionary<string, string> t)
    {
        var (subDevice, subVendor) = SplitSubsys(t.GetValueOrDefault("SUBSYS"));
        var vendor = Hex4(t.GetValueOrDefault("VEN"));
        return id with
        {
            VendorKind = vendor is null ? VendorIdKind.None : VendorIdKind.Pci,
            VendorId = vendor,
            DeviceId = Hex4(t.GetValueOrDefault("DEV")),
            SubsystemDeviceId = subDevice,
            SubsystemVendorId = subVendor,
            Revision = t.GetValueOrDefault("REV"),
            ClassCode = t.GetValueOrDefault("CC"),
        };
    }

    private static HardwareId ParseHdAudio(HardwareId id, Dictionary<string, string> t)
    {
        // HD-Audio SUBSYS is vendor first: SUBSYS_10431A30 → vendor 1043, device 1A30.
        var (subVendor, subDevice) = SplitSubsys(t.GetValueOrDefault("SUBSYS"));
        var vendor = Hex4(t.GetValueOrDefault("VEN"));
        return id with
        {
            VendorKind = vendor is null ? VendorIdKind.None : VendorIdKind.Pci,
            VendorId = vendor,
            DeviceId = Hex4(t.GetValueOrDefault("DEV")),
            SubsystemVendorId = subVendor,
            SubsystemDeviceId = subDevice,
            Revision = t.GetValueOrDefault("REV"),
            Interface = t.GetValueOrDefault("FUNC"),
        };
    }

    private static HardwareId ParseUsbLike(HardwareId id, Dictionary<string, string> t, string rest)
    {
        var vendor = Hex4(t.GetValueOrDefault("VID"));
        if (vendor is not null)
        {
            return id with
            {
                VendorKind = VendorIdKind.Usb,
                VendorId = vendor,
                DeviceId = Hex4(t.GetValueOrDefault("PID")),
                Revision = t.GetValueOrDefault("REV"),
                Interface = t.GetValueOrDefault("MI"),
                ClassCode = t.GetValueOrDefault("CLASS"),
            };
        }

        // Bluetooth HID: HID\{00001124-...}_VID&0002046D_PID&B023
        var bt = ParseBluetooth(id, rest);
        return bt.HasVendor ? bt : id with { ClassCode = t.GetValueOrDefault("CLASS") };
    }

    private static HardwareId ParseBluetooth(HardwareId id, string rest)
    {
        var m = BluetoothVidRegex().Match(rest);
        if (!m.Success)
        {
            return id;
        }

        // Source 0002 = USB-IF vendor ID; 0001 = Bluetooth SIG company ID (different registry, not mapped).
        var source = m.Groups["src"].Value;
        return source == "0002"
            ? id with { VendorKind = VendorIdKind.Usb, VendorId = m.Groups["vid"].Value, DeviceId = m.Groups["pid"].Value }
            : id;
    }

    private static HardwareId ParseAcpi(HardwareId id, Dictionary<string, string> t, string rest)
    {
        if (t.TryGetValue("VEN", out var ven) && t.TryGetValue("DEV", out var dev))
        {
            return id with { VendorKind = VendorIdKind.Acpi, VendorId = ven, DeviceId = dev };
        }

        var m = AcpiIdRegex().Match(rest);
        return m.Success
            ? id with { VendorKind = VendorIdKind.Acpi, VendorId = m.Groups["ven"].Value, DeviceId = m.Groups["dev"].Value }
            : id;
    }

    private static HardwareId ParseMonitor(HardwareId id, string rest)
    {
        var m = PnpIdRegex().Match(rest);
        return m.Success
            ? id with { VendorKind = VendorIdKind.Pnp, VendorId = m.Groups["ven"].Value, DeviceId = m.Groups["dev"].Value }
            : id;
    }

    /// <summary>Splits an 8-digit SUBSYS value into halves; the caller decides what each half means.</summary>
    private static (string? First, string? Second) SplitSubsys(string? subsys) =>
        subsys is { Length: 8 } && IsHex(subsys) ? (subsys[..4], subsys[4..]) : (null, null);

    private static string? Hex4(string? value) =>
        value is { Length: 4 } && IsHex(value) ? value : null;

    private static bool IsHex(string s) => s.All(Uri.IsHexDigit);

    [GeneratedRegex(@"_VID&(?<src>[0-9A-F]{4})(?<vid>[0-9A-F]{4})_PID&(?<pid>[0-9A-F]{4})", RegexOptions.CultureInvariant)]
    private static partial Regex BluetoothVidRegex();

    // "INT33A0" (3-letter PNP prefix) or "INTC1043"/"AMDI0010" (4-char ACPI vendor ID) + 4 hex digits.
    [GeneratedRegex(@"^(?<ven>[A-Z]{3}|[A-Z][A-Z0-9]{3})(?<dev>[0-9A-F]{4})$", RegexOptions.CultureInvariant)]
    private static partial Regex AcpiIdRegex();

    [GeneratedRegex(@"^(?<ven>[A-Z]{3})(?<dev>[0-9A-F]{4})$", RegexOptions.CultureInvariant)]
    private static partial Regex PnpIdRegex();
}
