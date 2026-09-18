using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using TerrariaModder.Core.UI;

namespace BossChecklist;

/// <summary>
/// 绘制辅助。原模组用 tModLoader 的 UIElements 体系，这里是即时模式，所以把常用的
/// 绘制、量文字、物品图标、富文本（[i:123] / [c/FF0000:文字]）都收在一起。
/// </summary>
internal static class UIU
{
    // ---------------- 常用颜色 ----------------

    public static readonly Color Faded = new Color(128, 128, 128, 128);
    public static readonly Color Amber = new Color(255, 235, 110);
    public static readonly Color Goldenrod = new Color(218, 165, 32);
    public static readonly Color SkyBlue = new Color(135, 206, 235);
    public static readonly Color Tomato = new Color(255, 99, 71);
    public static readonly Color LightGreen = new Color(144, 238, 144);
    public static readonly Color Paper = new Color(250, 235, 215);
    public static readonly Color Ink = new Color(60, 42, 22);
    public static readonly Color InkFaded = new Color(120, 100, 78);
    public static readonly Color PanelDark = new Color(30, 60, 30, 200);

    // ---------------- 贴图 ----------------

    public static Texture2D Tex(string name)
    {
        try { return GameRefs.ModTex(name); } catch { return null; }
    }

    // ---------------- 矩形 ----------------

