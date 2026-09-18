using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Terraria;

namespace BossChecklist;

/// <summary>
/// 每个世界各自的状态：原版没记录的那几个「已击败」标记、隐藏的条目、手动勾选的条目。
/// </summary>
internal static class WorldState
{
    public static bool DownedBloodMoon;
    public static bool DownedFrostMoon;
    public static bool DownedPumpkinMoon;
    public static bool DownedSolarEclipse;
    public static bool DownedDarkMage;
    public static bool DownedOgre;
    public static bool DownedFlyingDutchman;
    public static bool DownedMartianSaucer;

    public static readonly HashSet<string> Hidden = new HashSet<string>();
    public static readonly HashSet<string> Marked = new HashSet<string>();

    public static string WorldKey { get; set; } = "";
    public static string WorldName { get; set; } = "";

    public static void Reset()
    {
        DownedBloodMoon = DownedFrostMoon = DownedPumpkinMoon = DownedSolarEclipse = false;
        DownedDarkMage = DownedOgre = DownedFlyingDutchman = DownedMartianSaucer = false;
        Hidden.Clear();
        Marked.Clear();
        WorldKey = "";
        WorldName = "";
    }

    /// <summary>当前世界的键（UniqueId 优先，取不到就用世界名）。</summary>
    public static string CurrentWorldKey()
    {
        try
        {
            if (Main.ActiveWorldFileData != null && Main.ActiveWorldFileData.UniqueId != Guid.Empty)
                return Main.ActiveWorldFileData.UniqueId.ToString();
        }
        catch
        {
        }
        return "name:" + (Main.worldName ?? "");
    }

    public static void Load()
    {
        Reset();
        if (Main.gameMenu) return;
        WorldKey = CurrentWorldKey();
        WorldName = Main.worldName ?? "";
        SaveData.LoadWorldInto(WorldKey);
        ApplyToEntries();
    }

    public static void Save()
    {
        if (string.IsNullOrEmpty(WorldKey)) return;
        SaveData.StoreCurrentWorld(WorldKey, WorldName);
    }

    /// <summary>把世界状态同步到条目对象上。</summary>
    public static void ApplyToEntries()
    {
        foreach (EntryInfo entry in BossTracker.Entries)
        {
            entry.Hidden = Hidden.Contains(entry.Key);
            entry.Marked = Marked.Contains(entry.Key);
        }
    }

    public static void ToggleMarked(EntryInfo entry)
    {
        if (!Marked.Remove(entry.Key)) Marked.Add(entry.Key);
        entry.Marked = Marked.Contains(entry.Key);
        Save();
    }

    public static void ToggleHidden(EntryInfo entry)
    {
        if (!Hidden.Remove(entry.Key)) Hidden.Add(entry.Key);
        entry.Hidden = Hidden.Contains(entry.Key);
        Save();
    }

    public static void ClearMarked()
    {
        Marked.Clear();
        ApplyToEntries();
        Save();
    }

    public static void ClearHidden()
    {
        Hidden.Clear();
        ApplyToEntries();
        Save();
    }

    public static List<string> DownedFlagList()
    {
        List<string> list = new List<string>();
        if (DownedBloodMoon) list.Add("bloodmoon");
        if (DownedFrostMoon) list.Add("frostmoon");
        if (DownedPumpkinMoon) list.Add("pumpkinmoon");
        if (DownedSolarEclipse) list.Add("solareclipse");
        if (DownedDarkMage) list.Add("darkmage");
        if (DownedOgre) list.Add("ogre");
        if (DownedFlyingDutchman) list.Add("flyingdutchman");
        if (DownedMartianSaucer) list.Add("martiansaucer");
        return list;
    }

    public static void SetFlag(string flag)
    {
        switch (flag)
        {
            case "bloodmoon": DownedBloodMoon = true; break;
            case "frostmoon": DownedFrostMoon = true; break;
            case "pumpkinmoon": DownedPumpkinMoon = true; break;
            case "solareclipse": DownedSolarEclipse = true; break;
            case "darkmage": DownedDarkMage = true; break;
            case "ogre": DownedOgre = true; break;
            case "flyingdutchman": DownedFlyingDutchman = true; break;
            case "martiansaucer": DownedMartianSaucer = true; break;
        }
    }
}

