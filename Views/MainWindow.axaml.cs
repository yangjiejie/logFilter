using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using LogFilterApp.ViewModels;

namespace LogFilterApp.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var vm = new MainWindowViewModel();
        DataContext = vm;
        Closing += (_, _) => vm.OnClosing();

        // 支持直接把日志文件拖进窗口
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files))
        {
            return;
        }

        var paths = new List<string>();
        foreach (var item in e.Data.GetFiles() ?? Enumerable.Empty<IStorageItem>())
        {
            if (item is IStorageFile file)
            {
                paths.Add(file.Path.LocalPath);
            }
        }

        if (paths.Count > 0 && DataContext is MainWindowViewModel vm)
        {
            await vm.LoadFilesAsync(paths);
        }
    }
}
