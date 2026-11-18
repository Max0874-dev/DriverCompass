using System.Management;
using System.Runtime.Versioning;
using DriverCompass.Core.Abstractions;
using DriverCompass.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DriverCompass.Scanner.Windows;

/// <summary>
/// Reads devices via WMI: Win32_PnPEntity (incl. ConfigManagerErrorCode) joined with Win32_PnPSignedDriver,
/// plus Win32_ComputerSystem, Win32_BaseBoard, Win32_BIOS and Win32_OperatingSystem. Runs without admin rights.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WmiDeviceScanner(IWmiQuery wmi, ILogger<WmiDeviceScanner>? logger = null) : IDeviceScanner
{
    internal const string EntityQuery =
        "SELECT PNPDeviceID, DeviceID, Name, Description, Manufacturer, PNPClass, ClassGuid, Service, HardwareID, CompatibleID, ConfigManagerErrorCode, Present FROM Win32_PnPEntity";

    internal const string EntityFallbackQuery = "SELECT * FROM Win32_PnPEntity";

    internal const string DriverQuery =
        "SELECT DeviceID, DeviceName, Manufacturer, DriverProviderName, DriverVersion, DriverDate, DeviceClass, InfName, IsSigned, Signer FROM Win32_PnPSignedDriver";

    private readonly ILogger _logger = logger ?? NullLogger<WmiDeviceScanner>.Instance;

    public WmiDeviceScanner()
        : this(new WmiQueryRunner())
    {
    }

    public string Id => "wmi";

    public Task<ScanResult> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(cancellationToken), cancellationToken);

    private ScanResult Scan(CancellationToken ct)
    {
        var started = DateTimeOffset.Now;

        var entities = QueryEntities();
        ct.ThrowIfCancellationRequested();
        var drivers = SafeQuery(DriverQuery);
        ct.ThrowIfCancellationRequested();

        var system = WmiMapper.MapSystem(
            SafeQuery("SELECT Manufacturer, Model, SystemFamily, SystemSKUNumber FROM Win32_ComputerSystem").FirstOrDefault(),
            SafeQuery("SELECT Manufacturer, Product, Version FROM Win32_BaseBoard").FirstOrDefault(),
            SafeQuery("SELECT SerialNumber, SMBIOSBIOSVersion FROM Win32_BIOS").FirstOrDefault(),
            SafeQuery("SELECT Caption, Version FROM Win32_OperatingSystem").FirstOrDefault());

        var devices = WmiMapper.MapDevices(entities, drivers);
        _logger.LogInformation("WMI scan: {Devices} devices, {Drivers} driver entries in {Ms} ms",
            devices.Count, drivers.Count, (int)(DateTimeOffset.Now - started).TotalMilliseconds);

        return new ScanResult(system, devices, DateTimeOffset.Now, Id);
    }

    private IReadOnlyList<IReadOnlyDictionary<string, object?>> QueryEntities()
    {
        try
        {
            return wmi.Query(EntityQuery);
        }
        catch (ManagementException ex)
        {
            // Older builds may not know every property (e.g. PNPClass/Present) – fall back to all properties.
            _logger.LogWarning(ex, "Win32_PnPEntity property query failed, retrying with SELECT *");
            return wmi.Query(EntityFallbackQuery);
        }
    }

    private IReadOnlyList<IReadOnlyDictionary<string, object?>> SafeQuery(string wql)
    {
        try
        {
            return wmi.Query(wql);
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            _logger.LogWarning(ex, "WMI query failed: {Query}", wql);
            return [];
        }
    }
}