/// <summary>某个 Boss 的玩家个人纪录。</summary>
internal sealed class BossRecord
{
    public string Key;
    public int Kills;
    public int Deaths;
    public int Attempts;
    public long FirstVictoryTicks = -1;  // 首次击败时角色的总游戏时间（tick，60/秒）
    public int BestDuration = -1;        // 最快击杀用时（tick）
    public int PreviousDuration = -1;
    public int FirstDuration = -1;
    public int BestHits = -1;            // 最少被击中次数
    public int PreviousHits = -1;
    public int FirstHits = -1;

    // 运行期状态（不存档）
    public bool Tracking;
    public int TrackerTicks;
    public int TrackerHits;
    public int TrackerDeaths;

    public bool UnlockedFirstVictory => FirstVictoryTicks > 0;
    public bool UnlockedPersonalBest => Kills >= 2;
}

/// <summary>某个 Boss 的世界纪录（同世界的所有玩家共享）。</summary>
internal sealed class WorldBossRecord
{
    public string Key;
    public int Kills;
    public int Deaths;
    public int BestDuration = -1;
    public int BestHits = -1;
    public readonly List<string> DurationHolders = new List<string>();
    public readonly List<string> HitsHolders = new List<string>();
}

/// <summary>玩家纪录与世界纪录。</summary>
internal static class Records
{
    public static readonly Dictionary<string, BossRecord> Player = new Dictionary<string, BossRecord>();
    public static readonly Dictionary<string, WorldBossRecord> World = new Dictionary<string, WorldBossRecord>();

    public static BossRecord GetPlayer(string key)
    {
        if (!Player.TryGetValue(key, out BossRecord r))
        {
            r = new BossRecord { Key = key };
            Player[key] = r;
        }
        return r;
    }

    public static WorldBossRecord GetWorld(string key)
    {
        if (!World.TryGetValue(key, out WorldBossRecord r))
        {
            r = new WorldBossRecord { Key = key };
            World[key] = r;
        }
        return r;
    }

    public static void Clear()
    {
        Player.Clear();
        World.Clear();
    }

    /// <summary>玩家进世界时，把这个世界 / 这个玩家的纪录读出来。</summary>
    public static void LoadForWorld()
    {
        Clear();
        if (Main.gameMenu) return;
        SaveData.LoadRecordsInto(WorldState.CurrentWorldKey(), PlayerName(), Player, World);
    }

    // ---------------- 战斗追踪 ----------------

    public static void StartTracking(EntryInfo entry)
    {
        if (!entry.HasRecords) return;
        BossRecord r = GetPlayer(entry.Key);
        if (r.Tracking) return;
        r.Tracking = true;
        r.TrackerTicks = 0;
        r.TrackerHits = 0;
        r.TrackerDeaths = 0;
    }

    public static void ResetTracking()
    {
        foreach (BossRecord r in Player.Values)
        {
            r.Tracking = false;
            r.TrackerTicks = 0;
            r.TrackerHits = 0;
            r.TrackerDeaths = 0;
        }
    }

    public static void Tick()
    {
        foreach (BossRecord r in Player.Values)
        {
            if (r.Tracking) r.TrackerTicks++;
        }
    }

    public static void NoteHit()
    {
        foreach (BossRecord r in Player.Values)
        {
            if (r.Tracking) r.TrackerHits++;
        }
    }

    public static void NoteDeath()
    {
        foreach (BossRecord r in Player.Values)
        {
            if (r.Tracking) r.TrackerDeaths++;
        }
    }

