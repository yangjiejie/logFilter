using System;
using System.Collections.Generic;
using System.Linq;
using LogFilterApp.Models;

namespace LogFilterApp.Services;

/// <summary>
/// 路径 -&gt; 模块名。采用"最长前缀优先"，避免 JSON 里的书写顺序影响归属结果。
/// </summary>
public sealed class ModuleResolver
{
    private List<ModuleRule> _rules = new();

    public IReadOnlyList<ModuleRule> Rules => _rules;

    public void SetRules(IEnumerable<ModuleRule> rules)
    {
        _rules = rules
            .Where(r => !string.IsNullOrWhiteSpace(r.Name))
            .Select(r => new ModuleRule { Name = r.Name, Patterns = new System.Collections.ObjectModel.ObservableCollection<string>(r.Patterns) })
            .ToList();
    }

    public string Resolve(string? path, string fallback = "other")
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return fallback;
        }

        var p = path.Replace('\\', '/').TrimStart('.', '/');
        string? best = null;
        var bestLen = -1;

        foreach (var rule in _rules)
        {
            foreach (var raw in rule.Patterns)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                var prefix = raw.Replace('\\', '/').Trim().TrimStart('.', '/');
                if (prefix.Length == 0)
                {
                    continue;
                }

                if (p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && prefix.Length > bestLen)
                {
                    best = rule.Name;
                    bestLen = prefix.Length;
                }
            }
        }

        return best ?? fallback;
    }
}
