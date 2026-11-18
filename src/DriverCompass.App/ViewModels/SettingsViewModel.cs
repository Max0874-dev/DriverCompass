using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriverCompass.App.Services;
using DriverCompass.Core;
using DriverCompass.Core.Abstractions;
using DriverCompass.Core.Models;

namespace DriverCompass.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private static readonly string[] LanguageValues = ["system", "de-DE", "en-US"];

    private readonly ISettingsService _settings;

    public SettingsViewModel(ISettingsService settings, string catalogVersion, Func<string, bool?>? isSourceApplicable = null)
    {
        _settings = settings;
        var s = settings.Current;
        VersionSources = VersionSourceIds.All
            .Select(id => new VersionSourceToggle(id, s.EnabledVersionSources.Contains(id, StringComparer.Ordinal), isSourceApplicable?.Invoke(id)))
            .ToList();
        LanguageIndex = Math.Max(0, Array.FindIndex(LanguageValues, v => v.Equals(s.Language, StringComparison.OrdinalIgnoreCase)));
        IsExpertStart = s.StartView == ViewMode.Expert;
        ShowSystemDevices = s.ShowSystemDevices;
        UseSerialNumberInLinks = s.UseSerialNumberInLinks;
        IncludeSerialNumberInExports = s.IncludeSerialNumberInExports;
        UpdateCatalogOnline = s.UpdateCatalogOnline;
        AboutText = Localizer.F("Settings_AboutText", AppInfo.DisplayName, AppInfo.Version, catalogVersion);
    }

    public IReadOnlyList<string> LanguageOptions { get; } = [Localizer.T("Settings_Language_System"), "Deutsch", "English"];

    public IReadOnlyList<string> StartViewOptions { get; } = [Localizer.T("Cmd_ViewSimple"), Localizer.T("Cmd_ViewExpert")];

    /// <summary>One switch per version source, each with a plain statement of what it sends where.</summary>
    public IReadOnlyList<VersionSourceToggle> VersionSources { get; }

    public string AboutText { get; }

    public string DataDirectory => AppInfo.DataDirectory;

    [ObservableProperty] public partial int LanguageIndex { get; set; }
    [ObservableProperty] public partial bool IsExpertStart { get; set; }
    [ObservableProperty] public partial bool ShowSystemDevices { get; set; }
    [ObservableProperty] public partial bool UseSerialNumberInLinks { get; set; }
    [ObservableProperty] public partial bool IncludeSerialNumberInExports { get; set; }
    [ObservableProperty] public partial bool UpdateCatalogOnline { get; set; }

    public int StartViewIndex
    {
        get => IsExpertStart ? 1 : 0;
        set => IsExpertStart = value == 1;
    }

    public Task SaveAsync() => _settings.SaveAsync(_settings.Current with
    {
        Language = LanguageValues[Math.Clamp(LanguageIndex, 0, LanguageValues.Length - 1)],
        StartView = IsExpertStart ? ViewMode.Expert : ViewMode.Simple,
        ShowSystemDevices = ShowSystemDevices,
        UseSerialNumberInLinks = UseSerialNumberInLinks,
        IncludeSerialNumberInExports = IncludeSerialNumberInExports,
        UpdateCatalogOnline = UpdateCatalogOnline,
        EnabledVersionSources = [.. VersionSources.Where(v => v.IsEnabled).Select(v => v.Id)],
    });

    [RelayCommand]
    private static void OpenLogFolder()
    {
        var logs = Path.Combine(AppInfo.DataDirectory, "logs");
        Directory.CreateDirectory(logs);
        Launcher.OpenLocal(logs);
    }
}

public sealed partial class VersionSourceToggle(string id, bool isEnabled, bool? isApplicable) : ObservableObject
{
    public string Id { get; } = id;

    public string Title { get; } = Localizer.T($"VersionSource_{id}");

    public string Privacy { get; } = Localizer.T($"VersionSource_{id}_Privacy");

    /// <summary>Shown when the last scan says this source does not cover the PC (e.g. Dell catalog on an MSI board).</summary>
    public string ApplicabilityText { get; } = isApplicable == false ? Localizer.T("Settings_NotApplicable") : string.Empty;

    [ObservableProperty] public partial bool IsEnabled { get; set; } = isEnabled;
}
