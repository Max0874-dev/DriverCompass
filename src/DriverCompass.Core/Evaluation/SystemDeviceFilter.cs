using DriverCompass.Core.Models;

namespace DriverCompass.Core.Evaluation;

/// <summary>Identifies software/virtual devices that are hidden by default (ROOT\, SWD\, volumes …).</summary>
public static class SystemDeviceFilter
{
    private static readonly string[] HiddenEnumerators = ["ROOT", "SWD", "HTREE", "SW", "UMB"];

    private static readonly string[] HiddenInstancePrefixes = [@"STORAGE\VOLUME", @"BTH\MS_"];

    private static readonly HashSet<string> HiddenClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "SoftwareDevice",
        "SoftwareComponent",
        "Volume",
        "VolumeSnapshot",
        "LegacyDriver",
        "PrintQueue",
        "AudioEndpoint",
        "Computer",
    };

    public static bool IsSystemDevice(DeviceInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);

        var instance = device.InstanceId;
        var slash = instance.IndexOf('\\', StringComparison.Ordinal);
        var enumerator = slash > 0 ? instance[..slash] : instance;

        if (HiddenEnumerators.Contains(enumerator, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (HiddenInstancePrefixes.Any(p => instance.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return device.DeviceClass is not null && HiddenClasses.Contains(device.DeviceClass);
    }
}
