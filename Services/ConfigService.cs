using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LogFilterApp.Models;
using Newtonsoft.Json;

namespace LogFilterApp.Services;

/// <summary>
/// 配置读写。采用"便携模式"：配置保存在程序运行目录的 config 子目录下，
/// 首次启动时会自动尝试从 Unity 工程的 environment/LogFilter.json 导入模块映射。
/// </summary>
public sealed class ConfigService
{
    public string ConfigDir { get; }

    public string ModulesFile => Path.Combine(ConfigDir, "LogFilter.json");
    public string ProfileFile => Path.Combine(ConfigDir, "ParseProfile.json");
    public string DisplayFile => Path.Combine(ConfigDir, "TagDisplay.json");

    public ConfigService()
    {
        ConfigDir = Path.Combine(AppContext.BaseDirectory, "config");
        Directory.CreateDirectory(ConfigDir);
    }

    // ---------- 模块映射 ----------

    public List<ModuleRule> LoadModules()
    {
        var path = ModulesFile;
        if (!File.Exists(path))
        {
            // 兼容旧工程：自动从 environment/LogFilter.json 导入
            var imported = TryFindProjectLogFilter();
            if (imported != null && File.Exists(imported))
            {
                try
                {
                    File.Copy(imported, path, true);
                }
                catch
                {
                    /* 忽略：只读目录等情况 */
                }
            }
        }

        if (!File.Exists(path))
        {
            return new List<ModuleRule>();
        }

        try
        {
            var dict = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(File.ReadAllText(path));
            if (dict == null)
            {
                return new List<ModuleRule>();
            }

            return dict.Select(kv => new ModuleRule
            {
                Name = kv.Key,
                Patterns = new System.Collections.ObjectModel.ObservableCollection<string>(kv.Value ?? new List<string>()),
            }).ToList();
        }
        catch
        {
            return new List<ModuleRule>();
        }
    }

    public void SaveModules(IEnumerable<ModuleRule> rules) =>
        File.WriteAllText(ModulesFile, SerializeModules(rules));

    public static string SerializeModules(IEnumerable<ModuleRule> rules)
    {
        var dict = new Dictionary<string, List<string>>();
        foreach (var r in rules)
        {
            if (string.IsNullOrWhiteSpace(r.Name))
            {
                continue;
            }

            dict[r.Name] = r.Patterns.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToList();
        }

        return JsonConvert.SerializeObject(dict, Formatting.Indented);
    }

    // ---------- 解析规则 ----------

    public ParseProfile LoadProfile()
    {
        try
        {
            if (File.Exists(ProfileFile))
            {
                return JsonConvert.DeserializeObject<ParseProfile>(File.ReadAllText(ProfileFile)) ?? new ParseProfile();
            }
        }
        catch
        {
            /* 配置损坏则回退默认 */
        }

        return new ParseProfile();
    }

    public void SaveProfile(ParseProfile profile) =>
        File.WriteAllText(ProfileFile, JsonConvert.SerializeObject(profile, Formatting.Indented));

    // ---------- 显示配置 ----------

    public Dictionary<string, TagDisplayInfo> LoadDisplay()
    {
        try
        {
            if (File.Exists(DisplayFile))
            {
                return JsonConvert.DeserializeObject<Dictionary<string, TagDisplayInfo>>(File.ReadAllText(DisplayFile))
                       ?? new Dictionary<string, TagDisplayInfo>();
            }
        }
        catch
        {
            /* 忽略 */
        }

        return new Dictionary<string, TagDisplayInfo>();
    }

    public void SaveDisplay(Dictionary<string, TagDisplayInfo> display) =>
        File.WriteAllText(DisplayFile, JsonConvert.SerializeObject(display, Formatting.Indented));

    // ---------- 导入 / 导出 ----------

    public List<ModuleRule> LoadModulesFromFile(string file)
    {
        var dict = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(File.ReadAllText(file));
        return dict?.Select(kv => new ModuleRule
        {
            Name = kv.Key,
            Patterns = new System.Collections.ObjectModel.ObservableCollection<string>(kv.Value ?? new List<string>()),
        }).ToList() ?? new List<ModuleRule>();
    }

    public void SaveModulesToFile(string file, IEnumerable<ModuleRule> rules) =>
        File.WriteAllText(file, SerializeModules(rules));

    /// <summary>从程序目录向上查找 Unity 工程的 environment/LogFilter.json</summary>
    public static string? TryFindProjectLogFilter()
    {
        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 10 && dir != null; i++)
            {
                var candidate = Path.Combine(dir.FullName, "environment", "LogFilter.json");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }
        }
        catch
        {
            /* 忽略 */
        }

        return null;
    }
}