    public static void Fill(Rectangle r, Color c)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        try { UIRenderer.DrawRect(r.X, r.Y, r.Width, r.Height, c.R, c.G, c.B, c.A); } catch { }
    }

    public static void Outline(Rectangle r, Color c, int thickness = 1)
    {
        if (r.Width <= 0 || r.Height <= 0 || thickness <= 0) return;
        try { UIRenderer.DrawRectOutline(r.X, r.Y, r.Width, r.Height, c.R, c.G, c.B, c.A, thickness); } catch { }
    }

    /// <summary>带颜色的贴图绘制（UIRenderer 那个只认透明度，染色得自己走 spriteBatch）。</summary>
    public static void Draw(Texture2D t, Rectangle r, Color c)
    {
        if (!GameRefs.Alive(t) || r.Width <= 0 || r.Height <= 0) return;
        NoteDraw(t, r);
        try
        {
            if (Main.spriteBatch != null)
                Main.spriteBatch.Draw(t, r, null, c, 0f, Vector2.Zero, SpriteEffects.None, 0f);
        }
        catch
        {
        }
    }

    public static void Draw(Texture2D t, Rectangle r)
    {
        Draw(t, r, Color.White);
    }

    public static void DrawFlip(Texture2D t, Rectangle r, Color c)
    {
        if (!GameRefs.Alive(t) || r.Width <= 0 || r.Height <= 0) return;
        NoteDraw(t, r);
        try
        {
            if (Main.spriteBatch != null)
                Main.spriteBatch.Draw(t, r, null, c, 0f, Vector2.Zero, SpriteEffects.FlipHorizontally, 0f);
        }
        catch
        {
        }
    }

    public static void DrawNative(Texture2D t, int x, int y, Color c)
    {
        if (t == null) return;
        Draw(t, new Rectangle(x, y, t.Width, t.Height), c);
    }

    public static void DrawCentered(Texture2D t, Rectangle r, Color c)
    {
        if (t == null) return;
        Draw(t, new Rectangle(r.X + (r.Width - t.Width) / 2, r.Y + (r.Height - t.Height) / 2, t.Width, t.Height), c);
    }

    /// <summary>按整数倍缩放居中画贴图，保证像素不糊。</summary>
    public static void DrawScaled(Texture2D t, Rectangle r, Color c, float scale)
    {
        if (t == null) return;
        int w = Math.Max(1, (int)(t.Width * scale));
        int h = Math.Max(1, (int)(t.Height * scale));
        Draw(t, new Rectangle(r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2, w, h), c);
    }

    public static Color Mul(Color c, float f)
    {
        return new Color(
            (byte)Math.Max(0, Math.Min(255, (int)(c.R * f))),
            (byte)Math.Max(0, Math.Min(255, (int)(c.G * f))),
            (byte)Math.Max(0, Math.Min(255, (int)(c.B * f))),
            c.A);
    }

    public static Color Alpha(Color c, byte a)
    {
        return new Color(c.R, c.G, c.B, a);
    }

    public static int Clamp(int v, int min, int max)
    {
        if (v < min) return min;
        if (v > max) return max;
        return v;
    }

    // ---------------- 文字 ----------------

    public static int LineH()
    {
        try { return GameRefs.TextLine(); } catch { return 20; }
    }

    public static void Text(string s, int x, int y, Color c, float scale = 1f)
    {
        if (string.IsNullOrEmpty(s)) return;
        try
        {
            if (scale >= 0.999f) UIRenderer.DrawText(s, x, y, c.R, c.G, c.B, c.A);
            else UIRenderer.DrawTextScaled(s, x, y, c.R, c.G, c.B, c.A, scale);
        }
        catch
        {
        }
    }

    public static int TextW(string s, float scale = 1f)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        try { return (int)(GameRefs.TextWidth(s) * scale); } catch { return (int)(s.Length * 8 * scale); }
    }

    public static void TextCentered(string s, int centerX, int y, Color c, float scale = 1f)
    {
        Text(s, centerX - TextW(s, scale) / 2, y, c, scale);
    }

    /// <summary>把文字缩放到不超过 maxW；实在太长就截断加省略号。</summary>
    public static void TextFit(string s, int x, int y, Color c, int maxW, float scale = 1f)
    {
        if (string.IsNullOrEmpty(s) || maxW <= 8) return;
        int w = TextW(s, scale);
        if (w > maxW)
        {
            float fitted = scale * ((float)maxW / w);
            if (fitted < 0.6f)
            {
                float baseScale = 0.6f;
                int limit = (int)(maxW / baseScale);
                string cut = s;
                while (cut.Length > 1 && TextW(cut) > limit) cut = cut.Substring(0, cut.Length - 1);
                Text(cut + "...", x, y, c, baseScale);
                return;
            }
            scale = fitted;
        }
        Text(s, x, y, c, scale);
    }

    // ---------------- 渲染状态自检（排查卡死用） ----------------

    /// <summary>最近画过的一张贴图。真出问题的时候它会跟着写进日志，方便一眼看出是哪张图。</summary>
    public static string LastDrawn = "(还没画过)";

    private static void NoteDraw(Texture2D t, Rectangle r)
    {
        try
        {
            string name = string.IsNullOrEmpty(t.Name) ? "?" : t.Name;
            LastDrawn = name + " " + r.Width + "x" + r.Height + " @" + r.X + "," + r.Y;
        }
        catch
        {
        }
    }

    private static FieldInfo _sbBegunField;
    private static bool _sbBegunProbed;

    /// <summary>SpriteBatch 现在还停在「开始画了但没结束」的状态吗。</summary>
    public static bool BatchBegun()
    {
        try
        {
            SpriteBatch sb = Main.spriteBatch;
            if (sb == null) return false;
            if (!_sbBegunProbed)
            {
                _sbBegunProbed = true;
                const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
                Type st = typeof(SpriteBatch);
                _sbBegunField = st.GetField("inBeginEndPair", F) ?? st.GetField("beginCalled", F) ?? st.GetField("_beginCalled", F);
            }
            if (_sbBegunField == null) return false;
            return (bool)_sbBegunField.GetValue(sb);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>裁剪没关掉就补一刀，免得把 SpriteBatch 的状态丢给游戏本体。</summary>
    public static void SafeEndClip()
    {
        try { if (UIRenderer.IsClipping) UIRenderer.EndClip(); } catch { }
    }

    private static int _lastBatchWarnTick;

    /// <summary>
    /// 每帧最开始（原版 DoDraw 的最前面）看一眼 SpriteBatch 有没有漏掉 End()。
    /// 正常永远是「已经结束」；如果这里已经是「还没结束」，说明上一帧画了画不出来的东西，
    /// 游戏下一步就会崩，所以先把最后画的那张贴图记进日志。
    /// </summary>
    public static void CheckFrameStart()
    {
        try
        {
            if (!BatchBegun()) return;
            if (Environment.TickCount - _lastBatchWarnTick < 2000) return;
            _lastBatchWarnTick = Environment.TickCount;
            BossChecklistMod.Log?.Warn("[渲染状态] 上一帧的 SpriteBatch 没有正常收尾（Begin 少了配对的 End）。最后绘制的贴图：" + LastDrawn);
        }
        catch
        {
        }
    }

    // ---------------- 原版鼠标提示 ----------------

    private static readonly Item TipItem = new Item();

    /// <summary>和鼠标停在物品栏里一件物品上完全一样的提示（含属性行和稀有度配色）。</summary>
    public static void ItemTip(int type, int stack = 1)
    {
        if (type <= 0) return;
        try
        {
            TipItem.SetDefaults(type);
            TipItem.stack = Math.Max(1, stack);
            TipItem.tooltipContext = -1;
            TipItem.tooltipSlot = -1;
            Main.HoverItem = TipItem;
            Main.mouseText = true;
            int rare = 0;
            try { rare = TipItem.rare; } catch { }
            Main.instance.MouseText(GameRefs.ItemName(type), rare);
        }
        catch
        {
        }
    }

    /// <summary>纯文字提示，走原版画 NPC 名字那一套（同样的底板和字体）。</summary>
    public static void TextTip(string title, string desc = null)
    {
        if (string.IsNullOrEmpty(title)) return;
        try
        {
            Main.ClearHoverItem();
            Main.mouseText = true;
            Main.instance.MouseTextHackZoom(string.IsNullOrEmpty(desc) ? title : title + "\n" + desc);
        }
        catch
        {
        }
    }

    // ---------------- 物品图标 ----------------

    public static void ItemIcon(Rectangle r, int type, int stack = 1, float fitScale = 0.75f)
    {
        if (type <= 0 || r.Width <= 0 || r.Height <= 0) return;
        Texture2D tex = GameRefs.Item(type);
        if (!GameRefs.Alive(tex)) tex = null;
        if (tex != null && Main.spriteBatch != null)
        {
            try
            {
                Rectangle src = GameRefs.ItemFrame(type, tex);
                NoteDraw(tex, r);
                float fit = 1f;
                float limit = Math.Min(r.Width, r.Height);
                if (src.Width > limit || src.Height > limit)
                    fit = limit / Math.Max(src.Width, src.Height);
                fit *= fitScale;
                Vector2 pos = new Vector2(r.X + r.Width / 2f, r.Y + r.Height / 2f);
                Main.spriteBatch.Draw(tex, pos, src, Color.White, 0f,
                    new Vector2(src.Width / 2f, src.Height / 2f), fit, SpriteEffects.None, 0f);
            }
            catch
            {
            }
        }
        if (stack > 1)
        {
            string s = stack.ToString();
            Text(s, r.Right - TextW(s, 0.7f) - 2, r.Bottom - (int)(LineH() * 0.7f), Color.White, 0.7f);
        }
    }

    // ---------------- 富文本（[i:123] 物品图标 / [c/FF0000:彩色]） ----------------

    private struct Atom
    {
        public string Text;
        public Color Color;
        public int Item;
        public int W;
    }

    private static bool IsWide(char ch)
    {
        return ch >= 0x1100 && (
            ch <= 0x115F ||
            ch == 0x2329 || ch == 0x232A ||
            (ch >= 0x2E80 && ch <= 0xA4CF && ch != 0x303F) ||
            (ch >= 0xAC00 && ch <= 0xD7A3) ||
            (ch >= 0xF900 && ch <= 0xFAFF) ||
            (ch >= 0xFE30 && ch <= 0xFE6F) ||
            (ch >= 0xFF00 && ch <= 0xFF60) ||
            (ch >= 0xFFE0 && ch <= 0xFFE6));
    }

    private static bool IsSpaceAtom(Atom a) => a.Item <= 0 && a.Text != null && a.Text.Length > 0 && a.Text[0] == ' ';

    private static void FlushWord(List<Atom> list, StringBuilder word, Color color, float scale)
    {
        if (word.Length == 0) return;
        string s = word.ToString();
        word.Clear();
        list.Add(new Atom { Text = s, Color = color, Item = 0, W = TextW(s, scale) });
    }

    private static void AddTextAtoms(List<Atom> list, string text, Color color, float scale)
    {
        StringBuilder word = new StringBuilder();
        int i = 0;
        while (i < text.Length)
        {
            char ch = text[i];
            if (ch == ' ')
            {
                FlushWord(list, word, color, scale);
                int j = i;
                while (j < text.Length && text[j] == ' ') j++;
                string sp = new string(' ', j - i);
                list.Add(new Atom { Text = sp, Color = color, Item = 0, W = TextW(sp, scale) });
                i = j;
            }
            else if (ch == '\t')
            {
                FlushWord(list, word, color, scale);
                list.Add(new Atom { Text = "    ", Color = color, Item = 0, W = TextW("    ", scale) });
                i++;
            }
            else if (IsWide(ch))
            {
                FlushWord(list, word, color, scale);
                string one = ch.ToString();
                list.Add(new Atom { Text = one, Color = color, Item = 0, W = TextW(one, scale) });
                i++;
            }
            else
            {
                word.Append(ch);
                i++;
            }
        }
        FlushWord(list, word, color, scale);
    }

    private static void ParseAtoms(List<Atom> list, string s, Color color, float scale, int iconSize, int depth)
    {
        if (string.IsNullOrEmpty(s) || depth > 6) return;
        int i = 0;
        int textStart = 0;
        while (i < s.Length)
        {
            if (s[i] != '[')
            {
                i++;
                continue;
            }

            if (i > textStart) AddTextAtoms(list, s.Substring(textStart, i - textStart), color, scale);

            if (i + 2 < s.Length && s[i + 1] == 'i' && s[i + 2] == ':')
            {
                int close = s.IndexOf(']', i + 3);
                if (close > 0)
                {
                    int type;
                    if (int.TryParse(s.Substring(i + 3, close - i - 3), out type) && type > 0)
                    {
                        list.Add(new Atom { Text = null, Color = Color.White, Item = type, W = iconSize });
                        i = close + 1;
                        textStart = i;
                        continue;
                    }
                }
            }
            else if (i + 2 < s.Length && s[i + 1] == 'c' && s[i + 2] == '/')
            {
                int colon = s.IndexOf(':', i + 3);
                int close = colon > 0 ? s.IndexOf(']', colon + 1) : -1;
                if (colon > 0 && close > 0)
                {
                    string hex = s.Substring(i + 3, colon - i - 3);
                    string inner = s.Substring(colon + 1, close - colon - 1);
                    AddTextAtoms(list, inner, TryHex(hex, color), scale);
                    i = close + 1;
                    textStart = i;
                    continue;
                }
            }

            i++;
            textStart = i;
        }
        if (textStart < s.Length) AddTextAtoms(list, s.Substring(textStart), color, scale);
    }

    public static Color TryHex(string hex, Color fallback)
    {
        if (string.IsNullOrEmpty(hex)) return fallback;
        try
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 3)
            {
                byte r3 = Convert.ToByte(new string(hex[0], 2), 16);
                byte g3 = Convert.ToByte(new string(hex[1], 2), 16);
                byte b3 = Convert.ToByte(new string(hex[2], 2), 16);
                return new Color(r3, g3, b3);
            }
            if (hex.Length >= 6)
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                return new Color(r, g, b);
            }
        }
        catch
        {
        }
        return fallback;
    }

    private static List<List<Atom>> Layout(string raw, int maxW, float scale, Color baseColor, out int iconSize, out int lineHeight, out int lineSpacing)
    {
        iconSize = Math.Max(8, (int)(LineH() * scale));
        lineHeight = LineH();
        lineSpacing = (int)(lineHeight * scale) + Math.Max(1, (int)(2 * scale));

        List<List<Atom>> lines = new List<List<Atom>>();
        if (string.IsNullOrEmpty(raw)) return lines;

        string[] paragraphs = raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int p = 0; p < paragraphs.Length; p++)
        {
            List<Atom> atoms = new List<Atom>();
            ParseAtoms(atoms, paragraphs[p], baseColor, scale, iconSize, 0);

            List<Atom> line = new List<Atom>();
            int width = 0;
            for (int k = 0; k < atoms.Count; k++)
            {
                Atom a = atoms[k];
                if (maxW > 0 && line.Count > 0 && width + a.W > maxW && !IsSpaceAtom(a))
                {
                    TrimEnd(line, ref width);
                    lines.Add(line);
                    line = new List<Atom>();
                    width = 0;
                }
                if (line.Count == 0 && IsSpaceAtom(a)) continue;
                line.Add(a);
                width += a.W;
            }
            TrimEnd(line, ref width);
            lines.Add(line);
        }
        return lines;
    }

    private static void TrimEnd(List<Atom> line, ref int width)
    {
        while (line.Count > 0 && IsSpaceAtom(line[line.Count - 1]))
        {
            width -= line[line.Count - 1].W;
            line.RemoveAt(line.Count - 1);
        }
    }

    /// <summary>画一段富文本，自动换行。返回占用的总高度。</summary>
    public static int DrawMarkup(string raw, int x, int y, int maxW, float scale, Color color)
    {
        if (string.IsNullOrEmpty(raw)) return 0;
        int iconSize, lineHeight, lineSpacing;
        List<List<Atom>> lines = Layout(raw, maxW, scale, color, out iconSize, out lineHeight, out lineSpacing);

        int cy = y;
        for (int li = 0; li < lines.Count; li++)
        {
            List<Atom> line = lines[li];
            int cx = x;
            for (int ai = 0; ai < line.Count; ai++)
            {
                Atom a = line[ai];
                if (a.Item > 0)
                {
                    int box = iconSize;
                    ItemIcon(new Rectangle(cx, cy + (int)(lineHeight * scale - box) / 2, box, box), a.Item, 1, 0.9f);
                }
                else
                {
                    Text(a.Text, cx, cy, a.Color, scale);
                }
                cx += a.W;
            }
            cy += lineSpacing;
        }
        return lines.Count * lineSpacing;
    }

    /// <summary>只算高度，不画。</summary>
    public static int MeasureMarkup(string raw, int maxW, float scale)
    {
        if (string.IsNullOrEmpty(raw)) return 0;
        int iconSize, lineHeight, lineSpacing;
        List<List<Atom>> lines = Layout(raw, maxW, scale, Color.White, out iconSize, out lineHeight, out lineSpacing);
        return lines.Count * lineSpacing;
    }
}