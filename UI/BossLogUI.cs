using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.GameContent;
using TerrariaModder.Core.Events;
using TerrariaModder.Core.Reflection;
using TerrariaModder.Core.UI;
using Game = TerrariaModder.Core.Reflection.Game;

namespace BossChecklist;

/// <summary>
/// Boss 日志本。原模组是 tModLoader 的 UIElements 树，这里改成即时模式：
/// 每帧在 FrameEvents.OnPreDraw 里清一次标记，再在面板绘制回调里一次性算输入 + 画。
/// 所有尺寸都用「书内坐标」，最后乘 S（= 界面缩放 × 0.75）再平移到屏幕位置。
/// </summary>
internal static class BossLogUI
{
    public const string PanelId = "boss-checklist.log";
    public const int Page_TableOfContents = -1;
    public const int Page_Credits = -2;
    public const int Page_Prompt = -3;

    public enum SubPage
    {
        SpawnInfo = 0,
        LootAndCollectibles = 1,
        Records = 2
    }

    private const int BookW = 800;
    private const int BookH = 500;
    private const int PageW = 375;
    private const int PageH = 480;
    private const int TabW = 32;
    private const int TabH = 76;
    private const int RowH = 26;

    // ---------------- 状态 ----------------

    public static bool IsOpen { get; private set; }
    public static bool MouseOverPanel { get; private set; }
    public static int PageNum { get; private set; } = Page_TableOfContents;
    public static SubPage SelectedSubPage = SubPage.SpawnInfo;
    public static bool FilterPanelIsOpen;
    public static bool HiddenEntriesMode;
    public static int HoveredEntry = -1;

    private static bool _promptDisabledThisSession;
    private static int _spawnItemSelected;
    private static int _recipeSelected;
    private static int _recordCategory = 2;      // 2 = PreviousAttempt
    private static int _recordComparison = -1;
    private static int _scrollLeft, _scrollRight, _scrollLoot, _scrollRecords;
    private static int _hoverItemType;
    private static string _hoverTitle;
    private static string _hoverDesc;

    // 交互：同一帧只认一次点击
    private static bool _drewThisFrame;
    private static bool _leftDown, _rightDown, _clickEdge, _rclickEdge;
    private static bool _dragging;
    private static int _dragDX, _dragDY;

    // ---------------- 版面 ----------------

    private static float S = 1f;
    private static int BX, BY;
    private static Rectangle BookRect;
    private static Rectangle LeftPage;
    private static Rectangle RightPage;

    private static int Sc(int v) => (int)Math.Round(v * S);
    private static int X(int x) => BX + Sc(x);
    private static int Y(int y) => BY + Sc(y);
    private static Rectangle R(int x, int y, int w, int h) => new Rectangle(X(x), Y(y), Sc(w), Sc(h));
    private static Rectangle PageR(Rectangle page, int x, int y, int w, int h)
        => new Rectangle(page.X + Sc(x), page.Y + Sc(y), Sc(w), Sc(h));

    private static string L(string key) => Localization.Get("Log." + key);
    private static string LF(string key, params object[] args) => Localization.Format("Log." + key, args);

    public static bool ProgressiveMode => BossChecklistMod.Config.ProgressiveChecklist;

    // ---------------- 生命周期 ----------------

    public static void Init()
    {
        FrameEvents.OnPreDraw += OnFrameStart;
        UIRenderer.RegisterPanelDraw(PanelId, Draw);
    }

    public static void Unload()
    {
        Close();
        FrameEvents.OnPreDraw -= OnFrameStart;
        UIRenderer.UnregisterPanelDraw(PanelId);
    }

    private static void OnFrameStart()
    {
        _drewThisFrame = false;
        UIU.CheckFrameStart();
    }

    public static void OnConfigChanged()
    {
        _hoverItemType = 0;
        _hoverTitle = null;
        _hoverDesc = null;
    }

    public static void OnWorldLoad()
    {
        PageNum = Page_TableOfContents;
        _promptDisabledThisSession = false;
        _scrollLeft = _scrollRight = _scrollLoot = _scrollRecords = 0;
        try { HasOpenedLog = SaveData.LoadLogOpened(Records.PlayerName()); } catch { HasOpenedLog = false; }
    }

    /// <summary>这个角色有没有开过一次日志本（决定按钮要不要画彩虹描边）。</summary>
    public static bool HasOpenedLog { get; private set; }

    /// <summary>第一次打开时记一笔，之后就正常了。</summary>
    private static void MarkLogOpened()
    {
        if (HasOpenedLog) return;
        HasOpenedLog = true;
        try { SaveData.StoreLogOpened(Records.PlayerName(), true); } catch { }
    }

    public static void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    public static void Open()
    {
        _leftDown = true;
        _rightDown = true;
        _clickEdge = false;
        _rclickEdge = false;
        _drewThisFrame = false;
        IsOpen = true;
        Collected.ScanNow(); // 打开日志本时扫一遍背包 / 随身存储 / 装备与饰品
        bool firstOpen = !HasOpenedLog; // 原模组只在「这个角色第一次打开日志本」时问一次
        MarkLogOpened();
        try { BossChecklistUI.Close(); } catch { } // 日志本一开，右侧清单就收起来（原模组也是这个行为）
        if (!_promptDisabledThisSession && firstOpen && !BossChecklistMod.Config.PromptDisabled && PageNum == Page_TableOfContents)
            PageNum = Page_Prompt;
        try { Main.playerInventory = false; } catch { }
    }

    /// <summary>
    /// 调试用：不管是不是第一次打开，直接停在渐进式询问页，方便反复调版面。
    /// </summary>
    public static void OpenPrompt()
    {
        if (!IsOpen)
        {
            PageNum = Page_Prompt;
            Open();
        }
        FilterPanelIsOpen = false;
        PageNum = Page_Prompt;
    }

    public static void Close()
    {
        IsOpen = false;
        MouseOverPanel = false;
        _dragging = false;
        _leftDown = true;
        _rightDown = true;
        try { UIRenderer.UnregisterPanelBounds(PanelId); } catch { }
    }

    public static void ShowEntry(int index)
    {
        if (index < 0 || index >= BossTracker.Entries.Count) return;
        PageNum = index;
        SelectedSubPage = SubPage.SpawnInfo;
        _scrollRight = 0;
        _spawnItemSelected = 0;
        _recipeSelected = 0;
    }

    // ---------------- 输入 ----------------

    private static bool PressedLeft => _clickEdge || UIRenderer.MouseLeftClick;
    private static bool PressedRight => _rclickEdge || UIRenderer.MouseRightClick;

    private static void ConsumeLeft()
    {
        _clickEdge = false;
        try { UIRenderer.ConsumeClick(); } catch { }
    }

    private static void ConsumeRight()
    {
        _rclickEdge = false;
        try { UIRenderer.ConsumeRightClick(); } catch { }
    }

    private static bool Over(Rectangle r) => r.Width > 0 && r.Height > 0 && r.Contains(UIRenderer.MouseX, UIRenderer.MouseY);

