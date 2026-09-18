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
            "开关Boss清单",
            "打开或关闭右侧的Boss清单面板。",
            "p",
            OnToggleChecklist);

        context.RegisterKeybind(
            "toggle-log",
            "开关Boss日志",
            "打开或关闭Boss日志本。",
            "l",
            OnToggleLog);

        context.RegisterKeybind(
            "debug-prompt",
            "调试：渐进式询问页",
            "直接打开Boss日志的渐进式询问页（不管是不是第一次打开），用来检查这个页面的版面。",
            "o",
            OnDebugPrompt);

        BossTracker.Initialize();
        BossTracking.Init();
        BossLogUI.Init();
        BossChecklistUI.Init();
        BossRadarUI.Init();
        BossLogButton.Init();
        Log.Info("Boss Checklist 已初始化。默认按键：P = 清单，L = 日志本。");
    }

    public void OnContentReady(ModContext context)
    {
        BossTracker.InvalidateLoot();
    }

    public void OnConfigChanged()
    {
        Config = Config ?? new BossLogConfig();
        BossLogUI.OnConfigChanged();
        Log.Info("Boss Checklist 配置已重载。");
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
            Log.Error("打开Boss清单失败: " + ex.Message);
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
            Log.Error("打开Boss日志失败: " + ex.Message);
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
            Log.Error("打开渐进式询问页失败: " + ex.Message);
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
    [Label("日志本位置X")]
    [Description("日志本相对屏幕中心的横向偏移。日志本打开时按住右键拖动即可移动。")]
    public int WindowX { get; set; } = -1;

    [Client]
    [Label("日志本位置Y")]
    [Description("日志本相对屏幕中心的纵向偏移。日志本打开时按住右键拖动即可移动。")]
    public int WindowY { get; set; } = -1;

    [Client]
    [Label("日志按钮位置X")]
    [Description("右下角日志按钮相对屏幕右边缘的横向偏移。在游戏中按住右键拖动按钮即可移动。")]
    public int ButtonX { get; set; } = -270;

    [Client]
    [Label("日志按钮位置Y")]
    [Description("右下角日志按钮相对屏幕下边缘的纵向偏移。在游戏中按住右键拖动按钮即可移动。")]
    public int ButtonY { get; set; } = -50;

    [Client]
    [Label("日志本颜色 红")]
    [Description("日志本封面的红色分量（0-255）。")]
    [Range(0, 255)]
    public int BookColorR { get; set; } = 87;

    [Client]
    [Label("日志本颜色 绿")]
    [Description("日志本封面的绿色分量（0-255）。")]
    [Range(0, 255)]
    public int BookColorG { get; set; } = 181;

    [Client]
    [Label("日志本颜色 蓝")]
    [Description("日志本封面的蓝色分量（0-255）。")]
    [Range(0, 255)]
    public int BookColorB { get; set; } = 92;

    [Client]
    [Label("启用交互栏")]
    [Description("启用后，把鼠标停在右上角的小栏上会显示当前页面的其他操作方式说明。")]
    public bool ShowInteractionTooltips { get; set; } = true;

    // ---------------- 清单 ----------------

    [Client]
    [Label("自动勾选清单")]
    [Description("击败后自动勾选条目。关闭后需要自己手动勾选；无论开关，右键点条目名都能手动勾选。")]
    public bool AutomaticChecklist { get; set; } = true;

    [Client]
    [Label("渐进式清单")]
    [Description("在遇到或击败条目之前隐藏其内容。")]
    public bool ProgressiveChecklist { get; set; } = false;

    [Client]
    [Label("关闭渐进式清单提示")]
    [Description("启用后，新建角色不再弹出渐进式清单的询问窗口。")]
    public bool PromptDisabled { get; set; } = false;

    [Client]
    [Label("隐藏未解锁的Boss")]
    [Description("启用后，当前无法挑战的Boss（例如非圣诞节的霜月军团）不会出现在目录里。")]
    public bool HideUnavailable { get; set; } = true;

    [Client]
    [Label("隐藏未支持的Boss")]
    [Description("启用后，未完整接入的Boss不会出现在目录里。")]
    public bool HideUnsupported { get; set; } = false;

    [Client]
    [Label("日志只显示Boss内容")]
    [Description("启用后，清单和翻页只会显示Boss条目，下面的筛选配置会被忽略。")]
    public bool OnlyShowBossContent { get; set; } = false;

    [Client]
    [Label("筛选Boss")]
    [Description("0 = 显示，1 = 完成后隐藏，2 = 隐藏。")]
    [Range(0, 2)]
    public int FilterBosses { get; set; } = 0;

    [Client]
    [Label("筛选小Boss")]
    [Description("0 = 显示，1 = 完成后隐藏，2 = 隐藏。")]
    [Range(0, 2)]
    public int FilterMiniBosses { get; set; } = 0;

    [Client]
    [Label("筛选事件")]
    [Description("0 = 显示，1 = 完成后隐藏，2 = 隐藏。")]
    [Range(0, 2)]
    public int FilterEvents { get; set; } = 0;

    [Client]
    [Label("彩色Boss名")]
    [Description("目录里的Boss名已击败为绿色、未击败为红色；开启下一条标记时下一条为黄色。")]
    public bool ColoredBossText { get; set; } = true;

    [Client]
    [Label("清单标记样式")]
    [Description("0 = 对勾和空框，1 = 对勾和叉，2 = 叉和空框，3 = 删除线。")]
    [Range(0, 3)]
    public int SelectedCheckmarkType { get; set; } = 0;

    [Client]
    [Label("标记下一条Boss")]
    [Description("在勾选框里画一个圈，表示这是下一条还没击败的Boss。")]
    public bool DrawNextMark { get; set; } = true;

    [Client]
    [Label("显示进度条")]
    [Description("关闭后，目录底部两条总进度条不再显示。")]
    public bool ShowProgressBars { get; set; } = true;

    [Client]
    [Label("启用召唤物合成清单")]
    [Description("启用后，召唤信息里的合成配方会当成清单使用（材料够就勾上）。")]
    public bool SpawnItemCraftingChecklist { get; set; } = false;

    [Client]
    [Label("启用战利品/收藏品清单")]
    [Description("目录还会检查你是否获得了Boss的所有战利品和收藏品。")]
    public bool LootCheckVisibility { get; set; } = false;

    [Client]
    [Label("只检查可掉落的战利品")]
    [Description("如音乐盒这种不会由条目掉落的物品将不会计入战利品检查。")]
    public bool OnlyCheckDroppedLoot { get; set; } = false;

    // ---------------- 记录 / 调试 ----------------

    [Client]
    [Label("启用Boss记录追踪")]
    [Description("启用后，会追踪并生成战斗记录状态。")]
    public bool RecordTrackingEnabled { get; set; } = true;

    [Client]
    [Label("允许新记录")]
    [Description("禁用后，只会有你以前的尝试会被记录；首次胜利和个人的最佳记录不会被覆写。")]
    public bool AllowNewRecords { get; set; } = true;

    [Client]
    [Label("启用新记录日志发光")]
    [Description("启用后，创造新纪录时Boss日志按钮和目录里的条目名会发出彩虹光。")]
    public bool NewRecordLogGlow { get; set; } = true;

    [Client]
    [Label("时间值格式")]
    [Description("0 = 标准 [0:00:00.000]，1 = 简化 [0h 0m 0s 0ms]。")]
    [Range(0, 1)]
    public int TimeValueFormat { get; set; } = 0;

    [Client]
    [Label("启用重置数据选项")]
    [Description("启用后，你可以清除或重置Boss日志中的某些数据。")]
    public bool EnableResetOptions { get; set; } = false;

    [Client]
    [Label("访问条目键")]
    [Description("在每个条目页面上添加一个按钮，可以显示并把条目键复制到剪贴板，方便做跨Mod内容的开发者。")]
    public bool AccessInternalNames { get; set; } = false;

    [Client]
    [Label("使用进度值")]
    [Description("查看条目时显示其进度值，进度值决定了条目在清单中的排序。")]
    public bool ShowProgressionValue { get; set; } = false;

    [Client]
    [Label("显示自动检测的集合类型")]
    [Description("Boss的收藏品物品栏会被标记为指定的集合类型。")]
    public bool ShowCollectionType { get; set; } = false;

    // ---------------- 聊天栏提示 ----------------

    [Client]
    [Label("Boss消失通告")]
    [Description("0 = 关闭，1 = 常规消息，2 = 唯一消息。")]
    [Range(0, 2)]
    public int DespawnMessageType { get; set; } = 1;

    [Client]
    [Label("非Boss的被击败通告")]
    [Description("0 = 关闭，1 = 常规消息，2 = 唯一消息。")]
    [Range(0, 2)]
    public int LimbMessages { get; set; } = 1;

    [Client]
    [Label("月亮事件结束通告")]
    [Description("0 = 关闭，1 = 常规消息，2 = 唯一消息。")]
    [Range(0, 2)]
    public int MoonMessages { get; set; } = 1;

    [Client]
    [Label("重生倒计时声音")]
    [Description("启用后，重生时间的最后3秒钟将有声音提示。")]
    public bool TimerSounds { get; set; } = true;

    // ---------------- 地图检测 ----------------

    [Client]
    [Label("宝藏袋在小地图上显示")]
    [Description("宝藏袋会在地图上高亮显示。")]
    public bool TreasureBagsOnMap { get; set; } = true;

    [Client]
    [Label("四柱碎片在小地图上显示")]
    [Description("天界柱碎片会在地图上高亮显示。")]
    public bool FragmentsOnMap { get; set; } = true;

    [Client]
    [Label("暗影鳞片和血腥样本在小地图上显示")]
    [Description("暗影鳞片和血腥样本会在地图上高亮显示。")]
    public bool ScalesOnMap { get; set; } = false;

    // ---------------- Boss 雷达 ----------------

    [Client]
    [Label("启用Boss雷达")]
    [Description("在屏幕边缘显示指向屏幕外Boss的箭头。")]
    public bool EnableBossRadar { get; set; } = true;

    [Client]
    [Label("定位小Boss")]
    [Description("注意：此选项仅用于有小地图图像的小Boss。")]
    public bool RadarMiniBosses { get; set; } = false;

    [Client]
    [Label("Boss雷达不透明度")]
    [Description("调整雷达图标的透明度（35-85）。")]
    [Range(35, 85)]
    public int RadarOpacityPercent { get; set; } = 75;
}