    /// <summary>追踪中的这一场打完了，把结果写进纪录。</summary>
    public static void FinishFight(EntryInfo entry, bool victory)
    {
        BossRecord r = GetPlayer(entry.Key);
        if (!r.Tracking) return;

        int duration = r.TrackerTicks;
        int hits = r.TrackerHits;
        int deaths = r.TrackerDeaths;
        r.Tracking = false;
        r.TrackerTicks = 0;
        r.TrackerHits = 0;
        r.TrackerDeaths = 0;

        // 场次和死亡次数无论输赢都要记（死亡数 = 这一场里你被打死的次数）
        r.Attempts++;
        r.Deaths += deaths;
        GetWorld(entry.Key).Deaths += deaths;

        // 「上次尝试」记的是真正打过的那一场：打赢了，或者至少挨过打。
        // Boss 自己刷出来又走掉、你完全没碰过的情况不会覆盖上一次的成绩。
        if (victory || hits > 0)
        {
            r.PreviousDuration = duration;
            r.PreviousHits = hits;
        }

        // 关掉「允许新记录」之后只记场次 / 死亡 / 上次尝试，不刷新首次胜利和个人最佳
        if (!victory || !BossChecklistMod.Config.AllowNewRecords)
        {
            SaveData.Save();
            return;
        }

        r.Kills++;
        if (r.FirstDuration == -1)
        {
            r.FirstDuration = duration;
            r.FirstHits = hits;
            r.FirstVictoryTicks = PlayTimeTicks();
        }
        if (r.BestDuration == -1 || duration < r.BestDuration) r.BestDuration = duration;
        if (r.BestHits == -1 || hits < r.BestHits) r.BestHits = hits;

        WorldBossRecord w = GetWorld(entry.Key);
        w.Kills++;
        string me = PlayerName();
        if (w.BestDuration == -1 || duration < w.BestDuration || (duration == w.BestDuration && !w.DurationHolders.Contains(me)))
        {
            w.BestDuration = duration;
            if (!w.DurationHolders.Contains(me)) w.DurationHolders.Add(me);
        }
        else if (duration == w.BestDuration && !w.DurationHolders.Contains(me))
        {
            w.DurationHolders.Add(me);
        }
        if (w.BestHits == -1 || hits < w.BestHits)
        {
            w.BestHits = hits;
            w.HitsHolders.Clear();
            w.HitsHolders.Add(me);
        }
        else if (hits == w.BestHits && !w.HitsHolders.Contains(me))
        {
            w.HitsHolders.Add(me);
        }
        SaveData.Save();
    }
    public static string PlayerName()
    {
        try
        {
            string n = Main.LocalPlayer?.name;
            return string.IsNullOrEmpty(n) ? "Player" : n;
        }
        catch
        {
            return "Player";
        }
    }

    public static long PlayTimeTicks()
    {
        try
        {
            if (Main.ActivePlayerFileData != null)
                return (long)(Main.ActivePlayerFileData.GetPlayTime().TotalSeconds * 60.0);
        }
        catch
        {
        }
        return -1;
    }

    // ---------------- 显示 ----------------

    /// <summary>把 tick 数写成 0h 0m 0s 0ms（简单）或 0:00:00.000（标准）。</summary>
    public static string FormatTicks(int ticks, bool simple)
    {
        if (ticks < 0) return "-";
        long ms = (long)ticks * 1000 / 60;
        long h = ms / 3600000; ms %= 3600000;
        long m = ms / 60000; ms %= 60000;
        long s = ms / 1000; ms %= 1000;
        if (simple)
        {
            if (h > 0) return string.Format("{0}h {1}m {2}s {3}ms", h, m, s, ms);
            if (m > 0) return string.Format("{0}m {1}s {2}ms", m, s, ms);
            return string.Format("{0}s {1}ms", s, ms);
        }
        return h > 0
            ? string.Format("{0}:{1:00}:{2:00}.{3:000}", h, m, s, ms)
            : string.Format("{0:00}:{1:00}.{2:000}", m, s, ms);
    }

    public static string FormatTicks(int ticks) => FormatTicks(ticks, BossChecklistMod.Config.TimeValueFormat == 1);

    public static string FormatPlayTime(long ticks) => ticks < 0 ? "-" : FormatTicks((int)Math.Min(int.MaxValue, ticks), true);

    public static string HitsToString(int hits) => hits < 0 ? "-" : (hits == 0 ? Localization.Get("Log.Records.NoHit") : Localization.Format("Log.Records.Hit", hits));
}

/// <summary>
/// 数据文件读写：&lt;模组目录&gt;\data\boss-checklist.txt
/// 每个世界一节，节里包含该世界的状态、世界纪录和每个玩家的个人纪录。
/// </summary>
internal static class SaveData
{
    private static readonly object Gate = new object();
    private static bool _dirty;

