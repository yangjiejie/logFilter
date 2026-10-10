using System;
using System.Collections.Generic;
using System.Text;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LogFilterApp.Models;

public enum LogLevel
{
    Log = 0,
    Warning = 1,
    Error = 2,
    Exception = 3,
    Assert = 4,
}

public partial class LogEntry : ObservableObject
{
    /// <summary>全局递增序号，用于定位原始文件中的位置</summary>
    public int Index { get; set; }

    /// <summary>日志时间戳文本（原始，可能是 12:00:00.123 或 2024-01-01 12:00:00,123）</summary>
    public string Time { get; set; } = "";

    /// <summary>日志正文（可能包含多行）</summary>
    public string Message { get; set; } = "";

    /// <summary>堆栈信息（多行）</summary>
    public string StackTrace { get; set; } = "";

    public LogLevel Level { get; set; } = LogLevel.Log;

    /// <summary>从堆栈中提取到的源码路径，例如 hot_fix/ctrl/base/BcCtrl</summary>
    public string SourcePath { get; set; } = "";

    /// <summary>归属模块（由模块映射规则解析而来）</summary>
    public string ModuleName { get; set; } = "other";

    /// <summary>列表里显示的第一行正文</summary>
    public string FirstLine { get; set; } = "";

    // ==== 以下为视图展示用的运行时状态，由过滤阶段写入 ====
    [ObservableProperty] private string _displayStack = "";

    [ObservableProperty] private bool _hasDisplayStack;

    [ObservableProperty] private IBrush? _moduleBrush;

    public bool HasStack => !string.IsNullOrEmpty(StackTrace);

    public string LevelText => Level switch
    {
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Exception => "EXC",
        LogLevel.Assert => "AST",
        _ => "LOG",
    };

    public IBrush LevelBrush => LevelBrushCache.Get(Level);

    public string ToExportText(bool withStack)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(Time))
        {
            sb.Append('[').Append(Time).Append("] ");
        }

        sb.Append('[').Append(LevelText).Append("] ");
        sb.Append('[').Append(ModuleName).Append("] ");
        sb.Append(Message);
        if (withStack && HasStack)
        {
            sb.AppendLine();
            sb.Append(StackTrace);
        }

        return sb.ToString();
    }

    public override string ToString() => ToExportText(true);
}

internal static class LevelBrushCache
{
    private static readonly Dictionary<LogLevel, IBrush> Cache = new()
    {
        [LogLevel.Log] = new SolidColorBrush(Color.Parse("#9CDCFE")),
        [LogLevel.Warning] = new SolidColorBrush(Color.Parse("#E2C08D")),
        [LogLevel.Error] = new SolidColorBrush(Color.Parse("#F48771")),
        [LogLevel.Exception] = new SolidColorBrush(Color.Parse("#FF6B6B")),
        [LogLevel.Assert] = new SolidColorBrush(Color.Parse("#C586C0")),
    };

    public static IBrush Get(LogLevel level) =>
        Cache.TryGetValue(level, out var b) ? b : Cache[LogLevel.Log];
}