    private static bool AltDown()
    {
        try
        {
            return Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.LeftAlt)
                || Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.RightAlt);
        }
        catch
        {
            return false;
        }
    }

    // ---------------- 绘制入口 ----------------

    private static void Draw()
    {
        if (!IsOpen || _drewThisFrame) return;
        _drewThisFrame = true;
        try
        {


            // 和原模组一样：按背包键（或死掉）就自动合上日志本，不用去够快捷键
            try
            {
                if (Main.playerInventory || Main.LocalPlayer.dead) { Close(); return; }
            }
            catch
            {
            }

            bool left = false, right = false;
            try { left = Main.mouseLeft; right = Main.mouseRight; } catch { }
            _clickEdge = left && !_leftDown;
            _rclickEdge = right && !_rightDown;
            _leftDown = left;
            _rightDown = right;

            _hoverItemType = 0;
            _hoverTitle = null;
            _hoverDesc = null;
            HoveredEntry = -1;

            float ui = 1f;
            try { ui = Game.UIScale; } catch { }
            if (ui <= 0f) ui = 1f;
            S = ui * 0.75f;

            int sw = UIRenderer.ScreenWidth > 0 ? UIRenderer.ScreenWidth : 1920;
            int sh = UIRenderer.ScreenHeight > 0 ? UIRenderer.ScreenHeight : 1080;
            BX = (sw - Sc(BookW)) / 2;
            BY = (sh - Sc(BookH)) / 2 - Sc(6);
            BossLogConfig cfg = BossChecklistMod.Config;
            if (cfg.WindowX != 0 || cfg.WindowY != 0)
            {
                if (cfg.WindowX != -1) BX = (sw - Sc(BookW)) / 2 + cfg.WindowX;
                if (cfg.WindowY != -1) BY = (sh - Sc(BookH)) / 2 - Sc(6) + cfg.WindowY;
            }

            BookRect = new Rectangle(BX, BY, Sc(BookW), Sc(BookH));
            LeftPage = new Rectangle(X(20), Y(12), Sc(PageW), Sc(PageH));
            RightPage = new Rectangle(X(BookW - 15 - PageW), Y(12), Sc(PageW), Sc(PageH));

            MouseOverPanel = BookRect.Contains(UIRenderer.MouseX, UIRenderer.MouseY);
            try { UIRenderer.RegisterPanelBounds(PanelId, BookRect.X, BookRect.Y, BookRect.Width, BookRect.Height); } catch { }

            DrawBookShell();

            if (PageNum != Page_TableOfContents) FilterPanelIsOpen = false; // 离开目录页（含询问页）就收起筛选面板
            if (PageNum == Page_Prompt)
            {
                DrawPrompt();
            }
            else
            {
                DrawTabs();
                if (FilterPanelIsOpen) DrawFilterPanel();
                DrawIndicatorPanel();
            }

            DrawPageContent();
            DrawNavButtons();
            HandleDrag();

            if (_hoverItemType > 0) UIU.ItemTip(_hoverItemType);
            else if (!string.IsNullOrEmpty(_hoverTitle)) UIU.TextTip(_hoverTitle, _hoverDesc);

            foreach (EntryInfo e in BossTracker.Entries) e.Hidden = WorldState.Hidden.Contains(e.Key);

        }
        catch (Exception ex)
        {
            BossChecklistMod.Log?.Error("绘制 Boss 日志出错", ex);
        }
        finally
        {
            UIU.SafeEndClip();
        }
    }

    private static void DrawBookShell()
    {
        Texture2D back = UIU.Tex("LogUI_Back");
        Texture2D paper = UIU.Tex("LogUI_Paper");
        BossLogConfig cfg = BossChecklistMod.Config;
        Color cover = new Color(
            (byte)UIU.Clamp(cfg.BookColorR, 0, 255),
            (byte)UIU.Clamp(cfg.BookColorG, 0, 255),
            (byte)UIU.Clamp(cfg.BookColorB, 0, 255));
        if (back != null) UIU.Draw(back, BookRect, cover);
        else UIU.Fill(BookRect, UIU.Mul(cover, 0.75f));
        if (paper != null) UIU.Draw(paper, BookRect, Color.White);
    }

    /// <summary>日志本打开时，按住鼠标中键拖动可以挪位置（位置记在配置里）。</summary>
    private static void HandleDrag()
    {
        if (!Over(BookRect)) return;
        bool middle = false;
        try { middle = UIRenderer.MouseMiddle; } catch { }
        if (middle && !_dragging)
        {
            _dragging = true;
            _dragDX = UIRenderer.MouseX - BX;
            _dragDY = UIRenderer.MouseY - BY;
            return;
        }
        if (_dragging)
        {
            if (!middle)
            {
                _dragging = false;
                BossLogConfig cfg = BossChecklistMod.Config;
                cfg.WindowX = BX + _dragDX - (UIRenderer.ScreenWidth - Sc(BookW)) / 2;
                cfg.WindowY = BY + _dragDY - ((UIRenderer.ScreenHeight - Sc(BookH)) / 2 - Sc(6));
                BossChecklistMod.SaveConfig();
                return;
            }
            BX = UIRenderer.MouseX - _dragDX;
            BY = UIRenderer.MouseY - _dragDY;
            BookRect = new Rectangle(BX, BY, Sc(BookW), Sc(BookH));
            LeftPage = new Rectangle(X(20), Y(12), Sc(PageW), Sc(PageH));
            RightPage = new Rectangle(X(BookW - 15 - PageW), Y(12), Sc(PageW), Sc(PageH));
        }
    }

    // ---------------- 边缘标签页 ----------------

    private static Rectangle TabRect(int slot)
    {
        return R(-20, 50 + TabH * slot, TabW, TabH);
    }

    private static Rectangle RightTabRect(int slot)
    {
        return R(BookW - 12, 50 + TabH * slot, TabW, TabH);
    }

    /// <summary>标签页该在哪一侧：目录永远在左，致谢永远在右，其余看当前页和它自己的位置。</summary>
    private static bool TabOnLeftSide(int anchor)
    {
        if (anchor == Page_TableOfContents) return true;
        if (anchor == Page_Credits) return false;
        return PageNum > anchor || PageNum == Page_Credits;
    }

    private static bool TabVisible(int anchor)
    {
        if (PageNum == Page_Prompt) return false;
        BossLogConfig cfg = BossChecklistMod.Config;
        if (anchor == Page_TableOfContents) return true;
        if (anchor == Page_Credits) return PageNum != Page_Credits;
        if ((ProgressiveMode || cfg.OnlyShowBossContent) && anchor >= 0 && BossTracker.Entries.Count > anchor
            && BossTracker.Entries[anchor].Type != EntryType.Boss)
            return false;
        return anchor >= 0 && PageNum != anchor;
    }

    private static void DrawTabs()
    {
        Texture2D tab = UIU.Tex("LogUI_Tab");
        BossLogConfig cfg = BossChecklistMod.Config;

        // 目录 / 筛选 标签
        int tocSlot = 0;
        Rectangle tocRect = FilterPanelIsOpen
            ? R(-20 - Sc(50), 50, TabW, TabH)
            : TabRect(tocSlot);
        Texture2D tocIcon = UIU.Tex(FilterPanelIsOpen || PageNum == Page_TableOfContents ? "Nav_Filter" : "Nav_Contents");
        DrawTab(tocRect, tab, tocIcon, true, true);

        if (Over(tocRect) && PressedLeft)
        {
            ConsumeLeft();
            if (PageNum == Page_TableOfContents)
            {
                FilterPanelIsOpen = !FilterPanelIsOpen; // 已经在目录页：开关筛选面板
            }
            else
            {
                // 不在目录页：点这个标签就是「回到目录」，和原模组一样
                FilterPanelIsOpen = false;
                PageNum = Page_TableOfContents;
            }
        }
        if (Over(tocRect))
        {
            _hoverTitle = L("Tabs." + (PageNum == Page_TableOfContents ? "ToggleFilters" : "TableOfContents"));
        }

        // 下一条Boss / 小Boss / 事件 / 致谢
        int[] next = new int[]
        {
            BossTracker.FindNextEntry(EntryType.Boss),
            BossTracker.FindNextEntry(EntryType.MiniBoss),
            BossTracker.FindNextEntry(EntryType.Event)
        };
        string[] icons = new string[] { "Nav_Boss", "Nav_Miniboss", "Nav_Event" };
        for (int i = 0; i < 3; i++)
        {
            int anchor = next[i];
            bool visible = anchor >= 0 && anchor < BossTracker.Entries.Count;
            if (visible && (ProgressiveMode || cfg.OnlyShowBossContent) && BossTracker.Entries[anchor].Type != EntryType.Boss)
                visible = false;
            if (!visible) continue;

            bool onLeft = TabOnLeftSide(anchor);
            Rectangle r = onLeft ? R(-20, 50 + TabH * (i + 1), TabW, TabH) : R(BookW - 12, 50 + TabH * (i + 1), TabW, TabH);
            DrawTab(r, tab, UIU.Tex(icons[i]), onLeft, true);
            if (Over(r))
            {
                _hoverTitle = LF("Tabs.NextEntry", Localization.Common(BossTracker.Entries[anchor].Type), BossTracker.Entries[anchor].DisplayName);
                if (PressedLeft)
                {
                    ConsumeLeft();
                    ShowEntry(anchor);
                }
            }
        }

        // 致谢标签
        if (TabVisible(Page_Credits))
        {
            bool onLeft = TabOnLeftSide(Page_Credits);
            Rectangle r = onLeft ? R(-20, 50 + TabH * 4, TabW, TabH) : R(BookW - 12, 50 + TabH * 4, TabW, TabH);
            DrawTab(r, tab, UIU.Tex("Nav_Credits"), onLeft, true);
            if (Over(r))
            {
                _hoverTitle = L("Tabs.Credits");
                if (PressedLeft)
                {
                    ConsumeLeft();
                    PageNum = Page_Credits;
                }
            }
        }
    }

    private static void DrawTab(Rectangle r, Texture2D tex, Texture2D icon, bool onLeft, bool tan)
    {
        if (tex != null)
        {
            if (onLeft) UIU.Draw(tex, r, tan ? Color.Tan : Color.White);
            else UIU.DrawFlip(tex, r, tan ? Color.Tan : Color.White);
        }
        else
        {
            UIU.Fill(r, tan ? new Color(210, 180, 140) : new Color(120, 110, 100));
        }
        if (icon != null)
        {
            int offX = r.X < UIRenderer.ScreenWidth / 2 ? Sc(2) : -Sc(2);
            int w = Math.Max(2, Sc(icon.Width));
            int h = Math.Max(2, Sc(icon.Height));
            UIU.Draw(icon, new Rectangle(r.X + (r.Width - w) / 2 + offX, r.Y + (r.Height - h) / 2, w, h), Color.White);
        }
    }

    // ---------------- 筛选面板 ----------------

    private static readonly string[] FilterIcons = { "Nav_Boss", "Nav_Miniboss", "Nav_Event", "Nav_Hidden" };

    private static void DrawFilterPanel()
    {
        Texture2D panel = UIU.Tex("LogUI_Filter");
        Rectangle r = R(-20 - Sc(50) + TabW, 50, 50, 166);
        if (panel != null) UIU.Draw(panel, r, Color.White);
        else UIU.Fill(r, new Color(200, 188, 172));

        BossLogConfig cfg = BossChecklistMod.Config;
        for (int i = 0; i < FilterIcons.Length; i++)
        {
            Texture2D icon = UIU.Tex(FilterIcons[i]);
            if (icon == null) continue;
            int size = Sc(18);
            Rectangle box = new Rectangle(r.X + (r.Width - size) / 2, r.Y + Sc(15 + 34 * i), size, size);
            bool hiddenMode = i == 3;
            Color tint = hiddenMode ? (HiddenEntriesMode ? Color.White : Color.DimGray) : Color.White;
            UIU.Draw(icon, box, tint);

            Texture2D check = FilterCheckIcon(i);
            if (check != null && !hiddenMode)
                UIU.Draw(check, new Rectangle(box.Right - Sc(10), box.Bottom - Sc(15), Sc(22), Sc(20)), Color.White);

            if (Over(box))
            {
                _hoverTitle = FilterHoverText(i);
                if (PressedLeft)
                {
                    ConsumeLeft();
                    if (i == 3)
                    {
                        HiddenEntriesMode = !HiddenEntriesMode;
                    }
                    else if (i == 0)
                    {
                        cfg.FilterBosses = CycleFilter(cfg.FilterBosses, true);
                        BossChecklistMod.SaveConfig();
                    }
                    else if (i == 1 && !cfg.OnlyShowBossContent)
                    {
                        cfg.FilterMiniBosses = CycleFilter(cfg.FilterMiniBosses, false);
                        BossChecklistMod.SaveConfig();
                    }
                    else if (i == 2 && !cfg.OnlyShowBossContent)
                    {
                        cfg.FilterEvents = CycleFilter(cfg.FilterEvents, false);
                        BossChecklistMod.SaveConfig();
                    }
                }
            }
        }
    }

    private static int CycleFilter(int value, bool boss)
    {
        // Show -> HideWhenCompleted -> (boss ? Show : Hide) -> Show
        if (value == 0) return 1;
        if (value == 1) return boss ? 0 : 2;
        return 0;
    }

    private static Texture2D FilterCheckIcon(int index)
    {
        BossLogConfig cfg = BossChecklistMod.Config;
        if (index == 0) return UIU.Tex(cfg.FilterBosses == 0 ? "Checks_Check" : (cfg.FilterBosses == 1 ? "Checks_Next" : "Checks_X"));
        if (index == 1)
        {
            if (cfg.OnlyShowBossContent) return UIU.Tex("Checks_X");
            return UIU.Tex(cfg.FilterMiniBosses == 0 ? "Checks_Check" : (cfg.FilterMiniBosses == 1 ? "Checks_Next" : "Checks_X"));
        }
        if (index == 2)
        {
            if (cfg.OnlyShowBossContent) return UIU.Tex("Checks_X");
            return UIU.Tex(cfg.FilterEvents == 0 ? "Checks_Check" : (cfg.FilterEvents == 1 ? "Checks_Next" : "Checks_X"));
        }
        return null;
    }

    private static string FilterLabel(int value)
    {
        return Localization.Get("Configs.FilterType." + (value == 0 ? "Show" : (value == 1 ? "HideWhenCompleted" : "Hide")) + ".Label");
    }

    private static string FilterHoverText(int index)
    {
        BossLogConfig cfg = BossChecklistMod.Config;
        if (index == 3)
            return L("TableOfContents.Filter.ToggleHidden" + (HiddenEntriesMode ? "Close" : "Open"));
        if (cfg.OnlyShowBossContent && index > 0) return L("TableOfContents.Filter.Disabled");
        if (index == 0) return Localization.Get("Log.Common.BossPlural") + ": " + FilterLabel(cfg.FilterBosses);
        if (index == 1) return Localization.Get("Log.Common.MiniBossPlural") + ": " + FilterLabel(cfg.FilterMiniBosses);
        return Localization.Get("Log.Common.EventPlural") + ": " + FilterLabel(cfg.FilterEvents);
    }

    // ---------------- 右上角状态栏 ----------------

    private static void DrawIndicatorPanel()
    {
        BossLogConfig cfg = BossChecklistMod.Config;
        int count = 3;
        int w = 20 + 20 * count - 2;
        Rectangle panel = R(BookW, 0, 0, 0); // placeholder, replaced below
        int px = BookW - 15 - PageW + PageW - 25 - w;   // 右页右边 - 25 - 宽
        int py = 12 - 26 - 6;
        panel = R(px, py, w, 26);

        Texture2D section = UIU.Tex("LogUI_IndicatorSection");
        Texture2D end = UIU.Tex("LogUI_IndicatorEnd");
        Texture2D back = UIU.Tex("Indicator_Back");
        if (end != null && section != null)
        {
            int ew = Sc(end.Width);
            int eh = Sc(end.Height);
            Rectangle endR = new Rectangle(panel.Right - ew, panel.Y, ew, eh);
            UIU.Draw(end, new Rectangle(panel.X, panel.Y, ew, eh), Color.White);
            UIU.Draw(section, new Rectangle(panel.X + ew, panel.Y, panel.Width - ew * 2, eh), Color.White);
            UIU.DrawFlip(end, endR, Color.White);
        }
        else
        {
            UIU.Fill(panel, new Color(180, 170, 150));
        }

        string[] keys = { "Indicator_OnlyBosses", "Indicator_Manual", "Indicator_Progression" };
        bool[] on = { cfg.OnlyShowBossContent, cfg.AutomaticChecklist, ProgressiveMode };
        for (int i = 0; i < count; i++)
        {
            if (back != null)
                UIU.Draw(back, new Rectangle(panel.X + Sc(10) + Sc(20) * i, panel.Y + Sc(6), Sc(18), Sc(18)), Color.White);
            Texture2D icon = UIU.Tex(keys[i]);
            if (icon != null)
                UIU.Draw(icon, new Rectangle(panel.X + Sc(10) + Sc(20) * i, panel.Y + Sc(6), Sc(18), Sc(18)), on[i] ? Color.White : Color.DarkGray);

            Rectangle hit = new Rectangle(panel.X + Sc(10) + Sc(20) * i, panel.Y + Sc(6), Sc(18), Sc(18));
            if (Over(hit))
            {
                _hoverTitle = IndicatorHover(i);
                if (PressedLeft)
                {
                    ConsumeLeft();
                    if (i == 0) cfg.OnlyShowBossContent = !cfg.OnlyShowBossContent;
                    else if (i == 1) cfg.AutomaticChecklist = !cfg.AutomaticChecklist;
                    else cfg.ProgressiveChecklist = !cfg.ProgressiveChecklist;
                    BossChecklistMod.SaveConfig();
                }
            }
        }

        // 交互提示栏（左边那个小栏，鼠标停上去显示当前页的额外操作）
        if (cfg.ShowInteractionTooltips)
        {
            string hint = InteractionHint();
            if (!string.IsNullOrEmpty(hint))
            {
                Rectangle alt = R(px - 38 - 2, py, 38, 26);
                if (end != null && section != null)
                {
                    int ew = Sc(end.Width);
                    int eh = Sc(end.Height);
                    UIU.Draw(end, new Rectangle(alt.X, alt.Y, ew, eh), Color.White);
                    UIU.Draw(section, new Rectangle(alt.X + ew, alt.Y, alt.Width - ew * 2, eh), Color.White);
                    UIU.DrawFlip(end, new Rectangle(alt.Right - ew, alt.Y, ew, eh), Color.White);
                }
                else
                {
                    UIU.Fill(alt, new Color(180, 170, 150));
                }
                Texture2D icon = UIU.Tex("Indicator_Interaction");
                if (icon != null)
                    UIU.Draw(icon, new Rectangle(alt.X + (alt.Width - Sc(14)) / 2, alt.Y + Sc(5), Sc(14), Sc(16)), Color.White);
                if (Over(alt))
                {
                    _hoverTitle = hint;
                    if (PressedRight && cfg.EnableResetOptions)
                    {
                        ConsumeRight();
                        DoResetAction();
                    }
                }
            }
        }
    }

    private static string IndicatorHover(int index)
    {
        BossLogConfig cfg = BossChecklistMod.Config;
        if (index == 0) return L(cfg.OnlyShowBossContent ? "Indicator.OnlyBossContentEnabled" : "Indicator.OnlyBossContentDisabled");
        if (index == 1) return L(cfg.AutomaticChecklist ? "Indicator.AutomaticChecklist" : "Indicator.ManualChecklist");
        return L("Indicator.ProgressionMode");
    }

    private static string InteractionHint()
    {
        if (PageNum == Page_TableOfContents)
            return HiddenEntriesMode
                ? L("HintTexts.ClearHidden") + "\n" + L("TableOfContents.Filter.ToggleHiddenClose")
                : L("HintTexts.MarkEntry") + "\n" + L("HintTexts.HideEntry");
        if (PageNum >= 0 && SelectedSubPage == SubPage.SpawnInfo) return L("SpawnInfo.CycleRecipe");
        if (PageNum >= 0 && SelectedSubPage == SubPage.Records)
            return L("Records.Category.Cycle") + "\n" + L("Tabs.Records");
        return "";
    }

    private static void DoResetAction()
    {
        if (PageNum == Page_TableOfContents)
        {
            if (HiddenEntriesMode)
            {
                WorldState.ClearHidden();
                WorldState.Save();
            }
            else
            {
                WorldState.ClearMarked();
                WorldState.Save();
            }
        }
    }

    // ================= 页面内容 =================

    private static void DrawPageContent()
    {
        if (PageNum == Page_Credits)
        {
            DrawCredits();
            return;
        }
        if (PageNum == Page_TableOfContents)
        {
            DrawTableOfContents();
            return;
        }
        if (PageNum >= 0 && PageNum < BossTracker.Entries.Count)
        {
            DrawEntryPage(BossTracker.Entries[PageNum]);
        }
    }

    // ---------------- 目录 ----------------

    private static void DrawTableOfContents()
    {
        List<EntryInfo> pre = new List<EntryInfo>();
        List<EntryInfo> hard = new List<EntryInfo>();
        foreach (EntryInfo e in BossTracker.Entries)
        {
            if (!BossTracker.VisibleOnChecklist(e, HiddenEntriesMode)) continue;
            if (e.Progression <= VanillaContent.WallOfFlesh) pre.Add(e);
            else hard.Add(e);
        }

        UIU.TextCentered(L("TableOfContents.PreHardmode"), LeftPage.X + LeftPage.Width / 2, Y(18), UIU.Amber, 0.6f);
        UIU.TextCentered(L("TableOfContents.Hardmode"), RightPage.X + RightPage.Width / 2, Y(18), UIU.Amber, 0.6f);

        _scrollLeft = DrawEntryList(pre, LeftPage, 4, _scrollLeft);
        _scrollRight = DrawEntryList(hard, RightPage, 4, _scrollRight);

        if (BossChecklistMod.Config.ShowProgressBars && !HiddenEntriesMode)
        {
            Rectangle prevBtn = PageR(LeftPage, 8, 416, 18, 20);
            Rectangle nextBtn = PageR(RightPage, PageW - 18 - 12, 416, 18, 20);
            int barH = Sc(14);
            int left = prevBtn.Right + Sc(10);
            int right = nextBtn.X - Sc(10);
            int width = right - left;
            if (width > Sc(40))
            {
                DrawProgressBar(new Rectangle(left, prevBtn.Y + prevBtn.Height / 2 - barH / 2, width, barH), false);
            }
            if (!ProgressiveMode || SafeHardmode())
            {
                int hLeft = nextBtn.X - Sc(10) - width;
                if (hLeft > LeftPage.Right + Sc(10))
                    DrawProgressBar(new Rectangle(hLeft, nextBtn.Y + nextBtn.Height / 2 - barH / 2, width, barH), true);
            }
        }

        if (HiddenEntriesMode)
        {
            Rectangle note = PageR(LeftPage, 6, PageH - 40, PageW - 12, 34);
            UIU.TextCentered(L("TableOfContents.Filter.ToggleHiddenClose"), note.X + note.Width / 2, note.Y, UIU.Tomato, 0.8f);
        }
    }

    private static bool SafeHardmode()
    {
        try { return Main.hardMode; } catch { return false; }
    }

    private static int DrawEntryList(List<EntryInfo> list, Rectangle page, int baseX, int scroll)
    {
        Rectangle view = PageR(page, baseX + 4, 44, PageW - 60, PageH - 136);
        int rowH = Sc(RowH);
        int total = list.Count * rowH + Sc(5);
        int maxScroll = Math.Max(0, total - view.Height);
        scroll = UIU.Clamp(scroll, 0, maxScroll);

        if (Over(view))
        {
            int wheel = UIRenderer.ScrollWheel;
            if (wheel != 0)
            {
                scroll = UIU.Clamp(scroll - Math.Sign(wheel) * Sc(48), 0, maxScroll);
                UIRenderer.ConsumeScroll();
            }
        }

        try { UIRenderer.BeginClip(view.X, view.Y, view.Width, view.Height); } catch { }
        try
        {

            int next = BossTracker.FindNextEntry();
            BossLogConfig cfg = BossChecklistMod.Config;
            int y = view.Y + Sc(5) - scroll;
            foreach (EntryInfo e in list)
            {
                Rectangle row = new Rectangle(view.X, y, view.Width, rowH);
                if (row.Bottom >= view.Y && row.Y <= view.Bottom)
                {
                    bool hover = Over(row);
                    int index = BossTracker.IndexOf(e);
                    if (hover) HoveredEntry = index;

                    string name = e.DisplayName;
                    if (ProgressiveMode && !e.IsChecked && index != next) name = "???";
                    if (cfg.AutomaticChecklist && e.Marked) name += "*";

                    Color color;
                    if (HiddenEntriesMode) color = e.Hidden ? Color.DimGray : Color.DarkGray;
                    else if (e.Hidden || (!e.IsAvailable() && !e.IsChecked)) color = Color.DimGray;
                    else if (cfg.ColoredBossText) color = e.IsChecked ? UIU.LightGreen : UIU.Tomato;
                    else color = new Color(255, 239, 213);

                    bool isNext = cfg.DrawNextMark && index == next && !e.Hidden && !HiddenEntriesMode;
                    if (hover)
                    {
                        color = cfg.ColoredBossText ? UIU.SkyBlue : Color.Silver;
                        string extra = cfg.ShowProgressionValue ? " [" + e.Progression.ToString("0.#") + "f]" : "";
                        _hoverTitle = name + extra;
                    }
                    if (isNext && cfg.ColoredBossText) color = new Color(248, 235, 91);

                    int padLeft = Sc(e.Progression <= VanillaContent.WallOfFlesh ? 32 : 22);
                    int textX = view.X + padLeft;
                    UIU.TextFit(name, textX, y + (rowH - Sc(UIU.LineH())) / 2, color, view.Width - padLeft - Sc(6));

                    DrawCheckMark(new Rectangle(view.X + padLeft - Sc(24), y + (rowH - Sc(20)) / 2, 22, 20), e, isNext);

                    if (cfg.LootCheckVisibility && !HiddenEntriesMode)
                    {
                        bool allLoot = AllLootObtained(e, out bool allCollect);
                        if (allLoot)
                        {
                            bool gold = !cfg.OnlyCheckDroppedLoot && allCollect;
                            Texture2D chest = UIU.Tex(gold ? "Checks_Chest_Gold" : "Checks_Chest");
                            if (chest != null)
                            {
                                int hardOffset = Sc(e.Progression > VanillaContent.WallOfFlesh ? 10 : 0);
                                Rectangle cr = new Rectangle(view.X + view.Width - chest.Width - hardOffset, y - Sc(2), chest.Width, chest.Height);
                                UIU.Draw(chest, cr, Color.White);
                                if (Over(cr))
                                {
                                    _hoverTitle = gold
                                        ? L("TableOfContents.AllLoot") + "\n" + L("TableOfContents.AllCollectibles")
                                        : L("TableOfContents.AllLoot");
                                }
                            }
                        }
                    }

                    if (hover && PressedRight)
                    {
                        ConsumeRight();
                        if (AltDown() || HiddenEntriesMode) WorldState.ToggleHidden(e);
                        else WorldState.ToggleMarked(e);
                        WorldState.Save();
                    }
                    if (PressedLeft && hover)
                    {
                        ConsumeLeft();
                        if (HiddenEntriesMode) WorldState.ToggleHidden(e);
                        else if (!(ProgressiveMode && !e.IsChecked && index != next)) ShowEntry(index);
                    }
                }
                y += rowH;
            }

        }
        finally
        {
            UIU.SafeEndClip();
        }

        if (maxScroll > 0) scroll = DrawScrollbar(view, maxScroll, scroll);
        return scroll;
    }

    private static void DrawCheckMark(Rectangle box, EntryInfo e, bool isNext)
    {
        BossLogConfig cfg = BossChecklistMod.Config;
        if (HiddenEntriesMode)
        {
            UIU.Fill(box, e.Hidden ? new Color(90, 70, 60) : new Color(210, 190, 160));
            UIU.Outline(box, UIU.Ink);
            return;
        }
        int type = cfg.SelectedCheckmarkType;
        bool done = e.IsChecked;
        if (done && type == 3 && !e.Hidden)
        {
            DrawStrike(box);
            return;
        }
        Texture2D boxTex = UIU.Tex("Checks_Box");
        if (boxTex != null) UIU.Draw(boxTex, box, Color.White);
        else UIU.Outline(box, UIU.Ink);

        Texture2D mark = null;
        if (done) mark = UIU.Tex(type == 2 ? "Checks_Box" : (type == 1 ? "Checks_X" : "Checks_Check"));
        else if (isNext) mark = UIU.Tex("Checks_Next");
        else if (type == 1) mark = UIU.Tex("Checks_X");
        if (mark != null && !e.Hidden) UIU.Draw(mark, box, Color.White);
    }

    private static void DrawStrike(Rectangle box)
    {
        Texture2D strike = UIU.Tex("Checks_Strike");
        if (strike == null) return;
        int w = strike.Width / 3;
        int h = strike.Height;
        int y = box.Y + box.Height / 2 - Sc(h) / 2;
        UIU.Draw(strike, new Rectangle(box.X, y, Sc(w), Sc(h)), Color.White);
        UIU.Draw(strike, new Rectangle(box.X + Sc(w), y, Sc(w), Sc(h)), Color.Transparent);
        UIU.Draw(strike, new Rectangle(box.X + Sc(w) * 2, y, Sc(w), Sc(h)), Color.White);
    }

    private static void DrawProgressBar(Rectangle bar, bool hardmode)
    {
        ProgressCounts(bar, hardmode, out int done, out int total);
        float pct = total <= 0 ? 0f : (float)done / total;

        string label = (pct * 100f).ToString("0.0") + "%";
        UIU.TextCentered(label, bar.X + bar.Width / 2, bar.Y - Sc(16), UIU.Amber, 0.85f);

        Texture2D full = UIU.Tex("Extra_ProgressBar");
        if (full != null)
        {
            int wCut = full.Width / 3;
            int h = full.Height;
            int extra = 4;
            int barWidth = bar.Width - Sc(12) + Sc(extra);
            UIU.Draw(full, new Rectangle(bar.X, bar.Y, Sc(wCut), Sc(h)), Color.White);
            UIU.Draw(full, new Rectangle(bar.X + Sc(wCut), bar.Y, barWidth - Sc(extra), Sc(h)), Color.White);
            UIU.Draw(full, new Rectangle(bar.X + bar.Width - Sc(wCut), bar.Y, Sc(wCut), Sc(h)), Color.White);
            BossLogConfig cfg = BossChecklistMod.Config;
            Color c = new Color((byte)UIU.Clamp(cfg.BookColorR, 0, 255), (byte)UIU.Clamp(cfg.BookColorG, 0, 255), (byte)UIU.Clamp(cfg.BookColorB, 0, 255), (byte)180);
            UIU.Fill(new Rectangle(bar.X + Sc(4), bar.Y + Sc(4), (int)(barWidth * pct), bar.Height - Sc(8)), c);
        }
        else
        {
            UIU.Fill(bar, new Color(60, 50, 40));
            UIU.Fill(new Rectangle(bar.X, bar.Y, (int)(bar.Width * pct), bar.Height), UIU.Goldenrod);
        }

        if (Over(bar))
            _hoverTitle = L("TableOfContents.ProgressBarListTypes") + "\n" + L("TableOfContents.ProgressBarListMods");
    }

    private static void ProgressCounts(Rectangle bar, bool hardmode, out int done, out int total)
    {
        done = 0;
        total = 0;
        foreach (EntryInfo e in BossTracker.Entries)
        {
            bool isHard = e.Progression > VanillaContent.WallOfFlesh;
            if (isHard != hardmode) continue;
            if (!e.IsAvailable()) continue;
            if (e.Hidden) continue;
            total++;
            if (e.IsChecked) done++;
        }
    }

    private static int DrawScrollbar(Rectangle view, int maxScroll, int scroll)
    {
        int barW = Sc(10);
        bool over = Over(view);
        int thumbH = Math.Max(Sc(24), (int)((float)view.Height * view.Height / (view.Height + maxScroll)));
        int track = Math.Max(1, view.Height - thumbH);
        int thumbY = view.Y + (int)((float)track * scroll / maxScroll);
        Rectangle thumb = new Rectangle(view.Right + Sc(4), thumbY, barW, thumbH);
        UIU.Fill(new Rectangle(view.Right + Sc(4), view.Y, barW, view.Height), new Color((byte)20, (byte)22, (byte)34, (byte)110));
        UIU.Fill(thumb, over ? new Color(205, 215, 240, 240) : new Color(155, 165, 195, 220));
        UIU.Outline(thumb, new Color(60, 65, 90));
        return scroll;
    }    // ---------------- 条目详情页 ----------------

    private static void DrawEntryPage(EntryInfo entry)
    {
        BossLogConfig cfg = BossChecklistMod.Config;
        bool masked = ProgressiveMode && !entry.IsChecked;

        Texture2D portrait = entry.GetPortrait();
        if (portrait != null)
        {
            Rectangle src = entry.UsesNpcPortrait ? GameRefs.NpcFrame(entry.NpcIds[0], portrait)
                                                   : new Rectangle(0, 0, portrait.Width, portrait.Height);
            float scale = 1f;
            float xs = (float)LeftPage.Width / src.Width;
            float ys = (float)(LeftPage.Height - Sc(150)) / src.Height;
            if (xs < 1f || ys < 1f) scale = Math.Min(xs, ys);
            Color tint = masked ? Color.Black : Color.White;
            try
            {
                if (Main.spriteBatch != null)
                {
                    Vector2 pos = new Vector2(LeftPage.X + LeftPage.Width / 2f, LeftPage.Y + Sc(60) + (LeftPage.Height - Sc(150)) / 2f);
                    Main.spriteBatch.Draw(portrait, pos, src, tint, 0f,
                        new Vector2(src.Width / 2f, src.Height / 2f), scale, SpriteEffects.None, 0f);
                }
            }
            catch
            {
            }
        }

        List<Texture2D> heads = entry.GetHeadIcons();
        int offset = 0;
        int firstX = 0, firstY = 0, firstW = 0, firstH = 0;
        bool counted = false;
        for (int i = heads.Count - 1; i >= 0; i--)
        {
            Texture2D head = heads[i];
            if (head == null) continue;
            Rectangle src = new Rectangle(0, 0, head.Width, head.Height);
            if (entry.Key == "Terraria Deerclops" && head.Width >= 50 && head.Height >= 40)
                src = new Rectangle(2, 0, 48, 40);
            int w = Math.Max(2, Sc(src.Width));
            int h = Math.Max(2, Sc(src.Height));
            Rectangle dest = new Rectangle(LeftPage.Right - w - Sc(10) - Sc(src.Width + 2) * offset, LeftPage.Y + Sc(5), w, h);
            UIU.Draw(head, dest, masked ? Color.Black : Color.White);
            if (!counted)
            {
                firstX = dest.X; firstY = dest.Y; firstW = dest.Width; firstH = dest.Height;
                counted = true;
            }
            offset++;
        }

        if (counted)
        {
            Texture2D mark = UIU.Tex(entry.IsChecked ? "Checks_Check" : "Checks_X");
            if (mark != null)
                UIU.Draw(mark, new Rectangle(firstX + firstW / 2 - Sc(11), firstY + firstH - Sc(10), Sc(22), Sc(20)), Color.White);
            Rectangle hoverRect = new Rectangle(firstX - Sc(4), firstY, Math.Max(Sc(20), firstW + Sc(8)), firstH);
            if (Over(hoverRect)) _hoverTitle = L(entry.IsChecked ? "EntryPage.Defeated" : "EntryPage.Undefeated");
        }

        string title = (cfg.ShowProgressionValue ? "[" + entry.Progression.ToString("0.#") + "f] " : "") + entry.DisplayName;
        UIU.Text(title, LeftPage.X + Sc(5), LeftPage.Y + Sc(5), UIU.Goldenrod);
        UIU.Text(entry.SourceName, LeftPage.X + Sc(5), LeftPage.Y + Sc(30), new Color((byte)150, (byte)150, (byte)255));

        if (cfg.AccessInternalNames)
        {
            Texture2D key = UIU.Tex("Extra_Key");
            Rectangle kr = PageR(LeftPage, 5, 55, 24, 24);
            if (key != null) UIU.Draw(key, kr, Color.White);
            else UIU.Fill(kr, UIU.Goldenrod);
            if (Over(kr))
            {
                _hoverTitle = Localization.Format("Log.EntryPage.CopyKey", entry.Key);
            }
        }

        if (masked)
        {
            Rectangle mask = PageR(LeftPage, 0, PageH - 40, PageW, 34);
            UIU.TextCentered(L("ProgressionMode.IsEnabled"), mask.X + mask.Width / 2, mask.Y, UIU.Tomato, 0.7f);
        }

        DrawSubPageButton(PageR(RightPage, PageW / 2 - 158 - 8, 5, 158, 30), SubPage.SpawnInfo, L("Tabs.SpawnInfo"));
        DrawSubPageButton(PageR(RightPage, PageW / 2 + 8, 5, 158, 30), SubPage.LootAndCollectibles, L("Tabs.LootAndCollectibles"));
        DrawSubPageButton(PageR(RightPage, PageW / 2 - 79, 45, 158, 30), SubPage.Records, L("Tabs.Records"));

        if (SelectedSubPage == SubPage.SpawnInfo) DrawSpawnPage(entry);
        else if (SelectedSubPage == SubPage.LootAndCollectibles) DrawLootPage(entry);
        else DrawRecordsPage(entry);
    }

    private static void DrawSubPageButton(Rectangle r, SubPage page, string text)
    {
        Texture2D tex = UIU.Tex("Nav_SubPage_Button");
        Texture2D border = UIU.Tex("Nav_SubPage_Border");
        bool selected = SelectedSubPage == page;
        if (tex != null) UIU.Draw(tex, r, selected ? Color.White : Color.DarkGray);
        else UIU.Fill(r, selected ? new Color(200, 180, 150) : new Color(120, 110, 100));
        if (selected && border != null) UIU.Draw(border, r, Color.White);
        UIU.TextCentered(text, r.X + r.Width / 2, r.Y + (r.Height - Sc(UIU.LineH())) / 2, selected ? Color.White : Color.LightGray, 0.85f);
        if (Over(r) && PressedLeft)
        {
            ConsumeLeft();
            SelectedSubPage = page;
            _scrollRight = 0;
        }
    }

    // ---------------- 召唤信息页 ----------------

    private static void DrawSpawnPage(EntryInfo entry)
    {
        Rectangle box = PageR(RightPage, 5, 85, PageW - 34, PageH - 370);
        UIU.Fill(box, new Color((byte)40, (byte)30, (byte)20, (byte)120));
        UIU.Outline(box, new Color(90, 70, 50), 2);
        string info = Localization.SpawnInfo(entry.InternalName);
        UIU.DrawMarkup(info, box.X + Sc(6), box.Y + Sc(6), box.Width - Sc(12), 0.85f, UIU.Paper);

        if (entry.SpawnItems.Count == 0)
        {
            UIU.TextFit(L("SpawnInfo.NoSpawnItem"), RightPage.X + Sc(10), RightPage.Y + Sc(205), UIU.Paper, RightPage.Width - Sc(20));
            return;
        }

        if (_spawnItemSelected >= entry.SpawnItems.Count) _spawnItemSelected = 0;
        int spawnItem = entry.SpawnItems[_spawnItemSelected];
        if (spawnItem <= 0)
        {
            UIU.TextFit(L("SpawnInfo.NoSpawnItem"), RightPage.X + Sc(10), RightPage.Y + Sc(205), UIU.Paper, RightPage.Width - Sc(20));
            return;
        }

        List<Recipe> recipes = RecipesFor(spawnItem);
        Recipe rec = null;
        if (recipes.Count > 0)
        {
            if (_recipeSelected >= recipes.Count) _recipeSelected = 0;
            rec = recipes[_recipeSelected];
            string station = StationName(rec);
            string line = string.IsNullOrEmpty(station) ? L("SpawnInfo.ByHand") : LF("SpawnInfo.RecipeFrom", station);
            UIU.TextFit(line, RightPage.X + Sc(10), RightPage.Y + Sc(205), UIU.Paper, RightPage.Width - Sc(20));
        }
        else
        {
            UIU.TextFit(L("SpawnInfo.Noncraftable"), RightPage.X + Sc(10), RightPage.Y + Sc(205), UIU.Paper, RightPage.Width - Sc(20));
        }

        Rectangle slot = PageR(RightPage, 160, 230, 48, 48);
        DrawSlot(slot, spawnItem, 1, true);
        if (Over(slot)) _hoverItemType = spawnItem;

        bool cycle = entry.SpawnItems.Count > 1 || recipes.Count > 1;
        if (cycle)
        {
            Rectangle cyc = PageR(RightPage, 308, 230, 30, 30);
            Texture2D t = UIU.Tex("Extra_CycleRecipe");
            if (t != null) UIU.Draw(t, cyc, Color.White);
            else UIU.Fill(cyc, UIU.Goldenrod);
            if (Over(cyc))
            {
                _hoverTitle = L("SpawnInfo.CycleRecipe");
                if (PressedLeft)
                {
                    ConsumeLeft();
                    if (entry.SpawnItems.Count > 1)
                    {
                        _spawnItemSelected = (_spawnItemSelected + 1) % entry.SpawnItems.Count;
                        _recipeSelected = 0;
                    }
                    else
                    {
                        _recipeSelected = (_recipeSelected + 1) % recipes.Count;
                    }
                }
            }
        }

        if (rec != null)
        {
            int col = 0, row = 0;
            bool craftable = true;
            try
            {
                for (int i = 0; i < rec.requiredItem.Length && i < 21; i++)
                {
                    Item req = rec.requiredItem[i];
                    if (req == null || req.type <= 0) continue;
                    Rectangle r = PageR(RightPage, 20 + 48 * col, 288 + 48 * row, 44, 44);
                    int have = CountInInventory(req.type);
                    bool ok = have >= req.stack;
                    if (!ok) craftable = false;
                    DrawSlot(r, req.type, req.stack, BossChecklistMod.Config.SpawnItemCraftingChecklist ? ok : true);
                    if (Over(r)) _hoverItemType = req.type;
                    col++;
                    if (col >= 7) { col = 0; row++; }
                }
            }
            catch
            {
            }
            if (BossChecklistMod.Config.SpawnItemCraftingChecklist)
            {
                UIU.Text(craftable ? L("SpawnInfo.ByHand") : L("SpawnInfo.Noncraftable"),
                    RightPage.X + Sc(10), RightPage.Y + Sc(255), craftable ? UIU.LightGreen : UIU.Tomato, 0.8f);
            }
        }
    }

    private static List<Recipe> RecipesFor(int itemType)
    {
        List<Recipe> result = new List<Recipe>();
        try
        {
            Recipe[] all = Main.recipe;
            if (all == null) return result;
            for (int i = 0; i < all.Length; i++)
            {
                Recipe r = all[i];
                if (r == null || r.createItem == null) continue;
                if (r.createItem.type == itemType) result.Add(r);
            }
        }
        catch
        {
        }
        return result;
    }

    private static string StationName(Recipe rec)
    {
        try
        {
            int tile = rec.requiredTile;
            if (tile > -1)
            {
                string name = "";
                try { name = Recipe.GetRequiredTileName(tile); } catch { name = ""; }
                if (string.IsNullOrEmpty(name) || name == "Unknown")
                {
                    try { name = TileID.Search.GetName(tile); } catch { name = ""; }
                }
                if (!string.IsNullOrEmpty(name)) return name;
            }
        }
        catch
        {
        }
        return "";
    }

    private static int CountInInventory(int itemType)
    {
        int n = 0;
        try
        {
            Player p = Main.LocalPlayer;
            if (p == null) return 0;
            for (int i = 0; i < p.inventory.Length; i++)
            {
                Item it = p.inventory[i];
                if (it != null && it.type == itemType) n += it.stack;
            }
        }
        catch
        {
        }
        return n;
    }

    private static void DrawSlot(Rectangle r, int itemType, int stack, bool lit) => DrawSlot(r, itemType, stack, lit, 9);

    private static void DrawSlot(Rectangle r, int itemType, int stack, bool lit, int backIndex)
    {
        Texture2D back = GameRefs.Back(backIndex);
        if (back != null) UIU.Draw(back, r, lit ? Color.White : new Color((byte)110, (byte)110, (byte)110));
        else
        {
            UIU.Fill(r, new Color((byte)60, (byte)50, (byte)40, (byte)200));
            UIU.Outline(r, new Color(120, 100, 70));
        }
        UIU.ItemIcon(new Rectangle(r.X + Sc(2), r.Y + Sc(2), r.Width - Sc(4), r.Height - Sc(4)), itemType, stack);
    }    // ---------------- 战利品页 ----------------

    private static void DrawLootPage(EntryInfo entry)
    {
        try { BossTracker.EnsureLoot(); } catch { }

        int bag = entry.TreasureBag;
        Rectangle bagSlot = PageR(RightPage, PageW / 2 - 24, 88, 48, 48);
        if (bag > 0)
        {
            DrawSlot(bagSlot, bag, 1, true);
            if (Over(bagSlot)) _hoverItemType = bag;
        }
        else
        {
            Texture2D tex = UIU.Tex("Extra_TreasureBag");
            if (tex != null) UIU.Draw(tex, bagSlot, UIU.Faded);
            else UIU.Outline(bagSlot, UIU.InkFaded);
        }

        List<int> items = LootDisplayOrder(entry, bag);

        Rectangle view = PageR(RightPage, 0, 125, PageW - 25, PageH - 205);
        int cols = 6;
        int cell = Sc(56);
        int rowH = Sc(52);
        int rows = (items.Count + cols - 1) / cols;
        int maxScroll = Math.Max(0, rows * rowH + Sc(10) - view.Height);
        _scrollLoot = UIU.Clamp(_scrollLoot, 0, maxScroll);
        if (Over(view))
        {
            int wheel = UIRenderer.ScrollWheel;
            if (wheel != 0)
            {
                _scrollLoot = UIU.Clamp(_scrollLoot - Math.Sign(wheel) * Sc(48), 0, maxScroll);
                UIRenderer.ConsumeScroll();
            }
        }

        try { UIRenderer.BeginClip(view.X, view.Y, view.Width, view.Height); } catch { }
        try
        {
            for (int i = 0; i < items.Count; i++)
            {
                int col = i % cols;
                int row = i / cols;
                Rectangle r = new Rectangle(view.X + Sc(15) + cell * col, view.Y + rowH * row - _scrollLoot, Sc(48), Sc(48));
                if (r.Bottom < view.Y || r.Y > view.Bottom) continue;
                DrawLootSlot(entry, items[i], r);
            }
        }
        finally
        {
            UIU.SafeEndClip();
        }

        if (items.Count == 0)
            UIU.TextCentered(L("LootAndCollection.MaskedItems"), view.X + view.Width / 2, view.Y + Sc(20), UIU.InkFaded, 0.85f);

        if (maxScroll > 0) _scrollLoot = DrawScrollbar(view, maxScroll, _scrollLoot);
    }

    /// <summary>战利品页的显示顺序：收藏品按类型排在前面，剩下的掉落排在后面。</summary>
    private static List<int> LootDisplayOrder(EntryInfo entry, int bag)
    {
        BossLogConfig cfg = BossChecklistMod.Config;
        List<int> items = new List<int>();
        for (int t = 0; t < (int)CollectibleType.Generic; t++)
        {
            foreach (KeyValuePair<int, CollectibleType> kv in entry.Collectibles)
            {
                if (kv.Value != (CollectibleType)t || kv.Key <= 0 || kv.Key == bag) continue;
                if (!items.Contains(kv.Key)) items.Add(kv.Key);
            }
        }
        for (int i = 0; i < entry.Loot.Count; i++)
        {
            int item = entry.Loot[i];
            if (item <= 0 || item == bag || items.Contains(item)) continue;
            items.Add(item);
        }
        for (int i = items.Count - 1; i >= 0; i--)
        {
            int item = items[i];
            if (IsCoin(item) || IsEvilLocked(entry, item)) { items.RemoveAt(i); continue; }
            if (cfg.OnlyCheckDroppedLoot && !entry.Dropped.Contains(item)) items.RemoveAt(i);
        }
        return items;
    }

    private static bool IsCoin(int itemType)
    {
        try { return itemType > 0 && itemType < ItemID.Sets.CommonCoin.Length && ItemID.Sets.CommonCoin[itemType]; }
        catch { return false; }
    }

    /// <summary>
    /// 腐化 / 猩红世界专属的掉落：另一个邪恶世界里拿不到。
    /// 醉酒世界（05162020 之类）里两种邪恶地形同时存在，两边的掉落都拿得到，
    /// 所以那里一律不算被锁（原模组没有区分这一点，会把另一半当成永远拿不到）。
    /// </summary>
    private static bool IsEvilLocked(EntryInfo entry, int itemType)
    {
        try
        {
            if (Main.drunkWorld) return false;
            return (WorldGen.crimson && entry.CorruptionLocked.Contains(itemType))
                || (!WorldGen.crimson && entry.CrimsonLocked.Contains(itemType));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>专家 / 大师限定、当前难度拿不到的物品。</summary>
    private static bool IsModeRestricted(EntryInfo entry, int itemType)
    {
        try
        {
            if (entry.Collectibles.TryGetValue(itemType, out CollectibleType type)
                && (type == CollectibleType.Relic || type == CollectibleType.MasterPet)
                && !Main.masterMode) return true;
            if (!Main.expertMode && entry.ExpertLocked.Contains(itemType)) return true;
        }
        catch
        {
        }
        return false;
    }

    private static void DrawLootSlot(EntryInfo entry, int itemType, Rectangle r)
    {
        bool got = Collected.Has(itemType);
        bool restricted = !got && IsModeRestricted(entry, itemType);
        bool masked = !got && ProgressiveMode && !entry.IsChecked;
        DrawSlot(r, itemType, 1, !masked, got ? 3 : (restricted ? 11 : 7));

        if (entry.Collectibles.ContainsKey(itemType))
        {
            Texture2D highlight = UIU.Tex("Extra_HighlightedCollectible");
            if (highlight != null) UIU.Draw(highlight, r, Color.White);
        }

        if (got)
        {
            Texture2D check = UIU.Tex("Checks_Check");
            if (check != null)
            {
                int w = Math.Min(check.Width, r.Width);
                int h = Math.Min(check.Height, r.Height);
                UIU.Draw(check, new Rectangle(r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2, w, h), Color.White);
            }
        }

        if (!Over(r)) return;
        if (restricted)
        {
            bool master = entry.Collectibles.TryGetValue(itemType, out CollectibleType type)
                && (type == CollectibleType.Relic || type == CollectibleType.MasterPet);
            _hoverTitle = GameRefs.ItemName(itemType);
            _hoverDesc = master ? L("LootAndCollection.ItemIsMasterOnly") : L("LootAndCollection.ItemIsExpertOnly");
        }
        else if (got)
        {
            _hoverTitle = GameRefs.ItemName(itemType);
            _hoverDesc = L("LootAndCollection.Obtained");
        }
        else
        {
            _hoverItemType = itemType;
        }
    }

    /// <summary>这一条的战利品（以及收藏品）是不是都拿齐了。目录页的宝箱标记用它。</summary>
    private static bool AllLootObtained(EntryInfo entry, out bool allCollectibles)
    {
        bool allLoot = true;
        allCollectibles = true;
        try
        {
            for (int i = 0; i < entry.Loot.Count; i++)
            {
                int item = entry.Loot[i];
                if (item <= 0 || item == entry.TreasureBag || IsCoin(item) || IsEvilLocked(entry, item)) continue;
                if (!Main.expertMode && entry.ExpertLocked.Contains(item)) continue;
                if (!Collected.Has(item)) { allLoot = false; break; }
            }

            if (entry.Collectibles.Count == 0)
            {
                allCollectibles = allLoot;
            }
            else
            {
                foreach (KeyValuePair<int, CollectibleType> kv in entry.Collectibles)
                {
                    int item = kv.Key;
                    if (item <= 0 || item == entry.TreasureBag) continue;
                    if (BossChecklistMod.Config.OnlyCheckDroppedLoot && !entry.Dropped.Contains(item)) continue;
                    if (!Main.expertMode && entry.ExpertLocked.Contains(item)) continue;
                    if ((kv.Value == CollectibleType.Relic || kv.Value == CollectibleType.MasterPet) && !Main.masterMode) continue;
                    if (!Collected.Has(item)) { allCollectibles = false; break; }
                }
            }
        }
        catch
        {
            allLoot = false;
            allCollectibles = false;
        }
        return allLoot;
    }

    // ---------------- 记录页 ----------------

    private static readonly string[] RecordCategoryAssets =
    {
        "Nav_Record_FirstVictory", "Nav_Record_PersonalBest", "Nav_Record_PreviousAttempt", "Nav_Record_WorldRecord"
    };

    private static readonly string[] RecordCategoryNames = { "FirstVictory", "PersonalBest", "PreviousAttempt", "WorldRecord" };

    private static void DrawRecordsPage(EntryInfo entry)
    {
        BossLogConfig cfg = BossChecklistMod.Config;
        if (!entry.HasRecords)
        {
            DrawMiniBossRecords(entry);
            return;
        }

        BossRecord rec = Records.GetPlayer(entry.Key);
        WorldBossRecord world = Records.GetWorld(entry.Key);
        bool multi = false;
        try { multi = Main.netMode == 1; } catch { }

        bool[] available = { rec.UnlockedFirstVictory, rec.UnlockedPersonalBest, true, multi };
        if (_recordCategory < 0 || _recordCategory > 3 || !available[_recordCategory]) _recordCategory = 2;
        if (_recordComparison >= 0 && (_recordComparison > 3 || !available[_recordComparison])) _recordComparison = -1;
        if (_recordCategory == 3) _recordComparison = -1;

        int total = 0;
        for (int i = 0; i < 4; i++) if (available[i]) total++;
        int shown = 0;
        for (int i = 0; i < 4; i++)
        {
            if (!available[i]) continue;
            int xOffset = shown % 2 == 0 ? (shown + 1 == total ? 15 : 0) : 30;
            int yOffset = shown > 1 ? 30 : (total > 2 ? 0 : 15);
            Rectangle r = PageR(RightPage, PageW / 2 - 79 + 158 + 20 + xOffset, 45 + yOffset, 18, 20);
            Texture2D icon = UIU.Tex(RecordCategoryAssets[i]);
            if (icon != null) UIU.Draw(icon, r, _recordCategory == i ? Color.White : UIU.Faded);
            else UIU.Fill(r, _recordCategory == i ? UIU.Goldenrod : new Color((byte)90, (byte)80, (byte)70));
            if (Over(r))
            {
                _hoverTitle = L("Records.Category." + RecordCategoryNames[i]);
                if (PressedLeft)
                {
                    ConsumeLeft();
                    _recordCategory = i;
                    if (_recordComparison == i) _recordComparison = -1;
                }
                else if (PressedRight && _recordCategory != i)
                {
                    ConsumeRight();
                    _recordComparison = _recordComparison == i ? -1 : i;
                }
            }
            shown++;
        }

        Texture2D plate = UIU.Tex("Extra_RecordSlot");
        Texture2D achievements = GameRefs.Vanilla("Images/UI/Achievements");
        for (int slot = 0; slot < 4; slot++)
        {
            Rectangle r = PageR(RightPage, (PageW - 320) / 2, 35 + 75 * (slot + 1), 320, 64);
            if (plate != null) UIU.Draw(plate, r, Color.White);
            else UIU.Fill(r, new Color((byte)70, (byte)55, (byte)40, (byte)200));

            string title, value;
            RecordSlotText(rec, world, _recordCategory, slot, out title, out value);
            if (_recordComparison >= 0 && slot > 0)
            {
                string ctitle, cvalue;
                RecordSlotText(rec, world, _recordComparison, slot, out ctitle, out cvalue);
                value = value + "  |  " + cvalue;
                if (Over(r)) _hoverTitle = ctitle;
            }

            if (slot == 0)
            {
                Texture2D catIcon = UIU.Tex(RecordCategoryAssets[_recordCategory]);
                if (catIcon != null)
                    UIU.Draw(catIcon, new Rectangle(r.X + Sc(15), r.Y + (r.Height - Sc(20)) / 2, Sc(18), Sc(20)), Color.White);
            }
            else if (achievements != null)
            {
                int ax = AchX(_recordCategory, slot);
                int ay = AchY(_recordCategory, slot, world);
                if (ax >= 0 && ay >= 0)
                {
                    try
                    {
                        if (Main.spriteBatch != null)
                            Main.spriteBatch.Draw(achievements, new Rectangle(r.X, r.Y, Sc(64), Sc(64)),
                                new Rectangle(66 * ax, 66 * ay, 64, 64), Color.White);
                    }
                    catch
                    {
                    }
                }
            }

            UIU.TextCentered(title, r.X + r.Width / 2 + Sc(2), r.Y + Sc(6), slot == 0 ? UIU.Goldenrod : Color.Gold, 0.8f);
            UIU.TextCentered(value, r.X + r.Width / 2 + Sc(2), r.Bottom - Sc(22), slot == 0 ? Color.LightYellow : Color.White, 0.8f);
        }

        if (cfg.ShowInteractionTooltips && Over(RightPage))
            _hoverDesc = L("Records.Category.Cycle");
    }

    private static void RecordSlotText(BossRecord rec, WorldBossRecord world, int category, int slot, out string title, out string value)
    {
        bool isWorld = category == 3;
        string name = RecordCategoryNames[Math.Max(0, Math.Min(3, category))];
        if (slot == 0) title = L("Records.Category." + name);
        else if (slot == 1) title = L("Records.Title." + name);
        else if (slot == 2) title = L("Records.Title.Duration" + (isWorld ? "World" : ""));
        else title = L("Records.Title.HitsTaken" + (isWorld ? "World" : ""));

        if (isWorld)
        {
            if (slot == 0) value = SafeWorldName();
            else if (slot == 1) value = string.Format(Localization.Get("Log.Records.KDR"), world.Kills, world.Deaths);
            else if (slot == 2) value = Records.FormatTicks(world.BestDuration);
            else value = Records.HitsToString(world.BestHits);
        }
        else if (category == 0)
        {
            if (slot == 0) value = Records.PlayerName();
            else if (slot == 1) value = Records.FormatPlayTime(rec.FirstVictoryTicks);
            else if (slot == 2) value = Records.FormatTicks(rec.FirstDuration);
            else value = Records.HitsToString(rec.FirstHits);
        }
        else if (category == 1)
        {
            if (slot == 0) value = Records.PlayerName();
            else if (slot == 1) value = string.Format(Localization.Get("Log.Records.KDR"), rec.Kills, rec.Deaths);
            else if (slot == 2) value = Records.FormatTicks(rec.BestDuration);
            else value = Records.HitsToString(rec.BestHits);
        }
        else
        {
            if (slot == 0) value = Records.PlayerName();
            else if (slot == 1) value = rec.Attempts == 0 ? L("Records.Unchallenged") : "#" + rec.Attempts;
            else if (slot == 2) value = Records.FormatTicks(rec.PreviousDuration);
            else value = Records.HitsToString(rec.PreviousHits);
        }
    }

    private static string SafeWorldName()
    {
        try { return Main.worldName; } catch { return ""; }
    }

    private static int AchX(int category, int slot)
    {
        if (slot == 1) return category == 0 ? 7 : (category == 1 ? 0 : (category == 3 ? 4 : 0));
        if (slot == 2) return category == 3 ? 2 : 4;
        if (slot == 3) return category == 3 ? 0 : 3;
        return -1;
    }

    private static int AchY(int category, int slot, WorldBossRecord world)
    {
        if (slot == 1)
        {
            if (category == 0) return 10;
            if (category == 1) return 3;
            if (category == 3) return world.Kills >= world.Deaths ? 10 : 8;
            return 9;
        }
        if (slot == 2) return category == 3 ? 12 : 9;
        if (slot == 3) return category == 3 ? 7 : 0;
        return -1;
    }

    private static void DrawMiniBossRecords(EntryInfo entry)
    {
        Texture2D plate = UIU.Tex("Extra_RecordSlot");
        Rectangle r = PageR(RightPage, (PageW - 320) / 2, 110, 320, 64);
        if (plate != null) UIU.Draw(plate, r, Color.White);
        else UIU.Fill(r, new Color((byte)70, (byte)55, (byte)40, (byte)200));

        int kills = 0;
        try
        {
            for (int i = 0; i < entry.NpcIds.Count; i++)
            {
                int npc = entry.NpcIds[i];
                if (npc <= 0) continue;
                int banner = BannerSystem.NPCtoBanner(npc);
                if (banner > 0) kills += BannerSystem.GetKillCount(banner);
            }
        }
        catch
        {
        }
        UIU.TextCentered(L("Records.TotalKills"), r.X + r.Width / 2 + Sc(2), r.Y + Sc(6), UIU.Goldenrod, 0.85f);
        UIU.TextCentered(kills.ToString(), r.X + r.Width / 2 + Sc(2), r.Bottom - Sc(22), Color.LightYellow, 0.85f);

        if (entry.Type == EntryType.Event)
        {
            int y = RightPage.Y + Sc(200);
            int col = 0;
            for (int i = 0; i < entry.NpcIds.Count && i < 36; i++)
            {
                string name = GameRefs.NpcName(entry.NpcIds[i]);
                if (string.IsNullOrEmpty(name)) continue;
                int x = RightPage.X + Sc(15) + Sc(175) * col;
                UIU.TextFit(name, x, y + Sc(20) * (i % 13), UIU.Ink, Sc(165), 0.8f);
                if (i % 13 == 12) col++;
            }
        }
    }    // ---------------- 翻页 ----------------

    private static bool EntryVisibleAt(int index)
    {
        if (index < 0 || index >= BossTracker.Entries.Count) return false;
        EntryInfo e = BossTracker.Entries[index];
        if (!BossTracker.VisibleOnChecklist(e)) return false;
        if (ProgressiveMode) return BossTracker.VisibleOnPage(e);
        return true;
    }

    private static void DrawNavButtons()
    {
        if (PageNum == Page_Prompt) return;
        Texture2D prev = UIU.Tex("Nav_Prev");
        Texture2D next = UIU.Tex("Nav_Next");

        if (PageNum != Page_TableOfContents)
        {
            Rectangle r = PageR(LeftPage, 8, 416, 18, 20);
            bool over = Over(r);
            if (prev != null) UIU.Draw(prev, r, over ? Color.White : UIU.Faded);
            else UIU.Fill(r, UIU.Faded);
            if (over)
            {
                int target = -1;
                for (int i = PageNum - 1; i >= 0; i--) { if (EntryVisibleAt(i)) { target = i; break; } }
                _hoverTitle = target >= 0 ? BossTracker.Entries[target].DisplayName : L("Tabs.TableOfContents");
                if (PressedLeft)
                {
                    ConsumeLeft();
                    if (target >= 0) ShowEntry(target);
                    else { PageNum = Page_TableOfContents; }
                }
            }
        }

        if (PageNum != Page_Credits)
        {
            Rectangle r = PageR(RightPage, PageW - 18 - 12, 416, 18, 20);
            bool over = Over(r);
            if (next != null) UIU.Draw(next, r, over ? Color.White : UIU.Faded);
            else UIU.Fill(r, UIU.Faded);
            if (over)
            {
                int target = -1;
                if (PageNum < 0) target = BossTracker.FindNextEntry();
                else
                {
                    for (int i = PageNum + 1; i < BossTracker.Entries.Count; i++) { if (EntryVisibleAt(i)) { target = i; break; } }
                }
                _hoverTitle = target >= 0 ? BossTracker.Entries[target].DisplayName : L("Tabs.Credits");
                if (PressedLeft)
                {
                    ConsumeLeft();
                    if (target >= 0) ShowEntry(target);
                    else PageNum = Page_Credits;
                }
            }
        }
    }

    // ---------------- 致谢页 ----------------

    private static readonly string[][] Contributors =
    {
        new string[] { "Jopojelly", "Owner" },
        new string[] { "SheepishShepherd", "CoOwner" },
        new string[] { "direwolf420", "Contributor" },
        new string[] { "riveren", "Sprites" },
        new string[] { "Orian", "EarlyTesting" },
        new string[] { "Panini", "EarlyTesting" }
    };

    private static void DrawCredits()
    {
        UIU.TextCentered(L("Credits.Devs"), LeftPage.X + LeftPage.Width / 2, Y(18), UIU.Amber, 0.6f);
        UIU.TextCentered(L("Credits.Mods"), RightPage.X + RightPage.Width / 2, Y(18), UIU.Amber, 0.6f);
        UIU.TextCentered(L("Credits.Notice"), RightPage.X + RightPage.Width / 2, RightPage.Y + Sc(56), new Color((byte)250, (byte)128, (byte)114), 0.85f);

        // 开发者列表
        Rectangle view = new Rectangle(LeftPage.X + Sc(15), LeftPage.Y + Sc(60), LeftPage.Width - Sc(40), LeftPage.Height - Sc(120));
        int slotH = Sc(80);
        int maxScroll = Math.Max(0, Contributors.Length * slotH - view.Height);
        if (Over(view))
        {
            int wheel = UIRenderer.ScrollWheel;
            if (wheel != 0)
            {
                _scrollRecords = UIU.Clamp(_scrollRecords - Math.Sign(wheel) * Sc(48), 0, maxScroll);
                UIRenderer.ConsumeScroll();
            }
        }
        _scrollRecords = UIU.Clamp(_scrollRecords, 0, maxScroll);

        Texture2D devPanel = UIU.Tex("Credits_Panel_Dev");
        try { UIRenderer.BeginClip(view.X, view.Y, view.Width, view.Height); } catch { }
        try
        {
            for (int i = 0; i < Contributors.Length; i++)
            {
                Rectangle r = new Rectangle(view.X, view.Y + slotH * i - _scrollRecords, Sc(320), Sc(80));
                if (r.Bottom < view.Y || r.Y > view.Bottom) continue;
                if (devPanel != null) UIU.Draw(devPanel, r, Color.White);
                else UIU.Fill(r, new Color((byte)60, (byte)50, (byte)40, (byte)220));
                Texture2D icon = UIU.Tex("Credits_" + Contributors[i][0]);
                if (icon != null) UIU.Draw(icon, new Rectangle(r.X, r.Y, Sc(80), Sc(80)), Color.White);
                string header = Contributors[i][0];
                string sub = Localization.Get("Log.Credits.Titles." + Contributors[i][1]);
                UIU.TextFit(header, r.X + Sc(85), r.Y + Sc(14), Color.White, Sc(224));
                UIU.TextFit(sub, r.X + Sc(85), r.Y + Sc(45), Color.LemonChiffon, Sc(224), 0.85f);
            }
        }
        finally
        {
            UIU.SafeEndClip();
        }
        if (maxScroll > 0) _scrollRecords = DrawScrollbar(view, maxScroll, _scrollRecords);

        // 注册的模组列表（TerrariaModder 版没有别的模组能注册条目，所以永远是「空」）
        Texture2D noMods = UIU.Tex("Credits_Panel_NoMods");
        Rectangle nm = new Rectangle(RightPage.X + Sc(27), RightPage.Y + Sc(85), Sc(320), Sc(48));
        if (noMods != null) UIU.Draw(noMods, nm, Color.White);
        else { UIU.Fill(nm, new Color((byte)60, (byte)50, (byte)40, (byte)220)); }
        UIU.TextFit(L("Credits.ModsEmpty"), nm.X + Sc(40), nm.Y + Sc(14), Color.White, Sc(260));

        Texture2D reg = UIU.Tex("Credits_Panel_Register");
        Rectangle rr = new Rectangle(RightPage.X + Sc(27), RightPage.Y + Sc(85) + Sc(48), Sc(320), Sc(80));
        if (reg != null) UIU.Draw(reg, rr, Color.White);
        else UIU.Fill(rr, new Color((byte)60, (byte)50, (byte)40, (byte)220));
        UIU.TextFit(L("Credits.Register"), rr.X + Sc(45), rr.Y + Sc(14), Color.White, Sc(280));
        UIU.TextFit(L("Credits.Learn"), rr.X + Sc(25), rr.Y + Sc(45), Color.LemonChiffon, Sc(275), 0.85f);
    }

    // ---------------- 渐进式清单询问页 ----------------

    private static void DrawPrompt()
    {
        BossLogConfig cfg = BossChecklistMod.Config;

        // 左页：开场白 + 问题标题 + 完整说明（自动换行，不会溢出书页）
        UIU.Text(L("ProgressionMode.BeforeYouBegin"), LeftPage.X + Sc(10), LeftPage.Y + Sc(15), Color.White, 0.8f);
        DrawAutoScaleHeader(L("ProgressionMode.AskEnable"), LeftPage, 40, UIU.Amber, 0.9f);
        UIU.DrawMarkup(L("ProgressionMode.Description"), LeftPage.X + Sc(12), LeftPage.Y + Sc(64),
            LeftPage.Width - Sc(24), 0.8f, UIU.Paper);

        // 右页：两个选项格子（和原模组一样只画图标，长说明改成悬浮提示）
        DrawAutoScaleHeader(L("ProgressionMode.SelectAnOption"), RightPage, 40, UIU.Amber, 0.9f);

        Texture2D slot = UIU.Tex("Extra_PromptSlot");
        Rectangle enable = PageR(RightPage, PageW / 2 - 100 - 10, 125, 100, 100);
        Rectangle disable = PageR(RightPage, PageW / 2 + 10, 125, 100, 100);
        DrawPromptButton(enable, slot, "Extra_ProgressiveOn", L("ProgressionMode.SelectEnable"));
        DrawPromptButton(disable, slot, "Extra_ProgressiveOff", L("ProgressionMode.SelectDisable"));

        if (Over(enable) && PressedLeft)
        {
            ConsumeLeft();
            cfg.ProgressiveChecklist = true;
            _promptDisabledThisSession = true;
            PageNum = Page_TableOfContents;
            BossChecklistMod.SaveConfig();
        }
        else if (Over(disable) && PressedLeft)
        {
            ConsumeLeft();
            cfg.ProgressiveChecklist = false;
            _promptDisabledThisSession = true;
            PageNum = Page_TableOfContents;
            BossChecklistMod.SaveConfig();
        }

        // 「不再显示此内容」：勾选框 + 自动换行的说明
        Rectangle row = PageR(RightPage, 25, 250, 320, 64);
        Texture2D rowTex = UIU.Tex("Extra_RecordSlot");
        if (rowTex != null) UIU.Draw(rowTex, row, Color.White);
        else UIU.Outline(row, UIU.InkFaded);

        Texture2D boxTex = UIU.Tex("checkBox");
        Rectangle box = new Rectangle(row.X + Sc(15), row.Y + (row.Height - Sc(21)) / 2, Sc(19), Sc(21));
        if (boxTex != null) UIU.Draw(boxTex, box, Color.White);
        else UIU.Outline(box, UIU.Paper);
        if (cfg.PromptDisabled)
        {
            Texture2D mark = UIU.Tex("checkMark");
            if (mark != null) UIU.Draw(mark, box, Color.White);
        }

        int textX = box.Right + Sc(12);
        int textW = row.Right - textX - Sc(12);
        int textH = UIU.MeasureMarkup(L("ProgressionMode.DisablePrompt"), textW, 0.85f);
        UIU.DrawMarkup(L("ProgressionMode.DisablePrompt"), textX,
            row.Y + Math.Max(Sc(4), (row.Height - textH) / 2), textW, 0.85f, UIU.Paper);

        if (Over(row))
        {
            _hoverTitle = L("ProgressionMode.DisablePrompt");
            if (PressedLeft)
            {
                ConsumeLeft();
                cfg.PromptDisabled = !cfg.PromptDisabled;
                BossChecklistMod.SaveConfig();
            }
        }
    }

    /// <summary>居中画页首标题；翻译太长时自动缩小，避免越出书页。</summary>
    private static void DrawAutoScaleHeader(string text, Rectangle page, int y, Color c, float scale)
    {
        int maxW = page.Width - Sc(30);
        int w = UIU.TextW(text, scale);
        if (w > maxW && w > 0) scale *= (float)maxW / w;
        UIU.TextCentered(text, page.X + page.Width / 2, page.Y + Sc(y), c, scale);
    }

    /// <summary>原模组的选项按钮：格子 + 图标；那几行长说明只在悬浮时显示。</summary>
    private static void DrawPromptButton(Rectangle r, Texture2D slot, string iconName, string hover)
    {
        if (slot != null) UIU.Draw(slot, r, Color.White);
        else UIU.Fill(r, new Color((byte)80, (byte)70, (byte)55, (byte)220));
        Texture2D icon = UIU.Tex(iconName);
        if (icon != null)
        {
            int iw = Math.Min(r.Width, Sc(icon.Width));
            int ih = Math.Min(r.Height, Sc(icon.Height));
            UIU.Draw(icon, new Rectangle(r.X + (r.Width - iw) / 2, r.Y + (r.Height - ih) / 2, iw, ih), Color.White);
        }
        if (Over(r)) _hoverTitle = hover;
    }
}
