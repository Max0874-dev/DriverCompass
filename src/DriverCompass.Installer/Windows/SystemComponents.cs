using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using DriverCompass.Core.Models;
using DriverCompass.Scanner.Windows;

namespace DriverCompass.Installer.Windows;

/// <summary>Reads the device with the same WMI queries as the scanner.</summary>
[SupportedOSPlatform("windows")]
public sealed class WmiDeviceQuery(WmiDeviceScanner scanner) : IDeviceQuery
{
    public DeviceInfo? Find(string instanceId) => scanner.FindDevice(instanceId);
}

/// <summary>
/// Restore point via WMI <c>root\default:SystemRestore.CreateRestorePoint</c>.
/// Windows silently skips the point when one was created in the last 24 h or System Restore is off,
/// so success is judged by whether a NEW point exists afterwards.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WmiRestorePointCreator : IRestorePointCreator
{
    private const uint DeviceDriverInstall = 10;
    private const uint BeginSystemChange = 100;
    private static readonly TimeSpan FrequencyWindow = TimeSpan.FromHours(24);

    public RestorePointInfo Create(string description)
    {
        var before = Latest();
        uint code;
        try
        {
            using var restore = new ManagementClass(new ManagementScope(@"\\.\root\default"), new ManagementPath("SystemRestore"), null);
            using var input = restore.GetMethodParameters("CreateRestorePoint");
            input["Description"] = description;
            input["RestorePointType"] = DeviceDriverInstall;
            input["EventType"] = BeginSystemChange;
            using var output = restore.InvokeMethod("CreateRestorePoint", input, null);
            code = Convert.ToUInt32(output?["ReturnValue"] ?? uint.MaxValue, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return new RestorePointInfo(false, "Failed:" + ex.GetType().Name, before?.Created);
        }

        var after = Latest();
        if (after is not null && (before is null || after.Value.Sequence > before.Value.Sequence))
        {
            return new RestorePointInfo(true, null, after.Value.Created);
        }

        // 1058 = ERROR_SERVICE_DISABLED, 1717/1068 etc. – System Restore is off for the system drive.
        if (code != 0)
        {
            return new RestorePointInfo(false, code is 1058 or 1068 or 1717 ? "Disabled" : $"Failed:{code}", before?.Created);
        }

        return before is { } last && DateTimeOffset.Now - last.Created < FrequencyWindow
            ? new RestorePointInfo(false, "FrequencyLimit", last.Created)
            : new RestorePointInfo(false, "Disabled", before?.Created);
    }

    private static (uint Sequence, DateTimeOffset Created)? Latest()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(new ManagementScope(@"\\.\root\default"), new ObjectQuery("SELECT SequenceNumber, CreationTime FROM SystemRestore"));
            using var points = searcher.Get();
            (uint, DateTimeOffset)? latest = null;
            foreach (var point in points)
            {
                using (point)
                {
                    var sequence = Convert.ToUInt32(point["SequenceNumber"], CultureInfo.InvariantCulture);
                    var created = point["CreationTime"] is string s ? ManagementDateTimeConverter.ToDateTime(s) : DateTime.MinValue;
                    if (latest is null || sequence > latest.Value.Item1)
                    {
                        latest = (sequence, new DateTimeOffset(created));
                    }
                }
            }

            return latest;
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
}

/// <summary>Driver store access: lists C:\Windows\INF\oem*.inf and calls pnputil by absolute path.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDriverStore : IDriverStore
{
    private static readonly TimeSpan PnpUtilTimeout = TimeSpan.FromMinutes(5);
    private readonly string _infDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF");

    public IReadOnlyDictionary<string, string> Snapshot()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(_infDirectory, "oem*.inf"))
        {
            if (Hash(path) is { } hash)
            {
                map[Path.GetFileName(path)] = hash;
            }
        }

        return map;
    }

    public string? HashOf(string publishedName)
    {
        var path = Path.Combine(_infDirectory, Path.GetFileName(publishedName));
        return File.Exists(path) ? Hash(path) : null;
    }

    public int DeleteDriver(string publishedName)
    {
        var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "pnputil.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("/delete-driver");
        startInfo.ArgumentList.Add(Path.GetFileName(publishedName));
        startInfo.ArgumentList.Add("/uninstall");

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("pnputil did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(PnpUtilTimeout))
        {
            process.Kill(entireProcessTree: true);
            return -1;
        }

        Task.WaitAll(stdout, stderr);
        return process.ExitCode;
    }

    private static string? Hash(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
