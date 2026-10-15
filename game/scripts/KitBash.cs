using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 自前のキットバッシュ生成器。箱・円柱・配管・発光帯などの基本部品を PBR 素材(Poly Haven CC0, 三平面投影)で組み、
/// 設備・建物・人型をレシピとして生成する。角は立て、細部を密集させ、素材感で硬さを出す(ハード SF)。
/// </summary>
public sealed partial class KitBash
{
    readonly Random _rnd;
    public readonly StandardMaterial3D Steel, Rust, Paint, PaintDark, Concrete, Floor, Grate, Rubber, Hazard, GlowCyan, GlowOrange, GlowRed, GlowWhite, Glass, Foliage, Trunk;
    public readonly StandardMaterial3D WinWarm, WinCool, WinMixed, WinDark;   // 窓の格子(住居 = 暖色、商業 = 水色、混在、消灯)
    public readonly StandardMaterial3D GlowGreen;
    public const float Floor_ = 0.9f;

    public KitBash(int seed)
    {
        _rnd = new Random(seed);
        Steel = Pbr("metal_plate", 1.0f, new Color(0.85f, 0.88f, 0.94f), 1.0f, 0.85f);
        Rust = Pbr("rusty_metal_02", 1.0f, new Color(0.95f, 0.88f, 0.82f), 1.0f, 0.3f);
        Paint = Pbr("painted_metal_shutter", 1.2f, new Color(0.62f, 0.70f, 0.82f), 1.0f, 0.4f);
        PaintDark = Pbr("painted_metal_shutter", 1.2f, new Color(0.36f, 0.39f, 0.45f), 1.0f, 0.3f);
        Concrete = Pbr("concrete_wall_008", 0.5f, new Color(0.8f, 0.8f, 0.8f), 1.0f, 0.0f);
        Floor = Pbr("concrete_floor_painted", 0.5f, new Color(0.75f, 0.76f, 0.78f), 1.0f, 0.0f);
        Grate = Pbr("rusty_metal_grid", 1.5f, new Color(0.9f, 0.9f, 0.9f), 1.0f, 0.5f);
        Rubber = new StandardMaterial3D { AlbedoColor = new Color(0.16f, 0.16f, 0.18f), Roughness = 0.95f };
        Hazard = HazardStripes();
        GlowCyan = Glow(new Color(0.25f, 0.85f, 1.0f), 4f);
        GlowOrange = Glow(new Color(1.0f, 0.45f, 0.1f), 5f);
        GlowRed = Glow(new Color(1.0f, 0.15f, 0.1f), 4f);
        GlowWhite = Glow(new Color(0.9f, 0.95f, 1.0f), 3f);
        GlowGreen = Glow(new Color(0.4f, 1.0f, 0.5f), 4f);
        Glass = new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.18f, 0.26f), Roughness = 0.15f, Metallic = 0.6f };
        Foliage = new StandardMaterial3D { AlbedoColor = new Color(0.22f, 0.32f, 0.22f), Roughness = 0.95f };
        Trunk = new StandardMaterial3D { AlbedoColor = new Color(0.16f, 0.12f, 0.09f), Roughness = 0.95f };
        WinWarm = Windows(new Color(1.0f, 0.82f, 0.55f), 0.55f, 101);
        WinCool = Windows(new Color(0.55f, 0.85f, 1.0f), 0.5f, 102);
        WinMixed = Windows(new Color(0.95f, 0.9f, 0.8f), 0.45f, 103, new Color(0.45f, 0.8f, 1.0f));
        WinDark = Windows(new Color(0.9f, 0.85f, 0.7f), 0.12f, 104);
    }

    /// <summary>
    /// 窓の格子: 1 タイル = 1 階(0.9m) × 2 枚のガラス。8 × 8 タイル(7.2m 周期)で点灯を乱数化し、三平面投影で世界座標に貼る。
    /// アルベドは枠(暗い鋼)と暗いガラス、発光は点灯した窓だけ。
    /// </summary>
    static StandardMaterial3D Windows(Color lit, float litRate, int seed, Color? lit2 = null)
    {
        const int tile = 64, tiles = 8, size = tile * tiles;
        var r = new Random(seed);
        var albedo = Image.CreateEmpty(size, size, true, Image.Format.Rgb8);
        var emit = Image.CreateEmpty(size, size, true, Image.Format.Rgb8);
        var frame = new Color(0.10f, 0.11f, 0.13f);
        var spandrel = new Color(0.20f, 0.22f, 0.26f);
        for (int ty = 0; ty < tiles; ty++)
            for (int tx = 0; tx < tiles; tx++)
                for (int pane = 0; pane < 2; pane++)
                {
                    bool isLit = r.NextDouble() < litRate;
                    var c = lit2 != null && r.NextDouble() < 0.3 ? lit2.Value : lit;
                    float dim = 0.6f + (float)r.NextDouble() * 0.4f;
                    var glassCol = new Color(0.07f, 0.10f, 0.15f) * (0.8f + (float)r.NextDouble() * 0.4f);
                    for (int y = 0; y < tile; y++)
                        for (int x = 0; x < tile / 2; x++)
                        {
                            int px = tx * tile + pane * (tile / 2) + x, py = ty * tile + y;
                            bool isFrame = x < 3 || x >= tile / 2 - 3 || y < 4 || y >= tile - 4;
                            bool isSpandrel = y >= tile - 18 && !isFrame;      // 階の下部は腰壁
                            Color a = isFrame ? frame : isSpandrel ? spandrel : isLit ? c * (0.55f * dim) : glassCol;
                            Color e = (!isFrame && !isSpandrel && isLit) ? c * dim : new Color(0, 0, 0);
                            if (!isFrame && !isSpandrel && y % 20 == 10) { a = frame; e = new Color(0, 0, 0); }   // 中桟
                            albedo.SetPixel(px, py, a);
                            emit.SetPixel(px, py, e);
                        }
                }
        albedo.GenerateMipmaps(); emit.GenerateMipmaps();
        float k = 1f / (Floor_ * tiles);
        return new StandardMaterial3D
        {
            AlbedoTexture = ImageTexture.CreateFromImage(albedo),
            EmissionEnabled = true, EmissionTexture = ImageTexture.CreateFromImage(emit), EmissionEnergyMultiplier = 2.2f,
            Roughness = 0.25f, Metallic = 0.4f,
            Uv1Triplanar = true, Uv1WorldTriplanar = true, Uv1Scale = new Vector3(k, k, k), Uv1TriplanarSharpness = 8f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
    }

    static StandardMaterial3D Pbr(string asset, float scale, Color tint, float roughMul, float metallic)
    {
        string dir = ProjectSettings.GlobalizePath($"res://assets/pbr/{asset}/");
        var m = new StandardMaterial3D
        {
            AlbedoColor = tint,
            AlbedoTexture = Tex(dir + "diffuse.jpg"),
            NormalEnabled = true,
            NormalTexture = Tex(dir + "normal.jpg"),
            NormalScale = 1.0f,
            RoughnessTexture = Tex(dir + "rough.jpg"),
            Roughness = roughMul,
            Metallic = metallic,
            MetallicSpecular = 0.5f,
            Uv1Triplanar = true, Uv1WorldTriplanar = true,
            Uv1Scale = new Vector3(scale, scale, scale),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
        return m;
    }

    static ImageTexture Tex(string path)
    {
        var img = Image.LoadFromFile(path);
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    static StandardMaterial3D Glow(Color c, float energy) => new()
    {
        AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = energy, Roughness = 0.4f,
    };

    static StandardMaterial3D HazardStripes()
    {
        var img = Image.CreateEmpty(64, 64, false, Image.Format.Rgb8);
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
                img.SetPixel(x, y, ((x + y) / 8) % 2 == 0 ? new Color(0.85f, 0.65f, 0.1f) : new Color(0.08f, 0.08f, 0.08f));
        img.GenerateMipmaps();
        return new StandardMaterial3D { AlbedoTexture = ImageTexture.CreateFromImage(img), Uv1Triplanar = true, Uv1WorldTriplanar = true, Uv1Scale = new Vector3(1, 1, 1), Roughness = 0.7f };
    }

    // ---------- 基本部品 ----------
    // ---------- 一括描画: 同じ材質の箱は 1 つの MultiMesh にまとめる(窓や庇が数万個になるため) ----------
    readonly Dictionary<Material, List<Transform3D>> _batch = new();
    readonly ArrayMesh _unit = MeshParts.ChamferBox(0.035f);

    public void Box(Node3D p, Vector3 center, Vector3 size, Material m, Vector3? rotDeg = null)
    {
        var basis = rotDeg == null ? Basis.Identity : Basis.FromEuler(rotDeg.Value * (Mathf.Pi / 180f));
        basis = new Basis(basis.X * size.X, basis.Y * size.Y, basis.Z * size.Z);
        var global = p.GlobalTransform * new Transform3D(basis, center);
        if (!_batch.TryGetValue(m, out var list)) _batch[m] = list = new List<Transform3D>();
        list.Add(global);
    }

    /// <summary>溜めた箱を MultiMesh として親に追加する。シーン構築の最後に 1 回呼ぶ。</summary>
    public void Flush(Node3D parent)
    {
        foreach (var (m, list) in _batch)
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = _unit, InstanceCount = list.Count };
            for (int i = 0; i < list.Count; i++) mm.SetInstanceTransform(i, list[i]);
            parent.AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = m });
        }
        _batch.Clear();
    }

    public MeshInstance3D Cyl(Node3D p, Vector3 center, float r, float h, Material m, Vector3? rotDeg = null, float topR = -1, int seg = 24)
    {
        var mi = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = topR < 0 ? r : topR, BottomRadius = r, Height = h, RadialSegments = seg, Rings = 1 }, MaterialOverride = m, Position = center };
        if (rotDeg != null) mi.RotationDegrees = rotDeg.Value;
        p.AddChild(mi);
        return mi;
    }

    /// <summary>軸に沿った配管。a と b は同じ軸上にあること。</summary>
    public void Pipe(Node3D p, Vector3 a, Vector3 b, float r, Material m)
    {
        var d = b - a;
        float len = d.Length();
        var c = (a + b) / 2;
        Vector3 rot = Mathf.Abs(d.X) > 0.01f ? new Vector3(0, 0, 90) : Mathf.Abs(d.Z) > 0.01f ? new Vector3(90, 0, 0) : Vector3.Zero;
        Cyl(p, c, r, len, m, rot, seg: 16);
        foreach (var e in new[] { a, b })
        {
            Cyl(p, e, r * 1.45f, r * 1.0f, Steel, rot, seg: 16);
            // ボルト 6 本(フランジの周上)
            var axis = (b - a).Normalized();
            var t1 = Mathf.Abs(axis.Y) > 0.9f ? Vector3.Right : Vector3.Up;
            var t2 = axis.Cross(t1).Normalized(); t1 = t2.Cross(axis).Normalized();
            for (int i = 0; i < 6; i++)
            {
                float ang = i * Mathf.Tau / 6;
                Box(p, e + (t1 * Mathf.Cos(ang) + t2 * Mathf.Sin(ang)) * r * 1.2f, new Vector3(r * 0.28f, r * 0.28f, r * 0.28f), Rubber);
            }
        }
    }

    /// <summary>面の上に小さな箱を散らす(グリーブル)。normal は面の向き(軸単位ベクトル)。</summary>
    public void Greeble(Node3D p, Vector3 faceCenter, Vector3 faceSize, Vector3 normal, int count, Material? m = null)
    {
        m ??= Steel;
        var t1 = normal.Y != 0 ? Vector3.Right : Vector3.Up;
        var t2 = normal.Cross(t1);
        for (int i = 0; i < count; i++)
        {
            float u = (float)(_rnd.NextDouble() - 0.5), v = (float)(_rnd.NextDouble() - 0.5);
            float w = 0.06f + (float)_rnd.NextDouble() * 0.16f, h = 0.04f + (float)_rnd.NextDouble() * 0.12f, dep = 0.03f + (float)_rnd.NextDouble() * 0.06f;
            var pos = faceCenter + t1 * (u * (faceSize.X - w)) + t2 * (v * (faceSize.Y - h)) + normal * dep / 2;
            var size = t1 * w + t2 * h + normal * dep;
            Box(p, pos, new Vector3(Mathf.Abs(size.X), Mathf.Abs(size.Y), Mathf.Abs(size.Z)), _rnd.NextDouble() < 0.2 ? Rust : m);
        }
    }

    public void Strip(Node3D p, Vector3 center, Vector3 size, Material glow, Vector3? rotDeg = null) => Box(p, center, size, glow, rotDeg);

    public void Light(Node3D p, Vector3 pos, Color c, float range = 4f, float energy = 2f)
        => p.AddChild(new OmniLight3D { Position = pos, LightColor = c, LightEnergy = energy, OmniRange = range, ShadowEnabled = false });

    Node3D Root(Node3D parent, float x, float y, float z, float rot)
    {
        var n = new Node3D { Position = new Vector3(x, y, z), RotationDegrees = new Vector3(0, rot, 0) };
        parent.AddChild(n);
        return n;
    }

    float R(float a, float b) => a + (float)_rnd.NextDouble() * (b - a);

    // ---------- 設備(1 セル。原点はセルの中心、床面) ----------
    public Node3D Furnace(Node3D parent, float x, float y, float z, float rot = 0)
    {
        var n = Root(parent, x, y, z, rot);
        Box(n, new Vector3(0, 0.08f, 0), new Vector3(1.0f, 0.16f, 1.0f), Concrete);
        Box(n, new Vector3(0, 0.1f, 0.47f), new Vector3(0.96f, 0.06f, 0.06f), Hazard);
        Box(n, new Vector3(0, 0.55f, 0), new Vector3(0.82f, 0.78f, 0.82f), Rust);
        Box(n, new Vector3(0, 0.96f, 0), new Vector3(0.9f, 0.06f, 0.9f), Steel);           // 天板の縁
        Box(n, new Vector3(0, 0.42f, 0.38f), new Vector3(0.42f, 0.3f, 0.12f), Rubber);      // 火口
        Strip(n, new Vector3(0, 0.40f, 0.41f), new Vector3(0.34f, 0.2f, 0.02f), GlowOrange);
        Light(n, new Vector3(0, 0.45f, 0.7f), new Color(1.0f, 0.5f, 0.15f), 3f, 1.5f);
        Cyl(n, new Vector3(0.26f, 1.45f, -0.22f), 0.11f, 1.0f, Rust, seg: 10);                // 煙突
        Cyl(n, new Vector3(0.26f, 1.1f, -0.22f), 0.14f, 0.08f, Steel, seg: 10);
        Cyl(n, new Vector3(0.26f, 1.9f, -0.22f), 0.13f, 0.06f, Steel, seg: 10);
        Pipe(n, new Vector3(-0.45f, 0.7f, -0.2f), new Vector3(-0.45f, 0.7f, 0.3f), 0.05f, Steel);
        Pipe(n, new Vector3(-0.45f, 0.3f, 0.3f), new Vector3(-0.45f, 0.7f, 0.3f), 0.05f, Steel);
        Greeble(n, new Vector3(0, 0.55f, -0.41f), new Vector3(0.7f, 0.6f, 0), Vector3.Back * -1, 6);
        Greeble(n, new Vector3(0.41f, 0.55f, 0), new Vector3(0.6f, 0.6f, 0), Vector3.Right, 5);
        return n;
    }

    public Node3D Assembler(Node3D parent, float x, float y, float z, float rot = 0)
    {
        var n = Root(parent, x, y, z, rot);
        Box(n, new Vector3(0, 0.06f, 0), new Vector3(1.0f, 0.12f, 1.0f), Floor);
        Box(n, new Vector3(0, 0.42f, 0), new Vector3(0.9f, 0.6f, 0.9f), Paint);
        for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2)
            Box(n, new Vector3(sx * 0.42f, 0.5f, sz * 0.42f), new Vector3(0.08f, 0.9f, 0.08f), Steel);
        Box(n, new Vector3(0, 0.78f, 0), new Vector3(0.94f, 0.06f, 0.94f), Steel);
        Cyl(n, new Vector3(0, 0.95f, 0), 0.3f, 0.3f, Steel, seg: 16);
        Cyl(n, new Vector3(0, 1.14f, 0), 0.18f, 0.12f, Rust, seg: 16);
        Strip(n, new Vector3(0, 0.95f, 0), new Vector3(0.64f, 0.04f, 0.64f), GlowCyan);
        Strip(n, new Vector3(0, 0.3f, 0.46f), new Vector3(0.5f, 0.05f, 0.02f), GlowCyan);
        Light(n, new Vector3(0, 1.2f, 0), new Color(0.25f, 0.85f, 1.0f), 3f, 1.2f);
        for (int i = 0; i < 5; i++) Box(n, new Vector3(-0.46f, 0.25f + i * 0.09f, 0), new Vector3(0.02f, 0.03f, 0.5f), Rubber);   // 通気口
        Pipe(n, new Vector3(0.46f, 0.5f, -0.3f), new Vector3(0.46f, 0.5f, 0.3f), 0.04f, Rust);
        Cyl(n, new Vector3(0.3f, 1.35f, 0.3f), 0.015f, 0.5f, Steel, seg: 6);
        Greeble(n, new Vector3(0, 0.42f, -0.46f), new Vector3(0.8f, 0.5f, 0), Vector3.Forward, 7);
        return n;
    }

    public Node3D Miner(Node3D parent, float x, float y, float z, float rot = 0)
    {
        var n = Root(parent, x, y, z, rot);
        Box(n, new Vector3(0, 0.12f, 0), new Vector3(1.0f, 0.24f, 1.0f), Concrete);
        Box(n, new Vector3(0, 0.26f, 0), new Vector3(0.8f, 0.05f, 0.8f), Hazard);
        for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2)
            Box(n, new Vector3(sx * 0.36f, 1.0f, sz * 0.36f), new Vector3(0.09f, 1.55f, 0.09f), Steel);
        for (int sx = -1; sx <= 1; sx += 2)
        {
            Box(n, new Vector3(sx * 0.36f, 1.75f, 0), new Vector3(0.08f, 0.08f, 0.8f), Steel);
            Box(n, new Vector3(0, 1.75f, sx * 0.36f), new Vector3(0.8f, 0.08f, 0.08f), Steel);
            Box(n, new Vector3(sx * 0.36f, 1.0f, 0), new Vector3(0.05f, 0.05f, 0.8f), Rust, new Vector3(45, 0, 0));
        }
        Box(n, new Vector3(0, 1.95f, 0), new Vector3(0.5f, 0.32f, 0.5f), Paint);                 // 駆動部
        Cyl(n, new Vector3(0, 1.4f, 0), 0.07f, 0.9f, Steel, seg: 8);                            // 軸
        Cyl(n, new Vector3(0, 0.7f, 0), 0.22f, 0.6f, Rust, topR: 0.05f, seg: 10);              // 刃(逆円錐)
        Cyl(n, new Vector3(0, 0.95f, 0), 0.24f, 0.1f, Steel, seg: 10);
        Pipe(n, new Vector3(0.25f, 2.1f, 0), new Vector3(0.25f, 2.6f, 0), 0.05f, Rust);
        Pipe(n, new Vector3(0.6f, 0.5f, 0.25f), new Vector3(0.6f, 1.6f, 0.25f), 0.05f, Steel);
        Box(n, new Vector3(0.6f, 0.45f, 0.3f), new Vector3(0.3f, 0.25f, 0.4f), Steel);          // 排出口
        Strip(n, new Vector3(0, 2.14f, 0.26f), new Vector3(0.3f, 0.05f, 0.02f), GlowRed);
        Greeble(n, new Vector3(0, 1.95f, -0.26f), new Vector3(0.4f, 0.25f, 0), Vector3.Forward, 4);
        return n;
    }

    public Node3D Belt(Node3D parent, float x, float y, float z, float rot = 0, bool item = false)
    {
        var n = Root(parent, x, y, z, rot);
        Box(n, new Vector3(0, 0.07f, 0.36f), new Vector3(1.0f, 0.14f, 0.08f), Steel);
        Box(n, new Vector3(0, 0.07f, -0.36f), new Vector3(1.0f, 0.14f, 0.08f), Steel);
        Box(n, new Vector3(0, 0.12f, 0), new Vector3(1.0f, 0.04f, 0.64f), Rubber);
        for (int i = -1; i <= 1; i++) Cyl(n, new Vector3(i * 0.33f, 0.06f, 0), 0.05f, 0.7f, Steel, new Vector3(90, 0, 0), seg: 8);
        Box(n, new Vector3(0, 0.145f, 0), new Vector3(0.12f, 0.005f, 0.3f), GlowOrange);
        if (item) Box(n, new Vector3(R(-0.3f, 0.3f), 0.26f, 0), new Vector3(0.24f, 0.24f, 0.24f), Rust, new Vector3(0, R(0, 90), 0));
        return n;
    }

    public Node3D Chest(Node3D parent, float x, float y, float z, float rot = 0)
    {
        var n = Root(parent, x, y, z, rot);
        Box(n, new Vector3(0, 0.3f, 0), new Vector3(0.8f, 0.6f, 0.8f), Paint);
        Box(n, new Vector3(0, 0.62f, 0), new Vector3(0.84f, 0.06f, 0.84f), Steel);
        Box(n, new Vector3(0, 0.3f, 0.41f), new Vector3(0.6f, 0.08f, 0.02f), Hazard);
        for (int sx = -1; sx <= 1; sx += 2) Box(n, new Vector3(sx * 0.41f, 0.3f, 0), new Vector3(0.04f, 0.6f, 0.84f), Steel);
        return n;
    }

    public Node3D Tank(Node3D parent, float x, float y, float z, float r = 0.45f, float h = 1.4f)
    {
        var n = Root(parent, x, y, z, 0);
        Cyl(n, new Vector3(0, h / 2, 0), r, h, Rust, seg: 16);
        Cyl(n, new Vector3(0, h + 0.08f, 0), r * 0.95f, 0.16f, Steel, topR: r * 0.6f, seg: 16);
        for (int i = 0; i < 3; i++) Cyl(n, new Vector3(0, 0.2f + i * (h - 0.4f) / 2, 0), r * 1.03f, 0.06f, Steel, seg: 16);
        Box(n, new Vector3(r * 0.9f, h / 2, 0), new Vector3(0.06f, h * 0.9f, 0.25f), Steel);   // はしご
        for (int i = 0; i < 6; i++) Box(n, new Vector3(r * 0.9f, 0.2f + i * (h - 0.4f) / 5, 0), new Vector3(0.1f, 0.02f, 0.2f), Rubber);
        Pipe(n, new Vector3(-r, 0.3f, 0), new Vector3(-r - 0.5f, 0.3f, 0), 0.06f, Steel);
        return n;
    }

    public Node3D Chimney(Node3D parent, float x, float y, float z, float h = 2.6f, bool fire = true)
    {
        var n = Root(parent, x, y, z, 0);
        Cyl(n, new Vector3(0, h / 2, 0), 0.22f, h, Rust, topR: 0.17f, seg: 12);
        for (int i = 1; i < 4; i++) Cyl(n, new Vector3(0, h * i / 4, 0), 0.24f, 0.06f, Steel, seg: 12);
        Cyl(n, new Vector3(0, h + 0.03f, 0), 0.2f, 0.06f, Steel, seg: 12);
        Box(n, new Vector3(0.2f, h * 0.3f, 0), new Vector3(0.04f, h * 0.55f, 0.2f), Steel);
        if (fire) { Strip(n, new Vector3(0, h + 0.06f, 0), new Vector3(0.26f, 0.02f, 0.26f), GlowOrange); Light(n, new Vector3(0, h + 0.3f, 0), new Color(1.0f, 0.5f, 0.15f), 5f, 2.5f); }
        return n;
    }

    // ---------- 建物 ----------
    // ---------- 建物(SimCity 風の造形: 基壇・店舗階・胴体・段差・屋上機器・冠) ----------
    public enum Style { Podium, Slab, Glass, Tiered, Octagon }
    const float Fh = 0.9f;   // 階高

    public Node3D Tower(Node3D parent, float x, float y, float z, float w, float d, int floors, float rot = 0)
        => Building(parent, x, y, z, w, d, floors, (Style)_rnd.Next(5), rot);

    public Node3D Building(Node3D parent, float x, float y, float z, float w, float d, int floors, Style style, float rot = 0)
    {
        var n = Root(parent, x, y, z, rot);
        var body = _rnd.NextDouble() < 0.5 ? Paint : PaintDark;
        // 基壇と店舗階(発光帯・庇・入口)
        Box(n, new Vector3(0, 0.1f, 0), new Vector3(w + 0.5f, 0.2f, d + 0.5f), Concrete);
        float gh = 1.2f;
        Box(n, new Vector3(0, 0.2f + gh / 2, 0), new Vector3(w, gh, d), Concrete);
        Strip(n, new Vector3(0, 0.2f + gh * 0.55f, d / 2 + 0.01f), new Vector3(w * 0.8f, gh * 0.5f, 0.02f), _rnd.NextDouble() < 0.5 ? GlowWhite : GlowCyan);
        Box(n, new Vector3(0, 0.2f + gh + 0.05f, d / 2 + 0.3f), new Vector3(w * 0.9f, 0.08f, 0.6f), Steel);
        Box(n, new Vector3(0, 0.2f + gh * 0.4f, d / 2 + 0.02f), new Vector3(0.6f, gh * 0.8f, 0.04f), Rubber);
        float y0 = 0.2f + gh;
        int remain = Math.Max(1, floors - 1);
        switch (style)
        {
            case Style.Podium:
            {
                int pf = Math.Clamp(remain / 4, 2, 4);
                float top = Block(n, y0, w, d, pf, body, 0, 0, ledges: true);
                Parapet(n, top, w, d, 0, 0);
                float tw = w * 0.62f, td = d * 0.62f, ox = -w * 0.1f, oz = -d * 0.1f;
                int left = remain - pf;
                while (left > 0)
                {
                    int tier = Math.Min(left, 12);
                    top = Block(n, top, tw, td, tier, body, ox, oz, ledges: false, mullions: _rnd.NextDouble() < 0.5);
                    left -= tier;
                    if (left > 0) { Parapet(n, top, tw, td, ox, oz); tw *= 0.85f; td *= 0.85f; }
                }
                Roof(n, top, tw, td, ox, oz, _rnd.Next(3));
                break;
            }
            case Style.Slab:
            {
                float top = Block(n, y0, w, d, remain, body, 0, 0, ledges: true, balconies: true);
                Roof(n, top, w, d, 0, 0, _rnd.Next(2));
                break;
            }
            case Style.Glass:
            {
                float top = Block(n, y0, w, d, remain, WinCool, 0, 0, ledges: false, mullions: true, windows: false);
                Crown(n, top, w, d, 0, 0);
                Roof(n, top, w, d, 0, 0, 2);
                break;
            }
            case Style.Tiered:
            {
                float top = y0; float tw = w, td = d;
                int[] parts = { remain / 2, remain * 3 / 10, remain - remain / 2 - remain * 3 / 10 };
                for (int i = 0; i < 3; i++)
                {
                    if (parts[i] <= 0) continue;
                    top = Block(n, top, tw, td, parts[i], body, 0, 0, ledges: i == 0);
                    Parapet(n, top, tw, td, 0, 0);
                    if (i < 2) { tw *= 0.76f; td *= 0.76f; }
                }
                Roof(n, top, tw, td, 0, 0, 1);
                break;
            }
            case Style.Octagon:
            {
                float h = remain * Fh, r = Math.Max(w, d) / 2;
                Cyl(n, new Vector3(0, y0 + h / 2, 0), r, h, PickWindows(), seg: 8);
                Cyl(n, new Vector3(0, y0 + h + 0.02f, 0), r + 0.01f, 0.04f, PaintDark, seg: 8);
                for (int f = 0; f < remain; f += 3) Cyl(n, new Vector3(0, y0 + f * Fh + Fh * 0.5f, 0), r + 0.02f, 0.06f, GlowCyan, seg: 8);
                for (int f = 0; f < remain; f++) Cyl(n, new Vector3(0, y0 + f * Fh, 0), r + 0.05f, 0.05f, Steel, seg: 8);
                float top = y0 + h;
                Cyl(n, new Vector3(0, top + 0.15f, 0), r * 0.7f, 0.3f, PaintDark, seg: 8);
                Cyl(n, new Vector3(0, top + 1.2f, 0), 0.03f, 2.0f, Steel, seg: 6);
                Strip(n, new Vector3(0, top + 2.25f, 0), new Vector3(0.12f, 0.12f, 0.12f), GlowRed);
                break;
            }
        }
        // 裏面の配管と機器
        for (int i = 0; i < 2; i++) Pipe(n, new Vector3(-w / 2 - 0.08f, y0 + 0.2f, -d / 2 + 0.3f + i * 0.35f), new Vector3(-w / 2 - 0.08f, y0 + Math.Min(remain, 6) * Fh, -d / 2 + 0.3f + i * 0.35f), 0.05f, Rust);
        Greeble(n, new Vector3(0, y0 + Fh * 1.5f, -d / 2 - 0.02f), new Vector3(w * 0.8f, Fh * 2, 0), Vector3.Forward, 6, Rust);
        FlushBays(n);
        return n;
    }

    /// <summary>胴体の 1 区画: 窓格子の材質を貼った箱 + 角柱 + 任意で庇・縦リブ・ベランダ。上端の y を返す。</summary>
    float Block(Node3D n, float yBase, float w, float d, int floors, Material mat, float ox, float oz, bool ledges = false, bool mullions = false, bool balconies = false, bool windows = true)
    {
        float h = floors * Fh;
        if (windows || ReferenceEquals(mat, WinCool))
        {
            Box(n, new Vector3(ox, yBase + h / 2, oz), new Vector3(w - 0.4f, h, d - 0.4f), PaintDark);   // 壁の芯(ベイの奥。ガラスの奥行き 0.16 より深く)
            bool cool = ReferenceEquals(mat, WinCool) || _rnd.NextDouble() < 0.4;
            Facade(n, yBase, w, d, floors, ox, oz, 0.5f, cool);
            FarBox(n, new Vector3(ox, yBase + h / 2, oz), new Vector3(w, h, d), cool ? WinCool : WinWarm);
        }
        else Box(n, new Vector3(ox, yBase + h / 2, oz), new Vector3(w, h, d), mat);
        Box(n, new Vector3(ox, yBase + h + 0.02f, oz), new Vector3(w + 0.02f, 0.04f, d + 0.02f), PaintDark);   // 屋根板(上面に窓を出さない)
        for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2)
            Box(n, new Vector3(ox + sx * w / 2, yBase + h / 2, oz + sz * d / 2), new Vector3(0.14f, h + 0.05f, 0.14f), Steel);
        if (mullions)
        {
            // 外骨格の縦リブ: 2 列ごと
            int cx = Math.Max(1, (int)(w / 0.9f)), cz = Math.Max(1, (int)(d / 0.9f));
            for (int c = 1; c < cx; c++) { float t = (float)c / cx - 0.5f; Box(n, new Vector3(ox + t * w, yBase + h / 2, oz + d / 2 + 0.03f), new Vector3(0.06f, h, 0.06f), Steel); Box(n, new Vector3(ox + t * w, yBase + h / 2, oz - d / 2 - 0.03f), new Vector3(0.06f, h, 0.06f), Steel); }
            for (int c = 1; c < cz; c++) { float t = (float)c / cz - 0.5f; Box(n, new Vector3(ox + w / 2 + 0.03f, yBase + h / 2, oz + t * d), new Vector3(0.06f, h, 0.06f), Steel); Box(n, new Vector3(ox - w / 2 - 0.03f, yBase + h / 2, oz + t * d), new Vector3(0.06f, h, 0.06f), Steel); }
        }
        for (int f = 0; f < floors; f++)
        {
            float fy = yBase + f * Fh;
            if (ledges) Box(n, new Vector3(ox, fy + 0.02f, oz), new Vector3(w + 0.14f, 0.05f, d + 0.14f), Concrete);
            if (balconies && f % 2 == 1)
            {
                Box(n, new Vector3(ox, fy + 0.03f, oz + d / 2 + 0.2f), new Vector3(w * 0.7f, 0.06f, 0.4f), Concrete);
                Box(n, new Vector3(ox, fy + 0.25f, oz + d / 2 + 0.39f), new Vector3(w * 0.7f, 0.02f, 0.02f), Rust);
                for (int k = -2; k <= 2; k++) Box(n, new Vector3(ox + k * w * 0.17f, fy + 0.15f, oz + d / 2 + 0.39f), new Vector3(0.02f, 0.26f, 0.02f), Rust);
            }
        }
        return yBase + h;
    }

    Material PickWindows() => _rnd.Next(4) switch { 0 => WinWarm, 1 => WinCool, 2 => WinMixed, _ => WinDark };

    // ---------- 窓 1 区画(ベイ): 枠・奥まったガラス・庇・桟・腰壁を持つ部品メッシュを建物ごとに MultiMesh で並べる ----------
    public const float LodNear = 75f;   // これより遠い建物は窓テクスチャの箱に切り替える
    ArrayMesh? _bayLitWarm, _bayLitCool, _bayDark;
    readonly Dictionary<Node3D, (List<Transform3D> warm, List<Transform3D> cool, List<Transform3D> dark)> _bays = new();

    void EnsureBayMeshes()
    {
        if (_bayLitWarm != null) return;
        var glassLitWarm = Glow(new Color(1.0f, 0.82f, 0.55f), 1.6f); glassLitWarm.Roughness = 0.2f;
        var glassLitCool = Glow(new Color(0.55f, 0.85f, 1.0f), 1.6f); glassLitCool.Roughness = 0.2f;
        _bayLitWarm = MeshParts.Bay(PaintDark, glassLitWarm, Steel);
        _bayLitCool = MeshParts.Bay(PaintDark, glassLitCool, Steel);
        _bayDark = MeshParts.Bay(PaintDark, Glass, Steel);
    }

    /// <summary>壁面にベイを並べる。yBase から floors 階、w × d の箱の 4 面。</summary>
    void Facade(Node3D n, float yBase, float w, float d, int floors, float ox, float oz, float litRate, bool cool)
    {
        EnsureBayMeshes();
        if (!_bays.TryGetValue(n, out var lists)) _bays[n] = lists = (new(), new(), new());
        var sides = new[] { (0, 1, 0f), (0, -1, 180f), (1, 0, 90f), (-1, 0, -90f) };
        foreach (var (nx, nz, yaw) in sides)
        {
            float len = nx == 0 ? w : d;
            int bays = Math.Max(1, (int)(len / MeshParts.BayW));
            float margin = (len - bays * MeshParts.BayW) / 2;
            for (int f = 0; f < floors; f++)
                for (int b = 0; b < bays; b++)
                {
                    float t = -len / 2 + margin + (b + 0.5f) * MeshParts.BayW;
                    var pos = nx == 0 ? new Vector3(ox + t, yBase + f * Fh, oz + nz * d / 2) : new Vector3(ox + nx * w / 2, yBase + f * Fh, oz + t);
                    // 側面の並びは外向きに +x が右になるように符号を合わせる
                    if (nx == 0 && nz < 0) pos.X = ox - t; if (nx != 0) pos.Z = oz + (nx > 0 ? -t : t);
                    var basis = Basis.FromEuler(new Vector3(0, Mathf.DegToRad(yaw), 0));
                    var xf = n.GlobalTransform * new Transform3D(basis, pos);
                    bool lit = _rnd.NextDouble() < litRate;
                    (lit ? (cool ? lists.cool : lists.warm) : lists.dark).Add(xf);
                }
        }
    }

    /// <summary>建物のベイを MultiMesh にして追加(近距離用)。遠距離用は呼び出し側が窓テクスチャの箱を置く。</summary>
    void FlushBays(Node3D n)
    {
        if (!_bays.TryGetValue(n, out var lists)) return;
        foreach (var (mesh, list) in new[] { (_bayLitWarm!, lists.warm), (_bayLitCool!, lists.cool), (_bayDark!, lists.dark) })
        {
            if (list.Count == 0) continue;
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = list.Count };
            for (int i = 0; i < list.Count; i++) mm.SetInstanceTransform(i, list[i]);
            n.GetParent().AddChild(new MultiMeshInstance3D { Multimesh = mm, VisibilityRangeEnd = LodNear, VisibilityRangeEndMargin = 8f, VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self });
        }
        _bays.Remove(n);
    }

    public const float LodFar = 220f;   // 階モジュール(GLB)はこれより遠いと窓テクスチャの箱に切り替える(1 棟 60 階で数十 MB の負荷対策)

    /// <summary>階モジュールの遠景用: 窓テクスチャの箱を begin より遠くでだけ描く。</summary>
    public void FarBoxFrom(Node3D n, Vector3 center, Vector3 size, Material win, float begin)
    {
        n.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = win, Position = center, VisibilityRangeBegin = begin, VisibilityRangeBeginMargin = 10f, VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self });
    }

    /// <summary>節の下の全メッシュを end より遠くで隠す。</summary>
    public static void HideBeyond(Node node, float end)
    {
        foreach (var c in node.GetChildren()) HideBeyond(c, end);
        if (node is GeometryInstance3D gi) { gi.VisibilityRangeEnd = end; gi.VisibilityRangeEndMargin = 10f; gi.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self; }
    }

    /// <summary>遠距離用: 窓テクスチャを貼った箱(近距離ではベイに置き換わる)。</summary>
    void FarBox(Node3D n, Vector3 center, Vector3 size, Material win)
    {
        var mi = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = win, Position = center, VisibilityRangeBegin = LodNear, VisibilityRangeBeginMargin = 8f, VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self };
        n.AddChild(mi);
    }


    void Parapet(Node3D n, float top, float w, float d, float ox, float oz)
    {
        Box(n, new Vector3(ox, top + 0.03f, oz), new Vector3(w + 0.16f, 0.06f, d + 0.16f), Steel);
        Box(n, new Vector3(ox, top + 0.15f, oz + d / 2), new Vector3(w + 0.1f, 0.25f, 0.06f), Concrete);
        Box(n, new Vector3(ox, top + 0.15f, oz - d / 2), new Vector3(w + 0.1f, 0.25f, 0.06f), Concrete);
        Box(n, new Vector3(ox + w / 2, top + 0.15f, oz), new Vector3(0.06f, 0.25f, d + 0.1f), Concrete);
        Box(n, new Vector3(ox - w / 2, top + 0.15f, oz), new Vector3(0.06f, 0.25f, d + 0.1f), Concrete);
    }

    void Crown(Node3D n, float top, float w, float d, float ox, float oz)
    {
        Strip(n, new Vector3(ox, top + 0.04f, oz + d / 2), new Vector3(w, 0.06f, 0.06f), GlowCyan);
        Strip(n, new Vector3(ox, top + 0.04f, oz - d / 2), new Vector3(w, 0.06f, 0.06f), GlowCyan);
        Strip(n, new Vector3(ox + w / 2, top + 0.04f, oz), new Vector3(0.06f, 0.06f, d), GlowCyan);
        Strip(n, new Vector3(ox - w / 2, top + 0.04f, oz), new Vector3(0.06f, 0.06f, d), GlowCyan);
    }

    /// <summary>屋上: 機械室、空調機、給水塔、アンテナと赤灯。kind 0 = 機械室、1 = 尖塔、2 = ヘリポート。</summary>
    void Roof(Node3D n, float top, float w, float d, float ox, float oz, int kind)
    {
        Parapet(n, top, w, d, ox, oz);
        Box(n, new Vector3(ox - w * 0.2f, top + 0.35f, oz - d * 0.2f), new Vector3(w * 0.4f, 0.7f, d * 0.4f), PaintDark);
        for (int i = 0; i < 3; i++) Box(n, new Vector3(ox + w * 0.25f, top + 0.18f, oz - d * 0.3f + i * d * 0.25f), new Vector3(0.3f, 0.3f, 0.3f), Steel);
        Cyl(n, new Vector3(ox + w * 0.25f, top + 0.9f, oz + d * 0.25f), 0.22f, 0.5f, Rust, seg: 10);
        for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2) Box(n, new Vector3(ox + w * 0.25f + sx * 0.15f, top + 0.33f, oz + d * 0.25f + sz * 0.15f), new Vector3(0.04f, 0.66f, 0.04f), Steel);
        switch (kind)
        {
            case 1:
                Cyl(n, new Vector3(ox, top + 1.0f, oz), 0.25f, 1.2f, Steel, topR: 0.08f, seg: 8);
                Cyl(n, new Vector3(ox, top + 2.6f, oz), 0.03f, 2.4f, Steel, seg: 6);
                Strip(n, new Vector3(ox, top + 3.85f, oz), new Vector3(0.14f, 0.14f, 0.14f), GlowRed);
                break;
            case 2:
                Cyl(n, new Vector3(ox - w * 0.15f, top + 0.72f, oz + d * 0.15f), Math.Min(w, d) * 0.32f, 0.08f, Concrete, seg: 12);
                Cyl(n, new Vector3(ox - w * 0.15f, top + 0.78f, oz + d * 0.15f), Math.Min(w, d) * 0.33f, 0.02f, GlowCyan, seg: 12);
                Cyl(n, new Vector3(ox - w * 0.15f, top + 0.78f, oz + d * 0.15f), Math.Min(w, d) * 0.28f, 0.03f, Concrete, seg: 12);
                Cyl(n, new Vector3(ox + w * 0.3f, top + 1.2f, oz - d * 0.3f), 0.025f, 1.6f, Steel, seg: 6);
                Strip(n, new Vector3(ox + w * 0.3f, top + 2.05f, oz - d * 0.3f), new Vector3(0.12f, 0.12f, 0.12f), GlowRed);
                break;
            default:
                Cyl(n, new Vector3(ox + w * 0.3f, top + 1.0f, oz - d * 0.3f), 0.025f, 1.4f, Steel, seg: 6);
                Strip(n, new Vector3(ox + w * 0.3f, top + 1.75f, oz - d * 0.3f), new Vector3(0.12f, 0.12f, 0.12f), GlowRed);
                break;
        }
    }

    /// <summary>
    /// メガタワー(SimCity Cities of Tomorrow を参照): 積層モジュール、外骨格の角柱と斜め支柱、層間の発光デッキと空中庭園、中央コア、冠と尖塔。
    /// levels × floorsPerLevel 階。原点は基壇の中心。
    /// </summary>
    public Node3D MegaTower(Node3D parent, float x, float y, float z, float w, float d, int levels, int floorsPerLevel, float rot = 0)
    {
        var n = Root(parent, x, y, z, rot);
        float lh = floorsPerLevel * Fh;
        // 基壇とロビー(2 階分のガラスと発光帯)
        Box(n, new Vector3(0, 0.15f, 0), new Vector3(w + 1.6f, 0.3f, d + 1.6f), Concrete);
        Box(n, new Vector3(0, 0.3f + Fh, 0), new Vector3(w + 0.6f, Fh * 2, d + 0.6f), WinCool);
        Box(n, new Vector3(0, 0.3f + Fh * 2 + 0.1f, 0), new Vector3(w + 1.0f, 0.2f, d + 1.0f), Steel);
        Strip(n, new Vector3(0, 0.34f, (d + 1.6f) / 2), new Vector3(w + 1.6f, 0.05f, 0.05f), GlowCyan);
        Strip(n, new Vector3(0, 0.34f, -(d + 1.6f) / 2), new Vector3(w + 1.6f, 0.05f, 0.05f), GlowCyan);
        Box(n, new Vector3(0, 0.3f + Fh * 0.7f, (d + 0.6f) / 2 + 0.02f), new Vector3(1.6f, Fh * 1.4f, 0.04f), Rubber);
        Strip(n, new Vector3(0, 0.3f + Fh * 1.42f, (d + 0.6f) / 2 + 0.03f), new Vector3(1.6f, 0.06f, 0.02f), GlowWhite);
        Light(n, new Vector3(0, 1.0f, (d + 0.6f) / 2 + 1.2f), new Color(0.25f, 0.85f, 1.0f), 5f, 1.5f);
        float y0 = 0.3f + Fh * 2 + 0.2f;
        float top = y0;
        // 中央コア(全高 + 冠)
        float hAll = levels * lh + (levels - 1) * 0.5f;
        Cyl(n, new Vector3(0, y0 + hAll / 2 + 1.5f, 0), Math.Min(w, d) * 0.22f, hAll + 3.0f, PaintDark, seg: 12);
        // 積層モジュール
        var wins = new[] { WinWarm, WinCool, WinMixed };
        for (int L = 0; L < levels; L++)
        {
            float lw = w * (L % 2 == 0 ? 1.0f : 0.9f), ld = d * (L % 2 == 0 ? 1.0f : 0.9f);
            Box(n, new Vector3(0, top + lh / 2, 0), new Vector3(lw - 0.4f, lh, ld - 0.4f), PaintDark);
            Facade(n, top, lw, ld, floorsPerLevel, 0, 0, 0.6f, L % 2 == 1);
            FarBox(n, new Vector3(0, top + lh / 2, 0), new Vector3(lw, lh, ld), wins[L % 3]);
            // 外骨格: 角の太い柱と各面の斜め支柱(X 字)
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2)
                Box(n, new Vector3(sx * (lw / 2 + 0.08f), top + lh / 2, sz * (ld / 2 + 0.08f)), new Vector3(0.28f, lh, 0.28f), Steel);
            float ang = Mathf.RadToDeg(Mathf.Atan2(lh, lw));
            foreach (int sz in new[] { -1, 1 })
            {
                Box(n, new Vector3(0, top + lh / 2, sz * (ld / 2 + 0.05f)), new Vector3(0.08f, Mathf.Sqrt(lh * lh + lw * lw) * 0.98f, 0.08f), Steel, new Vector3(0, 0, 90 - ang));
                Box(n, new Vector3(0, top + lh / 2, sz * (ld / 2 + 0.05f)), new Vector3(0.08f, Mathf.Sqrt(lh * lh + lw * lw) * 0.98f, 0.08f), Steel, new Vector3(0, 0, ang - 90));
            }
            float angz = Mathf.RadToDeg(Mathf.Atan2(lh, ld));
            foreach (int sx in new[] { -1, 1 })
            {
                Box(n, new Vector3(sx * (lw / 2 + 0.05f), top + lh / 2, 0), new Vector3(0.08f, Mathf.Sqrt(lh * lh + ld * ld) * 0.98f, 0.08f), Steel, new Vector3(90 - angz, 0, 0));
                Box(n, new Vector3(sx * (lw / 2 + 0.05f), top + lh / 2, 0), new Vector3(0.08f, Mathf.Sqrt(lh * lh + ld * ld) * 0.98f, 0.08f), Steel, new Vector3(angz - 90, 0, 0));
            }
            // 中間の帯(2 階ごとの庇)
            for (int f = 2; f < floorsPerLevel; f += 2) Box(n, new Vector3(0, top + f * Fh, 0), new Vector3(lw + 0.12f, 0.05f, ld + 0.12f), Steel);
            top += lh;
            // 層間デッキ: 張り出した板、縁の発光帯、手すり、空中庭園
            Box(n, new Vector3(0, top + 0.12f, 0), new Vector3(w + 0.9f, 0.24f, d + 0.9f), Steel);
            Box(n, new Vector3(0, top + 0.26f, 0), new Vector3(w + 0.7f, 0.04f, d + 0.7f), Grate);
            Strip(n, new Vector3(0, top + 0.12f, (d + 0.9f) / 2 + 0.01f), new Vector3(w + 0.9f, 0.06f, 0.02f), GlowCyan);
            Strip(n, new Vector3(0, top + 0.12f, -(d + 0.9f) / 2 - 0.01f), new Vector3(w + 0.9f, 0.06f, 0.02f), GlowCyan);
            Strip(n, new Vector3((w + 0.9f) / 2 + 0.01f, top + 0.12f, 0), new Vector3(0.02f, 0.06f, d + 0.9f), GlowCyan);
            Strip(n, new Vector3(-(w + 0.9f) / 2 - 0.01f, top + 0.12f, 0), new Vector3(0.02f, 0.06f, d + 0.9f), GlowCyan);
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2)
            {
                Box(n, new Vector3(sx * (w / 2 + 0.25f), top + 0.42f, sz * (d / 2 + 0.25f)), new Vector3(0.5f, 0.3f, 0.5f), Foliage);
                Box(n, new Vector3(sx * (w / 2 + 0.25f), top + 0.62f, sz * (d / 2 + 0.25f)), new Vector3(0.3f, 0.3f, 0.3f), Foliage);
            }
            for (int sz = -1; sz <= 1; sz += 2) Box(n, new Vector3(0, top + 0.5f, sz * (d + 0.9f) / 2), new Vector3(w + 0.9f, 0.02f, 0.02f), Rust);
            top += 0.5f;
        }
        // 冠: 上部デッキ、環の発光、機器、尖塔とアンテナ群、赤灯
        Box(n, new Vector3(0, top + 0.3f, 0), new Vector3(w * 0.7f, 0.6f, d * 0.7f), PaintDark);
        Cyl(n, new Vector3(0, top + 0.7f, 0), Math.Min(w, d) * 0.3f, 0.08f, GlowCyan, seg: 16);
        for (int i = 0; i < 4; i++) Box(n, new Vector3(-w * 0.25f + i * w * 0.17f, top + 0.75f, -d * 0.2f), new Vector3(0.3f, 0.3f, 0.3f), Steel);
        Cyl(n, new Vector3(0, top + 2.0f, 0), 0.35f, 2.6f, Steel, topR: 0.06f, seg: 8);
        Cyl(n, new Vector3(0, top + 4.4f, 0), 0.03f, 2.2f, Steel, seg: 6);
        Strip(n, new Vector3(0, top + 5.55f, 0), new Vector3(0.16f, 0.16f, 0.16f), GlowRed);
        Light(n, new Vector3(0, top + 5.5f, 0), new Color(1.0f, 0.2f, 0.15f), 6f, 2f);
        for (int sx = -1; sx <= 1; sx += 2) { Cyl(n, new Vector3(sx * w * 0.3f, top + 1.5f, d * 0.25f), 0.02f, 1.6f, Steel, seg: 6); Strip(n, new Vector3(sx * w * 0.3f, top + 2.35f, d * 0.25f), new Vector3(0.1f, 0.1f, 0.1f), GlowRed); }
        FlushBays(n);
        return n;
    }

    /// <summary>塔どうしをつなぐ空中回廊。a と b は回廊の両端(世界座標)。</summary>
    public void SkyBridge(Node3D parent, Vector3 a, Vector3 b)
    {
        var n = Root(parent, 0, 0, 0, 0);
        var dlt = b - a; float len = dlt.Length();
        float yaw = Mathf.RadToDeg(Mathf.Atan2(dlt.X, dlt.Z));
        var c = (a + b) / 2;
        Box(n, c + new Vector3(0, 0.3f, 0), new Vector3(1.2f, 0.6f, len), WinCool, new Vector3(0, yaw, 0));
        Box(n, c + new Vector3(0, -0.08f, 0), new Vector3(1.5f, 0.16f, len), Steel, new Vector3(0, yaw, 0));
        Box(n, c + new Vector3(0, 0.68f, 0), new Vector3(1.4f, 0.1f, len), Steel, new Vector3(0, yaw, 0));
        Strip(n, c + new Vector3(0, -0.17f, 0), new Vector3(1.0f, 0.03f, len), GlowCyan, new Vector3(0, yaw, 0));
    }

    /// <summary>工場棟: 低く長い箱、鋸屋根風の通気、配管、煙突、扉の発光。</summary>
    public Node3D Factory(Node3D parent, float x, float y, float z, float w, float d, float h, float rot = 0)
    {
        var n = Root(parent, x, y, z, rot);
        Box(n, new Vector3(0, 0.08f, 0), new Vector3(w + 0.4f, 0.16f, d + 0.4f), Floor);
        Box(n, new Vector3(0, 0.16f + h / 2, 0), new Vector3(w, h, d), Rust);
        Box(n, new Vector3(0, 0.16f + h / 2, 0), new Vector3(w + 0.04f, h * 0.3f, d + 0.04f), Paint);        // 中段の帯
        Box(n, new Vector3(0, 0.16f + h + 0.05f, 0), new Vector3(w + 0.1f, 0.1f, d + 0.1f), Steel);
        for (int i = 0; i < (int)(w / 1.0f); i++)
            Box(n, new Vector3(-w / 2 + 0.5f + i, 0.16f + h + 0.35f, 0), new Vector3(0.6f, 0.5f, d * 0.7f), PaintDark, new Vector3(0, 0, 0));
        for (int i = 0; i < 4; i++) Box(n, new Vector3(-w / 2 + 0.3f + i * (w - 0.6f) / 3, 0.16f + h + 0.7f, 0), new Vector3(0.3f, 0.2f, 0.3f), Steel);
        Box(n, new Vector3(0, 0.16f + h * 0.35f, d / 2 + 0.02f), new Vector3(w * 0.3f, h * 0.7f, 0.04f), Rubber);     // 扉
        Strip(n, new Vector3(0, 0.16f + h * 0.72f, d / 2 + 0.03f), new Vector3(w * 0.3f, 0.06f, 0.02f), GlowOrange);
        Box(n, new Vector3(0, 0.2f, d / 2 + 0.1f), new Vector3(w * 0.5f, 0.06f, 0.1f), Hazard);
        Pipe(n, new Vector3(-w / 2 - 0.1f, 0.5f, -d / 2 + 0.4f), new Vector3(-w / 2 - 0.1f, 0.5f, d / 2 - 0.4f), 0.07f, Steel);
        Pipe(n, new Vector3(-w / 2 - 0.1f, 0.5f, -d / 2 + 0.4f), new Vector3(-w / 2 - 0.1f, h + 0.3f, -d / 2 + 0.4f), 0.07f, Steel);
        Greeble(n, new Vector3(0, 0.16f + h / 2, -d / 2 - 0.02f), new Vector3(w * 0.9f, h * 0.8f, 0), Vector3.Forward, 10);
        Greeble(n, new Vector3(w / 2 + 0.02f, 0.16f + h / 2, 0), new Vector3(h * 0.8f, d * 0.8f, 0), Vector3.Right, 8);
        return n;
    }

    /// <summary>居住モジュール(入植地): 角張った箱、扉と窓の発光、配管、アンテナ。</summary>
    public Node3D Habitat(Node3D parent, float x, float y, float z, float rot = 0)
    {
        var n = Root(parent, x, y, z, rot);
        Box(n, new Vector3(0, 0.1f, 0), new Vector3(2.2f, 0.2f, 1.4f), Concrete);
        Box(n, new Vector3(0, 0.75f, 0), new Vector3(2.0f, 1.1f, 1.2f), Paint);
        Box(n, new Vector3(0, 1.35f, 0), new Vector3(2.1f, 0.1f, 1.3f), Steel);
        for (int sx = -1; sx <= 1; sx += 2) Box(n, new Vector3(sx * 1.0f, 0.75f, 0), new Vector3(0.1f, 1.1f, 1.26f), Steel);
        Box(n, new Vector3(0.5f, 0.6f, 0.62f), new Vector3(0.4f, 0.8f, 0.04f), Rubber);
        Strip(n, new Vector3(0.5f, 1.02f, 0.63f), new Vector3(0.4f, 0.05f, 0.02f), GlowCyan);
        for (int i = 0; i < 2; i++) Strip(n, new Vector3(-0.3f - i * 0.5f, 0.85f, 0.61f), new Vector3(0.3f, 0.22f, 0.02f), GlowWhite);
        Box(n, new Vector3(-0.7f, 1.55f, -0.3f), new Vector3(0.5f, 0.3f, 0.5f), Steel);
        Cyl(n, new Vector3(0.8f, 1.8f, -0.4f), 0.02f, 0.8f, Steel, seg: 6);
        Pipe(n, new Vector3(1.08f, 0.4f, -0.3f), new Vector3(1.08f, 1.2f, -0.3f), 0.05f, Rust);
        Light(n, new Vector3(0.5f, 1.0f, 1.0f), new Color(0.25f, 0.85f, 1.0f), 3f, 1.2f);
        return n;
    }

    public Node3D Hangar(Node3D parent, float x, float y, float z, float w, float d, float h, float rot = 0)
    {
        var n = Root(parent, x, y, z, rot);
        Box(n, new Vector3(0, h / 2, 0), new Vector3(w, h, d), PaintDark);
        for (int i = 0; i <= 4; i++) Box(n, new Vector3(-w / 2 + i * w / 4, h / 2, 0), new Vector3(0.14f, h + 0.1f, d + 0.1f), Steel);
        Box(n, new Vector3(0, h + 0.06f, 0), new Vector3(w + 0.1f, 0.12f, d + 0.1f), Steel);
        Box(n, new Vector3(0, h * 0.45f, d / 2 + 0.02f), new Vector3(w * 0.5f, h * 0.9f, 0.04f), Rubber);
        Strip(n, new Vector3(0, h * 0.92f, d / 2 + 0.03f), new Vector3(w * 0.5f, 0.06f, 0.02f), GlowCyan);
        Strip(n, new Vector3(0, h * 0.45f, d / 2 + 0.03f), new Vector3(0.03f, h * 0.9f, 0.02f), GlowCyan);
        Box(n, new Vector3(0, 0.05f, d / 2 + 0.3f), new Vector3(w * 0.6f, 0.08f, 0.5f), Hazard);
        Light(n, new Vector3(0, h * 0.6f, d / 2 + 1.0f), new Color(0.25f, 0.85f, 1.0f), 6f, 2f);
        return n;
    }

    public Node3D Turret(Node3D parent, float x, float y, float z, float rot = 0)
    {
        var n = Root(parent, x, y, z, rot);
        Box(n, new Vector3(0, 0.15f, 0), new Vector3(0.8f, 0.3f, 0.8f), Concrete);
        Cyl(n, new Vector3(0, 0.45f, 0), 0.28f, 0.3f, Paint, seg: 10);
        Box(n, new Vector3(0, 0.75f, 0), new Vector3(0.5f, 0.3f, 0.6f), Steel);
        Cyl(n, new Vector3(0, 0.78f, 0.55f), 0.05f, 0.7f, Rubber, new Vector3(90, 0, 0), seg: 8);
        Strip(n, new Vector3(0, 0.9f, 0.1f), new Vector3(0.3f, 0.04f, 0.04f), GlowRed);
        return n;
    }

    public Node3D Solar(Node3D parent, float x, float y, float z, float rot = 0)
    {
        var n = Root(parent, x, y, z, rot);
        Box(n, new Vector3(0, 0.25f, 0), new Vector3(0.06f, 0.5f, 0.06f), Steel);
        Box(n, new Vector3(0, 0.55f, 0), new Vector3(0.9f, 0.04f, 1.3f), Glass, new Vector3(-22, 0, 0));
        Box(n, new Vector3(0, 0.53f, 0), new Vector3(0.94f, 0.03f, 1.34f), Steel, new Vector3(-22, 0, 0));
        return n;
    }

    public Node3D RailPillar(Node3D parent, float x, float y, float z, float h)
    {
        var n = Root(parent, x, y, z, 0);
        Box(n, new Vector3(0, h / 2, 0), new Vector3(0.3f, h, 0.3f), Concrete);
        Box(n, new Vector3(0, h - 0.1f, 0), new Vector3(1.0f, 0.2f, 0.4f), Steel);
        return n;
    }

    public Node3D RailSegment(Node3D parent, float x, float y, float z)
    {
        var n = Root(parent, x, y, z, 0);
        Box(n, new Vector3(0, 0, 0), new Vector3(0.7f, 0.25f, 1.0f), Paint);
        Box(n, new Vector3(0, 0.15f, 0), new Vector3(0.12f, 0.05f, 1.0f), Steel);
        Box(n, new Vector3(0.33f, 0.1f, 0), new Vector3(0.04f, 0.1f, 1.0f), Rust);
        Box(n, new Vector3(-0.33f, 0.1f, 0), new Vector3(0.04f, 0.1f, 1.0f), Rust);
        return n;
    }

    public Node3D Train(Node3D parent, float x, float y, float z, float len)
    {
        var n = Root(parent, x, y, z, 0);
        Box(n, new Vector3(0, 0.45f, 0), new Vector3(0.6f, 0.7f, len), PaintDark);
        Box(n, new Vector3(0, 0.85f, 0), new Vector3(0.5f, 0.1f, len * 0.9f), Steel);
        for (int i = 0; i < (int)(len / 0.5f); i++)
        {
            Strip(n, new Vector3(0.31f, 0.5f, -len / 2 + 0.3f + i * 0.5f), new Vector3(0.02f, 0.2f, 0.3f), GlowWhite);
            Strip(n, new Vector3(-0.31f, 0.5f, -len / 2 + 0.3f + i * 0.5f), new Vector3(0.02f, 0.2f, 0.3f), GlowWhite);
        }
        Strip(n, new Vector3(0, 0.35f, len / 2 + 0.01f), new Vector3(0.4f, 0.08f, 0.02f), GlowWhite);
        Light(n, new Vector3(0, 0.4f, len / 2 + 0.5f), new Color(0.9f, 0.95f, 1.0f), 5f, 2f);
        return n;
    }

    // ---------- 人型 ----------
    /// <summary>角張った体格(頭は小さく、胴は装甲板、関節は円柱)。robot は鋼の装甲と発光の目、市民は暗い作業服と兜。</summary>
    public Node3D Humanoid(Node3D parent, float x, float y, float z, float rot, bool robot)
    {
        var n = Root(parent, x, y, z, rot);
        var body = robot ? Steel : PaintDark;
        var joint = robot ? Rust : Rubber;
        // 脚
        for (int sx = -1; sx <= 1; sx += 2)
        {
            Box(n, new Vector3(sx * 0.11f, 0.2f, 0), new Vector3(0.14f, 0.4f, 0.16f), body);
            Cyl(n, new Vector3(sx * 0.11f, 0.42f, 0), 0.07f, 0.1f, joint, new Vector3(0, 0, 90), seg: 8);
            Box(n, new Vector3(sx * 0.11f, 0.62f, 0), new Vector3(0.15f, 0.34f, 0.17f), body);
            Box(n, new Vector3(sx * 0.11f, 0.04f, 0.03f), new Vector3(0.15f, 0.08f, 0.24f), joint);
        }
        // 胴と胸甲
        Box(n, new Vector3(0, 1.0f, 0), new Vector3(0.42f, 0.5f, 0.24f), body);
        Box(n, new Vector3(0, 1.08f, 0.13f), new Vector3(0.34f, 0.3f, 0.06f), robot ? Paint : Steel);
        Box(n, new Vector3(0, 0.78f, 0), new Vector3(0.36f, 0.1f, 0.22f), joint);
        // 肩と腕
        for (int sx = -1; sx <= 1; sx += 2)
        {
            Box(n, new Vector3(sx * 0.27f, 1.2f, 0), new Vector3(0.14f, 0.14f, 0.2f), robot ? Paint : Steel);
            Box(n, new Vector3(sx * 0.28f, 0.98f, 0), new Vector3(0.1f, 0.3f, 0.12f), body);
            Cyl(n, new Vector3(sx * 0.28f, 0.82f, 0), 0.06f, 0.1f, joint, new Vector3(0, 0, 90), seg: 8);
            Box(n, new Vector3(sx * 0.28f, 0.66f, 0.02f), new Vector3(0.09f, 0.28f, 0.11f), body);
        }
        // 頭
        Cyl(n, new Vector3(0, 1.28f, 0), 0.05f, 0.06f, joint, seg: 8);
        Box(n, new Vector3(0, 1.42f, 0), new Vector3(0.22f, 0.24f, 0.24f), robot ? Steel : PaintDark);
        if (robot) Strip(n, new Vector3(0, 1.44f, 0.125f), new Vector3(0.16f, 0.03f, 0.01f), GlowCyan);
        else { Box(n, new Vector3(0, 1.42f, 0.125f), new Vector3(0.16f, 0.1f, 0.01f), Glass); Box(n, new Vector3(0, 1.56f, 0), new Vector3(0.26f, 0.06f, 0.28f), Steel); }
        return n;
    }

    // ---------- 自然 ----------
    public Node3D Pine(Node3D parent, float x, float y, float z, float h = 2.2f)
    {
        var n = Root(parent, x, y, z, R(0, 360));
        Cyl(n, new Vector3(0, h * 0.25f, 0), 0.08f, h * 0.5f, Trunk, seg: 6);
        for (int i = 0; i < 3; i++)
        {
            float r = 0.55f - i * 0.14f, cy = h * 0.35f + i * h * 0.22f;
            Cyl(n, new Vector3(0, cy, 0), r, h * 0.3f, Foliage, topR: 0.02f, seg: 7);
        }
        return n;
    }

    public Node3D Crop(Node3D parent, float x, float y, float z)
    {
        var n = Root(parent, x, y, z, 0);
        for (int i = 0; i < 3; i++) Box(n, new Vector3(-0.3f + i * 0.3f, 0.18f, 0), new Vector3(0.14f, 0.36f, 0.9f), Foliage);
        return n;
    }

    public Node3D Rock(Node3D parent, float x, float y, float z, float s)
    {
        var n = Root(parent, x, y, z, R(0, 360));
        Box(n, new Vector3(0, s * 0.3f, 0), new Vector3(s, s * 0.7f, s * 0.8f), Concrete, new Vector3(R(-15, 15), 0, R(-15, 15)));
        Box(n, new Vector3(s * 0.3f, s * 0.2f, s * 0.2f), new Vector3(s * 0.6f, s * 0.5f, s * 0.5f), Concrete, new Vector3(R(-20, 20), R(0, 45), 0));
        return n;
    }
}
