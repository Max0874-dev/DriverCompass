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

    public SettingsViewModel(ISettingsService settings, string catalogVersion)
    {
        _settings = settings;
        var s = settings.Current;
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
    });

    [RelayCommand]
    private static void OpenLogFolder()
    {
        var logs = Path.Combine(AppInfo.DataDirectory, "logs");
        Directory.CreateDirectory(logs);
        Launcher.OpenLocal(logs);
    }
}
