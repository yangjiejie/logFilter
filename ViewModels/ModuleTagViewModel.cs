using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LogFilterApp.ViewModels;

public partial class ColorSwatch : ObservableObject
{
    public ColorSwatch(string hex, ICommand applyCommand)
    {
        Hex = hex;
        Brush = new SolidColorBrush(Avalonia.Media.Color.Parse(hex));
        ApplyCommand = applyCommand;
    }

    public string Hex { get; }

    public IBrush Brush { get; }

    public ICommand ApplyCommand { get; }
}

/// <summary>左侧模块（标签）筛选项</summary>
public partial class ModuleTagViewModel : ObservableObject
{
    public static readonly string[] PaletteColors =
    {
        "#D4D4D4", "#4FC3F7", "#81C784", "#FFB74D", "#BA68C8", "#F06292",
        "#4DB6AC", "#FFD54F", "#9575CD", "#64B5F6", "#A1887F", "#E57373",
    };

    public ModuleTagViewModel(string name)
    {
        Name = name;
        foreach (var hex in PaletteColors)
        {
            Palette.Add(new ColorSwatch(hex, ApplyColorCommand));
        }
    }

    public string Name { get; }

    public ObservableCollection<ColorSwatch> Palette { get; } = new();

    [ObservableProperty] private bool _isVisible = true;

    [ObservableProperty] private bool _showStackTrace;

    [ObservableProperty] private string _colorHex = PaletteColors[0];

    public IBrush Brush => new SolidColorBrush(Avalonia.Media.Color.Parse(ColorHex));

    private int _totalCount;
    private int _filteredCount;

    public int TotalCount
    {
        get => _totalCount;
        set
        {
            if (SetProperty(ref _totalCount, value))
            {
                OnPropertyChanged(nameof(CountText));
            }
        }
    }

    public int FilteredCount
    {
        get => _filteredCount;
        set
        {
            if (SetProperty(ref _filteredCount, value))
            {
                OnPropertyChanged(nameof(CountText));
            }
        }
    }

    public string CountText => $"{FilteredCount}/{TotalCount}";

    [RelayCommand]
    private void ApplyColor(ColorSwatch? swatch)
    {
        if (swatch != null)
        {
            ColorHex = swatch.Hex;
        }
    }

    partial void OnColorHexChanged(string value) => OnPropertyChanged(nameof(Brush));

    /// <summary>过滤条件变化时由宿主调用（避免逐条赋值触发大量通知）</summary>
    public void SetCountsSilent(int total, int filtered)
    {
        _totalCount = total;
        _filteredCount = filtered;
    }

    public void NotifyCounts()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(FilteredCount));
        OnPropertyChanged(nameof(CountText));
    }
}
