using System;
using Terraria;
using TerrariaModder.Core;
using TerrariaModder.Core.Config;
using TerrariaModder.Core.Logging;
using TerrariaModder.Core.Reflection;

namespace BossChecklist;

/// <summary>
/// Boss Checklist（TerrariaModder 版）。默认按键：P 开清单，L 开日志本。
/// </summary>
public class BossChecklistMod : IMod, IModLifecycle
{
    public string Id => "boss-checklist";

    public string Name => "Boss Checklist";

    public string Version => "1.0.0";

    internal static BossLogConfig Config { get; private set; }

    internal static ILogger Log { get; private set; }

    /// <summary>本模组自己的文件夹，存档写在里面的 data 子目录。</summary>
    internal static string DataFolder { get; private set; }

    public void Initialize(ModContext context)
    {
        Log = context.Logger;
        DataFolder = context.ModFolder;
        Config = context.GetConfig<BossLogConfig>() ?? new BossLogConfig();

        context.RegisterKeybind(
            "toggle-checklist",
            "Toggle Boss Checklist",
            "Open or close the Boss Checklist sidebar.",
            "p",
            OnToggleChecklist);

        context.RegisterKeybind(
            "toggle-log",
            "Toggle Boss Log",
            "Open or close the Boss Log book.",
            "l",
            OnToggleLog);

        context.RegisterKeybind(
            "debug-prompt",
            "Debug: Progression Prompt",
            "Opens the Boss Log directly on the progression mode prompt page (even if it was already answered), for checking that page's layout.",
            "o",
            OnDebugPrompt);

        BossTracker.Initialize();
        BossTracking.Init();
        BossLogUI.Init();
        BossChecklistUI.Init();
        BossRadarUI.Init();
        BossLogButton.Init();
        Log.Info("Boss Checklist initialized. Default keys: P = checklist, L = log book.");
    }

    public void OnContentReady(ModContext context)
    {
        BossTracker.InvalidateLoot();
    }

    public void OnConfigChanged()
    {
        Config = Config ?? new BossLogConfig();
        BossLogUI.OnConfigChanged();
        Log.Info("Boss Checklist config reloaded.");
    }

    public void OnWorldLoad()
    {
        BossLogUI.OnWorldLoad();
    }

    public void OnWorldUnload()
    {
        BossLogUI.Close();
    }

    public void Unload()
    {
        BossChecklistUI.Unload();
        BossRadarUI.Unload();
        BossLogButton.Unload();
        BossLogUI.Unload();
        BossTracking.Unload();
    }

    internal static void SaveConfig()
    {
        try { Config?.Save(); } catch { }
    }

    private static void OnToggleChecklist()
    {
        try
        {
            if (!Game.InWorld) return;
            BossChecklistUI.Toggle();
        }
        catch (Exception ex)
        {
            Log.Error("Boss Checklist toggle failed: " + ex.Message);
        }
    }

    private static void OnToggleLog()
    {
        try
        {
            if (!Game.InWorld) return;
            BossLogUI.Toggle();
        }
        catch (Exception ex)
        {
            Log.Error("Boss Log toggle failed: " + ex.Message);
        }
    }

    private static void OnDebugPrompt()
    {
        try
        {
            if (!Game.InWorld) return;
            BossLogUI.OpenPrompt();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to open the progression prompt: " + ex.Message);
        }
    }
}

/// <summary>
/// 配置。界面上的下拉项在原模组里是枚举，TerrariaModder 的配置系统只认
/// int/bool/float/string，所以这里一律存 int，用的时候再转成枚举。
/// </summary>
public class BossLogConfig : ModConfig
{
    public override int Version => 1;

    // ---------------- 日志本外观 ----------------

    [Client]
    [Label("Open Log Button Position X")]
    [Description("X offset of the log book from the center of the screen. Hold right-click on the book while it is open to drag it around.")]
    public int WindowX { get; set; } = -1;

    [Client]
    [Label("Open Log Button Position Y")]
    [Description("Y offset of the log book from the center of the screen. Hold right-click on the book while it is open to drag it around.")]
    public int WindowY { get; set; } = -1;

    [Client]
    [Label("Boss Log Button Position X")]
    [Description("Horizontal offset of the Boss Log button from the right edge of the screen. Hold right-click on the button in-game to move it.")]
    public int ButtonX { get; set; } = -270;

