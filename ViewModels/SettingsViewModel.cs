using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogFilterApp.Models;
using LogFilterApp.Services;

namespace LogFilterApp.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ConfigService _config;

    public BulkObservableCollection<ModuleRule> Rules { get; } = new();

    public string ConfigDir => _config.ConfigDir;

    /// <summary>保存后需要主窗口重新应用配置</summary>
    public bool Saved { get; private set; }

    /// <summary>请求关闭窗口</summary>
    public event EventHandler? RequestClose;

    [ObservableProperty] private ModuleRule? _selectedRule;

    // ---- 解析规则 ----
    [ObservableProperty] private string _timePattern = "";

    [ObservableProperty] private string _stackLinePattern = "";

    [ObservableProperty] private string _stackMethodPattern = "";

    [ObservableProperty] private string _pathPatternsText = "";

    [ObservableProperty] private string _skipPrefixesText = "";

    [ObservableProperty] private string _unknownModuleName = "other";

    [ObservableProperty] private bool _mergeMultilineMessage = true;

    [ObservableProperty] private string _assertPatterns = "";

    [ObservableProperty] private string _exceptionPatterns = "";

    [ObservableProperty] private string _errorPatterns = "";

    [ObservableProperty] private string _warningPatterns = "";

    [ObservableProperty] private string _statusText = "";

    public SettingsViewModel(ConfigService config, IEnumerable<ModuleRule> rules, ParseProfile profile)
    {
        _config = config;
        Rules.ReplaceAll(rules.Select(r => new ModuleRule
        {
            Name = r.Name,
            Patterns = new System.Collections.ObjectModel.ObservableCollection<string>(r.Patterns),
        }));
        SelectedRule = Rules.FirstOrDefault();
        LoadProfile(profile);
    }

    private void LoadProfile(ParseProfile p)
    {
        TimePattern = p.TimePattern;
        StackLinePattern = p.StackLinePattern;
        StackMethodPattern = p.StackMethodPattern;
        PathPatternsText = Lines(p.PathPatterns);
        SkipPrefixesText = Lines(p.SkipStackPrefixes);
        UnknownModuleName = p.UnknownModuleName;
        MergeMultilineMessage = p.MergeMultilineMessage;
        AssertPatterns = Lines(GetLevel(p, "Assert"));
        ExceptionPatterns = Lines(GetLevel(p, "Exception"));
        ErrorPatterns = Lines(GetLevel(p, "Error"));
        WarningPatterns = Lines(GetLevel(p, "Warning"));
    }

    private ParseProfile BuildProfile() => new()
    {
        TimePattern = TimePattern,
        StackLinePattern = StackLinePattern,
        StackMethodPattern = StackMethodPattern,
        PathPatterns = SplitLines(PathPatternsText),
        SkipStackPrefixes = SplitLines(SkipPrefixesText),
        UnknownModuleName = string.IsNullOrWhiteSpace(UnknownModuleName) ? "other" : UnknownModuleName,
        MergeMultilineMessage = MergeMultilineMessage,
        LevelPatterns = new Dictionary<string, List<string>>
        {
            ["Assert"] = SplitLines(AssertPatterns),
            ["Exception"] = SplitLines(ExceptionPatterns),
            ["Error"] = SplitLines(ErrorPatterns),
            ["Warning"] = SplitLines(WarningPatterns),
        },
    };

    private static List<string> GetLevel(ParseProfile p, string name) =>
        p.LevelPatterns != null && p.LevelPatterns.TryGetValue(name, out var v) ? v : new List<string>();

    private static string Lines(IEnumerable<string>? values) =>
        values == null ? "" : string.Join(Environment.NewLine, values);

    private static List<string> SplitLines(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? new List<string>()
            : text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .Where(s => s.Length > 0)
                  .ToList();

    [RelayCommand]
    private void AddRule()
    {
        var rule = new ModuleRule { Name = "新模块" };
        Rules.Add(rule);
        SelectedRule = rule;
    }

    [RelayCommand]
    private void RemoveRule(ModuleRule? rule)
    {
        if (rule == null)
        {
            return;
        }

        var index = Rules.IndexOf(rule);
        Rules.Remove(rule);
        SelectedRule = Rules.Count > 0 ? Rules[Math.Min(index, Rules.Count - 1)] : null;
    }

    [RelayCommand]
    private async Task ImportFromFileAsync()
    {
        var owner = AppHelper.MainWindow;
        if (owner == null)
        {
            return;
        }

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入模块映射 (LogFilter.json)",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } },
                FilePickerFileTypes.All,
            },
        });

        var path = files.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            Rules.ReplaceAll(_config.LoadModulesFromFile(path!));
            SelectedRule = Rules.FirstOrDefault();
            StatusText = $"已导入 {Rules.Count} 条规则 · {path}";
        }
        catch (Exception ex)
        {
            StatusText = $"导入失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportToFileAsync()
    {
        var owner = AppHelper.MainWindow;
        if (owner == null)
        {
            return;
        }

        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出模块映射",
            SuggestedFileName = "LogFilter",
            DefaultExtension = "json",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } },
                FilePickerFileTypes.All,
            },
        });

        if (file == null)
        {
            return;
        }

        try
        {
            var content = ConfigService.SerializeModules(Rules);
            await using (var stream = await file.OpenWriteAsync())
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                await writer.WriteAsync(content);
            }

            StatusText = $"已导出到 {file.Name}";
        }
        catch (Exception ex)
        {
            StatusText = $"导出失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private void Save()
    {
        try
        {
            _config.SaveModules(Rules);
            _config.SaveProfile(BuildProfile());
            Saved = true;
            StatusText = $"已保存到 {_config.ConfigDir}";
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StatusText = $"保存失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private void ResetProfile()
    {
        LoadProfile(new ParseProfile());
        StatusText = "解析规则已恢复默认（需点击保存生效）";
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(this, EventArgs.Empty);
}
