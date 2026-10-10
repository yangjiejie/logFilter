using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogFilterApp.Models;
using LogFilterApp.Services;

namespace LogFilterApp.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly ConfigService _config = new();
    private readonly ModuleResolver _resolver = new();
    private readonly List<LogEntry> _allLogs = new();
    private readonly Dictionary<string, TagDisplayInfo> _display = new();
    private readonly HashSet<ModuleTagViewModel> _subscribed = new();

    private LogParser _parser = null!;
    private ParseProfile _profile = null!;
    private readonly DispatcherTimer _searchTimer;
    private Regex? _searchRegex;
    private bool _searchRegexDirty = true;
    private int _nextIndex;
    private string? _lastDir;

    public BulkObservableCollection<LogEntry> VisibleLogs { get; } = new();

    public ObservableCollection<ModuleTagViewModel> Modules { get; } = new();

    public BulkObservableCollection<ModuleRule> ModuleRules { get; } = new();

    public string ConfigDir => _config.ConfigDir;

    public MainWindowViewModel()
    {
        _profile = _config.LoadProfile();
        _parser = new LogParser(_resolver, _profile);
        ModuleRules.ReplaceAll(_config.LoadModules());
        _resolver.SetRules(ModuleRules);

        foreach (var kv in _config.LoadDisplay())
        {
            _display[kv.Key] = kv.Value;
        }

        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            ApplyFilter();
        };

        StatusText = $"就绪 · 配置目录 {_config.ConfigDir}";
    }

    // ---------------- 过滤条件 ----------------

    [ObservableProperty] private string _searchText = "";

    [ObservableProperty] private bool _useRegex;

    [ObservableProperty] private bool _caseSensitive;

    [ObservableProperty] private bool _showLog = true;

    [ObservableProperty] private bool _showWarning = true;

    [ObservableProperty] private bool _showError = true;

    [ObservableProperty] private bool _showException = true;

    [ObservableProperty] private bool _showAssert = true;

    [ObservableProperty] private LogEntry? _selectedLog;

    [ObservableProperty] private string _detailText = "";

    [ObservableProperty] private string _statusText = "就绪";

    [ObservableProperty] private bool _isBusy;

    [ObservableProperty] private int _totalCount;

    [ObservableProperty] private int _visibleCount;

    partial void OnSearchTextChanged(string value)
    {
        _searchRegexDirty = true;
        RestartSearchTimer();
    }

    partial void OnUseRegexChanged(bool value)
    {
        _searchRegexDirty = true;
        ApplyFilter();
    }

    partial void OnCaseSensitiveChanged(bool value)
    {
        _searchRegexDirty = true;
        ApplyFilter();
    }

    partial void OnShowLogChanged(bool value) => ApplyFilter();

    partial void OnShowWarningChanged(bool value) => ApplyFilter();

    partial void OnShowErrorChanged(bool value) => ApplyFilter();

    partial void OnShowExceptionChanged(bool value) => ApplyFilter();

    partial void OnShowAssertChanged(bool value) => ApplyFilter();

    partial void OnSelectedLogChanged(LogEntry? value) => BuildDetail(value);

    // ---------------- 数据加载 ----------------

    public async Task LoadFilesAsync(IEnumerable<string> paths)
    {
        var list = paths.Where(File.Exists).ToList();
        if (list.Count == 0)
        {
            return;
        }

        IsBusy = true;
        StatusText = "解析中...";
        try
        {
            var parsed = new List<LogEntry>();
            foreach (var path in list)
            {
                var text = await Task.Run(() => ReadAllTextAuto(path));
                parsed.AddRange(await Task.Run(() => _parser.Parse(text, _nextIndex + parsed.Count)));
            }

            AppendLogs(parsed, list.Count == 1 ? Path.GetFileName(list[0]) : $"{list.Count} 个文件");
        }
        catch (Exception ex)
        {
            StatusText = $"解析失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LoadTextAsync(string text, string? sourceName = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        IsBusy = true;
        StatusText = "解析中...";
        try
        {
            var parsed = await Task.Run(() => _parser.Parse(text, _nextIndex));
            AppendLogs(parsed, sourceName);
        }
        catch (Exception ex)
        {
            StatusText = $"解析失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AppendLogs(List<LogEntry> logs, string? sourceName = null)
    {
        if (logs.Count > 0)
        {
            _nextIndex = logs[^1].Index + 1;
        }

        _allLogs.AddRange(logs);
        RebuildModules();
        ApplyFilter();
        StatusText = $"已加载 {logs.Count} 条{(string.IsNullOrEmpty(sourceName) ? "" : " · " + sourceName)} · 总计 {_allLogs.Count} 条";
    }

    // ---------------- 模块列表 ----------------

    private void RebuildModules()
    {
        foreach (var old in Modules)
        {
            old.PropertyChanged -= OnModulePropertyChanged;
        }

        _subscribed.Clear();

        var existing = Modules.ToDictionary(m => m.Name, m => m);
        var groups = _allLogs
            .GroupBy(l => l.ModuleName)
            .Select(g => (Name: g.Key, Count: g.Count()))
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var next = new List<ModuleTagViewModel>(groups.Count);
        var colorIndex = 0;
        foreach (var (name, count) in groups)
        {
            if (!existing.TryGetValue(name, out var vm))
            {
                vm = new ModuleTagViewModel(name);
                if (_display.TryGetValue(name, out var saved))
                {
                    vm.IsVisible = saved.IsVisible;
                    vm.ColorHex = saved.ColorHex;
                    vm.ShowStackTrace = saved.ShowStackTrace;
                }
                else
                {
                    vm.ColorHex = ModuleTagViewModel.PaletteColors[colorIndex++ % ModuleTagViewModel.PaletteColors.Length];
                }
            }

            vm.TotalCount = count;
            next.Add(vm);
        }

        Modules.Clear();
        foreach (var vm in next)
        {
            Modules.Add(vm);
            if (_subscribed.Add(vm))
            {
                vm.PropertyChanged += OnModulePropertyChanged;
            }
        }
    }

    private void OnModulePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ModuleTagViewModel.IsVisible)
            or nameof(ModuleTagViewModel.ShowStackTrace)
            or nameof(ModuleTagViewModel.ColorHex))
        {
            ApplyFilter();
        }
    }

    /// <summary>模块映射规则被修改后，重新计算已有日志的归属</summary>
    private void ReResolveModules()
    {
        foreach (var log in _allLogs)
        {
            log.ModuleName = _resolver.Resolve(log.SourcePath, _profile.UnknownModuleName);
        }

        RebuildModules();
    }

    // ---------------- 过滤 ----------------

    private void RestartSearchTimer()
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void BuildSearchRegex()
    {
        _searchRegexDirty = false;
        _searchRegex = null;
        if (!UseRegex || string.IsNullOrWhiteSpace(SearchText))
        {
            return;
        }

        try
        {
            _searchRegex = new Regex(
                SearchText,
                RegexOptions.Compiled | (CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase));
        }
        catch
        {
            _searchRegex = null;
        }
    }

    private bool LevelAllowed(LogLevel level) => level switch
    {
        LogLevel.Log => ShowLog,
        LogLevel.Warning => ShowWarning,
        LogLevel.Error => ShowError,
        LogLevel.Exception => ShowException,
        LogLevel.Assert => ShowAssert,
        _ => true,
    };

    private bool MatchSearch(LogEntry log)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        if (UseRegex)
        {
            if (_searchRegex == null)
            {
                return true; // 正则非法时不拦截
            }

            return _searchRegex.IsMatch(log.Message)
                   || _searchRegex.IsMatch(log.StackTrace)
                   || _searchRegex.IsMatch(log.ModuleName);
        }

        var cmp = CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return log.Message.Contains(SearchText, cmp)
               || log.StackTrace.Contains(SearchText, cmp)
               || log.ModuleName.Contains(SearchText, cmp);
    }

    private void ApplyFilter()
    {
        if (_searchRegexDirty)
        {
            BuildSearchRegex();
        }

        var map = new Dictionary<string, ModuleTagViewModel>(Modules.Count);
        foreach (var m in Modules)
        {
            m.SetCountsSilent(m.TotalCount, 0);
            map[m.Name] = m;
        }

        var result = new List<LogEntry>();
        foreach (var log in _allLogs)
        {
            if (!LevelAllowed(log.Level))
            {
                continue;
            }

            if (!map.TryGetValue(log.ModuleName, out var tag) || !tag.IsVisible)
            {
                continue;
            }

            if (!MatchSearch(log))
            {
                continue;
            }

            tag.SetCountsSilent(tag.TotalCount, tag.FilteredCount + 1);
            var stack = tag.ShowStackTrace ? log.StackTrace : "";
            log.DisplayStack = stack ?? "";
            log.HasDisplayStack = !string.IsNullOrEmpty(stack);
            log.ModuleBrush = tag.Brush;
            result.Add(log);
        }

        foreach (var m in Modules)
        {
            m.NotifyCounts();
        }

        VisibleLogs.ReplaceAll(result);
        TotalCount = _allLogs.Count;
        VisibleCount = result.Count;
    }

    private void BuildDetail(LogEntry? log)
    {
        if (log == null)
        {
            DetailText = "";
            return;
        }

        var sb = new StringBuilder();
        sb.Append('[').Append(log.Time.Length > 0 ? log.Time : "--:--:--").Append("]  ");
        sb.Append('[').Append(log.Level).Append("]  ");
        sb.Append("模块: ").Append(log.ModuleName).Append("   #").Append(log.Index);
        if (!string.IsNullOrEmpty(log.SourcePath))
        {
            sb.Append("   源码: ").Append(log.SourcePath);
        }

        sb.AppendLine();
        sb.AppendLine(new string('-', 60));
        sb.AppendLine(log.Message);
        if (log.HasStack)
        {
            sb.AppendLine();
            sb.AppendLine("---- 堆栈 ----");
            sb.AppendLine(log.StackTrace);
        }

        DetailText = sb.ToString();
    }

    // ---------------- 命令 ----------------

    [RelayCommand]
    private async Task OpenFilesAsync()
    {
        var owner = AppHelper.MainWindow;
        if (owner == null)
        {
            return;
        }

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择日志文件",
            AllowMultiple = true,
            SuggestedStartLocation = await TryGetStartFolderAsync(owner),
            FileTypeFilter = new[]
            {
                new FilePickerFileType("日志文件") { Patterns = new[] { "*.log", "*.txt", "*.csv" } },
                FilePickerFileTypes.All,
            },
        });

        var paths = files.Select(f => f.TryGetLocalPath()).Where(p => !string.IsNullOrEmpty(p)).Select(p => p!).ToList();
        if (paths.Count > 0)
        {
            _lastDir = Path.GetDirectoryName(paths[0]);
            await LoadFilesAsync(paths);
        }
    }

    private async Task<IStorageFolder?> TryGetStartFolderAsync(TopLevel owner)
    {
        var dir = _lastDir ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        try
        {
            return await owner.StorageProvider.TryGetFolderFromPathAsync(dir);
        }
        catch
        {
            return null;
        }
    }

    [RelayCommand]
    private async Task PasteAsync()
    {
        var owner = AppHelper.MainWindow;
        if (owner?.Clipboard == null)
        {
            return;
        }

        var text = await owner.Clipboard.GetTextAsync();
        if (!string.IsNullOrEmpty(text))
        {
            await LoadTextAsync(text, "剪贴板");
        }
    }

    [RelayCommand]
    private void ClearAll()
    {
        _allLogs.Clear();
        _nextIndex = 0;
        SelectedLog = null;
        Modules.Clear();
        VisibleLogs.ReplaceAll(Array.Empty<LogEntry>());
        TotalCount = 0;
        VisibleCount = 0;
        StatusText = "已清空";
    }

    [RelayCommand]
    private void SelectAllModules()
    {
        foreach (var m in Modules)
        {
            m.IsVisible = true;
        }
    }

    [RelayCommand]
    private void InvertModules()
    {
        foreach (var m in Modules)
        {
            m.IsVisible = !m.IsVisible;
        }
    }

    [RelayCommand]
    private void ShowAllStack()
    {
        var target = Modules.Any(m => !m.ShowStackTrace);
        foreach (var m in Modules)
        {
            m.ShowStackTrace = target;
        }
    }

    [RelayCommand]
    private async Task CopyVisibleAsync()
    {
        var sb = new StringBuilder();
        var tagMap = Modules.ToDictionary(m => m.Name, m => m);
        foreach (var log in VisibleLogs)
        {
            var withStack = tagMap.TryGetValue(log.ModuleName, out var t) && t.ShowStackTrace;
            sb.AppendLine(log.ToExportText(withStack));
        }

        var owner = AppHelper.MainWindow;
        if (owner?.Clipboard != null)
        {
            await owner.Clipboard.SetTextAsync(sb.ToString());
            StatusText = $"已复制 {VisibleLogs.Count} 条日志到剪贴板";
        }
    }

    [RelayCommand]
    private async Task CopySelectedAsync()
    {
        if (SelectedLog == null || AppHelper.MainWindow?.Clipboard == null)
        {
            return;
        }

        await AppHelper.MainWindow.Clipboard.SetTextAsync(SelectedLog.ToExportText(true));
        StatusText = "已复制当前日志";
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        var owner = AppHelper.MainWindow;
        if (owner == null || VisibleLogs.Count == 0)
        {
            return;
        }

        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出当前可见日志",
            SuggestedFileName = $"logfilter_{DateTime.Now:yyyyMMdd_HHmmss}",
            DefaultExtension = "txt",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("文本") { Patterns = new[] { "*.txt" } },
                FilePickerFileTypes.All,
            },
        });

        if (file == null)
        {
            return;
        }

        var sb = new StringBuilder();
        var tagMap = Modules.ToDictionary(m => m.Name, m => m);
        foreach (var log in VisibleLogs)
        {
            var withStack = tagMap.TryGetValue(log.ModuleName, out var t) && t.ShowStackTrace;
            sb.AppendLine(log.ToExportText(withStack));
        }

        await using (var stream = await file.OpenWriteAsync())
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
        {
            await writer.WriteAsync(sb.ToString());
        }

        StatusText = $"已导出 {VisibleLogs.Count} 条到 {file.Name}";
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        var owner = AppHelper.MainWindow;
        if (owner == null)
        {
            return;
        }

        var vm = new SettingsViewModel(_config, ModuleRules, _profile);
        var window = new Views.SettingsWindow { DataContext = vm };
        await window.ShowDialog(owner);

        if (vm.Saved)
        {
            ModuleRules.ReplaceAll(_config.LoadModules());
            _resolver.SetRules(ModuleRules);
            _profile = _config.LoadProfile();
            _parser.SetProfile(_profile);
            ReResolveModules();
            ApplyFilter();
            StatusText = "配置已应用";
        }
    }

    [RelayCommand]
    private void SaveDisplay()
    {
        foreach (var m in Modules)
        {
            _display[m.Name] = new TagDisplayInfo
            {
                IsVisible = m.IsVisible,
                ColorHex = m.ColorHex,
                ShowStackTrace = m.ShowStackTrace,
            };
        }

        _config.SaveDisplay(_display);
        StatusText = "显示配置已保存";
    }

    public void OnClosing()
    {
        SaveDisplay();
    }

    // ---------------- 编码探测 ----------------

    private static string ReadAllTextAuto(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var enc = DetectBom(bytes) ?? Encoding.UTF8;
        var text = enc.GetString(bytes);
        if (text.Contains('�'))
        {
            try
            {
                var gbk = Encoding.GetEncoding("GB18030");
                var alt = gbk.GetString(bytes);
                if (!alt.Contains('�'))
                {
                    return alt;
                }
            }
            catch
            {
                /* 不支持该编码时使用 UTF8 结果 */
            }
        }

        return text;
    }

    private static Encoding? DetectBom(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return new UTF8Encoding(true);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode;
        }

        return null;
    }
}
