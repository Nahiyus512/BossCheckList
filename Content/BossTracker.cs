using System;
using System.Collections.Generic;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;

namespace BossChecklist;

/// <summary>
/// 条目登记表：排序、按 NPC 反查、掉落表、可见性筛选。
/// 所有条目都是原版内容（TerrariaModder 里没有其它 tModLoader 模组可以注册条目）。
/// </summary>
internal static class BossTracker
{
    public static List<EntryInfo> Entries { get; private set; } = new List<EntryInfo>();
    public static List<string> BossRecordKeys { get; private set; } = new List<string>();

    private static bool _lootFilled;

    public static void Initialize()
    {
        Entries = VanillaContent.Build();
        Entries.Sort((a, b) =>
        {
            int c = a.Progression.CompareTo(b.Progression);
            if (c != 0) return c;
            c = a.Type.CompareTo(b.Type);
            if (c != 0) return c;
            return string.CompareOrdinal(a.InternalName, b.InternalName);
        });

        BossRecordKeys = new List<string>();
        for (int i = 0; i < Entries.Count; i++)
        {
            if (Entries[i].HasRecords) BossRecordKeys.Add(Entries[i].Key);
        }
        _lootFilled = false;
        Collected.InvalidateWatch();
    }

    public static EntryInfo FromKey(string key)
    {
        for (int i = 0; i < Entries.Count; i++)
        {
            if (Entries[i].Key == key) return Entries[i];
        }
        return null;
    }

    public static int IndexOf(EntryInfo entry) => Entries.IndexOf(entry);

    public static int IndexOfKey(string key)
    {
        for (int i = 0; i < Entries.Count; i++)
        {
            if (Entries[i].Key == key) return i;
        }
        return -1;
    }

    /// <summary>这个 NPC 属于哪条条目（排除分体）。</summary>
    public static EntryInfo FindByNpc(int npcType)
    {
        for (int i = 0; i < Entries.Count; i++)
        {
            EntryInfo e = Entries[i];
            if (e.Limbs.Contains(npcType)) continue;
            if (e.NpcIds.Contains(npcType)) return e;
        }
        return null;
    }

    /// <summary>这个 NPC 是不是某条条目的分体（骷髅王之手、机械骷髅王的部件、天界柱之类）。</summary>
    public static EntryInfo FindLimbEntry(int npcType)
    {
        for (int i = 0; i < Entries.Count; i++)
        {
            if (Entries[i].Limbs.Contains(npcType)) return Entries[i];
        }
        return null;
    }

    public static bool IsEntryNpc(int npcType) => FindByNpc(npcType) != null || FindLimbEntry(npcType) != null;

    /// <summary>下一条还没完成的条目（原版 "Next" 标签用）。</summary>
    public static int FindNextEntry(EntryType? type = null)
    {
        for (int i = 0; i < Entries.Count; i++)
        {
            EntryInfo e = Entries[i];
            if (e.IsChecked || !e.IsAvailable() || !VisibleOnChecklist(e)) continue;
            if (type.HasValue && e.Type != type.Value) continue;
            return i;
        }
        return -1;
    }

    /// <summary>清单列表里是否显示（配置文件里的筛选条件都算进来）。</summary>
    public static bool VisibleOnChecklist(EntryInfo entry, bool hiddenListMode = false)
    {
        BossLogConfig cfg = BossChecklistMod.Config;
        if (cfg.OnlyShowBossContent && entry.Type != EntryType.Boss) return false;
        if (hiddenListMode) return true;
        if (entry.Hidden) return false;
        if (!entry.IsAvailable() && cfg.HideUnavailable && !entry.IsChecked) return false;

        switch (entry.Type)
        {
            case EntryType.Boss:
                if ((FilterType)cfg.FilterBosses == FilterType.HideWhenCompleted && entry.IsChecked) return false;
                break;
            case EntryType.MiniBoss:
                if ((FilterType)cfg.FilterMiniBosses == FilterType.Hide) return false;
                if ((FilterType)cfg.FilterMiniBosses == FilterType.HideWhenCompleted && entry.IsChecked) return false;
                break;
            case EntryType.Event:
                if ((FilterType)cfg.FilterEvents == FilterType.Hide) return false;
                if ((FilterType)cfg.FilterEvents == FilterType.HideWhenCompleted && entry.IsChecked) return false;
                break;
        }
        return true;
    }

