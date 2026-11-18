using DriverCompass.App.Services;
using DriverCompass.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace DriverCompass.App.Views;

public sealed partial class SettingsDialog : ContentDialog
{
    public SettingsDialog(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Title = Localizer.T("Settings_Title");
        PrimaryButtonText = Localizer.T("Settings_Save");
        CloseButtonText = Localizer.T("Settings_Cancel");
    }

    public SettingsViewModel ViewModel { get; }
}
