using System.ComponentModel;
using DriverCompass.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace DriverCompass.App.Views;

public sealed partial class ExpertView : UserControl, INotifyPropertyChanged
{
    private MainViewModel? _viewModel;

    public ExpertView()
    {
        InitializeComponent();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Set once by the window before the view is loaded.</summary>
    public MainViewModel ViewModel
    {
        get => _viewModel ?? throw new InvalidOperationException("ViewModel not set.");
        set
        {
            _viewModel = value;
            value.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.SelectedItem))
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelection)));
                    if (!ReferenceEquals(DeviceList.SelectedItem, value.SelectedItem))
                    {
                        DeviceList.SelectedItem = value.SelectedItem;
                    }
                }
            };
        }
    }

    public bool HasSelection => _viewModel?.SelectedItem is not null;

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.SelectedItem = DeviceList.SelectedItem as DeviceItemViewModel;
        }
    }
}