    [Client]
    [Label("Boss Log Button Position Y")]
    [Description("Vertical offset of the Boss Log button from the bottom edge of the screen. Hold right-click on the button in-game to move it.")]
    public int ButtonY { get; set; } = -50;

    [Client]
    [Label("Boss Log Color R")]
    [Description("Red component of the log book cover color (0-255).")]
    [Range(0, 255)]
    public int BookColorR { get; set; } = 87;

    [Client]
    [Label("Boss Log Color G")]
    [Description("Green component of the log book cover color (0-255).")]
    [Range(0, 255)]
    public int BookColorG { get; set; } = 181;

    [Client]
    [Label("Boss Log Color B")]
    [Description("Blue component of the log book cover color (0-255).")]
    [Range(0, 255)]
    public int BookColorB { get; set; } = 92;

    [Client]
    [Label("Enable Interactions Tab")]
    [Description("When enabled, a small tab can be hovered over for an explanation on how to use the alternative interactions of the current page.")]
    public bool ShowInteractionTooltips { get; set; } = true;

    // ---------------- 清单 ----------------

    [Client]
    [Label("Automatic Checklist")]
    [Description("Entries will be marked automatically when defeated. Disabling requires the user to manually check off entries. Either way you can right-click a name to mark it.")]
    public bool AutomaticChecklist { get; set; } = true;

    [Client]
    [Label("Progressive Checklist")]
    [Description("Hides content until the entry is encountered or beaten.")]
    public bool ProgressiveChecklist { get; set; } = false;

    [Client]
    [Label("Disable Progressive Checklist prompt")]
    [Description("While enabled, this will prevent the prompt from appearing for newly-created characters.")]
    public bool PromptDisabled { get; set; } = false;

    [Client]
    [Label("Hide unavailable bosses")]
    [Description("If enabled, unavailable bosses will be removed from the Boss Log's table of contents.")]
    public bool HideUnavailable { get; set; } = true;

    [Client]
    [Label("Hide unsupported bosses")]
    [Description("If enabled, bosses that have not fully integrated will be removed from the Boss Log's table of contents.")]
    public bool HideUnsupported { get; set; } = false;

    [Client]
    [Label("Boss Log only shows boss content")]
    [Description("If enabled, the checklist and page navigation will only give access to boss entries. Note: the filter configs below will be ignored when enabled.")]
    public bool OnlyShowBossContent { get; set; } = false;

    [Client]
    [Label("Filter bosses in list")]
    [Description("0 = Show, 1 = Hide When Completed, 2 = Hide.")]
    [Range(0, 2)]
    public int FilterBosses { get; set; } = 0;

    [Client]
    [Label("Filter mini bosses in list")]
    [Description("0 = Show, 1 = Hide When Completed, 2 = Hide.")]
    [Range(0, 2)]
    public int FilterMiniBosses { get; set; } = 0;

    [Client]
    [Label("Filter events in list")]
    [Description("0 = Show, 1 = Hide When Completed, 2 = Hide.")]
    [Range(0, 2)]
    public int FilterEvents { get; set; } = 0;

    [Client]
    [Label("Colored Boss Text")]
    [Description("The boss text in the table of contents will be green when defeated and red when not. If next check is enabled, the next boss will be yellow.")]
    public bool ColoredBossText { get; set; } = true;

    [Client]
    [Label("Checklist Marking Type")]
    [Description("0 = check and empty box, 1 = check and X, 2 = X and empty box, 3 = strike-through.")]
    [Range(0, 3)]
    public int SelectedCheckmarkType { get; set; } = 0;

    [Client]
    [Label("Next Boss Check")]
    [Description("Puts a circle in the checkbox to indicate it is the next undefeated boss to fight.")]
    public bool DrawNextMark { get; set; } = true;

    [Client]
    [Label("Show Progress Bars")]
    [Description("When disabled, the total progress bars at the bottom of the table of contents will not show.")]
    public bool ShowProgressBars { get; set; } = true;

    [Client]
    [Label("Enable spawn item crafting checklist")]
    [Description("When enabled, crafting recipes for spawn info items will function as a checklist.")]
    public bool SpawnItemCraftingChecklist { get; set; } = false;

    [Client]
    [Label("Enable loot/collectible checklist")]
    [Description("The table of contents will also check if you have obtained all of a boss's loot and collectible items.")]
    public bool LootCheckVisibility { get; set; } = false;

