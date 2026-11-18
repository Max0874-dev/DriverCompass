using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using DriverCompass.Core.Abstractions;
using DriverCompass.Core.Evaluation;
using DriverCompass.Core.Models;

namespace DriverCompass.Core.Catalog;

/// <summary>In-memory, indexed vendor catalog. Construct only from validated data.</summary>
public sealed class VendorCatalog : IVendorCatalog
{
    /// <summary>Timeout for every catalog-supplied regular expression (protects against ReDoS from a bad list).</summary>
    public static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    private readonly Dictionary<(VendorIdKind Kind, string Id), ChipVendor> _chipIndex = [];
    private readonly ConcurrentDictionary<string, Regex> _regexCache = new(StringComparer.Ordinal);

    public VendorCatalog(
        int schemaVersion,
        string dataVersion,
        IReadOnlyList<ChipVendor> chipVendors,
        IReadOnlyList<OemVendor> oems,
        IReadOnlyList<GenericDriverRule> genericDrivers)
    {
        SchemaVersion = schemaVersion;
        DataVersion = dataVersion;
        ChipVendors = chipVendors;
        Oems = oems;
        GenericDrivers = genericDrivers;

        foreach (var vendor in chipVendors)
        {
            Index(vendor, VendorIdKind.Pci, vendor.PciIds);
            Index(vendor, VendorIdKind.Usb, vendor.UsbIds);
            Index(vendor, VendorIdKind.Acpi, vendor.AcpiIds);
            Index(vendor, VendorIdKind.Pnp, vendor.PnpIds);
        }
    }

    public int SchemaVersion { get; }
    public string DataVersion { get; }
    public IReadOnlyList<ChipVendor> ChipVendors { get; }
    public IReadOnlyList<OemVendor> Oems { get; }
    public IReadOnlyList<GenericDriverRule> GenericDrivers { get; }

    public ChipVendor? FindChipVendor(HardwareId id)
    {
        if (!id.HasVendor)
        {
            return null;
        }

        return _chipIndex.GetValueOrDefault((id.VendorKind, id.VendorId!.ToUpperInvariant()));
    }

    public OemMatch? FindOem(SystemInfo system)
    {
        var csVendor = SystemInfoSanitizer.Clean(system.Manufacturer);
        if (csVendor is not null && MatchOem(csVendor) is { } byCs)
        {
            return new OemMatch(byCs, OemMatchSource.ComputerSystem);
        }

        var boardVendor = SystemInfoSanitizer.Clean(system.BaseBoardManufacturer);
        if (boardVendor is not null && MatchOem(boardVendor) is { } byBoard)
        {
            return new OemMatch(byBoard, OemMatchSource.BaseBoard);
        }

        return null;
    }

    public GenericDriverRule? MatchGenericDriver(DeviceInfo device)
    {
        var driver = device.Driver;
        if (driver is null || !driver.HasData)
        {
            return null;
        }

        foreach (var rule in GenericDrivers)
        {
            if (!IsMatch(rule.ProviderPattern, driver.ProviderName))
            {
                continue;
            }

            if (rule.InfName is not null && !string.Equals(rule.InfName, driver.InfName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (rule.DeviceClass is not null && !string.Equals(rule.DeviceClass, device.DeviceClass, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (rule.Enumerators.Count > 0 &&
                !rule.Enumerators.Any(e => device.InstanceId.StartsWith(e + "\\", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (rule.DeviceNamePattern is not null &&
                !IsMatch(rule.DeviceNamePattern, device.Name) &&
                !IsMatch(rule.DeviceNamePattern, driver.DeviceName))
            {
                continue;
            }

            return rule;
        }

        return null;
    }

    private OemVendor? MatchOem(string manufacturer) =>
        Oems.FirstOrDefault(o => o.MatchPatterns.Any(p => IsMatch(p, manufacturer)));

    private bool IsMatch(string pattern, string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return false;
        }

        var regex = _regexCache.GetOrAdd(pattern, p =>
            new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout));
        try
        {
            return regex.IsMatch(input);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private void Index(ChipVendor vendor, VendorIdKind kind, IEnumerable<string> ids)
    {
        foreach (var id in ids)
        {
            _chipIndex.TryAdd((kind, id.ToUpperInvariant()), vendor);
        }
    }
}
