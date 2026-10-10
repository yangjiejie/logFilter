using System;
using Avalonia.Controls;
using LogFilterApp.ViewModels;

namespace LogFilterApp.Views;

public partial class SettingsWindow : Window
{
    private SettingsViewModel? _vm;

    public SettingsWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closing += OnClosing;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        Bind();
    }

    private void Bind()
    {
        if (_vm != null)
        {
            _vm.RequestClose -= OnRequestClose;
        }

        _vm = DataContext as SettingsViewModel;
        if (_vm != null)
        {
            _vm.RequestClose += OnRequestClose;
        }
    }

    private void OnRequestClose(object? sender, EventArgs e) => Close();

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_vm != null)
        {
            _vm.RequestClose -= OnRequestClose;
        }
    }
}
