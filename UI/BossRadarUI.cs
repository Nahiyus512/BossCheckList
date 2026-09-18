using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using TerrariaModder.Core.Events;
using TerrariaModder.Core.Reflection;
using TerrariaModder.Core.UI;
using Game = TerrariaModder.Core.Reflection.Game;

namespace BossChecklist;

/// <summary>
/// Boss 雷达：屏幕外的 Boss 会在屏幕边缘画一个 Boss 头像和指向箭头。
/// 绿色箭头表示视线通畅，红色表示被地形挡住。
/// </summary>
internal static class BossRadarUI
{
    private static bool _init;
    private static readonly Dictionary<int, bool> _whitelist = new Dictionary<int, bool>();

    public static void Init()
    {
        if (_init) return;
        _init = true;
        FrameEvents.OnUIOverlay += Draw;
    }

    public static void Unload()
    {
        if (!_init) return;
        _init = false;
        FrameEvents.OnUIOverlay -= Draw;
        _whitelist.Clear();
    }

    private static void BuildWhitelist(bool miniBosses)
    {
        _whitelist.Clear();
        foreach (EntryInfo entry in BossTracker.Entries)
        {
            if (entry.Type == EntryType.Event) continue;
            if (entry.Type == EntryType.MiniBoss && !miniBosses) continue;
            foreach (int npc in entry.NpcIds)
            {
                if (npc <= 0) continue;
                if (npc >= NPCID.Sets.BossHeadTextures.Length) continue;
                if (NPCID.Sets.BossHeadTextures[npc] == -1) continue;
                _whitelist[npc] = true;
            }
        }
    }

    private static bool _builtForMiniBosses;

    private static void Draw()
    {
        try
        {
            BossLogConfig cfg = BossChecklistMod.Config;
            if (!cfg.EnableBossRadar || !Game.InWorld) return;
            if (Main.gameMenu || Main.LocalPlayer == null) return;
            if (cfg.RadarMiniBosses != _builtForMiniBosses || _whitelist.Count == 0)
            {
                BuildWhitelist(cfg.RadarMiniBosses);
                _builtForMiniBosses = cfg.RadarMiniBosses;
            }
            if (_whitelist.Count == 0) return;

            Texture2D arrow = UIU.Tex("Extra_RadarArrow");
            Vector2 half = new Vector2(UIRenderer.ScreenWidth / 2f, UIRenderer.ScreenHeight / 2f);
            Vector2 player = Main.LocalPlayer.Center;
            float opacity = UIU.Clamp(cfg.RadarOpacityPercent, 35, 85) / 100f;
            int drawn = 0;

            for (int i = 0; i < Main.maxNPCs && drawn < 20; i++)
            {
                NPC npc = Main.npc[i];
                if (npc == null || !npc.active) continue;
                if (!_whitelist.ContainsKey(npc.type)) continue;

                Vector2 dir = npc.Center - player;
                if (dir.X == 0f) dir.X = 0.0001f;
                if (dir.Y == 0f) dir.Y = 0.0001f;

                int head = NPCID.Sets.BossHeadTextures[npc.type];
                Texture2D headTex = GameRefs.NpcHeadBoss(head);
                int hw = headTex != null ? headTex.Width : 32;
                int hh = headTex != null ? headTex.Height : 32;

                float marginX = half.X - hw - 24;
                float marginY = half.Y - hh - 24;
                if (marginX <= 4f || marginY <= 4f) continue;

                float tx = marginX / Math.Abs(dir.X);
                float ty = marginY / Math.Abs(dir.Y);
                float t = Math.Min(tx, ty);
                if (t >= 1f) continue; // 已经在屏幕里了

                Vector2 pos = half + dir * t;
                bool los = true;
                try
                {
                    los = Collision.CanHitLine(Main.LocalPlayer.position, Main.LocalPlayer.width, Main.LocalPlayer.height,
                        npc.position, npc.width, npc.height);
                }
                catch
                {
                }

                float alpha = los ? opacity : Math.Max(0f, opacity - 0.25f);
                if (headTex != null)
                {
                    Color headColor = Color.LightGray;
                    headColor.A = (byte)(255 * alpha);
                    UIU.Draw(headTex, new Rectangle((int)pos.X - hw / 2, (int)pos.Y - hh / 2, hw, hh), headColor);
                }

                if (arrow != null)
                {
                    Vector2 offset = Vector2.Normalize(dir) * (24f + (hw / 2f));
                    Vector2 arrowPos = pos + offset;
                    Color arrowColor = (los ? Color.Green : Color.Red);
                    arrowColor.A = (byte)150;
                    try
                    {
                        if (Main.spriteBatch != null)
                        {
                            Main.spriteBatch.Draw(arrow, arrowPos, null, arrowColor, dir.ToRotation(),
                                new Vector2(arrow.Width / 2f, arrow.Height / 2f), 1f, SpriteEffects.None, 0f);
                        }
                    }
                    catch
                    {
                    }
                }
                drawn++;
            }
        }
        catch (Exception ex)
        {
            BossChecklistMod.Log?.Error("绘制 Boss 雷达出错", ex);
        }
        finally
        {
            UIU.SafeEndClip();
        }
    }
}