using System.Globalization;
using DriverCompass.Core;
using DriverCompass.Core.Abstractions;
using Microsoft.Windows.ApplicationModel.Resources;

namespace DriverCompass.App.Services;

/// <summary>
/// Reads texts from Strings/&lt;lang&gt;/Resources.resw (MRT Core) for the language chosen in the settings.
/// Missing keys return the key itself, so a missing translation never crashes the UI.
/// </summary>
public sealed class Localizer : ITextProvider
{
    public static readonly string[] SupportedLanguages = ["de-DE", "en-US"];

    private readonly ResourceMap? _map;
    private readonly ResourceContext? _context;

    public Localizer(string language)
    {
        Language = language;
        try
        {
            var manager = new ResourceManager();
            _map = manager.MainResourceMap.TryGetSubtree("Resources");
            _context = manager.CreateResourceContext();
            _context.QualifierValues["Language"] = language;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or FileNotFoundException)
        {
            Serilog.Log.Warning(ex, "Resource map could not be loaded");
        }
    }

    /// <summary>Instance used by XAML (<c>{x:Bind svc:Localizer.T('Key')}</c>).</summary>
    public static Localizer Current { get; set; } = new("en-US");

    public string Language { get; }

    public static string T(string key) => Current.Get(key);

    public static string F(string key, params object?[] args) => Core.Reporting.AssessmentTexts.Format(Current.Get(key), args);

    /// <summary>Maps the setting ("system", "de-DE", "en-US") to a supported language.</summary>
    public static string Resolve(string? setting)
    {
        if (!string.IsNullOrWhiteSpace(setting) && SupportedLanguages.Contains(setting, StringComparer.OrdinalIgnoreCase))
        {
            return SupportedLanguages.First(l => l.Equals(setting, StringComparison.OrdinalIgnoreCase));
        }

        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("de", StringComparison.OrdinalIgnoreCase) ? "de-DE" : "en-US";
    }

    public string Get(string key)
    {
        if (_map is null || _context is null)
        {
            return key;
        }

        try
        {
            var value = _map.TryGetValue(key, _context)?.ValueAsString;
            return string.IsNullOrEmpty(value) ? key : value.Replace("{AppName}", AppInfo.DisplayName, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException)
        {
            return key;
        }
    }
}
