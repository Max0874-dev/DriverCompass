using System.Diagnostics;
using DriverCompass.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace DriverCompass.App.Services;

/// <summary>Opens links only after re-checking that they are HTTPS – nothing is opened automatically.</summary>
public static class Launcher
{
    public static async Task<bool> OpenAsync(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps)
        {
            Serilog.Log.Warning("Refused to open non-HTTPS link");
            return false;
        }

        // The URL may contain the serial number (OEM links) – log the host only.
        Serilog.Log.Information("Opening driver link on {Host}", uri.Host);
        return await Windows.System.Launcher.LaunchUriAsync(uri);
    }

    /// <summary>Opens Windows' own "Optional updates" page, where Windows Update lists driver updates. Fixed URI.</summary>
    public static async Task<bool> OpenWindowsUpdateAsync()
    {
        Serilog.Log.Information("Opening Windows Update optional updates");
        return await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:windowsupdate-optionalupdates"));
    }

    /// <summary>Opens a local file or folder with the default app (reports, log folder).</summary>
    public static void OpenLocal(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }

    public static void CopyText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }
}

/// <summary>Traffic-light colours and icons. Uses theme-aware system brushes (light/dark/high contrast).</summary>
public static class StatusVisuals
{
    public static Brush Brush(DeviceStatus status) => Resource(status switch
    {
        DeviceStatus.Error or DeviceStatus.MissingDriver => "SystemFillColorCriticalBrush",
        DeviceStatus.GenericDriver => "SystemFillColorCautionBrush",
        DeviceStatus.Ok => "SystemFillColorSuccessBrush",
        _ => "SystemFillColorNeutralBrush",
    });

    /// <summary>Segoe Fluent Icons glyph – colour is never the only signal.</summary>
    public static string Glyph(DeviceStatus status) => status switch
    {
        DeviceStatus.Error or DeviceStatus.MissingDriver => "",
        DeviceStatus.GenericDriver => "",
        DeviceStatus.Ok => "",
        _ => "",
    };

    /// <summary>Full-contrast text for "newer version" (the accent colour can be grey), green for "nothing newer", muted for unknown/failed.</summary>
    public static Brush VersionBrush(VersionCheckState? state) => Resource(state switch
    {
        VersionCheckState.NewerAvailable => "TextFillColorPrimaryBrush",
        VersionCheckState.UpToDate => "SystemFillColorSuccessBrush",
        _ => "TextFillColorSecondaryBrush",
    });

    public static Visibility VisibleIf(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility VisibleIfNot(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility VisibleIfText(string? value) => string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;

    private static Brush Resource(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is Brush b ? b : new SolidColorBrush(Microsoft.UI.Colors.Gray);
}
