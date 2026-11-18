using System.Globalization;
using DriverCompass.Core.Abstractions;
using DriverCompass.Core.Models;

namespace DriverCompass.Core.Reporting;

/// <summary>
/// Turns status/reason/class values into localized texts. Shared by the app and the reports.
/// Convention: <see cref="ITextProvider.Get"/> returns the key itself when no text exists.
/// </summary>
public static class AssessmentTexts
{
    public static string Status(ITextProvider t, DeviceStatus status) => t.Get($"Status_{status}");

    public static string Reason(ITextProvider t, DeviceAssessment a) => a.Reason switch
    {
        StatusReason.DeviceError => Format(t.Get("Reason_DeviceError"), a.Device.ConfigManagerErrorCode, ErrorCode(t, a.Device.ConfigManagerErrorCode)),
        StatusReason.GenericDriver => t.Get($"Reason_Generic_{a.GenericCategory ?? GenericDriverCategory.Other}"),
        _ => t.Get($"Reason_{a.Reason}"),
    };

    /// <summary>Plain-language text for a Configuration Manager error code.</summary>
    public static string ErrorCode(ITextProvider t, int code)
    {
        var key = $"CmError_{code}";
        var text = t.Get(key);
        return text == key ? Format(t.Get("CmError_Unknown"), code) : text;
    }

    /// <summary>Friendly category for a setup class ("Display" → "Grafikkarte"); falls back to the class name.</summary>
    public static string Category(ITextProvider t, string? deviceClass)
    {
        if (string.IsNullOrWhiteSpace(deviceClass))
        {
            return t.Get("Class_Unknown");
        }

        var key = $"Class_{deviceClass}";
        var text = t.Get(key);
        return text == key ? deviceClass : text;
    }

    public static string LinkLabel(ITextProvider t, DriverLink link) => link.Kind switch
    {
        DriverLinkKind.OemSupport => Format(t.Get($"Link_OemSupport_{link.Specificity}"), link.SourceName),
        DriverLinkKind.ChipVendor => Format(t.Get("Link_ChipVendor"), link.SourceName),
        _ => t.Get("Link_MicrosoftCatalog"),
    };

    public static string Format(string template, params object?[] args)
    {
        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }
}

/// <summary>Text provider that returns keys unchanged (tests, fallback).</summary>
public sealed class KeyTextProvider : ITextProvider
{
    public static KeyTextProvider Instance { get; } = new();

    public string Get(string key) => key;
}
