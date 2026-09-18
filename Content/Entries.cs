using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent.Events;
using Terraria.ID;

namespace BossChecklist;

internal enum EntryType
{
    Boss,
    MiniBoss,
    Event
}

internal enum CollectibleType
{
    Relic,
    MasterPet,
    Trophy,
    Mask,
    Music,
    Generic,
}

/// <summary>
/// 一条清单条目（一个 Boss / 小 Boss / 事件）。
/// 数据来自 tModLoader 版 BossChecklist，判定条件全部用原版字段，没有硬编码的存档数值。
/// </summary>
internal sealed class EntryInfo
{
    public string Key { get; internal set; }
    public EntryType Type;
    public float Progression;
    public Func<bool> Downed;
    public Func<bool> Available = () => true;
    public Func<string> Name;
    public Func<string> SpawnInfo;

    public List<int> NpcIds = new List<int>();
    public List<int> Limbs = new List<int>();
    public List<int> SpawnItems = new List<int>();
    public List<string> Portraits = new List<string>();
    public List<string> RawHeadTextures = new List<string>();
    public List<string> VanillaHeadTextures = new List<string>();
    public List<int> HeadNpcIds = new List<int>();

    public List<int> Loot = new List<int>();
    public Dictionary<int, CollectibleType> Collectibles = new Dictionary<int, CollectibleType>();

    // 掉落表里能确定的信息：哪些是 NPC 直接掉的、哪些是腐化 / 猩红世界专属的
    public readonly HashSet<int> Dropped = new HashSet<int>();
    public readonly HashSet<int> CorruptionLocked = new HashSet<int>();
    public readonly HashSet<int> CrimsonLocked = new HashSet<int>();
    public readonly HashSet<int> ExpertLocked = new HashSet<int>();

    // 每个世界各自的状态
    public bool Hidden;
    public bool Marked;

    public bool IsBoss => Type == EntryType.Boss;
    public bool HasRecords => Type == EntryType.Boss;
    public string DisplayName => Name != null ? Name() : Key;
    public string SourceName => Key.StartsWith("Terraria ") ? "Terraria" : Key.Substring(0, Key.IndexOf(' '));
    public string InternalName => Key.Substring(Key.IndexOf(' ') + 1);

    public bool IsChecked => Marked || (BossChecklistMod.Config.AutomaticChecklist && Downed != null && SafeInvoke(Downed));

    private static bool SafeInvoke(Func<bool> f)
    {
        try { return f(); } catch { return false; }
    }

    public bool IsAvailable()
    {
        try { return Available(); } catch { return true; }
    }

    public EntryInfo Portrait(string rawName)
    {
        Portraits.Add(rawName);
        return this;
    }

    public EntryInfo LimbsOf(params int[] npcTypes)
    {
        Limbs.AddRange(npcTypes);
        return this;
    }

    public EntryInfo SpawnedBy(params int[] itemTypes)
    {
        SpawnItems.AddRange(itemTypes);
        return this;
    }

    public EntryInfo AvailableWhen(Func<bool> f)
    {
        Available = f;
        return this;
    }

    public EntryInfo HeadRaw(string rawName)
    {
        RawHeadTextures.Add(rawName);
        return this;
    }

    public EntryInfo HeadVanilla(string path)
    {
        VanillaHeadTextures.Add(path);
        return this;
    }

    public EntryInfo HeadNpc(params int[] npcTypes)
    {
        HeadNpcIds.AddRange(npcTypes);
        return this;
    }

    /// <summary>条目主贴图：优先用模组自带的大图，没有就用 NPC 贴图的第一帧。</summary>
    public Texture2D GetPortrait()
    {
        for (int i = 0; i < Portraits.Count; i++)
        {
            Texture2D t = GameRefs.ModTex(Portraits[i]);
            if (t != null) return t;
        }
        if (NpcIds.Count > 0) return GameRefs.Npc(NpcIds[0]);
        return null;
    }

    public bool UsesNpcPortrait => Portraits.Count == 0 && NpcIds.Count > 0;

    /// <summary>条目头像图标（详情页右上角那排小图标）。</summary>
    public List<Texture2D> GetHeadIcons()
    {
        List<Texture2D> icons = new List<Texture2D>();
        for (int i = 0; i < RawHeadTextures.Count; i++)
        {
            Texture2D t = GameRefs.ModTex(RawHeadTextures[i]);
            if (t != null) icons.Add(t);
        }
        if (icons.Count > 0) return icons;

        for (int i = 0; i < VanillaHeadTextures.Count; i++)
        {
            Texture2D t = GameRefs.Vanilla(VanillaHeadTextures[i]);
            if (t != null) icons.Add(t);
        }
        if (icons.Count > 0) return icons;

        List<int> npcs = HeadNpcIds.Count > 0 ? HeadNpcIds : NpcIds;
        for (int i = 0; i < npcs.Count; i++)
        {
            int npc = npcs[i];
            if (npc == NPCID.DD2DarkMageT1 || npc == NPCID.DD2OgreT2) continue;
            if (npc <= 0 || npc >= NPCID.Sets.BossHeadTextures.Length) continue;
            int head = NPCID.Sets.BossHeadTextures[npc];
            if (head != -1)
            {
                Texture2D t = GameRefs.NpcHeadBoss(head);
                if (t != null) icons.Add(t);
            }
        }
        if (icons.Count > 0) return icons;

        Texture2D fallback = GameRefs.NpcHead(0);
        if (fallback != null) icons.Add(fallback);
        return icons;
    }

    /// <summary>宝藏袋（原版 ItemID.Sets.BossBag 标记的那些物品）。</summary>
    public int TreasureBag
    {
        get
        {
            for (int i = 0; i < Loot.Count; i++)
            {
                int item = Loot[i];
                if (item > 0 && item < ItemID.Sets.BossBag.Length && ItemID.Sets.BossBag[item]) return item;
            }
            return 0;
        }
    }
}

