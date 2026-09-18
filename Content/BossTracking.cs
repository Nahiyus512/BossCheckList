using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using TerrariaModder.Core.Events;

namespace BossChecklist;

/// <summary>
/// 游戏内的事件挂钩：Boss 出现 / 被打死 / 消失、玩家受伤和死亡、月亮事件结束。
/// 负责纪录追踪、原版没记的那几个「已击败」标记，以及聊天栏里的提示。
/// </summary>
internal static class BossTracking
{
    // 这次出现是不是被打死的（打死了就不再算「逃跑」）
    private static readonly Dictionary<string, bool> Killed = new Dictionary<string, bool>();
    // 上一帧这些条目是不是还有活着的 NPC
    private static readonly Dictionary<string, bool> Active = new Dictionary<string, bool>();

    // 月亮事件：晚上见到过 → 天亮/天黑时算「已发生」
    private static bool _sawBloodMoon, _sawFrostMoon, _sawPumpkinMoon, _sawEclipse;

    public static void Init()
    {
        FrameEvents.OnPostUpdate += OnPostUpdate;
        GameEvents.OnWorldLoad += OnWorldLoad;
        GameEvents.OnWorldUnload += OnWorldUnload;
        GameEvents.OnWorldSave += SaveData.Flush;
        GameEvents.OnDayStart += OnDayStart;
        GameEvents.OnNightStart += OnNightStart;
        NPCEvents.OnNPCSpawn += OnNpcSpawn;
        NPCEvents.OnNPCDeath += OnNpcDeath;
        PlayerEvents.OnPlayerHurt += OnPlayerHurt;
        PlayerEvents.OnPlayerDeath += OnPlayerDeath;
        Collected.Init();
    }

    public static void Unload()
    {
        FrameEvents.OnPostUpdate -= OnPostUpdate;
        GameEvents.OnWorldLoad -= OnWorldLoad;
        GameEvents.OnWorldUnload -= OnWorldUnload;
        GameEvents.OnWorldSave -= SaveData.Flush;
        GameEvents.OnDayStart -= OnDayStart;
        GameEvents.OnNightStart -= OnNightStart;
        NPCEvents.OnNPCSpawn -= OnNpcSpawn;
        NPCEvents.OnNPCDeath -= OnNpcDeath;
        PlayerEvents.OnPlayerHurt -= OnPlayerHurt;
        PlayerEvents.OnPlayerDeath -= OnPlayerDeath;
        Collected.Unload();
    }

    private static void OnWorldLoad()
    {
        if (BossTracker.Entries.Count == 0) BossTracker.Initialize();
        BossTracker.EnsureLoot();
        Collected.InvalidateWatch();
        Collected.LoadForPlayer();
        WorldState.Load();
        Records.LoadForWorld();
        Killed.Clear();
        Active.Clear();
        _sawBloodMoon = _sawFrostMoon = _sawPumpkinMoon = _sawEclipse = false;
    }

    private static void OnWorldUnload()
    {
        WorldState.Save();
        Collected.Save();
        SaveData.Flush();
        Records.ResetTracking();
        Killed.Clear();
        Active.Clear();
    }

    // ---------------- 每条条目还活着吗 ----------------

    private static bool EntryHasLiveNpc(EntryInfo entry)
    {
        try
        {
            for (int i = 0; i < Main.npc.Length; i++)
            {
                NPC npc = Main.npc[i];
                if (npc == null || !npc.active || npc.life <= 0) continue;
                if (entry.NpcIds.Contains(npc.type)) return true;
            }
        }
        catch
        {
        }
        return false;
    }