    private sealed class WorldSection
    {
        public string Name = "";
        public string Flags = "";
        public string Hidden = "";
        public string Marked = "";
        public readonly Dictionary<string, WorldBossRecord> WorldRecords = new Dictionary<string, WorldBossRecord>();
        public readonly Dictionary<string, Dictionary<string, BossRecord>> PlayerRecords = new Dictionary<string, Dictionary<string, BossRecord>>();
    }

    /// <summary>按角色名存的、和世界无关的数据（目前只有「拿到过的 Boss 掉落」）。</summary>
    private sealed class PlayerSection
    {
        public readonly HashSet<int> Collected = new HashSet<int>();
        public bool LogOpened;
    }

    private static readonly Dictionary<string, WorldSection> Worlds = new Dictionary<string, WorldSection>();
    private static readonly Dictionary<string, PlayerSection> Players = new Dictionary<string, PlayerSection>();
    private static bool _loaded;

    public static string FilePath
    {
        get
        {
            string dir = Path.Combine(BossChecklistMod.DataFolder, "data");
            return Path.Combine(dir, "boss-checklist.txt");
        }
    }

    public static void Save()
    {
        lock (Gate) _dirty = true;
        Flush();
    }

    /// <summary>立刻落盘（世界保存 / 退出 / 切世界时调用）。</summary>
    public static void Flush()
    {
        lock (Gate)
        {
            if (!_dirty) return;
            _dirty = false;
            try
            {
                string path = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                StringBuilder sb = new StringBuilder();
                sb.Append("V1\n");
                foreach (KeyValuePair<string, WorldSection> kv in Worlds)
                {
                    WorldSection w = kv.Value;
                    sb.Append("#world ").Append(Clean(kv.Key)).Append('|').Append(Clean(w.Name)).Append('\n');
                    sb.Append("downed=").Append(w.Flags).Append('\n');
                    sb.Append("hidden=").Append(w.Hidden).Append('\n');
                    sb.Append("marked=").Append(w.Marked).Append('\n');
                    foreach (WorldBossRecord r in w.WorldRecords.Values)
                    {
                        sb.Append("wr\t").Append(Clean(r.Key)).Append('\t').Append(r.Kills).Append('\t').Append(r.Deaths)
                          .Append('\t').Append(r.BestDuration).Append('\t').Append(r.BestHits)
                          .Append('\t').Append(string.Join("|", CleanList(r.DurationHolders)))
                          .Append('\t').Append(string.Join("|", CleanList(r.HitsHolders))).Append('\n');
                    }
                    foreach (KeyValuePair<string, Dictionary<string, BossRecord>> pkv in w.PlayerRecords)
                    {
                        foreach (BossRecord r in pkv.Value.Values)
                        {
                            sb.Append("pr\t").Append(Clean(pkv.Key)).Append('\t').Append(Clean(r.Key))
                              .Append('\t').Append(r.Kills).Append('\t').Append(r.Deaths).Append('\t').Append(r.Attempts)
                              .Append('\t').Append(r.FirstVictoryTicks).Append('\t').Append(r.BestDuration).Append('\t').Append(r.PreviousDuration)
                              .Append('\t').Append(r.FirstDuration).Append('\t').Append(r.BestHits).Append('\t').Append(r.PreviousHits)
                              .Append('\t').Append(r.FirstHits).Append('\n');
                        }
                    }
                }

                foreach (KeyValuePair<string, PlayerSection> pkv in Players)
                {
                    sb.Append("#player ").Append(Clean(pkv.Key)).Append('\n');
                    sb.Append("collected=").Append(ItemIdList(pkv.Value.Collected)).Append('\n');
                    sb.Append("logopened=").Append(pkv.Value.LogOpened ? "1" : "0").Append('\n');
                }

                string tmp = path + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception ex)
            {
                BossChecklistMod.Log?.Warn("保存 Boss Checklist 数据失败: " + ex.Message);
            }
        }
    }

    /// <summary>把磁盘上的数据读进内存。</summary>
    public static void LoadAll()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            string path = FilePath;
            if (!File.Exists(path)) return;
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            WorldSection current = null;
            string currentPlayer = null;