    /// <summary>进度模式（Progressive Checklist）下，只显示已完成的和下一条。</summary>
    public static bool VisibleOnPage(EntryInfo entry)
    {
        if (BossChecklistMod.Config.ProgressiveChecklist)
        {
            int next = FindNextEntry();
            if (!entry.IsChecked && IndexOf(entry) != next) return false;
        }
        return VisibleOnChecklist(entry);
    }

    public static int CountCompleted(EntryType type)
    {
        int n = 0;
        for (int i = 0; i < Entries.Count; i++)
        {
            if (Entries[i].Type == type && Entries[i].IsChecked) n++;
        }
        return n;
    }

    public static int CountTotal(EntryType type)
    {
        int n = 0;
        for (int i = 0; i < Entries.Count; i++)
        {
            if (Entries[i].Type == type && Entries[i].IsAvailable()) n++;
        }
        return n;
    }

    // ---------------- 掉落表 ----------------

    /// <summary>从原版掉落数据库里把每条条目的掉落物读出来（和原模组一样的做法）。</summary>
    public static void EnsureLoot()
    {
        if (_lootFilled) return;
        try
        {
            if (Main.ItemDropsDB == null) return;
        }
        catch
        {
            return;
        }
        _lootFilled = true;
        foreach (EntryInfo entry in Entries) FillLoot(entry);
    }

    public static void InvalidateLoot() => _lootFilled = false;

    private static void FillLoot(EntryInfo entry)
    {
        if (entry.Loot.Count > 0) return;
        List<int> loot = new List<int>();
        HashSet<int> seen = new HashSet<int>();

        foreach (int npc in entry.NpcIds)
        {
            foreach (int item in DropItemsForNpc(npc, entry))
            {
                if (item > 0 && seen.Add(item)) loot.Add(item);
            }
        }

        // 原版 Terraria 的 ItemDropDatabase 只有 GetRulesForNPCID，没有按物品查规则的接口
        // （宝藏袋的内容硬编码在 Player.OpenBossBag 里），所以这里不展开宝藏袋。
        // 专家/大师独占掉落本来就挂在 Boss 自己的规则上，不会漏。

        foreach (int item in entry.Collectibles.Keys)
        {
            if (item > 0 && seen.Add(item)) loot.Add(item);
        }

        // 两处特例：火把神的恩惠不是掉出来的；克苏鲁之脑的样本是仆从掉的
        if (entry.Key == "Terraria TorchGod" && seen.Add(ItemID.TorchGodsFavor)) loot.Add(ItemID.TorchGodsFavor);
        if (entry.Key == "Terraria BrainofCthulhu" && seen.Add(ItemID.TissueSample)) loot.Add(ItemID.TissueSample);

        entry.Loot.Clear();
        entry.Loot.AddRange(loot);
    }

    private static List<int> DropItemsForNpc(int npc, EntryInfo entry)
    {
        List<int> result = new List<int>();
        try
        {
            List<IItemDropRule> rules = Main.ItemDropsDB.GetRulesForNPCID(npc, false);
            if (rules == null) return result;
            List<DropRateInfo> info = new List<DropRateInfo>();
            foreach (IItemDropRule rule in rules)
            {
                try { rule.ReportDroprates(info, new DropRateInfoChainFeed(1f)); } catch { }
            }
            foreach (DropRateInfo d in info)
            {
                if (d.itemId <= 0) continue;
                result.Add(d.itemId);
                entry.Dropped.Add(d.itemId);
                if (d.conditions == null) continue;
                for (int i = 0; i < d.conditions.Count; i++)
                {
                    IItemDropRuleCondition c = d.conditions[i];
                    if (c is Conditions.IsExpert || c is Conditions.LegacyHack_IsBossAndExpert) entry.ExpertLocked.Add(d.itemId);
                    else if (c is Conditions.IsCorruption || c is Conditions.IsCorruptionAndNotExpert) entry.CorruptionLocked.Add(d.itemId);
                    else if (c is Conditions.IsCrimson || c is Conditions.IsCrimsonAndNotExpert) entry.CrimsonLocked.Add(d.itemId);
                }
            }
        }
        catch
        {
        }
        return result;
    }
}