/// <summary>原版的全部条目（Boss、小 Boss、事件）。数值与 tModLoader 版 BossChecklist 一致。</summary>
internal static class VanillaContent
{
    internal const float KingSlime = 1f;
    internal const float EyeOfCthulhu = 2f;
    internal const float EaterOfWorlds = 3f;
    internal const float QueenBee = 4f;
    internal const float Skeletron = 5f;
    internal const float DeerClops = 6f;
    internal const float WallOfFlesh = 7f;
    internal const float QueenSlime = 8f;
    internal const float TheTwins = 9f;
    internal const float TheDestroyer = 10f;
    internal const float SkeletronPrime = 11f;
    internal const float Plantera = 12f;
    internal const float Golem = 13f;
    internal const float DukeFishron = 14f;
    internal const float EmpressOfLight = 15f;
    internal const float Betsy = 16f;
    internal const float LunaticCultist = 17f;
    internal const float Moonlord = 18f;

    internal const float TorchGod = 1.5f;
    internal const float BloodMoon = 2.5f;
    internal const float GoblinArmy = 3.33f;
    internal const float OldOnesArmy = 3.66f;
    internal const float DarkMage = OldOnesArmy + 0.01f;
    internal const float Ogre = SkeletronPrime + 0.01f;
    internal const float FrostLegion = 7.33f;
    internal const float PirateInvasion = 7.66f;
    internal const float PirateShip = PirateInvasion + 0.01f;
    internal const float SolarEclipse = 11.5f;
    internal const float PumpkinMoon = 13.25f;
    internal const float MourningWood = PumpkinMoon + 0.01f;
    internal const float Pumpking = PumpkinMoon + 0.02f;
    internal const float FrostMoon = 13.5f;
    internal const float Everscream = FrostMoon + 0.01f;
    internal const float SantaNK1 = FrostMoon + 0.02f;
    internal const float IceQueen = FrostMoon + 0.03f;
    internal const float MartianMadness = 13.75f;
    internal const float MartianSaucer = MartianMadness + 0.01f;
    internal const float LunarEvent = LunaticCultist + 0.01f;

    private const string Terraria = "Terraria";

    private static EntryInfo Make(EntryType type, float progression, string internalName, Func<bool> downed)
    {
        string key = Terraria + " " + internalName;
        return new EntryInfo
        {
            Key = key,
            Type = type,
            Progression = progression,
            Downed = downed,
            SpawnInfo = () => Localization.SpawnInfo(internalName),
            Name = () => Localization.EntryName(internalName)
        };
    }

    private static EntryInfo Boss(float progression, string internalName, int npcId, Func<bool> downed)
    {
        EntryInfo e = Make(EntryType.Boss, progression, internalName, downed);
        e.NpcIds.Add(npcId);
        e.SpawnItems.AddRange(SpawnItemsFor(internalName));
        foreach (KeyValuePair<int, CollectibleType> kv in CollectiblesFor(internalName)) e.Collectibles[kv.Key] = kv.Value;
        return e;
    }

    private static EntryInfo Boss(float progression, string internalName, List<int> npcIds, Func<bool> downed)
    {
        EntryInfo e = Make(EntryType.Boss, progression, internalName, downed);
        e.NpcIds.AddRange(npcIds);
        e.SpawnItems.AddRange(SpawnItemsFor(internalName));
        foreach (KeyValuePair<int, CollectibleType> kv in CollectiblesFor(internalName)) e.Collectibles[kv.Key] = kv.Value;
        return e;
    }

    private static EntryInfo MiniBoss(float progression, string internalName, List<int> npcIds, Func<bool> downed)
    {
        EntryInfo e = Make(EntryType.MiniBoss, progression, internalName, downed);
        e.NpcIds.AddRange(npcIds);
        e.SpawnItems.AddRange(SpawnItemsFor(internalName));
        foreach (KeyValuePair<int, CollectibleType> kv in CollectiblesFor(internalName)) e.Collectibles[kv.Key] = kv.Value;
        return e;
    }

    private static EntryInfo MiniBoss(float progression, string internalName, int npcId, Func<bool> downed)
    {
        return MiniBoss(progression, internalName, new List<int>() { npcId }, downed);
    }

    private static EntryInfo Event(float progression, string internalName, Func<bool> downed)
    {
        EntryInfo e = Make(EntryType.Event, progression, internalName, downed);
        e.NpcIds.AddRange(EventNpcsFor(internalName));
        e.SpawnItems.AddRange(SpawnItemsFor(internalName));
        foreach (KeyValuePair<int, CollectibleType> kv in CollectiblesFor(internalName)) e.Collectibles[kv.Key] = kv.Value;
        return e;
    }

    private static List<int> SpawnItemsFor(string internalName)
    {
        return EntrySpawnItems.TryGetValue("Terraria " + internalName, out List<int> items) ? new List<int>(items) : new List<int>();
    }

    private static Dictionary<int, CollectibleType> CollectiblesFor(string internalName)
    {
        return EntryCollectibles.TryGetValue("Terraria " + internalName, out Dictionary<int, CollectibleType> items)
            ? items : new Dictionary<int, CollectibleType>();
    }
    // ======== 事件条目的 NPC 池 ========
    //
    // 事件页上的掉落物，是把「这个事件会出现哪些 NPC」的掉落表合起来算的，
    // 小 Boss 和小怪的掉落都算在这个事件头上。
    // 名单来自原模组的 EventNPCs 表，以及原版 NPC.GetNPCInvasionGroup 的分组
    // —— 游戏自己就是拿它判断「这只怪算不算入侵成员」的。
    //
    // 重要：这里只能出现 const 常量。模组初始化时游戏的静态构造还没跑，
    // 去读 NPCID / NPC 的静态成员（NPCID.Count 之类）会直接抛
    // TypeInitializationException，导致整个模组加载失败。