            foreach (string raw in lines)
            {
                try
                {
                    ParseLine(raw, ref current, ref currentPlayer);
                }
                catch
                {
                    // 单行格式不对就跳过这一行，不能让整份存档都读不进来
                }
            }
        }
        catch (Exception ex)
        {
            BossChecklistMod.Log?.Warn("读取 Boss Checklist 数据失败: " + ex.Message);
        }
    }

    /// <summary>解析存档文件里的一行。任何一行格式不对都只影响这一行，不会连累整份存档。</summary>
    private static void ParseLine(string raw, ref WorldSection current, ref string currentPlayer)
    {
        string line = (raw ?? "").TrimEnd('\r', '\n');
        if (line.Length == 0 || line == "V1") return;

        if (line.StartsWith("#player ") && line.Length > 8)
        {
            current = null;
            currentPlayer = line.Substring(8).Trim();
            if (!Players.TryGetValue(currentPlayer, out PlayerSection ps))
            {
                ps = new PlayerSection();
                Players[currentPlayer] = ps;
            }
            return;
        }
        if (currentPlayer != null && line.StartsWith("collected="))
        {
            PlayerSection ps = Players[currentPlayer];
            ps.Collected.Clear();
            foreach (string s in line.Substring(10).Split(','))
            {
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) && v > 0)
                    ps.Collected.Add(v);
            }
            return;
        }
        if (currentPlayer != null && line.StartsWith("logopened="))
        {
            Players[currentPlayer].LogOpened = line.Substring(10).Trim() == "1";
            return;
        }
        if (line.StartsWith("#world ") && line.Length > 7)
        {
            string[] parts = line.Substring(7).Split('|');
            string key = parts.Length > 0 ? parts[0] : "";
            current = new WorldSection { Name = parts.Length > 1 ? parts[1] : "" };
            Worlds[key] = current;
            return;
        }
        if (current == null) return;

        if (line.StartsWith("downed=")) { current.Flags = line.Substring(7); return; }
        if (line.StartsWith("hidden=")) { current.Hidden = line.Substring(7); return; }
        if (line.StartsWith("marked=")) { current.Marked = line.Substring(7); return; }

        if (line.StartsWith("wr\t"))
        {
            string[] p = line.Split('\t');
            if (p.Length >= 7)
            {
                WorldBossRecord w = new WorldBossRecord
                {
                    Key = p[1],
                    Kills = Int(p[2]),
                    Deaths = Int(p[3]),
                    BestDuration = Int(p[4]),
                    BestHits = Int(p[5])
                };
                foreach (string h in p[6].Split('|')) if (h.Length > 0) w.DurationHolders.Add(h);
                if (p.Length >= 8) foreach (string h in p[7].Split('|')) if (h.Length > 0) w.HitsHolders.Add(h);
                current.WorldRecords[w.Key] = w;
            }
            return;
        }
        if (line.StartsWith("pr\t"))
        {
            string[] p = line.Split('\t');
            if (p.Length >= 13)
            {
                string owner = p[1];
                if (!current.PlayerRecords.TryGetValue(owner, out Dictionary<string, BossRecord> list))
                {
                    list = new Dictionary<string, BossRecord>();
                    current.PlayerRecords[owner] = list;
                }
                BossRecord r = new BossRecord
                {
                    Key = p[2],
                    Kills = Int(p[3]),
                    Deaths = Int(p[4]),
                    Attempts = Int(p[5]),
                    FirstVictoryTicks = Long(p[6]),
                    BestDuration = Int(p[7]),
                    PreviousDuration = Int(p[8]),
                    FirstDuration = Int(p[9]),
                    BestHits = Int(p[10]),
                    PreviousHits = Int(p[11]),
                    FirstHits = Int(p[12])
                };
                list[r.Key] = r;
            }
            return;
        }
    }

    /// <summary>把某个世界的状态读进 WorldState。</summary>
    public static void LoadWorldInto(string worldKey)
    {
        LoadAll();
        if (!Worlds.TryGetValue(worldKey, out WorldSection w)) return;
        WorldState.WorldKey = worldKey;
        WorldState.WorldName = w.Name;
        foreach (string f in w.Flags.Split('|')) if (f.Length > 0) WorldState.SetFlag(f);
        foreach (string h in w.Hidden.Split('|')) if (h.Length > 0) WorldState.Hidden.Add(h);
        foreach (string m in w.Marked.Split('|')) if (m.Length > 0) WorldState.Marked.Add(m);
    }

    public static void LoadRecordsInto(string worldKey, string playerName, Dictionary<string, BossRecord> player, Dictionary<string, WorldBossRecord> world)
    {
        LoadAll();
        if (!Worlds.TryGetValue(worldKey, out WorldSection section)) return;
        foreach (KeyValuePair<string, WorldBossRecord> kv in section.WorldRecords) world[kv.Key] = kv.Value;
        if (playerName != null && section.PlayerRecords.TryGetValue(playerName, out Dictionary<string, BossRecord> list))
        {
            foreach (KeyValuePair<string, BossRecord> kv in list)
            {
                BossRecord r = kv.Value;
                r.Tracking = false;
                r.TrackerTicks = r.TrackerHits = r.TrackerDeaths = 0;
                player[kv.Key] = r;
            }
        }
    }

    /// <summary>把某个角色「拿到过的 Boss 掉落」读出来。</summary>
    public static void LoadCollectedInto(string playerName, HashSet<int> into)
    {
        LoadAll();
        if (playerName == null) return;
        if (!Players.TryGetValue(playerName, out PlayerSection p)) return;
        into.Clear();
        foreach (int item in p.Collected) into.Add(item);
    }

    /// <summary>把某个角色「拿到过的 Boss 掉落」写回内存表（真正落盘由 Flush 完成）。</summary>
    public static void StoreCollected(string playerName, HashSet<int> items)
    {
        LoadAll();
        if (playerName == null) return;
        if (!Players.TryGetValue(playerName, out PlayerSection p))
        {
            p = new PlayerSection();
            Players[playerName] = p;
        }
        p.Collected.Clear();
        foreach (int item in items) if (item > 0) p.Collected.Add(item);
        Save();
    }

    /// <summary>这个角色有没有开过一次 Boss 日志本。</summary>
    public static bool LoadLogOpened(string playerName)
    {
        LoadAll();
        if (playerName == null) return false;
        return Players.TryGetValue(playerName, out PlayerSection p) && p.LogOpened;
    }

    /// <summary>记下「已经开过日志本」并落盘。</summary>
    public static void StoreLogOpened(string playerName, bool opened)
    {
        LoadAll();
        if (playerName == null) return;
        if (!Players.TryGetValue(playerName, out PlayerSection p))
        {
            p = new PlayerSection();
            Players[playerName] = p;
        }
        if (p.LogOpened == opened) return;
        p.LogOpened = opened;
        Save();
    }

    private static string ItemIdList(HashSet<int> items)
    {
        List<int> ids = new List<int>(items);
        ids.Sort();
        string[] parts = new string[ids.Count];
        for (int i = 0; i < ids.Count; i++) parts[i] = ids[i].ToString(CultureInfo.InvariantCulture);
        return string.Join(",", parts);
    }

    /// <summary>把当前世界的状态与纪录写回内存表（真正落盘由 Flush 完成）。</summary>
    public static void StoreCurrentWorld(string worldKey, string worldName)
    {
        LoadAll();
        if (!Worlds.TryGetValue(worldKey, out WorldSection w))
        {
            w = new WorldSection();
            Worlds[worldKey] = w;
        }
        w.Name = worldName;
        w.Flags = string.Join("|", WorldState.DownedFlagList());
        w.Hidden = string.Join("|", CleanList(WorldState.Hidden));
        w.Marked = string.Join("|", CleanList(WorldState.Marked));
        w.WorldRecords.Clear();
        foreach (KeyValuePair<string, WorldBossRecord> kv in Records.World) w.WorldRecords[kv.Key] = kv.Value;
        w.PlayerRecords[Records.PlayerName()] = new Dictionary<string, BossRecord>(Records.Player);
        Flush();
    }

    private static int Int(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

    private static long Long(string s) => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : -1;

    private static string Clean(string s) => (s ?? "").Replace("\t", " ").Replace("|", "/").Replace("\n", " ").Replace("\r", " ");

    private static List<string> CleanList(IEnumerable<string> items)
    {
        List<string> list = new List<string>();
        foreach (string s in items) list.Add(Clean(s));
        return list;
    }
}
