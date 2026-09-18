using System;
using System.Text;

namespace BossChecklist;

/// <summary>
/// 界面文字的统一入口。词条放在各语言目录的 LocalizationData.cs 里（键名就是原模组 hjson 的键，
/// 去掉了 "Mods.BossChecklist." 前缀）。这里额外处理两件原版文字系统的工作：
///   - {^0:单数;复数} 这种按数字变化的写法（原版 LocalizedText 的复数语法）
///   - 条目的显示名：优先用词条表里的特例，否则取第一个 NPC 的原版名字（跟着游戏语言走）
/// </summary>
internal static class Localization
{
    public static string Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        if (LocalizationData.Strings.TryGetValue(key, out string value)) return value;
        return key;
    }

    public static bool Has(string key)
    {
        return !string.IsNullOrEmpty(key) && LocalizationData.Strings.ContainsKey(key);
    }

    public static string Format(string key, params object[] args)
    {
        return FormatText(Get(key), args);
    }

    /// <summary>把 {0} 之类替换掉，顺便把 {^0:单数;复数} 展开。</summary>
    public static string FormatText(string raw, params object[] args)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        if (args == null || args.Length == 0) return raw;
        try
        {
            return string.Format(ExpandPlurals(raw, args), args);
        }
        catch
        {
            return raw;
        }
    }

    /// <summary>原版写法 {^0:a;b}：第 0 个参数等于 1 时取 a，否则取 b。</summary>
    private static string ExpandPlurals(string raw, object[] args)
    {
        int idx = raw.IndexOf("{^", StringComparison.Ordinal);
        if (idx < 0) return raw;

        StringBuilder sb = new StringBuilder(raw.Length + 16);
        int pos = 0;
        while (idx >= 0)
        {
            sb.Append(raw, pos, idx - pos);
            int p = idx + 2;
            int numStart = p;
            while (p < raw.Length && char.IsDigit(raw[p])) p++;

            int argIndex;
            if (p >= raw.Length || raw[p] != ':' ||
                !int.TryParse(raw.Substring(numStart, p - numStart), out argIndex))
            {
                sb.Append("{^");
                pos = idx + 2;
                idx = raw.IndexOf("{^", pos, StringComparison.Ordinal);
                continue;
            }

            p++; // 跳过 ':'
            int depth = 0;
            int semi = -1;
            int end = -1;
            for (int i = p; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c == '{') depth++;
                else if (c == '}')
                {
                    if (depth == 0) { end = i; break; }
                    depth--;
                }
                else if (c == ';' && depth == 0 && semi < 0) semi = i;
            }

            if (end < 0)
            {
                sb.Append(raw, idx, raw.Length - idx);
                pos = raw.Length;
                break;
            }

            string one = semi >= 0 ? raw.Substring(p, semi - p) : raw.Substring(p, end - p);
            string many = semi >= 0 ? raw.Substring(semi + 1, end - semi - 1) : one;

            long n = 0;
            if (argIndex >= 0 && argIndex < args.Length && args[argIndex] != null)
            {
                try { n = Convert.ToInt64(args[argIndex]); } catch { n = 0; }
            }
            sb.Append(n == 1 ? one : many);

            pos = end + 1;
            idx = raw.IndexOf("{^", pos, StringComparison.Ordinal);
        }

        sb.Append(raw, pos, raw.Length - pos);
        return sb.ToString();
    }

    /// <summary>条目的显示名（清单里、Boss 页标题、提示里都用它）。</summary>
    public static string EntryName(string internalName)
    {
        if (string.IsNullOrEmpty(internalName)) return "";

        // 事件、双子魔眼这种没法直接用单个 NPC 名字的，走词条表里的特例
        if (LocalizationData.EntryNames.TryGetValue(internalName, out string custom)) return custom;

        try
        {
            EntryInfo entry = BossTracker.FromKey("Terraria " + internalName);
            if (entry != null && entry.NpcIds.Count > 0)
            {
                string name = GameRefs.NpcName(entry.NpcIds[0]);
                if (!string.IsNullOrEmpty(name)) return name;
            }
        }
        catch
        {
        }
        return internalName;
    }

    /// <summary>条目的召唤方式说明（BossSpawnInfo 那批词条）。</summary>
    public static string SpawnInfo(string internalName)
    {
        string key = "BossSpawnInfo." + internalName;
        if (LocalizationData.Strings.TryGetValue(key, out string value)) return value;
        return Get("Log.SpawnInfo.NoInfo");
    }

    /// <summary>"Boss" / "Mini-boss" / "Event" 这三种类别的名字。</summary>
    public static string Common(EntryType type)
    {
        return Get("Log.Common." + type);
    }
}