    [Client]
    [Label("Only check droppable loot")]
    [Description("Items that are not dropped by the entry, such as music boxes, will not be counted towards the loot check.")]
    public bool OnlyCheckDroppedLoot { get; set; } = false;

    // ---------------- 记录 / 调试 ----------------

    [Client]
    [Label("Enable Boss Record Tracking")]
    [Description("When enabled, fight stats will be tracked for records.")]
    public bool RecordTrackingEnabled { get; set; } = true;

    [Client]
    [Label("Allow New Records")]
    [Description("When disabled, only your previous attempt is recorded. First victory and personal best records will not be overwritten.")]
    public bool AllowNewRecords { get; set; } = true;

    [Client]
    [Label("Enable New Record Log Glow")]
    [Description("When enabled, setting a new record will put a rainbow glow on the Boss Log button and the table of contents entry name.")]
    public bool NewRecordLogGlow { get; set; } = true;

    [Client]
    [Label("Time Value Format")]
    [Description("0 = standard [0:00:00.000], 1 = simple [0h 0m 0s 0ms].")]
    [Range(0, 1)]
    public int TimeValueFormat { get; set; } = 0;

    [Client]
    [Label("Enable Reset Data Options")]
    [Description("When enabled, you are able to clear or reset certain data within the Boss Log.")]
    public bool EnableResetOptions { get; set; } = false;

    [Client]
    [Label("Access Entry Keys")]
    [Description("Adds a button on each entry page that displays and copies the Entry Key to the in-game clipboard.")]
    public bool AccessInternalNames { get; set; } = false;

    [Client]
    [Label("Access Progression Values")]
    [Description("Shows the progression value when hovering over an entry within the table of contents and when viewing an entry's page.")]
    public bool ShowProgressionValue { get; set; } = false;

    [Client]
    [Label("Show Auto-detected Collectible Type")]
    [Description("Boss collectible item slots will be labeled with the assigned collectible type.")]
    public bool ShowCollectionType { get; set; } = false;

    // ---------------- 聊天栏提示 ----------------

    [Client]
    [Label("Boss Despawn Annoucements")]
    [Description("0 = disabled, 1 = generic messages, 2 = unique messages.")]
    [Range(0, 2)]
    public int DespawnMessageType { get; set; } = 1;

    [Client]
    [Label("Non-Boss Defeated Annoucements")]
    [Description("0 = disabled, 1 = generic messages, 2 = unique messages.")]
    [Range(0, 2)]
    public int LimbMessages { get; set; } = 1;

    [Client]
    [Label("Moon Event Ending Annoucements")]
    [Description("0 = disabled, 1 = generic messages, 2 = unique messages.")]
    [Range(0, 2)]
    public int MoonMessages { get; set; } = 1;

    [Client]
    [Label("Respawn Timer Sounds")]
    [Description("If enabled, the last 3 seconds of your respawn time will have sound indicators.")]
    public bool TimerSounds { get; set; } = true;

    // ---------------- 地图检测 ----------------

    [Client]
    [Label("Treasure bags display on the map")]
    [Description("Treasure bags will be highlighted on the map.")]
    public bool TreasureBagsOnMap { get; set; } = true;

    [Client]
    [Label("Fragments display on the map")]
    [Description("Celestial pillar fragments will be highlighted on the map.")]
    public bool FragmentsOnMap { get; set; } = true;

    [Client]
    [Label("Shadow Scales and Tissue Samples display on the map")]
    [Description("Shadow Scales and Tissue Samples will be highlighted on the map.")]
    public bool ScalesOnMap { get; set; } = false;

    // ---------------- Boss 雷达 ----------------

    [Client]
    [Label("Enable Boss Radar")]
    [Description("Shows an arrow pointing to bosses that are off screen.")]
    public bool EnableBossRadar { get; set; } = true;

    [Client]
    [Label("Whitelist mini bosses")]
    [Description("Note: this only works properly with mini bosses that have head icons.")]
    public bool RadarMiniBosses { get; set; } = false;

    [Client]
    [Label("Radar Opacity")]
    [Description("Adjust how transparent the radar's icon will be (35-85).")]
    [Range(35, 85)]
    public int RadarOpacityPercent { get; set; } = 75;
}
