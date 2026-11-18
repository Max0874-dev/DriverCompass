using DriverCompass.Core.Abstractions;
using DriverCompass.Core.Models;

namespace DriverCompass.Core.Parsing;

/// <summary>
/// Name lookup based on the pci.ids file (https://pci-ids.ucw.cz/, used under the 3-clause BSD license).
/// Only the vendor and device levels are loaded; subsystems and classes are skipped. Parsing is lazy.
/// </summary>
public sealed class PciIdsDatabase : IDeviceNameLookup
{
    private readonly Lazy<Dictionary<string, VendorEntry>> _vendors;

    public PciIdsDatabase(Func<Stream?> openStream)
    {
        ArgumentNullException.ThrowIfNull(openStream);
        _vendors = new Lazy<Dictionary<string, VendorEntry>>(() =>
        {
            using var stream = openStream();
            return stream is null ? [] : Parse(stream);
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public int VendorCount => _vendors.Value.Count;

    public string? GetVendorName(VendorIdKind kind, string vendorId) =>
        kind == VendorIdKind.Pci && _vendors.Value.TryGetValue(vendorId.ToUpperInvariant(), out var v) ? v.Name : null;

    public string? GetDeviceName(VendorIdKind kind, string vendorId, string deviceId) =>
        kind == VendorIdKind.Pci &&
        _vendors.Value.TryGetValue(vendorId.ToUpperInvariant(), out var v) &&
        v.Devices.TryGetValue(deviceId.ToUpperInvariant(), out var name)
            ? name
            : null;

    internal static Dictionary<string, VendorEntry> Parse(Stream stream)
    {
        var vendors = new Dictionary<string, VendorEntry>(StringComparer.Ordinal);
        using var reader = new StreamReader(stream);
        VendorEntry? current = null;

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            // Device class section starts at the end of the file – not needed.
            if (line.StartsWith("C ", StringComparison.Ordinal))
            {
                break;
            }

            if (line[0] != '\t')
            {
                current = TrySplit(line, out var id, out var name) ? new VendorEntry(name) : null;
                if (current is not null)
                {
                    vendors[id] = current;
                }
            }
            else if (current is not null && line.Length > 1 && line[1] != '\t')
            {
                if (TrySplit(line[1..], out var id, out var name))
                {
                    current.Devices.TryAdd(id, name);
                }
            }

            // Lines starting with two tabs are subsystems – skipped.
        }

        return vendors;
    }

    private static bool TrySplit(string line, out string id, out string name)
    {
        id = name = string.Empty;
        if (line.Length < 6 || line[4] != ' ')
        {
            return false;
        }

        var candidate = line[..4];
        if (!candidate.All(Uri.IsHexDigit))
        {
            return false;
        }

        id = candidate.ToUpperInvariant();
        name = line[5..].Trim();
        return name.Length > 0;
    }

    internal sealed class VendorEntry(string name)
    {
        public string Name { get; } = name;
        public Dictionary<string, string> Devices { get; } = new(StringComparer.Ordinal);
    }
}
