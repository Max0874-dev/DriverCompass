using DriverCompass.App.Services;
using DriverCompass.App.ViewModels;
using DriverCompass.App.Views;
using DriverCompass.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.Storage.Pickers;

namespace DriverCompass.App;

public sealed partial class MainWindow : Window, IShellService
{
    private bool _started;

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        ViewModel.Shell = this;
        InitializeComponent();

        Title = AppInfo.DisplayName;
        SimpleViewControl.ViewModel = viewModel;
        ExpertViewControl.ViewModel = viewModel;

        if (Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
        {
            SystemBackdrop = new MicaBackdrop();
        }

        AppWindow.Resize(new SizeInt32(1320, 860));
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "DriverCompass.ico");
        if (File.Exists(icon))
        {
            AppWindow.SetIcon(icon);
        }

        Root.Loaded += OnLoaded;
    }

    public MainViewModel ViewModel { get; }

    public async Task<string?> PickSaveFileAsync(string suggestedName, string fileTypeLabel, string extension)
    {
        var picker = new FileSavePicker { SuggestedFileName = suggestedName, SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeChoices.Add(fileTypeLabel, [extension]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    public async Task<bool> ShowSettingsAsync()
    {
        var settings = App.Services.GetRequiredService<Core.Abstractions.ISettingsService>();
        var vm = new SettingsViewModel(settings, ViewModel.CatalogVersion);
        var dialog = new SettingsDialog(vm) { XamlRoot = Root.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return false;
        }

        await vm.SaveAsync();
        return true;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        await ViewModel.ScanCommand.ExecuteAsync(null);
    }
}