    private static void OnPostUpdate()
    {
        if (Main.gameMenu) return;
        bool cfgTracking = BossChecklistMod.Config.RecordTrackingEnabled;

        // 月亮事件：夜里见到过就记下来
        try
        {
            if (Main.bloodMoon) _sawBloodMoon = true;
            if (Main.snowMoon) _sawFrostMoon = true;
            if (Main.pumpkinMoon) _sawPumpkinMoon = true;
            if (Main.eclipse) _sawEclipse = true;
        }
        catch
        {
        }

        if (cfgTracking) Records.Tick();
        Collected.Tick();

        for (int i = 0; i < BossTracker.Entries.Count; i++)
        {
            EntryInfo entry = BossTracker.Entries[i];
            bool live = EntryHasLiveNpc(entry);
            bool was = Active.TryGetValue(entry.Key, out bool prev) && prev;
            if (live == was) continue;
            Active[entry.Key] = live;

            if (live)
            {
                Killed[entry.Key] = false;
                if (cfgTracking && entry.HasRecords) Records.StartTracking(entry);
                continue;
            }

            bool killed = Killed.TryGetValue(entry.Key, out bool k) && k;
            Killed[entry.Key] = false;
            if (!killed)
            {
                if (cfgTracking && entry.HasRecords) Records.FinishFight(entry, false);
                AnnounceDespawn(entry);
            }
        }
    }

    private static void OnNpcSpawn(NPCSpawnEventArgs args)
    {
        EntryInfo entry = BossTracker.FindByNpc(args.NPCType);
        if (entry == null) return;
        if (!BossChecklistMod.Config.RecordTrackingEnabled) return;
        Active[entry.Key] = true;
        Killed[entry.Key] = false;
        if (entry.HasRecords) Records.StartTracking(entry);
    }

    private static void OnNpcDeath(NPCDeathEventArgs args)
    {
        int npcType = args.NPCType;

        // 原版自己没记的那几个「已击败」标记
        switch (npcType)
        {
            case NPCID.DD2DarkMageT1:
            case NPCID.DD2DarkMageT3:
                MarkExtra(WorldState.DownedDarkMage, "DarkMage", () => WorldState.DownedDarkMage = true);
                break;
            case NPCID.DD2OgreT2:
            case NPCID.DD2OgreT3:
                MarkExtra(WorldState.DownedOgre, "Ogre", () => WorldState.DownedOgre = true);
                break;
            case NPCID.PirateShip:
                MarkExtra(WorldState.DownedFlyingDutchman, "PirateShip", () => WorldState.DownedFlyingDutchman = true);
                break;
            case NPCID.MartianSaucerCore:
                MarkExtra(WorldState.DownedMartianSaucer, "MartianSaucer", () => WorldState.DownedMartianSaucer = true);
                break;
            case NPCID.LunarTowerVortex:
                MarkExtra(NPC.downedTowerVortex, "LunarTowerVortex", () => NPC.downedTowerVortex = true);
                break;
            case NPCID.LunarTowerStardust:
                MarkExtra(NPC.downedTowerStardust, "LunarTowerStardust", () => NPC.downedTowerStardust = true);
                break;
            case NPCID.LunarTowerNebula:
                MarkExtra(NPC.downedTowerNebula, "LunarTowerNebula", () => NPC.downedTowerNebula = true);
                break;
            case NPCID.LunarTowerSolar:
                MarkExtra(NPC.downedTowerSolar, "LunarTowerSolar", () => NPC.downedTowerSolar = true);
                break;
        }

        // 分体（骷髅王之手、机械骷髅王部件、天界柱……）被打掉时给个提示
        EntryInfo limbEntry = BossTracker.FindLimbEntry(npcType);
        if (limbEntry != null && (MessageType)BossChecklistMod.Config.LimbMessages != MessageType.Disabled)
        {
            string name = GameRefs.NpcName(npcType);
            string msg = (MessageType)BossChecklistMod.Config.LimbMessages == MessageType.Unique
                ? Localization.Format("ChatMessages.Defeated." + LimbKey(npcType), name)
                : Localization.Format("ChatMessages.Defeated.Generic", name);
            Chat(msg, 120, 220, 120);
        }

        // 整条条目的 NPC 都死了 → 这一场算赢
        EntryInfo entry = BossTracker.FindByNpc(npcType);
        if (entry == null) return;
        if (EntryHasLiveNpc(entry)) return; // 还有别的部件活着

        Killed[entry.Key] = true;
        if (BossChecklistMod.Config.RecordTrackingEnabled && entry.HasRecords)
            Records.FinishFight(entry, true);
    }

    private static void MarkExtra(bool alreadyDone, string internalName, Action mark)
    {
        if (alreadyDone) return;
        mark();
        WorldState.Save();
    }

