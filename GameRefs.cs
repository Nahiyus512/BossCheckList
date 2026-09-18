using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Terraria;
using Terraria.ID;
using Terraria.DataStructures;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace BossChecklist;

/// <summary>
/// 贴图与文字的取用入口。
///
/// - 原版贴图（TextureAssets.Item/Npc/...）在 ReLogic 的 Asset&lt;Texture2D&gt; 里，运行期不能直接引用该类型，只能反射
/// - 原版其它路径的贴图（Images/UI/PanelBackground、Images/NPC_Head_Boss_5 之类）走 Main.Assets，同样反射
/// - 模组自带贴图是 assets\*.rawimg（12 字节头 + RGBA），嵌进 DLL 后自己建 Texture2D
/// </summary>
internal static class GameRefs
{
    // ---------------- 原版 TextureAssets ----------------

    private static bool _init;
    private static PropertyInfo _valueProp;
    private static FieldInfo _fItem, _fNpc, _fNpcHead, _fNpcHeadBoss;
    private static readonly FieldInfo[] _fBack = new FieldInfo[32];

    private static readonly Dictionary<int, Texture2D> _itemCache = new Dictionary<int, Texture2D>();
    private static readonly Dictionary<int, Texture2D> _npcCache = new Dictionary<int, Texture2D>();
    private static readonly Dictionary<int, Texture2D> _headCache = new Dictionary<int, Texture2D>();
    private static readonly Dictionary<int, Texture2D> _backCache = new Dictionary<int, Texture2D>();
    private static readonly Dictionary<string, Texture2D> _vanillaCache = new Dictionary<string, Texture2D>();

    // ---------------- 贴图缓存与显卡设备 ----------------

    /// <summary>建这些贴图时用的是哪个显卡设备。</summary>
    private static GraphicsDevice _cacheDevice;

    /// <summary>
    /// 换过显示设备（改分辨率、切窗口、驱动重启）之后，之前建的贴图就全都作废了，
    /// 还拿旧贴图画会让游戏在刷新画面的时候直接崩掉，所以这里发现设备变了就把贴图缓存清空重建。
    /// </summary>
    private static void SyncDevice()
    {
        try
        {
            GraphicsDevice dev = Main.graphics != null ? Main.graphics.GraphicsDevice : null;
            if (dev == null || ReferenceEquals(dev, _cacheDevice)) return;
            bool firstTime = _cacheDevice == null;
            _cacheDevice = dev;
            if (firstTime) return;

            _itemCache.Clear();
            _npcCache.Clear();
            _headCache.Clear();
            _backCache.Clear();
            _modTexCache.Clear();
            _vanillaCache.Clear();
            _assetInit = false;
            _assetRepo = null;
            _assetRequest = null;
            _assetRequestMode = null;
        }
        catch
        {
        }
    }

    /// <summary>这张贴图还能不能用（设备换过、或者被提前释放过就不能用了）。</summary>
    public static bool Alive(Texture2D tex)
    {
        if (tex == null) return false;
        try { return !tex.IsDisposed; } catch { return false; }
    }

    private static void Init()
    {
        if (_init) return;
        _init = true;
        try
        {
            Type t = typeof(Main).Assembly.GetType("Terraria.GameContent.TextureAssets");
            if (t == null) return;
            const BindingFlags F = BindingFlags.Public | BindingFlags.Static;
            _fItem = t.GetField("Item", F);
            _fNpc = t.GetField("Npc", F);
            _fNpcHead = t.GetField("NpcHead", F);
            _fNpcHeadBoss = t.GetField("NpcHeadBoss", F);
            for (int i = 1; i < _fBack.Length; i++) _fBack[i] = t.GetField("InventoryBack" + i, F);
            _valueProp = ProbeValue(_fItem) ?? ProbeValue(_fNpc) ?? ProbeValue(_fNpcHead);
            if (_valueProp == null)
            {
                // Item 数组里可能还没有东西，换个一定有的字段来探
                _valueProp = ProbeValue(t.GetField("MagicPixel", F));
            }
        }
        catch
        {
        }
    }

