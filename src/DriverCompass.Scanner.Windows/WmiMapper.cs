using System.Globalization;
using DriverCompass.Core.Models;

namespace DriverCompass.Scanner.Windows;

/// <summary>Converts raw WMI rows into Core models. Pure – no WMI calls.</summary>
internal static class WmiMapper
{
    public static IReadOnlyList<DeviceInfo> MapDevices(
        IEnumerable<IReadOnlyDictionary<string, object?>> entities,
        IEnumerable<IReadOnlyDictionary<string, object?>> signedDrivers)
    {
        var drivers = new Dictionary<string, DriverInfo>(StringComparer.OrdinalIgnoreCase);
        var driverClasses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in signedDrivers)
        {
            var id = Str(row, "DeviceID");
            if (id is null || drivers.ContainsKey(id))
            {
                continue;
            }

            drivers[id] = new DriverInfo
            {
                ProviderName = Str(row, "DriverProviderName"),
                Version = Str(row, "DriverVersion"),
                Date = CimDate(Str(row, "DriverDate")),
                InfName = Str(row, "InfName"),
                Manufacturer = Str(row, "Manufacturer"),
                DeviceName = Str(row, "DeviceName"),
                IsSigned = row.TryGetValue("IsSigned", out var signed) && signed is bool b ? b : null,
                Signer = Str(row, "Signer"),
            };

            if (Str(row, "DeviceClass") is { } cls)
            {
                driverClasses[id] = cls;
            }
        }

        var devices = new List<DeviceInfo>();
        foreach (var row in entities)
        {
            var instanceId = Str(row, "PNPDeviceID") ?? Str(row, "DeviceID");
            if (instanceId is null)
            {
                continue;
            }

            drivers.TryGetValue(instanceId, out var driver);
            devices.Add(new DeviceInfo
            {
                InstanceId = instanceId,
                Name = Str(row, "Name") ?? driver?.DeviceName,
                Description = Str(row, "Description"),
                Manufacturer = Str(row, "Manufacturer"),
                DeviceClass = Str(row, "PNPClass") ?? driverClasses.GetValueOrDefault(instanceId),
                ClassGuid = Str(row, "ClassGuid"),
                Service = Str(row, "Service"),
                HardwareIds = StrArray(row, "HardwareID"),
                CompatibleIds = StrArray(row, "CompatibleID"),
                ConfigManagerErrorCode = Int(row, "ConfigManagerErrorCode"),
                IsPresent = row.TryGetValue("Present", out var present) && present is bool p ? p : null,
                Driver = driver is { HasData: true } ? driver : null,
            });
        }

        return devices;
    }

    public static SystemInfo MapSystem(
        IReadOnlyDictionary<string, object?>? computerSystem,
        IReadOnlyDictionary<string, object?>? baseBoard,
        IReadOnlyDictionary<string, object?>? bios,
        IReadOnlyDictionary<string, object?>? os) => new()
    {
        Manufacturer = Str(computerSystem, "Manufacturer"),
        Model = Str(computerSystem, "Model"),
        SystemFamily = Str(computerSystem, "SystemFamily"),
        SystemSku = Str(computerSystem, "SystemSKUNumber"),
        SerialNumber = Str(bios, "SerialNumber"),
        BiosVersion = Str(bios, "SMBIOSBIOSVersion"),
        BaseBoardManufacturer = Str(baseBoard, "Manufacturer"),
        BaseBoardProduct = Str(baseBoard, "Product"),
        BaseBoardVersion = Str(baseBoard, "Version"),
        OperatingSystem = Str(os, "Caption"),
        OperatingSystemVersion = Str(os, "Version"),
    };

    /// <summary>CIM datetime "yyyyMMddHHmmss.ffffff+UUU" → date (time zone irrelevant for driver dates).</summary>
    internal static DateOnly? CimDate(string? value) =>
        value is { Length: >= 8 } &&
        DateOnly.TryParseExact(value[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;

    private static string? Str(IReadOnlyDictionary<string, object?>? row, string key) =>
        row is not null && row.TryGetValue(key, out var v) && v is not null && v.ToString() is { } s && !string.IsNullOrWhiteSpace(s)
            ? s.Trim()
            : null;

    private static IReadOnlyList<string> StrArray(IReadOnlyDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var v) switch
        {
            true when v is string[] arr => arr.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList(),
            true when v is string single && !string.IsNullOrWhiteSpace(single) => [single.Trim()],
            _ => [],
        };

    private static int Int(IReadOnlyDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var v) && v is not null && int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
            ? i
            : 0;
}
