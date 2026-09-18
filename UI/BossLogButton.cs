using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using TerrariaModder.Core.Events;
using TerrariaModder.Core.UI;
using Game = TerrariaModder.Core.Reflection.Game;

namespace BossChecklist;

/// <summary>
/// 屏幕右下角的「Boss 日志」按钮（原模组那本小书）。打开背包或右侧清单时出现：
/// 左键打开日志本，按住右键拖动可以换位置（位置记在配置里，和原模组一样）。
/// </summary>
internal static class BossLogButton
{
    private const string PanelId = "boss-checklist.logbutton";
    private const int BookW = 34;
    private const int BookH = 38;

    private static bool _drewThisFrame;
    private static bool _dragging;
    private static int _dragDX, _dragDY;
    private static bool _leftDown, _rightDown;
    private static bool _boundsRegistered;

    public static void Init()
    {
        FrameEvents.OnPreDraw += OnFrameStart;
        UIRenderer.RegisterPanelDraw(PanelId, Draw);
    }

    public static void Unload()
    {
        FrameEvents.OnPreDraw -= OnFrameStart;
        UIRenderer.UnregisterPanelDraw(PanelId);
        UnregisterBounds();
    }

    private static void OnFrameStart()
    {
        _drewThisFrame = false;
    }

    private static void UnregisterBounds()
    {
        if (!_boundsRegistered) return;
        _boundsRegistered = false;
        try { UIRenderer.UnregisterPanelBounds(PanelId); } catch { }
    }

    private static void Draw()
    {
        if (_drewThisFrame) return;
        _drewThisFrame = true;
        try
        {


            bool show = false;
            try { show = !Main.gameMenu && Game.InWorld && (Main.playerInventory || BossChecklistUI.IsOpen); } catch { }
            if (!show)
            {
                _dragging = false;
                _rightDown = false;
                UnregisterBounds();
                return;
            }

            float ui = 1f;
            try { ui = Game.UIScale; } catch { }
            if (ui <= 0f) ui = 1f;

            int w = Math.Max(2, (int)Math.Round(BookW * ui));
            int h = Math.Max(2, (int)Math.Round(BookH * ui));
            int sw = UIRenderer.ScreenWidth > 0 ? UIRenderer.ScreenWidth : 1920;
            int sh = UIRenderer.ScreenHeight > 0 ? UIRenderer.ScreenHeight : 1080;

            BossLogConfig cfg = BossChecklistMod.Config;
            int mx = UIRenderer.MouseX, my = UIRenderer.MouseY;
            bool left = false, right = false;
            try { left = Main.mouseLeft; right = Main.mouseRight; } catch { }
            bool leftEdge = left && !_leftDown;
            bool rightEdge = right && !_rightDown;
            _leftDown = left;
            _rightDown = right;

            int x = sw + cfg.ButtonX;
            int y = sh + cfg.ButtonY;

            if (_dragging)
            {
                x = mx - _dragDX;
                y = my - _dragDY;
                if (!right)
                {
                    x = UIU.Clamp(x, 0, Math.Max(0, sw - w));
                    y = UIU.Clamp(y, 0, Math.Max(0, sh - h));
                    cfg.ButtonX = x - sw;
                    cfg.ButtonY = y - sh;
                    _dragging = false;
                    BossChecklistMod.SaveConfig();
                }
            }

            x = UIU.Clamp(x, 0, Math.Max(0, sw - w));
            y = UIU.Clamp(y, 0, Math.Max(0, sh - h));
            Rectangle r = new Rectangle(x, y, w, h);
            bool over = r.Contains(mx, my);

            if (over && rightEdge && !_dragging)
            {
                _dragging = true;
                _dragDX = mx - r.X;
                _dragDY = my - r.Y;
            }

            // 挡住底下压住的物品栏格子，免得点按钮的时候顺手用了道具
            try { UIRenderer.RegisterPanelBounds(PanelId, r.X, r.Y, r.Width, r.Height); _boundsRegistered = true; } catch { }

            if (over && leftEdge && !_dragging)
            {
                try { UIRenderer.ConsumeClick(); } catch { }
                if (!BossLogUI.IsOpen) BossLogUI.Open();
            }

            if (_dragging) return; // 拖动时只画个位置，不做别的

            Color cover = new Color(
                (byte)UIU.Clamp(cfg.BookColorR, 0, 255),
                (byte)UIU.Clamp(cfg.BookColorG, 0, 255),
                (byte)UIU.Clamp(cfg.BookColorB, 0, 255),
                (byte)(over ? 255 : 128));
            Texture2D fill = UIU.Tex(over ? "Book_Color" : "Book_Faded");
            if (fill != null) UIU.Draw(fill, r, cover);
            else UIU.Fill(r, cover);
            Texture2D outline = UIU.Tex("Book_Outline");
            if (outline != null) UIU.Draw(outline, r, Color.White);
            else UIU.Outline(r, Color.Black);

            // 描边颜色和原模组一致：悬浮是金色，关掉记录追踪是红色，还没开过日志本是彩虹色。
            Color? border = null;
            if (over) border = UIU.Goldenrod;
            else if (!cfg.RecordTrackingEnabled || !cfg.AllowNewRecords) border = Color.Firebrick;
            else if (!BossLogUI.HasOpenedLog) { try { border = new Color((byte)UIU.Clamp(Main.DiscoR, 0, 255), (byte)UIU.Clamp(Main.DiscoG, 0, 255), (byte)UIU.Clamp(Main.DiscoB, 0, 255)); } catch { } }
            if (border.HasValue)
            {
                Texture2D bt = UIU.Tex("Book_Border");
                if (bt != null) UIU.Draw(bt, r, border.Value);
                else UIU.Outline(r, border.Value, 2);
            }

            if (over)
            {
                string tip = Localization.Get("Log.Common.BossLog");
                UIU.Text(tip, r.X - UIU.TextW(tip) / 3, r.Y - (int)(UIU.LineH() * 1.3f), Color.White);
            }

        }
        catch (Exception ex)
        {
            BossChecklistMod.Log?.Error("绘制日志本按钮出错", ex);
        }
        finally
        {
            UIU.SafeEndClip();
        }
    }
}