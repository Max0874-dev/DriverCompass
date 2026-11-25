using DriverCompass.App.Services;
using DriverCompass.App.ViewModels;
using DriverCompass.App.Views;
using DriverCompass.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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

    public async Task<PrivacyChoices> ShowPrivacyNoticeAsync(PrivacyChoices current)
    {
        var panel = new StackPanel { Spacing = 10, MaxWidth = 540 };
        panel.Children.Add(Text(Localizer.T("Privacy_Intro")));

        var catalog = new ToggleSwitch { Header = Localizer.T("Settings_UpdateCatalog"), IsOn = current.UpdateCatalogOnline };
        panel.Children.Add(catalog);
        panel.Children.Add(Text(Localizer.T("Privacy_Catalog_Help"), secondary: true));

        // The Store edition has no update hint (the Store updates the app) and installs no drivers.
        ToggleSwitch? updates = null;
        if (MainViewModel.UpdateHintAvailable)
        {
            updates = new ToggleSwitch { Header = Localizer.T("Settings_CheckAppUpdates"), IsOn = current.CheckForAppUpdates };
            panel.Children.Add(updates);
            panel.Children.Add(Text(Localizer.T("Settings_CheckAppUpdates_Help"), secondary: true));
        }

        panel.Children.Add(Text(Localizer.T(ViewModel.InstallationAvailable ? "Privacy_OnClick" : "Privacy_OnClick_NoInstall")));
        panel.Children.Add(Text(Localizer.T("Privacy_Later"), secondary: true));

        var policy = new HyperlinkButton { Content = Localizer.T("Privacy_ReadPolicy"), Padding = new Thickness(0) };
        policy.Click += async (_, _) => await OpenPrivacyPolicyAsync();
        panel.Children.Add(policy);

        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = Localizer.T("Privacy_Title"),
            Content = new ScrollViewer { Content = panel, MaxHeight = 560 },
            PrimaryButtonText = Localizer.T("Privacy_Continue"),
            DefaultButton = ContentDialogButton.Primary,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        };

        // Any way of closing keeps what the switches show – there is no "accept or leave".
        await dialog.ShowAsync();
        return new PrivacyChoices(catalog.IsOn, updates?.IsOn ?? false);
    }

    /// <summary>Opens the PRIVACY.md shipped next to the EXE; falls back to the copy in the repository.</summary>
    private static async Task OpenPrivacyPolicyAsync()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "PRIVACY.md");
        if (File.Exists(local))
        {
            try
            {
                Launcher.OpenLocal(local);
                return;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // No app is associated with .md files – show the online copy instead.
            }
        }

        if (Uri.TryCreate($"{AppInfo.RepositoryUrl.TrimEnd('/')}/blob/main/PRIVACY.md", UriKind.Absolute, out var online))
        {
            await Launcher.OpenAsync(online);
        }
    }

    public async Task<bool> ShowSettingsAsync()
    {
        var settings = App.Services.GetRequiredService<Core.Abstractions.ISettingsService>();
        var vm = new SettingsViewModel(settings, ViewModel.CatalogVersion, ViewModel.IsVersionSourceApplicable);
        var dialog = new SettingsDialog(vm) { XamlRoot = Root.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return false;
        }

        await vm.SaveAsync();
        return true;
    }

    public async Task<bool> ConfirmInstallAsync(InstallConfirmation confirmation)
    {
        var panel = new StackPanel { Spacing = 8, MaxWidth = 520 };
        var oem = confirmation.SourceId != Core.Models.VersionSourceIds.WindowsUpdate;
        var vendor = Localizer.T($"VersionSource_{confirmation.SourceId}");
        panel.Children.Add(Text(oem ? Localizer.F("Install_Intro_Oem", vendor) : Localizer.T("Install_Intro")));
        panel.Children.Add(Text(Localizer.F("Install_Device", confirmation.DeviceName), bold: true));
        panel.Children.Add(Text(Localizer.F("Install_Driver", confirmation.DriverTitle)));
        if (!string.IsNullOrWhiteSpace(confirmation.VersionText))
        {
            panel.Children.Add(Text(Localizer.F("Install_Version", confirmation.VersionText)));
        }

        var steps = confirmation.SourceId == Core.Models.VersionSourceIds.Lenovo ? "Install_Steps_Lenovo" : "Install_Steps_Oem";
        panel.Children.Add(Text(oem ? Localizer.F(steps, vendor, Localizer.T($"PackageHost_{confirmation.SourceId}")) : Localizer.T("Install_Steps"), secondary: true));

        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = Localizer.T("Install_Title"),
            Content = panel,
            PrimaryButtonText = Localizer.T("Install_Confirm"),
            CloseButtonText = Localizer.T("Install_Cancel"),
            DefaultButton = ContentDialogButton.Close,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        };

        if (confirmation.NeedsDisclaimer)
        {
            // First installation: the disclaimer must be acknowledged explicitly.
            panel.Children.Add(new InfoBar
            {
                IsOpen = true,
                IsClosable = false,
                Severity = InfoBarSeverity.Warning,
                Message = Localizer.T("Install_Disclaimer"),
            });
            var accept = new CheckBox { Content = Localizer.T("Install_DisclaimerAccept") };
            dialog.IsPrimaryButtonEnabled = false;
            accept.Checked += (_, _) => dialog.IsPrimaryButtonEnabled = true;
            accept.Unchecked += (_, _) => dialog.IsPrimaryButtonEnabled = false;
            panel.Children.Add(accept);
        }

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public async Task<bool> ConfirmAsync(string title, string message, string primaryText)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 },
            PrimaryButtonText = primaryText,
            CloseButtonText = Localizer.T("Install_Cancel"),
            DefaultButton = ContentDialogButton.Close,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public async Task ShowGuideAsync(DeviceItemViewModel item, Core.Reporting.DriverGuideContent guide)
    {
        var installRequested = false;
        ContentDialog? dialog = null;

        var panel = new StackPanel { Spacing = 12, MaxWidth = 560 };
        panel.Children.Add(Text(item.Name, bold: true));
        if (!string.IsNullOrWhiteSpace(guide.Intro))
        {
            panel.Children.Add(Text(guide.Intro, secondary: true));
        }

        if (guide.Shortcut is { } shortcut)
        {
            panel.Children.Add(GuideRow(null, shortcut));
        }

        for (var i = 0; i < guide.Steps.Count; i++)
        {
            panel.Children.Add(GuideRow((i + 1).ToString(System.Globalization.CultureInfo.CurrentCulture), guide.Steps[i]));
        }

        if (guide.Hints.Count > 0)
        {
            panel.Children.Add(Text(Localizer.T("Guide_HintsHeader"), bold: true));
            foreach (var hint in guide.Hints)
            {
                panel.Children.Add(GuideRow("•", hint, secondary: true));
            }
        }

        dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = Localizer.T("Guide_Title"),
            Content = new ScrollViewer { Content = panel, MaxHeight = 560 },
            CloseButtonText = Localizer.T("Guide_Close"),
            DefaultButton = ContentDialogButton.Close,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        };

        await dialog.ShowAsync();

        // Only one dialog can be open: the install confirmation follows after the guide is closed.
        if (installRequested && item.InstallCommand.CanExecute(null))
        {
            await item.InstallCommand.ExecuteAsync(null);
        }

        FrameworkElement GuideRow(string? number, Core.Reporting.GuideStep step, bool secondary = false)
        {
            var grid = new Grid { ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(number is null ? 0 : 20) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            if (number is not null)
            {
                var n = Text(number, bold: true, secondary: secondary);
                grid.Children.Add(n);
            }

            var content = new StackPanel { Spacing = 4 };
            content.Children.Add(Text(step.Text, secondary: secondary));
            if (GuideButton(step) is { } button)
            {
                content.Children.Add(button);
            }

            Grid.SetColumn(content, 1);
            grid.Children.Add(content);
            return grid;
        }

        ButtonBase? GuideButton(Core.Reporting.GuideStep step)
        {
            (string? Label, Func<Task>? Action) choice = step.Action switch
            {
                Core.Reporting.GuideAction.OpenPrimaryLink when item.PrimaryLink is { } link =>
                    (link.Label, link.OpenLinkAsync),
                Core.Reporting.GuideAction.OpenVersionSource when item.VersionSourceUri is { } uri =>
                    (Localizer.T("Version_OpenSource"), () => Launcher.OpenAsync(uri)),
                Core.Reporting.GuideAction.OpenWindowsUpdate =>
                    (Localizer.T("Version_OpenWindowsUpdate"), () => Launcher.OpenWindowsUpdateAsync()),
                Core.Reporting.GuideAction.OpenCatalogSearch when step.Value is { } id =>
                    (Localizer.T("Guide_Action_Catalog"), () => Launcher.OpenAsync(Core.Links.MicrosoftCatalogLinkSource.BuildSearchUri(id))),
                Core.Reporting.GuideAction.Copy when step.Value is { } value =>
                    (Localizer.T("Guide_Action_Copy"), () => { Launcher.CopyText(value); return Task.CompletedTask; }),
                Core.Reporting.GuideAction.Install when item.CanInstall =>
                    (Localizer.T("Simple_Install"), () => { installRequested = true; dialog?.Hide(); return Task.CompletedTask; }),
                _ => (null, null),
            };

            if (choice is not { Label: { } label, Action: { } action })
            {
                return null;
            }

            ButtonBase button = step.Action == Core.Reporting.GuideAction.Install
                ? new Button { Content = label, Style = (Style)Application.Current.Resources["AccentButtonStyle"] }
                : new HyperlinkButton { Content = label, Padding = new Thickness(0) };
            button.Click += async (_, _) => await action();
            return button;
        }
    }

    public async Task<Guid?> ShowInstallHistoryAsync(IReadOnlyList<InstallHistoryItem> items)
    {
        var dialog = new InstallHistoryDialog(items) { XamlRoot = Root.XamlRoot };
        return await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.Selected is { CanRollBack: true } selected
            ? selected.Id
            : null;
    }

    private static TextBlock Text(string text, bool bold = false, bool secondary = false)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        if (bold)
        {
            block.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        }

        if (secondary)
        {
            block.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        }

        return block;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_started)
        {
            return;
        }

        _started = true;

        // First start: the privacy notice comes before any network connection (vendor list, update check).
        await ViewModel.ShowPrivacyNoticeIfNeededAsync();

        var updateCheck = ViewModel.CheckForAppUpdateAsync();
        await ViewModel.ScanCommand.ExecuteAsync(null);
        await updateCheck;
    }
}
