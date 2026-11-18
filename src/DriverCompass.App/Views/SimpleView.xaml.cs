using DriverCompass.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace DriverCompass.App.Views;

public sealed partial class SimpleView : UserControl
{
    private MainViewModel? _viewModel;

    public SimpleView()
    {
        InitializeComponent();
    }

    /// <summary>Set once by the window before the view is loaded.</summary>
    public MainViewModel ViewModel
    {
        get => _viewModel ?? throw new InvalidOperationException("ViewModel not set.");
        set
        {
            _viewModel = value;
            GroupsSource.Source = value.SimpleGroups;
        }
    }
}