    private static readonly Dictionary<string, List<int>> EventNpcs = new Dictionary<string, List<int>>()
    {
        { "TorchGod", new List<int>() {
            NPCID.TorchGod } },
        { "BloodMoon", new List<int>() {
            NPCID.BloodZombie, NPCID.Drippler, NPCID.TheGroom, NPCID.TheBride,
            NPCID.CorruptBunny, NPCID.CrimsonBunny, NPCID.CorruptGoldfish, NPCID.CrimsonGoldfish,
            NPCID.CorruptPenguin, NPCID.CrimsonPenguin, NPCID.Clown, NPCID.ChatteringTeethBomb,
            NPCID.EyeballFlyingFish, NPCID.ZombieMerman, NPCID.GoblinShark, NPCID.BloodEelHead,
            NPCID.BloodSquid, NPCID.BloodNautilus } },
        { "GoblinArmy", new List<int>() {
            NPCID.GoblinPeon, NPCID.GoblinThief, NPCID.GoblinWarrior, NPCID.GoblinSorcerer,
            NPCID.GoblinArcher, NPCID.GoblinSummoner, NPCID.ShadowFlameApparition } },
        { "FrostLegion", new List<int>() {
            NPCID.SnowmanGangsta, NPCID.MisterStabby, NPCID.SnowBalla } },
        { "OldOnesArmy", new List<int>() {
            NPCID.DD2GoblinT1, NPCID.DD2GoblinT2, NPCID.DD2GoblinT3,
            NPCID.DD2GoblinBomberT1, NPCID.DD2GoblinBomberT2, NPCID.DD2GoblinBomberT3,
            NPCID.DD2WyvernT1, NPCID.DD2WyvernT2, NPCID.DD2WyvernT3,
            NPCID.DD2JavelinstT1, NPCID.DD2JavelinstT2, NPCID.DD2JavelinstT3,
            NPCID.DD2DarkMageT1, NPCID.DD2DarkMageT3, NPCID.DD2SkeletonT1, NPCID.DD2SkeletonT3,
            NPCID.DD2WitherBeastT2, NPCID.DD2WitherBeastT3, NPCID.DD2DrakinT2, NPCID.DD2DrakinT3,
            NPCID.DD2KoboldWalkerT2, NPCID.DD2KoboldWalkerT3, NPCID.DD2KoboldFlyerT2, NPCID.DD2KoboldFlyerT3,
            NPCID.DD2OgreT2, NPCID.DD2OgreT3, NPCID.DD2LightningBugT3, NPCID.DD2Betsy } },
        { "PirateInvasion", new List<int>() {
            NPCID.PirateDeckhand, NPCID.PirateCorsair, NPCID.PirateDeadeye, NPCID.PirateCrossbower,
            NPCID.PirateCaptain, NPCID.Parrot, NPCID.PirateShip, NPCID.PirateShipCannon,
            NPCID.PirateGhost } },
        { "Eclipse", new List<int>() {
            NPCID.Eyezor, NPCID.Frankenstein, NPCID.SwampThing, NPCID.Vampire,
            NPCID.CreatureFromTheDeep, NPCID.Fritz, NPCID.ThePossessed, NPCID.Reaper,
            NPCID.Butcher, NPCID.DeadlySphere, NPCID.DrManFly, NPCID.Nailhead,
            NPCID.Psycho, NPCID.Mothron, NPCID.MothronSpawn } },
        { "PumpkinMoon", new List<int>() {
            NPCID.Scarecrow1, NPCID.Scarecrow2, NPCID.Scarecrow3, NPCID.Scarecrow4, NPCID.Scarecrow5,
            NPCID.Scarecrow6, NPCID.Scarecrow7, NPCID.Scarecrow8, NPCID.Scarecrow9, NPCID.Scarecrow10,
            NPCID.HeadlessHorseman, NPCID.Splinterling, NPCID.Hellhound, NPCID.Poltergeist,
            NPCID.MourningWood, NPCID.Pumpking, NPCID.PumpkingBlade } },
        { "FrostMoon", new List<int>() {
            NPCID.PresentMimic, NPCID.Flocko, NPCID.GingerbreadMan, NPCID.ZombieElf,
            NPCID.ZombieElfBeard, NPCID.ZombieElfGirl, NPCID.ElfArcher, NPCID.Nutcracker,
            NPCID.NutcrackerSpinning, NPCID.Yeti, NPCID.ElfCopter, NPCID.Krampus,
            NPCID.Everscream, NPCID.SantaNK1, NPCID.IceQueen } },
        { "MartianMadness", new List<int>() {
            NPCID.BrainScrambler, NPCID.RayGunner, NPCID.MartianOfficer, NPCID.MartianEngineer,
            NPCID.GrayGrunt, NPCID.MartianTurret, NPCID.MartianDrone, NPCID.GigaZapper,
            NPCID.ScutlixRider, NPCID.Scutlix, NPCID.MartianSaucerCannon, NPCID.MartianSaucerCore,
            NPCID.MartianWalker } },
        { "LunarEvent", new List<int>() {
            NPCID.LunarTowerSolar, NPCID.SolarSolenian, NPCID.SolarSpearman, NPCID.SolarCorite,
            NPCID.SolarSroller, NPCID.SolarCrawltipedeHead, NPCID.SolarDrakomire, NPCID.SolarDrakomireRider,
            NPCID.LunarTowerVortex, NPCID.VortexHornet, NPCID.VortexHornetQueen, NPCID.VortexLarva,
            NPCID.VortexRifleman, NPCID.VortexSoldier,
            NPCID.LunarTowerNebula, NPCID.NebulaBeast, NPCID.NebulaBrain, NPCID.NebulaHeadcrab,
            NPCID.NebulaSoldier,
            NPCID.LunarTowerStardust, NPCID.StardustCellBig, NPCID.StardustJellyfishBig,
            NPCID.StardustSoldier, NPCID.StardustSpiderBig, NPCID.StardustWormHead } },
    };

