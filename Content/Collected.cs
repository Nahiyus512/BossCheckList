using System;
using System.Collections.Generic;
using Terraria;
using TerrariaModder.Core.Events;

namespace BossChecklist;

/// <summary>
/// 玩家「拿到过」的 Boss 掉落 / 收藏品。
///
/// 和原模组一样在「捡起物品」的钩子里记录（ItemEvents.OnItemPickup），所以东西捡起来之后
/// 放进箱子、卖掉、丢掉都不影响记录。另外每隔几帧扫一次背包和装备栏作为兜底，
/// 顺便覆盖合成、购买、开宝藏袋这些不经过拾取钩子的来源。
/// </summary>
internal static class Collected
{
    /// <summary>已经拿到过的物品。</summary>
    private static readonly HashSet<int> Items = new HashSet<int>();

    /// <summary>可能是 Boss 掉落 / 召唤物的物品，只有这些才值得记。</summary>
    private static readonly HashSet<int> Watch = new HashSet<int>();

    private static bool _watchBuilt;
    private static bool _dirty;
    private static int _timer;

    public static void Init()
    {
        try { ItemEvents.OnItemPickup += OnItemPickup; } catch { }
    }

    public static void Unload()
    {
        try { ItemEvents.OnItemPickup -= OnItemPickup; } catch { }
    }

    /// <summary>捡起来的瞬间就记下（只看本地玩家）。</summary>
    private static void OnItemPickup(ItemPickupEventArgs e)
    {
        try
        {
            if (Main.gameMenu) return;
            int who = e.PlayerIndex;
            if (who >= 0 && who != Main.myPlayer) return; // 别人捡的不算在自己头上
            Record(e.ItemType);
            Scan(); // 顺手把背包与装备再对一遍（捡起来的东西可能直接被塞进身上各处）
        }
        catch
        {
        }
    }

    /// <summary>掉落表变了（换世界、重读数据）之后要重建一次。</summary>
    public static void InvalidateWatch()
    {
        _watchBuilt = false;
        Watch.Clear();
    }

    private static void BuildWatch()
    {
        if (_watchBuilt) return;
        _watchBuilt = true;
        for (int i = 0; i < BossTracker.Entries.Count; i++)
        {
            EntryInfo e = BossTracker.Entries[i];
            for (int j = 0; j < e.Loot.Count; j++) if (e.Loot[j] > 0) Watch.Add(e.Loot[j]);
            for (int j = 0; j < e.SpawnItems.Count; j++) if (e.SpawnItems[j] > 0) Watch.Add(e.SpawnItems[j]);
        }
    }

    public static bool Has(int itemType) => itemType > 0 && Items.Contains(itemType);

    public static void LoadForPlayer()
    {
        try
        {
            BuildWatch();
            Items.Clear();
            SaveData.LoadCollectedInto(Records.PlayerName(), Items);
        }
        catch
        {
        }
    }

    public static void Clear()
    {
        Items.Clear();
        _dirty = false;
    }

    /// <summary>打开日志本 / 清单时立刻扫一遍（玩家最关心「我现在有什么」的时刻）。</summary>
    public static void ScanNow()
    {
        if (Main.gameMenu) return;
        Scan();
    }

    /// <summary>
    /// 一秒一次的兜底扫描：合成、购买、开宝藏袋这些不经过拾取钩子的来源靠它补齐。
    /// 开销很小——只看本机玩家的十来个物品数组，空格子第一眼就跳过了。
    /// </summary>
    public static void Tick()
    {
        if (Main.gameMenu) return;
        if (++_timer < 60) return;
        _timer = 0;
        Scan();
    }

    /// <summary>
    /// 扫一遍：背包、随身存储（猪猪存钱罐 / 保险箱 / 护卫熔炉 / 虚空保险库）、
    /// 装备栏（含钩爪宠物这些杂项装备）、饰品栏、时装栏、染料栏。
    /// </summary>
    private static void Scan()
    {
        try
        {
            Player p = Main.LocalPlayer;
            if (p == null) return;
            Add(p.inventory);
            Add(p.armor);
            Add(p.dye);
            Add(p.miscEquips);
            Add(p.miscDyes);
            Add(p.bank);
            Add(p.bank2);
            Add(p.bank3);
            Add(p.bank4);
        }
        catch
        {
        }
    }

    private static void Add(Chest chest)
    {
        if (chest == null) return;
        Add(chest.item);
    }

    private static void Add(Item[] items)
    {
        if (items == null) return;
        for (int i = 0; i < items.Length; i++)
        {
            Item it = items[i];
            if (it == null || it.type <= 0 || it.stack <= 0) continue;
            Record(it.type);
        }
    }

    /// <summary>记下一件物品，没记过就顺手落盘。</summary>
    private static void Record(int itemType)
    {
        if (itemType <= 0) return;
        BuildWatch();
        if (!Watch.Contains(itemType)) return;
        if (!Items.Add(itemType)) return;
        _dirty = true;
        Save();
    }

    /// <summary>把当前列表写回存档。</summary>
    public static void Save()
    {
        if (!_dirty) return;
        _dirty = false;
        SaveData.StoreCollected(Records.PlayerName(), Items);
    }
}