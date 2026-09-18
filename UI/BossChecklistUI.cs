using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using TerrariaModder.Core.Events;
using TerrariaModder.Core.Reflection;
using TerrariaModder.Core.UI;
using Game = TerrariaModder.Core.Reflection.Game;

namespace BossChecklist;

/// <summary>
/// 右侧的 Boss 清单侧栏（按 P 开关）。只在背包打开时显示，和原模组一样。
/// </summary>
internal static class BossChecklistUI
{
    private const string PanelId = "boss-checklist.sidebar";
    private const int PanelW = 250;

    public static bool IsOpen { get; private set; }

    private static bool _drewThisFrame;
    private static bool _leftDown, _rightDown, _clickEdge, _rclickEdge;
    private static int _scroll;
    private static Rectangle _panel;

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
    }

    public static void Toggle()
    {
        if (IsOpen) Close();
        else
        {
            IsOpen = true;
            _leftDown = true;
            _rightDown = true;
            _drewThisFrame = false;
            Collected.ScanNow(); // 打开清单时扫一遍拿到过的掉落
            try { if (Main.playerInventory) Main.playerInventory = false; } catch { }
        }
    }

    public static void Close()
    {
        IsOpen = false;
        try { UIRenderer.UnregisterPanelBounds(PanelId); } catch { }
    }

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

    private static void Draw()
    {
        if (!IsOpen || _drewThisFrame) return;
        _drewThisFrame = true;
        try
        {


            bool left = false, right = false;
            try { left = Main.mouseLeft; right = Main.mouseRight; } catch { }
            _clickEdge = left && !_leftDown;
            _rclickEdge = right && !_rightDown;
            _leftDown = left;
            _rightDown = right;

            int sw = UIRenderer.ScreenWidth > 0 ? UIRenderer.ScreenWidth : 1920;
            int sh = UIRenderer.ScreenHeight > 0 ? UIRenderer.ScreenHeight : 1080;
            float s = 1f;
            try { s = Game.UIScale; } catch { }
            if (s <= 0f) s = 1f;
            int S(int v) => (int)Math.Round(v * s);

            int w = S(PanelW);
            _panel = new Rectangle(sw - w - S(26), S(50), w, sh - S(100));
            try { UIRenderer.RegisterPanelBounds(PanelId, _panel.X, _panel.Y, _panel.Width, _panel.Height); } catch { }

            // 和原版 [材料] 面板一样的底色
            UIU.Fill(_panel, new Color((byte)73, (byte)94, (byte)171, (byte)230));
            UIU.Outline(_panel, new Color((byte)30, (byte)40, (byte)80), S(2));

            BossLogConfig cfg = BossChecklistMod.Config;
            Texture2D navBoss = UIU.Tex("Nav_Boss");
            Texture2D navMini = UIU.Tex("Nav_Miniboss");
            Texture2D navEvent = UIU.Tex("Nav_Event");
            Texture2D navHidden = UIU.Tex("Nav_Hidden");
            Texture2D[] icons = { navBoss, navMini, navEvent, navHidden };
            int spacing = S(8);
            for (int i = 0; i < 4; i++)
            {
                Rectangle br = new Rectangle(_panel.X + spacing + S(32) * i, _panel.Y + spacing, S(28), S(28));
                bool active = i == 0 ? !cfg.OnlyShowBossContent
                            : i == 1 ? (cfg.FilterMiniBosses != 2 && !cfg.OnlyShowBossContent)
                            : i == 2 ? (cfg.FilterEvents != 2 && !cfg.OnlyShowBossContent)
                            : true;
                if (icons[i] != null) UIU.Draw(icons[i], br, active ? Color.White : new Color((byte)110, (byte)110, (byte)110));
                else UIU.Fill(br, active ? Color.Wheat : Color.Gray);

                if (Over(br))
                {
                    string tip = i == 0 ? Localization.Get("Checklist.ToggleCompletedTooltip")
                               : i == 1 ? Localization.Get("Checklist.ToggleMiniBossesTooltip")
                               : i == 2 ? Localization.Get("Checklist.ToggleEventsTooltip")
                               : Localization.Get("Checklist.ToggleShowHiddenBossesTooltip");
                    UIU.TextTip(tip);
                    if (PressedLeft)
                    {
                        ConsumeLeft();
                        if (i == 0) cfg.FilterBosses = cfg.FilterBosses == 1 ? 0 : 1;
                        else if (i == 1) cfg.FilterMiniBosses = cfg.FilterMiniBosses == 2 ? 0 : 2;
                        else if (i == 2) cfg.FilterEvents = cfg.FilterEvents == 2 ? 0 : 2;
                        else { cfg.OnlyShowBossContent = !cfg.OnlyShowBossContent; }
                        BossChecklistMod.SaveConfig();
                    }
                }
            }

            int listTop = _panel.Y + spacing + S(38);
            Rectangle view = new Rectangle(_panel.X + S(6), listTop, _panel.Width - S(12), _panel.Bottom - listTop - S(6));
            List<EntryInfo> list = new List<EntryInfo>();
            foreach (EntryInfo e in BossTracker.Entries)
            {
                if (!BossTracker.VisibleOnChecklist(e)) continue;
                if (cfg.OnlyShowBossContent && e.Type != EntryType.Boss) continue;
                list.Add(e);
            }

            int rowH = S(26);
            int maxScroll = Math.Max(0, list.Count * rowH + S(6) - view.Height);
            _scroll = UIU.Clamp(_scroll, 0, maxScroll);
            if (Over(view))
            {
                int wheel = UIRenderer.ScrollWheel;
                if (wheel != 0)
                {
                    _scroll = UIU.Clamp(_scroll - Math.Sign(wheel) * S(48), 0, maxScroll);
                    UIRenderer.ConsumeScroll();
                }
            }

            try { UIRenderer.BeginClip(view.X, view.Y, view.Width, view.Height); } catch { }
            try
            {
                int y = view.Y + S(2) - _scroll;
                int next = BossTracker.FindNextEntry();
                foreach (EntryInfo e in list)
                {
                    Rectangle row = new Rectangle(view.X, y, view.Width, rowH);
                    if (row.Bottom >= view.Y && row.Y <= view.Bottom)
                    {
                        bool hover = Over(row);
                        int index = BossTracker.IndexOf(e);
                        string name = e.DisplayName;
                        if (cfg.AutomaticChecklist && e.Marked) name += "*";
                        Color color = e.IsChecked ? UIU.LightGreen : UIU.Tomato;
                        if (e.Hidden) color = Color.DimGray;
                        else if (!e.IsAvailable() && !e.IsChecked) color = Color.DimGray;
                        if (hover) color = Color.White;
                        if (cfg.DrawNextMark && index == next && !e.Hidden && cfg.ColoredBossText) color = new Color((byte)248, (byte)235, (byte)91);

                        UIU.TextFit(name, row.X + S(28), row.Y + (rowH - S(UIU.LineH())) / 2, color, row.Width - S(34));

                        Texture2D box = UIU.Tex("Checks_Box");
                        Rectangle boxR = new Rectangle(row.X + S(2), row.Y + (rowH - S(20)) / 2, S(22), S(20));
                        if (box != null) UIU.Draw(box, boxR, Color.White);
                        else UIU.Outline(boxR, Color.Wheat);
                        if (e.IsChecked)
                        {
                            Texture2D mark = UIU.Tex(cfg.SelectedCheckmarkType == 2 ? "Checks_Box" : (cfg.SelectedCheckmarkType == 1 ? "Checks_X" : "Checks_Check"));
                            if (mark != null) UIU.Draw(mark, boxR, Color.White);
                        }

                        if (PressedLeft && hover)
                        {
                            ConsumeLeft();
                            WorldState.ToggleMarked(e);
                            WorldState.Save();
                        }
                        else if (PressedRight && hover)
                        {
                            ConsumeRight();
                            WorldState.ToggleHidden(e);
                            WorldState.Save();
                        }
                    }
                    y += rowH;
                }
            }
            finally
            {
                UIU.SafeEndClip();
            }

        }
        catch (Exception ex)
        {
            BossChecklistMod.Log?.Error("绘制 Boss 清单出错", ex);
        }
        finally
        {
            UIU.SafeEndClip();
        }
    }
}
