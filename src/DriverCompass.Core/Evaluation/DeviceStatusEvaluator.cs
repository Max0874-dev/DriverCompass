using DriverCompass.Core.Abstractions;
using DriverCompass.Core.Models;
using DriverCompass.Core.Parsing;

namespace DriverCompass.Core.Evaluation;

/// <summary>
/// Status logic:
/// code 28 → missing driver · code 22 → disabled · code 45 → not connected (hidden) · other codes → error ·
/// Microsoft inbox driver matching a catalog "genericDrivers" rule → generic · everything else → OK.
/// </summary>
public sealed class DeviceStatusEvaluator(IDeviceNameLookup? names = null) : IDeviceStatusEvaluator
{
    public const int CodeDisabled = 22;
    public const int CodeNoDriver = 28;
    public const int CodeNotPresent = 45;

    public DeviceAssessment Evaluate(DeviceInfo device, IVendorCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(catalog);

        var hardwareIds = HardwareIdParser.ParseAll(device.HardwareIds);
        var primary = hardwareIds.FirstOrDefault(h => h.HasVendor)
                      ?? HardwareIdParser.ParseAll(device.CompatibleIds).FirstOrDefault(h => h.HasVendor)
                      ?? hardwareIds.FirstOrDefault();

        var chip = primary is null ? null : catalog.FindChipVendor(primary);
        var (vendorName, detectedName) = LookupNames(primary, chip, device);
        var (status, reason, category) = Classify(device, catalog);

        return new DeviceAssessment
        {
            Device = device,
            Status = status,
            Reason = reason,
            GenericCategory = category,
            IsSystemDevice = reason == StatusReason.NotPresent || SystemDeviceFilter.IsSystemDevice(device),
            HardwareIds = hardwareIds,
            PrimaryId = primary,
            ChipVendor = chip,
            VendorName = vendorName,
            DetectedName = detectedName,
        };
    }

    internal static (DeviceStatus Status, StatusReason Reason, GenericDriverCategory? Category) Classify(DeviceInfo device, IVendorCatalog catalog)
    {
        switch (device.ConfigManagerErrorCode)
        {
            case CodeNoDriver:
                return (DeviceStatus.MissingDriver, StatusReason.NoDriverInstalled, null);
            case CodeDisabled:
                return (DeviceStatus.Disabled, StatusReason.Disabled, null);
            case CodeNotPresent:
                return (DeviceStatus.Ok, StatusReason.NotPresent, null);
            case not 0:
                return (DeviceStatus.Error, StatusReason.DeviceError, null);
        }

        if (catalog.MatchGenericDriver(device) is { } rule)
        {
            return (DeviceStatus.GenericDriver, StatusReason.GenericDriver, rule.Category);
        }

        var driver = device.Driver;
        if (driver is null || !driver.HasData)
        {
            return (DeviceStatus.Ok, StatusReason.NoDriverInfo, null);
        }

        return IsMicrosoft(driver.ProviderName)
            ? (DeviceStatus.Ok, StatusReason.InboxDriver, null)
            : (DeviceStatus.Ok, StatusReason.VendorDriver, null);
    }

    private (string? Vendor, string? Detected) LookupNames(HardwareId? primary, ChipVendor? chip, DeviceInfo device)
    {
        string? vendor = chip?.Name;
        string? detected = null;

        if (primary is { HasVendor: true } && names is not null)
        {
            var pciIdsVendor = names.GetVendorName(primary.VendorKind, primary.VendorId!);
            vendor ??= pciIdsVendor;

            // Device names only for real PCI IDs – HD-Audio codec IDs live in a different number space.
            if (primary.Bus == HardwareBus.Pci && primary.DeviceId is not null)
            {
                detected = names.GetDeviceName(primary.VendorKind, primary.VendorId!, primary.DeviceId);
            }
        }

        vendor ??= SystemInfoSanitizer.Clean(device.Manufacturer) is { } m && !IsGenericManufacturer(m) ? m : null;
        return (vendor, detected);
    }

    private static bool IsMicrosoft(string? provider) =>
        provider is not null && provider.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase);

    /// <summary>Windows reports "(Standard system devices)" etc. for inbox drivers – not a real vendor.</summary>
    private static bool IsGenericManufacturer(string manufacturer) =>
        manufacturer.StartsWith('(') || manufacturer.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase);
}