    /// <summary>事件条目的成员名单（没登记的事件就是空的）。</summary>
    private static List<int> EventNpcsFor(string internalName)
    {
        return EventNpcs.TryGetValue(internalName, out List<int> ids) ? new List<int>(ids) : new List<int>();
    }    public static List<EntryInfo> Build()
    {
        List<EntryInfo> list = new List<EntryInfo>();

        // ---------------- Boss ----------------
        list.Add(Boss(KingSlime, "KingSlime", NPCID.KingSlime, () => NPC.downedSlimeKing).Portrait("Boss50"));
        list.Add(Boss(EyeOfCthulhu, "EyeofCthulhu", NPCID.EyeofCthulhu, () => NPC.downedBoss1));
        list.Add(Boss(EaterOfWorlds, "EaterofWorlds", new List<int>() { NPCID.EaterofWorldsHead, NPCID.EaterofWorldsBody, NPCID.EaterofWorldsTail }, () => NPC.downedBoss2)
            .AvailableWhen(() => !WorldGen.crimson || Main.drunkWorld)
            .Portrait("Boss13"));
        list.Add(Boss(EaterOfWorlds, "BrainofCthulhu", NPCID.BrainofCthulhu, () => NPC.downedBoss2)
            .AvailableWhen(() => WorldGen.crimson || Main.drunkWorld));
        list.Add(Boss(QueenBee, "QueenBee", NPCID.QueenBee, () => NPC.downedQueenBee));
        list.Add(Boss(Skeletron, "Skeletron", new List<int>() { NPCID.SkeletronHead }, () => NPC.downedBoss3)
            .LimbsOf(NPCID.SkeletronHand)
            .Portrait("Boss35"));
        list.Add(Boss(DeerClops, "Deerclops", NPCID.Deerclops, () => NPC.downedDeerclops).Portrait("Boss668"));
        list.Add(Boss(WallOfFlesh, "WallofFlesh", new List<int>() { NPCID.WallofFlesh }, () => Main.hardMode).Portrait("Boss113"));
        list.Add(Boss(QueenSlime, "QueenSlimeBoss", NPCID.QueenSlimeBoss, () => NPC.downedQueenSlime).Portrait("Boss657"));
        list.Add(Boss(TheTwins, "TheTwins", new List<int>() { NPCID.Retinazer, NPCID.Spazmatism }, () => NPC.downedMechBoss2)
            .LimbsOf(NPCID.Retinazer, NPCID.Spazmatism)
            .Portrait("Boss125"));
        list.Add(Boss(TheDestroyer, "TheDestroyer", NPCID.TheDestroyer, () => NPC.downedMechBoss1).Portrait("Boss134"));
        list.Add(Boss(SkeletronPrime, "SkeletronPrime", NPCID.SkeletronPrime, () => NPC.downedMechBoss3)
            .LimbsOf(NPCID.PrimeCannon, NPCID.PrimeSaw, NPCID.PrimeVice, NPCID.PrimeLaser)
            .Portrait("Boss127"));
        list.Add(Boss(Plantera, "Plantera", NPCID.Plantera, () => NPC.downedPlantBoss));
        list.Add(Boss(Golem, "Golem", new List<int>() { NPCID.Golem }, () => NPC.downedGolemBoss)
            .LimbsOf(NPCID.GolemFistLeft, NPCID.GolemFistRight, NPCID.GolemHead)
            .Portrait("Boss245")
            .HeadVanilla("Images/NPC_Head_Boss_5"));
        list.Add(Boss(Betsy, "DD2Betsy", NPCID.DD2Betsy, () => DD2Event.DownedInvasionT3).Portrait("Boss551"));
        list.Add(Boss(EmpressOfLight, "HallowBoss", NPCID.HallowBoss, () => NPC.downedEmpressOfLight).Portrait("Boss636"));
        list.Add(Boss(DukeFishron, "DukeFishron", NPCID.DukeFishron, () => NPC.downedFishron));
        list.Add(Boss(LunaticCultist, "CultistBoss", NPCID.CultistBoss, () => NPC.downedAncientCultist).Portrait("Boss439"));
        list.Add(Boss(Moonlord, "MoonLord", new List<int>() { NPCID.MoonLordHead, NPCID.MoonLordCore }, () => NPC.downedMoonlord)
            .LimbsOf(NPCID.MoonLordHead, NPCID.MoonLordHand)
            .Portrait("Boss396"));

        // ---------------- 小 Boss / 事件 ----------------
        list.Add(Event(TorchGod, "TorchGod", () => Main.LocalPlayer.unlockedBiomeTorches)
            .HeadVanilla("Images/Item_" + ItemID.TorchGodsFavor));
        list.Add(Event(BloodMoon, "BloodMoon", () => WorldState.DownedBloodMoon)
            .Portrait("EventBloodMoon")
            .HeadRaw("EventBloodMoon_Head"));
        list.Add(Event(GoblinArmy, "GoblinArmy", () => NPC.downedGoblins)
            .Portrait("EventGoblinArmy")
            .HeadVanilla("Images/Extra_9"));
        list.Add(Event(OldOnesArmy, "OldOnesArmy", () => DD2Event.DownedInvasionAnyDifficulty)
            .Portrait("EventOldOnesArmy")
            .HeadVanilla("Images/Extra_79"));
        list.Add(MiniBoss(DarkMage, "DarkMage", new List<int>() { NPCID.DD2DarkMageT3, NPCID.DD2DarkMageT1 }, () => WorldState.DownedDarkMage)
            .Portrait("Boss565"));
        list.Add(MiniBoss(Ogre, "Ogre", new List<int>() { NPCID.DD2OgreT3, NPCID.DD2OgreT2 }, () => WorldState.DownedOgre)
            .Portrait("Boss577"));
        list.Add(Event(FrostLegion, "FrostLegion", () => NPC.downedFrost)
            .AvailableWhen(() => Main.xMas)
            .Portrait("EventFrostLegion")
            .HeadVanilla("Images/Extra_7"));
        list.Add(Event(PirateInvasion, "PirateInvasion", () => NPC.downedPirates)
            .Portrait("EventPirateInvasion")
            .HeadVanilla("Images/Extra_11"));
        list.Add(MiniBoss(PirateShip, "PirateShip", NPCID.PirateShip, () => WorldState.DownedFlyingDutchman).Portrait("Boss491"));
        list.Add(Event(SolarEclipse, "Eclipse", () => WorldState.DownedSolarEclipse)
            .Portrait("EventSolarEclipse")
            .HeadRaw("EventSolarEclipse_Head"));
        list.Add(Event(PumpkinMoon, "PumpkinMoon", () => WorldState.DownedPumpkinMoon)
            .Portrait("EventPumpkinMoon")
            .HeadVanilla("Images/Extra_12"));
        list.Add(MiniBoss(MourningWood, "MourningWood", NPCID.MourningWood, () => NPC.downedHalloweenTree));
        list.Add(MiniBoss(Pumpking, "Pumpking", NPCID.Pumpking, () => NPC.downedHalloweenKing).Portrait("Boss327"));
        list.Add(Event(FrostMoon, "FrostMoon", () => WorldState.DownedFrostMoon)
            .Portrait("EventFrostMoon")
            .HeadVanilla("Images/Extra_8"));
        list.Add(MiniBoss(Everscream, "Everscream", NPCID.Everscream, () => NPC.downedChristmasTree));
        list.Add(MiniBoss(SantaNK1, "SantaNK1", NPCID.SantaNK1, () => NPC.downedChristmasSantank));
        list.Add(MiniBoss(IceQueen, "IceQueen", NPCID.IceQueen, () => NPC.downedChristmasIceQueen));
        list.Add(Event(MartianMadness, "MartianMadness", () => NPC.downedMartians)
            .Portrait("EventMartianMadness")
            .HeadVanilla("Images/Extra_10"));
        list.Add(MiniBoss(MartianSaucer, "MartianSaucer", new List<int>() { NPCID.MartianSaucer, NPCID.MartianSaucerCore }, () => WorldState.DownedMartianSaucer));
        list.Add(Event(LunarEvent, "LunarEvent", () => NPC.downedTowers)
            .LimbsOf(NPCID.LunarTowerVortex, NPCID.LunarTowerStardust, NPCID.LunarTowerNebula, NPCID.LunarTowerSolar)
            .Portrait("EventLunarEvent")
            .HeadNpc(NPCID.LunarTowerNebula, NPCID.LunarTowerVortex, NPCID.LunarTowerSolar, NPCID.LunarTowerStardust));

        return list;
    }