    private static string LimbKey(int npcType)
    {
        switch (npcType)
        {
            case NPCID.SkeletronHand: return "SkeletronHand";
            case NPCID.PrimeCannon: return "PrimeCannon";
            case NPCID.PrimeSaw: return "PrimeSaw";
            case NPCID.PrimeVice: return "PrimeVice";
            case NPCID.PrimeLaser: return "PrimeLaser";
            case NPCID.GolemFistLeft: return "GolemFistLeft";
            case NPCID.GolemFistRight: return "GolemFistRight";
            case NPCID.GolemHead: return "GolemHead";
            case NPCID.MoonLordHead: return "MoonLordHead";
            case NPCID.MoonLordHand: return "MoonLordHand";
            case NPCID.Retinazer: return "Retinazer";
            case NPCID.Spazmatism: return "Spazmatism";
            case NPCID.LunarTowerVortex: return "LunarTowerVortex";
            case NPCID.LunarTowerStardust: return "LunarTowerStardust";
            case NPCID.LunarTowerNebula: return "LunarTowerNebula";
            case NPCID.LunarTowerSolar: return "LunarTowerSolar";
            default: return "Generic";
        }
    }

    private static void OnPlayerHurt(PlayerHurtEventArgs args)
    {
        if (args.PlayerIndex != Main.myPlayer) return;
        if (!BossChecklistMod.Config.RecordTrackingEnabled) return;
        Records.NoteHit();
    }

    private static void OnPlayerDeath(PlayerDeathEventArgs args)
    {
        if (args.PlayerIndex != Main.myPlayer) return;
        if (!BossChecklistMod.Config.RecordTrackingEnabled) return;
        Records.NoteDeath();
    }

    // ---------------- 月亮事件 ----------------

    private static void OnDayStart()
    {
        if (Main.bloodMoon && _sawBloodMoon) FinishMoon(WorldState.DownedBloodMoon, "BloodMoon", () => WorldState.DownedBloodMoon = true);
        if (Main.snowMoon && _sawFrostMoon) FinishMoon(WorldState.DownedFrostMoon, "FrostMoon", () => WorldState.DownedFrostMoon = true);
        if (Main.pumpkinMoon && _sawPumpkinMoon) FinishMoon(WorldState.DownedPumpkinMoon, "PumpkinMoon", () => WorldState.DownedPumpkinMoon = true);
        _sawBloodMoon = _sawFrostMoon = _sawPumpkinMoon = false;
        _sawEclipse = false;
    }

    private static void OnNightStart()
    {
        if (Main.eclipse && _sawEclipse) FinishMoon(WorldState.DownedSolarEclipse, "Eclipse", () => WorldState.DownedSolarEclipse = true);
        _sawEclipse = false;
    }

    private static void FinishMoon(bool alreadyDone, string internalName, Action mark)
    {
        if (!alreadyDone)
        {
            mark();
            WorldState.Save();
        }
        if ((MessageType)BossChecklistMod.Config.MoonMessages == MessageType.Disabled) return;
        string msg = (MessageType)BossChecklistMod.Config.MoonMessages == MessageType.Unique
            ? Localization.Format("ChatMessages.EventEnd." + internalName)
            : Localization.Format("ChatMessages.EventEnd.Generic", Localization.EntryName(internalName));
        Chat(msg, 50, 255, 130);
    }

    private static void AnnounceDespawn(EntryInfo entry)
    {
        if (entry.Type == EntryType.Event) return;
        if ((MessageType)BossChecklistMod.Config.DespawnMessageType == MessageType.Disabled) return;
        if (Main.gameMenu) return;

        bool allDead = true;
        try
        {
            for (int i = 0; i < Main.player.Length; i++)
            {
                Player p = Main.player[i];
                if (p != null && p.active && !p.dead) { allDead = false; break; }
            }
        }
        catch
        {
            allDead = false;
        }

        string key = allDead ? "ChatMessages.Loss." + entry.InternalName : "ChatMessages.Despawn.Generic";
        string msg = Localization.Format(key, entry.DisplayName);
        Chat(msg, (byte)(allDead ? 255 : 150), 120, (byte)(allDead ? 120 : 150));
    }

    private static void Chat(string text, byte r, byte g, byte b)
    {
        if (string.IsNullOrEmpty(text)) return;
        try { Main.NewText(text, r, g, b); } catch { }
    }
}
