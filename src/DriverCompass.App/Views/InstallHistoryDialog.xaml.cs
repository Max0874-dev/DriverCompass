using DriverCompass.App.Services;
using DriverCompass.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DriverCompass.App.Views;

/// <summary>Install history; the primary button rolls back the selected installation (if possible).</summary>
public sealed partial class InstallHistoryDialog : ContentDialog
{
    public InstallHistoryDialog(IReadOnlyList<InstallHistoryItem> items)
    {
        InitializeComponent();
        Title = Localizer.T("History_Title");
        PrimaryButtonText = Localizer.T("History_Rollback");
        CloseButtonText = Localizer.T("History_Close");
        IsPrimaryButtonEnabled = false;

        HistoryList.ItemsSource = items;
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryList.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    public InstallHistoryItem? Selected => HistoryList.SelectedItem as InstallHistoryItem;

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        IsPrimaryButtonEnabled = Selected?.CanRollBack == true;
}