    // ======== 原模组自带的「生成物 / 收藏品」表（物品 id 直接用原版 ItemID 常量） ========

    internal static readonly Dictionary<string, List<int>> EntrySpawnItems = new Dictionary<string, List<int>>() {
			#region Boss SpawnItems
			{ "Terraria KingSlime", new List<int>() { ItemID.SlimeCrown } },
			{ "Terraria EyeofCthulhu", new List<int>() { ItemID.SuspiciousLookingEye } },
			{ "Terraria EaterofWorlds", new List<int>() { ItemID.WormFood } },
			{ "Terraria BrainofCthulhu", new List<int>() { ItemID.BloodySpine } },
			{ "Terraria QueenBee", new List<int>() { ItemID.Abeemination } },
			{ "Terraria Skeletron", new List<int>() { ItemID.ClothierVoodooDoll } },
			{ "Terraria Deerclops", new List<int>() { ItemID.DeerThing } },
			{ "Terraria WallofFlesh", new List<int>() { ItemID.GuideVoodooDoll } },
			{ "Terraria QueenSlimeBoss", new List<int>() { ItemID.QueenSlimeCrystal } },
			{ "Terraria TheTwins", new List<int>() { ItemID.MechanicalEye } },
			{ "Terraria TheDestroyer", new List<int>() { ItemID.MechanicalWorm } },
			{ "Terraria SkeletronPrime", new List<int>() { ItemID.MechanicalSkull } },
			// Terraria Plantera: none
			{ "Terraria Golem", new List<int>() { ItemID.LihzahrdAltar, ItemID.LihzahrdPowerCell } },
			{ "Terraria HallowBoss", new List<int>() { ItemID.EmpressButterfly } },
			{ "Terraria DD2Betsy", new List<int>() { ItemID.DD2ElderCrystal, ItemID.DD2ElderCrystalStand } },
			{ "Terraria DukeFishron", new List<int>() { ItemID.TruffleWorm } },
			// Terraria CultistBoss : none
			{ "Terraria MoonLord", new List<int>() { ItemID.CelestialSigil } },
			#endregion
			// Mini-bosses tied to events will not display spawn items
			#region Event Collectibles
			{ "Terraria TorchGod", new List<int>() { ItemID.Torch } },
			{ "Terraria BloodMoon", new List<int>() { ItemID.BloodMoonStarter } },
			{ "Terraria GoblinArmy", new List<int>() { ItemID.GoblinBattleStandard } },
			{ "Terraria OldOnesArmy", new List<int>() { ItemID.DD2ElderCrystal, ItemID.DD2ElderCrystalStand } },
			{ "Terraria FrostLegion", new List<int>() { ItemID.SnowGlobe } },
			{ "Terraria Eclipse", new List<int>() { ItemID.SolarTablet } },
			{ "Terraria PirateInvasion", new List<int>() { ItemID.PirateMap } },
			{ "Terraria PumpkinMoon", new List<int>() { ItemID.PumpkinMoonMedallion } },
			{ "Terraria FrostMoon", new List<int>() { ItemID.NaughtyPresent } },
			// Terraria MartianMadness: none
			// Terraria LunarEvent: none
			#endregion
		};

