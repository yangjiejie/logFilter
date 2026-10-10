using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LogFilterApp.Models;

namespace LogFilterApp.Services;

/// <summary>
/// 通用日志解析器：不依赖 Unity 运行时，输入任意文本（线上日志 / 粘贴的控制台文本），
/// 输出带模块归属的日志条目。
/// </summary>
public sealed class LogParser
{
    private readonly ModuleResolver _resolver;
    private ParseProfile _profile;

    /// <summary>非法正则的兜底：永不匹配</summary>
    private static readonly Regex NeverMatch = new("(?!x)x", RegexOptions.Compiled);

    // 预编译缓存
    private Regex _timeRegex = NeverMatch;
    private Regex _stackLineRegex = NeverMatch;
    private Regex _stackMethodRegex = NeverMatch;
    private List<Regex> _pathRegexes = new();
    private List<(LogLevel Level, Regex Regex)> _levelRegexes = new();

    public ParseProfile Profile => _profile;

    public LogParser(ModuleResolver resolver, ParseProfile profile)
    {
        _resolver = resolver;
        _profile = profile ?? new ParseProfile();
        SetProfile(_profile);
    }

    public void SetProfile(ParseProfile profile)
    {
        _profile = profile ?? new ParseProfile();
        _timeRegex = SafeRegex(_profile.TimePattern);
        _stackLineRegex = SafeRegex(_profile.StackLinePattern);
        _stackMethodRegex = SafeRegex(_profile.StackMethodPattern);
        _pathRegexes = (_profile.PathPatterns ?? new List<string>())
            .Select(SafeRegex)
            .ToList();
        _levelRegexes = new List<(LogLevel, Regex)>();

        if (_profile.LevelPatterns != null)
        {
            // Unity LogType 的顺序：Assert/Error/Exception 优先于 Warning/Log
            var order = new[] { "Assert", "Exception", "Error", "Warning", "Log" };
            foreach (var name in order)
            {
                if (!_profile.LevelPatterns.TryGetValue(name, out var list))
                {
                    continue;
                }

                if (!Enum.TryParse<LogLevel>(name, true, out var level))
                {
                    continue;
                }

                foreach (var p in list)
                {
                    _levelRegexes.Add((level, SafeRegex(p)));
                }
            }
        }
    }

    public List<LogEntry> Parse(string text, int startIndex = 0)
    {
        var result = new List<LogEntry>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        LogEntry? cur = null;
        var stackStarted = false;
        var index = startIndex;
        var stackBuilder = new System.Text.StringBuilder();
        var messageBuilder = new System.Text.StringBuilder();
        string? curPath = null;

        void Flush()
        {
            if (cur == null)
            {
                return;
            }

            cur.Message = messageBuilder.ToString().TrimEnd();
            cur.StackTrace = stackBuilder.ToString().TrimEnd();
            cur.FirstLine = FirstLineOf(cur.Message);
            cur.SourcePath = curPath ?? "";
            cur.ModuleName = _resolver.Resolve(curPath, _profile.UnknownModuleName);
            cur.Level = DetectLevel(cur.Message, cur.StackTrace);
            cur.Index = index++;
            result.Add(cur);

            cur = null;
            stackStarted = false;
            stackBuilder.Clear();
            messageBuilder.Clear();
            curPath = null;
        }

        void NewEntry(string time, string message)
        {
            Flush();
            cur = new LogEntry { Time = time };
            if (message.Length > 0)
            {
                messageBuilder.Append(message);
            }
        }

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.Trim().Length == 0)
            {
                continue;
            }

            // 1) 行首时间戳：一定是新的一条日志
            var timeMatch = _timeRegex.Match(line);
            if (timeMatch.Success)
            {
                var g = timeMatch.Groups["t"];
                var time = g.Success ? g.Value : timeMatch.Value;
                var rest = line.Substring(Math.Min(timeMatch.Length, line.Length)).TrimStart(' ', '\t');
                // 只吃掉紧跟着时间戳的分隔符，保留正文里的 [Error] 等标记
                if (rest.StartsWith("]") || rest.StartsWith(":") || rest.StartsWith("-"))
                {
                    rest = rest.Substring(1).TrimStart(' ', '\t');
                }

                NewEntry(time, rest);
                continue;
            }

            // 2) 堆栈行（已进入堆栈上下文时符号行也算；命中跳过前缀的封装行也算）
            if (_stackLineRegex.IsMatch(line)
                || (stackStarted && _stackMethodRegex.IsMatch(line))
                || MatchesSkipPrefix(line))
            {
                if (cur == null)
                {
                    // 孤立的堆栈行（没有时间戳的日志格式），也当成一条日志
                    NewEntry("", line);
                    continue;
                }

                if (stackBuilder.Length > 0)
                {
                    stackBuilder.Append('\n');
                }

                stackBuilder.Append(line);
                stackStarted = true;

                if (curPath == null && !MatchesSkipPrefix(line))
                {
                    curPath = ExtractPath(line);
                }

                continue;
            }

            // 3) 普通文本行
            if (cur == null)
            {
                NewEntry("", line);
                continue;
            }

            if (stackStarted)
            {
                // 堆栈已经结束又出现普通行 -&gt; 无时间戳格式的日志，这里开启新的一条
                NewEntry("", line);
                continue;
            }

            if (_profile.MergeMultilineMessage)
            {
                if (messageBuilder.Length > 0)
                {
                    messageBuilder.Append('\n');
                }

                messageBuilder.Append(line);
            }
            else
            {
                NewEntry("", line);
            }
        }

        Flush();
        return result;
    }

    /// <summary>命中"跳过前缀"的行同样视为堆栈行，但不参与模块路径提取</summary>
    private bool MatchesSkipPrefix(string line)
    {
        var t = line.TrimStart();
        return _profile.SkipStackPrefixes != null &&
               _profile.SkipStackPrefixes.Any(p => !string.IsNullOrWhiteSpace(p) && t.StartsWith(p.Trim(), StringComparison.Ordinal));
    }

    private string? ExtractPath(string stackLine)
    {
        foreach (var r in _pathRegexes)
        {
            var m = r.Match(stackLine);
            if (m.Success && m.Groups.Count > 1 && m.Groups[1].Success)
            {
                return m.Groups[1].Value.Replace('\\', '/');
            }
        }

        return null;
    }

    private LogLevel DetectLevel(string message, string stack)
    {
        var head = string.IsNullOrEmpty(message) ? stack : message;
        foreach (var (level, regex) in _levelRegexes)
        {
            if (regex.IsMatch(head))
            {
                return level;
            }
        }

        return LogLevel.Log;
    }

    private static string FirstLineOf(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return "";
        }

        var i = message.IndexOf('\n');
        return i < 0 ? message : message.Substring(0, i);
    }

    private static Regex SafeRegex(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return NeverMatch;
        }

        try
        {
            return new Regex(pattern, RegexOptions.Compiled);
        }
        catch
        {
            return NeverMatch;
        }
    }
}
