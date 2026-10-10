using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;

namespace LogFilterApp.Models;

/// <summary>
/// 模块映射规则：模块名 -&gt; 若干路径前缀。
/// 与 Unity 工程里 environment/LogFilter.json 的格式保持一致（Dictionary&lt;string, List&lt;string&gt;&gt;）。
/// </summary>
public partial class ModuleRule : ObservableObject
{
    [ObservableProperty] private string _name = "";

    public ObservableCollection<string> Patterns { get; set; } = new();

    /// <summary>UI 里用换行分隔编辑</summary>
    [JsonIgnore]
    public string PatternsText
    {
        get => string.Join(Environment.NewLine, Patterns);
        set
        {
            Patterns.Clear();
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            foreach (var line in value.Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (line.Length > 0)
                {
                    Patterns.Add(line);
                }
            }
        }
    }
}

/// <summary>
/// 解析规则。线上/离线日志格式千差万别，这里全部做成可配置的正则，
/// 默认值与 Unity 的 LogHelper 保持一致。
/// </summary>
public sealed class ParseProfile
{
    /// <summary>行首时间戳，命名组 t 为时间文本</summary>
    public string TimePattern { get; set; } =
        @"^\[?\s*(?<t>(?:\d{4}[-/]\d{2}[-/]\d{2}[ T])?\d{1,2}:\d{2}:\d{2}(?:[.,]\d{1,3})?)\s*\]?";

    /// <summary>判定某一行是否为堆栈行</summary>
    public string StackLinePattern { get; set; } =
        @"(?:^|\()\s*at\s+|^\s*UnityEngine\.[A-Za-z0-9_.]+:|^\s*\(Filename:|^\s*\[string ""|^\s*#\d+\s+0x";

    /// <summary>
    /// 已经进入堆栈上下文后，额外按"命名空间.类:方法(参数)"形式判定堆栈行。
    /// 例如封装日志工具产生的 HotFix.UtilTool.CommonUtils:Log(String)。
    /// </summary>
    public string StackMethodPattern { get; set; } =
        @"^\s*[A-Za-z0-9_.]+:[A-Za-z0-9_.<>\[\]0-9, ]+\s*(?:\(|\[)";

    /// <summary>
    /// 从堆栈行里提取源码路径的正则，第 1 个分组必须是路径。
    /// 取第一个匹配成功的规则。
    /// </summary>
    public List<string> PathPatterns { get; set; } = new()
    {
        @"\bat\s+Assets/([^:\s]+)\.cs:\d+",
        @"\[string ""([^""]+)""\]:",
    };

    /// <summary>
    /// 封装日志工具产生的堆栈行前缀：这类行一律按堆栈处理，但不参与模块归属判定
    /// </summary>
    public List<string> SkipStackPrefixes { get; set; } = new()
    {
        "HotFix.UtilTool.CommonUtils",
        "UnityEngine.Debug",
        "UnityEngine.Logger",
        "UnityEngine.DebugLogHandler",
    };

    /// <summary>级别识别规则：级别名 -&gt; 正则列表，按顺序匹配，先命中者生效</summary>
    public Dictionary<string, List<string>> LevelPatterns { get; set; } = new()
    {
        ["Exception"] = new List<string>
        {
            @"(?i)^\s*[A-Za-z0-9_.]*Exception\b",
            @"(?i)\[\s*Exception\s*\]",
            @"(?i)^\s*Unhandled\s+exception",
        },
        ["Assert"] = new List<string>
        {
            @"(?i)^\s*\[?\s*Assert(ion)?\s*\]?\s*[:：]?",
            @"(?i)^\s*Assertion\s+failed",
        },
        ["Error"] = new List<string>
        {
            @"(?i)^\s*\[?\s*Error\s*\]?\s*[:：]",
            @"(?i)\[\s*Error\s*\]",
            @"(?i)^\s*\[?\s*ERR\s*\]",
        },
        ["Warning"] = new List<string>
        {
            @"(?i)^\s*\[?\s*Warning\s*\]?\s*[:：]",
            @"(?i)\[\s*Warning\s*\]",
            @"(?i)^\s*\[?\s*(WRN|Warn)\s*\]",
        },
    };

    /// <summary>匹配不到任何模块时的兜底模块名</summary>
    public string UnknownModuleName { get; set; } = "other";

    /// <summary>多行正文是否合并到同一条日志</summary>
    public bool MergeMultilineMessage { get; set; } = true;

    public ParseProfile Clone()
    {
        var json = JsonConvert.SerializeObject(this);
        return JsonConvert.DeserializeObject<ParseProfile>(json) ?? new ParseProfile();
    }
}

/// <summary>每个模块在界面上的显示配置（是否显示 / 颜色 / 是否展开堆栈）</summary>
public sealed class TagDisplayInfo
{
    public bool IsVisible { get; set; } = true;

    public string ColorHex { get; set; } = "#D4D4D4";

    public bool ShowStackTrace { get; set; }
}