    internal static readonly Dictionary<string, Dictionary<int, CollectibleType>> EntryCollectibles = new Dictionary<string, Dictionary<int, CollectibleType>>() {
			#region Boss Collectibles
			{ "Terraria KingSlime",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.KingSlimeMasterTrophy, CollectibleType.Relic },
					{ ItemID.KingSlimePetItem, CollectibleType.MasterPet },
					{ ItemID.KingSlimeTrophy, CollectibleType.Trophy },
					{ ItemID.KingSlimeMask, CollectibleType.Mask },
					{ ItemID.MusicBoxBoss1, CollectibleType.Music },
					{ ItemID.MusicBoxOWBoss1, CollectibleType.Music },
				}
			},
			{ "Terraria EyeofCthulhu",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.EyeofCthulhuMasterTrophy, CollectibleType.Relic },
					{ ItemID.EyeOfCthulhuPetItem, CollectibleType.MasterPet },
					{ ItemID.EyeofCthulhuTrophy, CollectibleType.Trophy },
					{ ItemID.EyeMask, CollectibleType.Mask },
					{ ItemID.MusicBoxBoss1, CollectibleType.Music },
					{ ItemID.MusicBoxOWBoss1, CollectibleType.Music },
					{ ItemID.AviatorSunglasses, CollectibleType.Generic },
					{ ItemID.BadgersHat, CollectibleType.Generic },
				}
			},
			{ "Terraria EaterofWorlds",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.EaterofWorldsMasterTrophy, CollectibleType.Relic },
					{ ItemID.EaterOfWorldsPetItem, CollectibleType.MasterPet },
					{ ItemID.EaterofWorldsTrophy, CollectibleType.Trophy },
					{ ItemID.EaterMask, CollectibleType.Mask },
					{ ItemID.MusicBoxBoss1, CollectibleType.Music },
					{ ItemID.MusicBoxOWBoss1, CollectibleType.Music },
					{ ItemID.EatersBone, CollectibleType.Generic },
				}
			},
			{ "Terraria BrainofCthulhu",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.BrainofCthulhuMasterTrophy, CollectibleType.Relic },
					{ ItemID.BrainOfCthulhuPetItem, CollectibleType.MasterPet },
					{ ItemID.BrainofCthulhuTrophy, CollectibleType.Trophy },
					{ ItemID.BrainMask, CollectibleType.Mask },
					{ ItemID.MusicBoxBoss3, CollectibleType.Music },
					{ ItemID.MusicBoxOWBoss1, CollectibleType.Music },
					{ ItemID.BoneRattle, CollectibleType.Generic },
				}
			},
			{ "Terraria QueenBee",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.QueenBeeMasterTrophy, CollectibleType.Relic },
					{ ItemID.QueenBeePetItem, CollectibleType.MasterPet },
					{ ItemID.QueenBeeTrophy, CollectibleType.Trophy },
					{ ItemID.BeeMask, CollectibleType.Mask },
					{ ItemID.MusicBoxBoss5, CollectibleType.Music },
					{ ItemID.MusicBoxOWBoss1, CollectibleType.Music },
					{ ItemID.Nectar, CollectibleType.Generic },
				}
			},
			{ "Terraria Skeletron",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.SkeletronMasterTrophy, CollectibleType.Relic },
					{ ItemID.SkeletronPetItem, CollectibleType.MasterPet },
					{ ItemID.SkeletronTrophy, CollectibleType.Trophy },
					{ ItemID.SkeletronMask, CollectibleType.Mask },
					{ ItemID.MusicBoxBoss1, CollectibleType.Music },
					{ ItemID.MusicBoxOWBoss1, CollectibleType.Music },
					{ ItemID.ChippysCouch, CollectibleType.Generic },
				}
			},
			{ "Terraria Deerclops",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.DeerclopsMasterTrophy, CollectibleType.Relic },
					{ ItemID.DeerclopsPetItem, CollectibleType.MasterPet },
					{ ItemID.DeerclopsTrophy, CollectibleType.Trophy },
					{ ItemID.DeerclopsMask, CollectibleType.Mask },
					{ ItemID.MusicBoxDeerclops, CollectibleType.Music },
					{ ItemID.MusicBoxOWBoss1, CollectibleType.Music },
				}
			},
			{ "Terraria WallofFlesh",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.WallofFleshMasterTrophy, CollectibleType.Relic },
					{ ItemID.WallOfFleshGoatMountItem, CollectibleType.MasterPet },
					{ ItemID.WallofFleshTrophy, CollectibleType.Trophy },
					{ ItemID.FleshMask, CollectibleType.Mask },
					{ ItemID.MusicBoxBoss2, CollectibleType.Music },
					{ ItemID.MusicBoxOWWallOfFlesh, CollectibleType.Music },
					{ ItemID.BadgersHat, CollectibleType.Generic },
				}
			},
			{ "Terraria QueenSlimeBoss",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.QueenSlimeMasterTrophy, CollectibleType.Relic },
					{ ItemID.QueenSlimePetItem, CollectibleType.MasterPet },
					{ ItemID.QueenSlimeTrophy, CollectibleType.Trophy },
					{ ItemID.QueenSlimeMask, CollectibleType.Mask },
					{ ItemID.MusicBoxQueenSlime, CollectibleType.Music },
					{ ItemID.MusicBoxOWBoss2, CollectibleType.Music },
				}
			},
			{ "Terraria TheTwins",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.TwinsMasterTrophy, CollectibleType.Relic },
					{ ItemID.TwinsPetItem, CollectibleType.MasterPet },
					{ ItemID.RetinazerTrophy, CollectibleType.Trophy },
					{ ItemID.SpazmatismTrophy, CollectibleType.Trophy },
					{ ItemID.TwinMask, CollectibleType.Mask },
					{ ItemID.MusicBoxBoss2, CollectibleType.Music },
					{ ItemID.MusicBoxOWBoss2, CollectibleType.Music },
				}
			},
			{ "Terraria TheDestroyer",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.DestroyerMasterTrophy, CollectibleType.Relic },
					{ ItemID.DestroyerPetItem, CollectibleType.MasterPet },
					{ ItemID.DestroyerTrophy, CollectibleType.Trophy },
					{ ItemID.DestroyerMask, CollectibleType.Mask },
					{ ItemID.MusicBoxBoss3, CollectibleType.Music },
					{ ItemID.MusicBoxOWBoss2, CollectibleType.Music },
				}
			},
			{ "Terraria SkeletronPrime",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.SkeletronPrimeMasterTrophy, CollectibleType.Relic },
					{ ItemID.SkeletronPrimePetItem, CollectibleType.MasterPet },
					{ ItemID.SkeletronPrimeTrophy, CollectibleType.Trophy },
					{ ItemID.SkeletronPrimeMask, CollectibleType.Mask },
					{ ItemID.MusicBoxBoss1, CollectibleType.Music },
					{ ItemID.MusicBoxOWBoss2, CollectibleType.Music },
				}
			},
			{ "Terraria Plantera",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.PlanteraMasterTrophy, CollectibleType.Relic },
					{ ItemID.PlanteraPetItem, CollectibleType.MasterPet },
					{ ItemID.PlanteraTrophy, CollectibleType.Trophy },
					{ ItemID.PlanteraMask, CollectibleType.Mask },
					{ ItemID.MusicBoxPlantera, CollectibleType.Music },
					{ ItemID.MusicBoxOWPlantera, CollectibleType.Music },
					{ ItemID.Seedling, CollectibleType.Generic },
				}
			},
			{ "Terraria Golem",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.GolemMasterTrophy, CollectibleType.Relic },
					{ ItemID.GolemPetItem, CollectibleType.MasterPet },
					{ ItemID.GolemTrophy, CollectibleType.Trophy },
					{ ItemID.GolemMask, CollectibleType.Mask },
					{ ItemID.MusicBoxBoss5, CollectibleType.Music },
					{ ItemID.MusicBoxOWBoss2, CollectibleType.Music },
				}
			},
			{ "Terraria HallowBoss",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.FairyQueenMasterTrophy, CollectibleType.Relic },
					{ ItemID.FairyQueenPetItem, CollectibleType.MasterPet },
					{ ItemID.FairyQueenTrophy, CollectibleType.Trophy },
					{ ItemID.FairyQueenMask, CollectibleType.Mask },
					{ ItemID.MusicBoxEmpressOfLight, CollectibleType.Music },
					{ ItemID.MusicBoxOWBoss2, CollectibleType.Music },
					{ ItemID.HallowBossDye, CollectibleType.Generic },
					{ ItemID.RainbowCursor, CollectibleType.Generic },
				}
			},
			{ "Terraria DD2Betsy",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.BetsyMasterTrophy, CollectibleType.Relic },
					{ ItemID.DD2BetsyPetItem, CollectibleType.MasterPet },
					{ ItemID.BossTrophyBetsy, CollectibleType.Trophy },
					{ ItemID.BossMaskBetsy, CollectibleType.MasterPet },
					{ ItemID.MusicBoxDD2, CollectibleType.Music },
					{ ItemID.MusicBoxOWInvasion, CollectibleType.Music },
				}
			},
			{ "Terraria DukeFishron",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.DukeFishronMasterTrophy, CollectibleType.Relic },
					{ ItemID.DukeFishronPetItem, CollectibleType.MasterPet },
					{ ItemID.DukeFishronTrophy, CollectibleType.Trophy },
					{ ItemID.DukeFishronMask, CollectibleType.Mask },
					{ ItemID.MusicBoxDukeFishron, CollectibleType.Music },
					{ ItemID.MusicBoxOWBoss2, CollectibleType.Music },
				}
			},
			{ "Terraria CultistBoss",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.LunaticCultistMasterTrophy, CollectibleType.Relic },
					{ ItemID.LunaticCultistPetItem, CollectibleType.MasterPet },
					{ ItemID.AncientCultistTrophy, CollectibleType.Trophy },
					{ ItemID.BossMaskCultist, CollectibleType.Mask },
					{ ItemID.MusicBoxBoss5, CollectibleType.Music },
					{ ItemID.MusicBoxOWBoss2, CollectibleType.Music },
				}
			},
			{ "Terraria MoonLord",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.MoonLordMasterTrophy, CollectibleType.Relic },
					{ ItemID.MoonLordPetItem, CollectibleType.MasterPet },
					{ ItemID.MoonLordTrophy, CollectibleType.Trophy },
					{ ItemID.BossMaskMoonlord, CollectibleType.Mask },
					{ ItemID.MusicBoxLunarBoss, CollectibleType.Music },
					{ ItemID.MusicBoxOWMoonLord, CollectibleType.Music },
				}
			},
			#endregion
			#region Mini-boss Collectibles
			{ "Terraria DD2DarkMageT3",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.DarkMageMasterTrophy, CollectibleType.Relic },
					{ ItemID.DarkMageBookMountItem, CollectibleType.MasterPet },
					{ ItemID.BossTrophyDarkmage, CollectibleType.Trophy },
					{ ItemID.BossMaskDarkMage, CollectibleType.Mask },
					{ ItemID.DD2PetDragon, CollectibleType.Generic },
					{ ItemID.DD2PetGato, CollectibleType.Generic },
				}
			},
			{ "Terraria PirateShip",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.FlyingDutchmanMasterTrophy, CollectibleType.Relic },
					{ ItemID.PirateShipMountItem, CollectibleType.MasterPet },
					{ ItemID.FlyingDutchmanTrophy, CollectibleType.Trophy },
				}
			},
			{ "Terraria DD2OgreT3",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.OgreMasterTrophy, CollectibleType.Relic },
					{ ItemID.DD2OgrePetItem, CollectibleType.MasterPet },
					{ ItemID.BossTrophyOgre, CollectibleType.Trophy },
					{ ItemID.BossMaskOgre, CollectibleType.Mask },
					{ ItemID.DD2PetGhost, CollectibleType.Generic },
				}
			},
			{ "Terraria MourningWood",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.MourningWoodMasterTrophy, CollectibleType.Relic },
					{ ItemID.SpookyWoodMountItem, CollectibleType.MasterPet },
					{ ItemID.MourningWoodTrophy, CollectibleType.Trophy },
					{ ItemID.CursedSapling, CollectibleType.Generic },
				}
			},
			{ "Terraria Pumpking",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.PumpkingMasterTrophy, CollectibleType.Relic },
					{ ItemID.PumpkingPetItem, CollectibleType.MasterPet },
					{ ItemID.PumpkingTrophy, CollectibleType.Trophy },
					{ ItemID.SpiderEgg, CollectibleType.Generic },
				}
			},
			{ "Terraria Everscream",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.EverscreamMasterTrophy, CollectibleType.Relic },
					{ ItemID.EverscreamPetItem, CollectibleType.MasterPet },
					{ ItemID.EverscreamTrophy, CollectibleType.Trophy },
				}
			},
			{ "Terraria SantaNK1",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.SantankMasterTrophy, CollectibleType.Relic },
					{ ItemID.SantankMountItem, CollectibleType.MasterPet },
					{ ItemID.SantaNK1Trophy, CollectibleType.Trophy },
				}
			},
			{ "Terraria IceQueen",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.IceQueenMasterTrophy, CollectibleType.Relic },
					{ ItemID.IceQueenPetItem, CollectibleType.MasterPet },
					{ ItemID.IceQueenTrophy, CollectibleType.Trophy },
					{ ItemID.BabyGrinchMischiefWhistle, CollectibleType.Generic },
				}
			},
			{ "Terraria MartianSaucer",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.UFOMasterTrophy, CollectibleType.Relic },
					{ ItemID.MartianPetItem, CollectibleType.MasterPet },
					{ ItemID.MartianSaucerTrophy, CollectibleType.Trophy },
				}
			},
			#endregion
			#region Event Collectibles
			{ "Terraria TorchGod",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.MusicBoxBoss3, CollectibleType.Music },
					{ ItemID.MusicBoxOWWallOfFlesh, CollectibleType.Music },
				}
			},
			{ "Terraria BloodMoon",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.MusicBoxEerie, CollectibleType.Music },
					{ ItemID.MusicBoxOWBloodMoon, CollectibleType.Music },
				}
			},
			{ "Terraria GoblinArmy",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.MusicBoxGoblins, CollectibleType.Music },
					{ ItemID.MusicBoxOWInvasion, CollectibleType.Music },
				}
			},
			{ "Terraria OldOnesArmy",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.MusicBoxDD2, CollectibleType.Music },
					{ ItemID.MusicBoxOWInvasion, CollectibleType.Music },
				}
			},
			{ "Terraria FrostLegion",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.MusicBoxBoss3, CollectibleType.Music },
					{ ItemID.MusicBoxOWInvasion, CollectibleType.Music },
				}
			},
			{ "Terraria Eclipse",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.MusicBoxEclipse, CollectibleType.Music },
					{ ItemID.MusicBoxOWBloodMoon, CollectibleType.Music },
				}
			},
			{ "Terraria PirateInvasion",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.MusicBoxPirates, CollectibleType.Music },
					{ ItemID.MusicBoxOWInvasion, CollectibleType.Music },
				}
			},
			{ "Terraria PumpkinMoon",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.MusicBoxPumpkinMoon, CollectibleType.Music },
					{ ItemID.MusicBoxOWInvasion, CollectibleType.Music },
				}
			},
			{ "Terraria FrostMoon",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.MusicBoxFrostMoon, CollectibleType.Music },
					{ ItemID.MusicBoxOWInvasion, CollectibleType.Music },
				}
			},
			{ "Terraria MartianMadness",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.MusicBoxMartians, CollectibleType.Music },
					{ ItemID.MusicBoxOWInvasion, CollectibleType.Music },
				}
			},
			{ "Terraria LunarEvent",
				new Dictionary<int, CollectibleType>() {
					{ ItemID.MusicBoxTowers, CollectibleType.Music },
					{ ItemID.MusicBoxOWTowers, CollectibleType.Music },
				}
			}
			#endregion
		};
}