    private static PropertyInfo ProbeValue(FieldInfo f)
    {
        try
        {
            object v = f?.GetValue(null);
            if (v == null) return null;
            if (v is Array a)
            {
                for (int i = 0; i < a.Length && i < 8; i++)
                {
                    object e = a.GetValue(i);
                    if (e != null) return e.GetType().GetProperty("Value");
                }
                return null;
            }
            return v.GetType().GetProperty("Value");
        }
        catch
        {
            return null;
        }
    }

    private static Texture2D Fetch(FieldInfo f, int index, Action load)
    {
        Init();
        if (f == null || _valueProp == null) return null;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                object asset = null;
                object v = f.GetValue(null);
                if (v is Array a)
                {
                    if (index < 0 || index >= a.Length) return null;
                    asset = a.GetValue(index);
                }
                else
                {
                    asset = v;
                }
                if (asset != null && _valueProp.GetValue(asset) is Texture2D tex) return tex;
            }
            catch
            {
            }
            if (attempt == 0 && load != null)
            {
                try { load(); } catch { }
            }
        }
        return null;
    }

    public static Texture2D Item(int type)
    {
        if (type <= 0) return null;
        SyncDevice();
        if (_itemCache.TryGetValue(type, out Texture2D cached) && Alive(cached)) return cached;
        _itemCache.Remove(type);
        Texture2D tex = Fetch(_fItem, type, () => { if (Main.instance != null) Main.instance.LoadItem(type); });
        if (tex != null) _itemCache[type] = tex;
        return tex;
    }

    public static Texture2D Npc(int type)
    {
        if (type <= 0) return null;
        SyncDevice();
        if (_npcCache.TryGetValue(type, out Texture2D cached) && Alive(cached)) return cached;
        _npcCache.Remove(type);
        Texture2D tex = Fetch(_fNpc, type, () => { if (Main.instance != null) Main.instance.LoadNPC(type); });
        if (tex != null) _npcCache[type] = tex;
        return tex;
    }

    /// <summary>小怪头像（TextureAssets.NpcHead），索引就是 NPCID.Sets.NPCHeadTextures 之类里的编号。</summary>
    public static Texture2D NpcHead(int index)
    {
        if (index < 0) return null;
        SyncDevice();
        if (_headCache.TryGetValue(index, out Texture2D cached) && Alive(cached)) return cached;
        _headCache.Remove(index);
        Texture2D tex = Fetch(_fNpcHead, index, null);
        if (tex != null) _headCache[index] = tex;
        return tex;
    }

    /// <summary>Boss 头图（TextureAssets.NpcHeadBoss），index 是 NPCID.Sets.BossHeadTextures 的值。</summary>
    public static Texture2D NpcHeadBoss(int index)
    {
        if (index < 0) return null;
        SyncDevice();
        if (_headCache.TryGetValue(~index, out Texture2D cached) && Alive(cached)) return cached;
        _headCache.Remove(~index);
        Texture2D tex = Fetch(_fNpcHeadBoss, index, null);
        if (tex != null) _headCache[~index] = tex;
        return tex;
    }

    /// <summary>背包格子底板：n = 1..31 对应 InventoryBack{n}，0 就是 InventoryBack。</summary>
    public static Texture2D Back(int n)
    {
        if (n <= 0) return Fetch(Field("InventoryBack"), 0, null);
        if (n >= _fBack.Length) n = 9;
        SyncDevice();
        if (_backCache.TryGetValue(n, out Texture2D cached) && Alive(cached)) return cached;
        _backCache.Remove(n);
        Init();
        FieldInfo f = _fBack[n] ?? Field("InventoryBack" + n);
        Texture2D tex = f == null ? null : Fetch(f, 0, null);
        if (tex != null) _backCache[n] = tex;
        return tex;
    }

    private static FieldInfo Field(string name)
    {
        try
        {
            Type t = typeof(Main).Assembly.GetType("Terraria.GameContent.TextureAssets");
            return t?.GetField(name, BindingFlags.Public | BindingFlags.Static);
        }
        catch
        {
            return null;
        }
    }

    // ---------------- 原版任意路径贴图（Main.Assets） ----------------

    private static bool _assetInit;
    private static object _assetRepo;
    private static MethodInfo _assetRequest;
    private static Type _assetRequestMode;

    private static void InitAssetRepo()
    {
        if (_assetInit) return;
        _assetInit = true;
        try
        {
            const BindingFlags F = BindingFlags.Public | BindingFlags.Static;
            PropertyInfo p = typeof(Main).GetProperty("Assets", F);
            if (p != null) _assetRepo = p.GetValue(null);
            if (_assetRepo == null)
            {
                FieldInfo f = typeof(Main).GetField("Assets", F);
                if (f != null) _assetRepo = f.GetValue(null);
            }
            if (_assetRepo == null) return;

            Type repoType = _assetRepo.GetType();
            Type iface = repoType.GetInterface("ReLogic.Content.IAssetRepository");
            MethodInfo m = FindRequest(repoType) ?? (iface != null ? FindRequest(iface) : null);
            if (m != null && m.IsGenericMethodDefinition && m.GetParameters().Length == 2)
            {
                _assetRequestMode = m.GetParameters()[1].ParameterType;
                _assetRequest = m;
            }
        }
        catch
        {
        }
    }

    private static MethodInfo FindRequest(Type t)
    {
        try
        {
            MethodInfo[] ms = t.GetMethods();
            for (int i = 0; i < ms.Length; i++)
            {
                if (ms[i].Name != "Request" || !ms[i].IsGenericMethodDefinition) continue;
                ParameterInfo[] ps = ms[i].GetParameters();
                if (ps.Length == 2 && ps[0].ParameterType == typeof(string)) return ms[i];
            }
        }
        catch
        {
        }
        return null;
    }

    /// <summary>
    /// 取原版贴图，path 是不带扩展名的资源路径，例如 "Images/UI/PanelBackground"、"Images/Extra_9"、
    /// "Images/NPC_Head_Boss_5"。取不到返回 null（调用处自己退回别的画法）。
    /// </summary>
    public static Texture2D Vanilla(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        SyncDevice();
        if (_vanillaCache.TryGetValue(path, out Texture2D cached) && Alive(cached)) return cached;
        _vanillaCache.Remove(path);
        Texture2D tex = null;
        try
        {
            InitAssetRepo();
            if (_assetRequest != null)
            {
                object mode = null;
                if (_assetRequestMode != null)
                {
                    try { mode = Enum.Parse(_assetRequestMode, "ImmediateLoad"); }
                    catch { mode = Enum.ToObject(_assetRequestMode, 0); }
                }
                MethodInfo call = _assetRequest.MakeGenericMethod(typeof(Texture2D));
                object asset = mode != null ? call.Invoke(_assetRepo, new object[] { path, mode }) : call.Invoke(_assetRepo, new object[] { path });
                if (asset != null)
                {
                    PropertyInfo vp = asset.GetType().GetProperty("Value");
                    tex = vp?.GetValue(asset) as Texture2D;
                }
            }
        }
        catch
        {
            tex = null;
        }
        if (tex != null) _vanillaCache[path] = tex;
        return tex;
    }

    // ---------------- 模组自带贴图（assets\*.rawimg） ----------------

    private static readonly Dictionary<string, Texture2D> _modTexCache = new Dictionary<string, Texture2D>();

    /// <summary>
    /// 取本模组 assets 里的贴图（文件名去掉 .rawimg），例如 "LogUI_Back"、"Checks_Check"、"Boss50"。
    /// rawimg 就是「12 字节头（版本/宽/高）+ 宽*高*4 的 RGBA」，和 tModLoader 里画出来的完全一样。
    /// </summary>
    public static Texture2D ModTex(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        SyncDevice();
        if (_modTexCache.TryGetValue(name, out Texture2D cached) && Alive(cached)) return cached;
        _modTexCache.Remove(name);
        try
        {
            using (Stream s = typeof(GameRefs).Assembly.GetManifestResourceStream("BossChecklist.assets." + name + ".rawimg"))
            {
                if (s == null)
                {
                    _modTexCache[name] = null;
                    return null;
                }
                byte[] data = new byte[s.Length];
                int read = 0;
                while (read < data.Length)
                {
                    int n = s.Read(data, read, data.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                if (read < 12)
                {
                    _modTexCache[name] = null;
                    return null;
                }
                int w = BitConverter.ToInt32(data, 4);
                int h = BitConverter.ToInt32(data, 8);
                int len = w * h * 4;
                if (w <= 0 || h <= 0 || read < 12 + len)
                {
                    _modTexCache[name] = null;
                    return null;
                }
                GraphicsDevice dev = Main.graphics != null ? Main.graphics.GraphicsDevice : null;
                if (dev == null) return null; // 设备还没准备好：这次不缓存，下次再试
                Texture2D tex = new Texture2D(dev, w, h, false, SurfaceFormat.Color);
                tex.Name = "assets/" + name;
                tex.SetData(data, 12, len);
                _modTexCache[name] = tex;
                return tex;
            }
        }
        catch
        {
            _modTexCache[name] = null;
            return null;
        }
    }

    // ---------------- 名称 / 行高 ----------------

    public static string ItemName(int type)
    {
        try { return type > 0 ? Lang.GetItemNameValue(type) : ""; }
        catch { return ""; }
    }

    public static string NpcName(int type)
    {
        try { return type > 0 ? Lang.GetNPCNameValue(type) : ""; }
        catch { return ""; }
    }

    private static int _lineH;

    /// <summary>一行字的高度（跟着玩家的界面字号走）。量不到就先按 20 算。</summary>
    public static int TextLine()
    {
        if (_lineH > 0) return _lineH;
        try
        {
            Type fa = typeof(Main).Assembly.GetType("Terraria.GameContent.FontAssets");
            object asset = fa?.GetField("MouseText", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            object font = asset?.GetType().GetProperty("Value")?.GetValue(asset);
            if (font != null)
            {
                MethodInfo m = font.GetType().GetMethod("MeasureString", BindingFlags.Instance | BindingFlags.Public, null, new Type[] { typeof(string) }, null);
                if (m != null && m.Invoke(font, new object[] { "Ay" }) is Vector2 v && v.Y > 0f)
                    _lineH = (int)Math.Ceiling(v.Y);
            }
        }
        catch
        {
        }
        return _lineH > 0 ? _lineH : 20;
    }

    public static float TextWidth(string text)
    {
        try
        {
            Type fa = typeof(Main).Assembly.GetType("Terraria.GameContent.FontAssets");
            object asset = fa?.GetField("MouseText", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            object font = asset?.GetType().GetProperty("Value")?.GetValue(asset);
            if (font != null)
            {
                MethodInfo m = font.GetType().GetMethod("MeasureString", BindingFlags.Instance | BindingFlags.Public, null, new Type[] { typeof(string) }, null);
                if (m != null && m.Invoke(font, new object[] { text ?? "" }) is Vector2 v) return v.X;
            }
        }
        catch
        {
        }
        return (text?.Length ?? 0) * 8f;
    }

    /// <summary>物品贴图里的第一帧（有动画的物品只用第一帧）。</summary>
    public static Rectangle ItemFrame(int type, Texture2D tex)
    {
        try
        {
            DrawAnimation[] anims = Main.itemAnimations;
            if (anims != null && type > 0 && type < anims.Length && anims[type] != null)
            {
                try { anims[type].FrameCounter = 0; } catch { }
                Rectangle f = anims[type].GetFrame(tex, -1);
                if (f.Width > 0 && f.Height > 0 && f.X >= 0 && f.Y >= 0 && f.Right <= tex.Width && f.Bottom <= tex.Height) return f;
            }
        }
        catch
        {
        }
        return new Rectangle(0, 0, tex.Width, tex.Height);
    }

    /// <summary>NPC 贴图的第一帧（Boss 图集都是纵向多帧）。</summary>
    public static Rectangle NpcFrame(int type, Texture2D tex)
    {
        try
        {
            int frames = Main.npcFrameCount[type];
            if (frames > 1) return new Rectangle(0, 0, tex.Width, tex.Height / frames);
        }
        catch
        {
        }
        return new Rectangle(0, 0, tex.Width, tex.Height);
    }
}
