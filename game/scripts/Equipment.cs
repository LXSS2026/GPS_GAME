using GPS.Sim;
using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

/// <summary>設備(工場の機械)の配置。部品は tools/blender/make_equipment.py が assets/equipment/*.glb と data/equipment.json に書く。
/// 1 セル角、原点はセルの床の中心、正面は Blender の +Y(GLB の -Z)。rot は正面の向き(度)。
/// 1 階 100x100 マスに数千台を置くため、同じ部品の同じメッシュを 1 つの MultiMesh にまとめて描く(Place で溜め、Flush で追加)。</summary>
public sealed class Equipment
{
    public sealed record Def(string Id, int H);
    public readonly Dictionary<string, Def> Defs = new();
    readonly Dictionary<string, List<(Mesh mesh, Transform3D local)>> _parts = new();
    readonly Dictionary<string, List<Transform3D>> _placed = new();
    readonly FloorModules _fm;
    public int Count { get; private set; }

    public Equipment(FloorModules fm)
    {
        _fm = fm;
        using var doc = JsonDocument.Parse(FileAccess.GetFileAsString("res://data/equipment.json"));
        foreach (var e in doc.RootElement.GetProperty("equipment").EnumerateArray())
            Defs[e.GetProperty("id").GetString()!] = new Def(e.GetProperty("id").GetString()!, e.GetProperty("h").GetInt32());
    }

    /// <summary>セル (cx, cz) の床 y に設備を置く(溜めるだけ)。rot は 0 = -Z 向き(手前)、90 = +X、180 = +Z、270 = -X。</summary>
    public void Place(string id, int cx, float y, int cz, int rot = 0)
    {
        if (!_placed.TryGetValue(id, out var list)) _placed[id] = list = new List<Transform3D>();
        list.Add(new Transform3D(Basis.FromEuler(new Vector3(0, Mathf.DegToRad(rot), 0)), new Vector3(cx + 0.5f, y, cz + 0.5f)));
        Count++;
    }

    /// <summary>設備の定義 → 部品の割り当て。部品が無い種類は近い部品で代用する。</summary>
    public static string Model(StructureDef def) => def.Kind switch
    {
        "furnace" => "furnace", "assembler" => "assembler", "mech_assembler" => "assembler", "miner" => "miner", "belt" => "belt", "inserter" => "inserter", "chest" => "chest",
        "tank" => "tank", "reactor" => "tank", "port" => "chest", "pump" => "miner", "battery" => "tank", "trade" => "chest", _ => "pole",
    };

    public static Color ReactorColor(int gen) => gen switch { 0 => new Color(1f, 0.2f, 0.15f), 1 => new Color(1f, 0.5f, 0.1f), 2 => new Color(1f, 0.9f, 0.2f), 3 => new Color(0.6f, 1f, 0.2f), 4 => new Color(0.2f, 1f, 0.4f), 5 => new Color(0.25f, 0.5f, 1f), _ => new Color(0.75f, 0.35f, 1f) };

    /// <summary>設備 1 台の見た目を置く(部品 + 種類ごとの付属物)。原点はセル (x, z) の床。Flush は呼び出し側。</summary>
    /// <param name="liftDir">昇降機の口: +1 = 上の階へ、-1 = 下の階へ、0 = 未接続(矢印なし)。</param>
    public static void PlaceVisual(Equipment eq, KitBash kb, Node3D root, StructureDef def, int x, float y, int z, int rot, int liftDir = 0)
    {
        if (def.Kind == "pipe") { kb.Pipe(root, new Vector3(x, y + 0.3f, z + 0.5f), new Vector3(x + 1, y + 0.3f, z + 0.5f), 0.12f, kb.Rust); kb.Pipe(root, new Vector3(x + 0.5f, y + 0.3f, z), new Vector3(x + 0.5f, y + 0.3f, z + 1), 0.12f, kb.Rust); return; }
        string model = Model(def);
        if (def.W == 1 && def.D == 1) eq.Place(model, x, y, z, rot);
        else for (int i = 0; i < def.W; i++) for (int j = 0; j < def.D; j++) eq.Place(model, x + i, y, z + j, rot);
        if (def.Kind == "reactor")
        {
            var col = ReactorColor(def.Generation);
            var top = new Vector3(x + 0.5f, y + 2.3f, z + 0.5f);
            root.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.35f, Height = 0.7f }, MaterialOverride = new StandardMaterial3D { AlbedoColor = col, EmissionEnabled = true, Emission = col, EmissionEnergyMultiplier = 2.5f }, Position = top });
            root.AddChild(ReactorParticles(col, top));   // 発光部から粒子(2026-10-15 指示)
        }
        else if (def.Kind == "battery")
        {   // バッテリー: タンクの部品に世代の色の帯 3 本(専用部品は未作成)。リアクターと同じ大きさ
            var col = ReactorColor(def.Generation);
            var mat = new StandardMaterial3D { AlbedoColor = col, EmissionEnabled = true, Emission = col, EmissionEnergyMultiplier = 1.5f };
            for (int k = 0; k < 3; k++) kb.Box(root, new Vector3(x + 0.5f, y + 0.6f + k * 0.5f, z + 0.5f), new Vector3(0.9f, 0.06f, 0.9f), mat);
        }
        else if (def.Kind == "mech_assembler")
        {   // 機体組立機: 4x4 の組立機の上に門形のクレーンと橙の帯(専用部品は未作成)
            kb.Box(root, new Vector3(x + 0.3f, y + 1.9f, z + 2f), new Vector3(0.25f, 3.8f, 0.25f), kb.Steel); kb.Box(root, new Vector3(x + 3.7f, y + 1.9f, z + 2f), new Vector3(0.25f, 3.8f, 0.25f), kb.Steel);
            kb.Box(root, new Vector3(x + 2f, y + 3.8f, z + 2f), new Vector3(3.8f, 0.3f, 0.5f), kb.Hazard);
            kb.Strip(root, new Vector3(x + 2f, y + 2.05f, z + 0.05f), new Vector3(3.6f, 0.06f, 0.06f), kb.GlowOrange);
        }
        else if (def.Kind == "trade")
        {   // 交易所: 箱 2x2 の上に屋根と橙の看板(専用部品は未作成)
            kb.Box(root, new Vector3(x + 1f, y + 1.3f, z + 1f), new Vector3(2.2f, 0.1f, 2.2f), kb.PaintDark);
            kb.Strip(root, new Vector3(x + 1f, y + 1.38f, z + 0.1f), new Vector3(1.6f, 0.06f, 0.06f), kb.GlowOrange);
        }
        else if (def.Kind == "port")
        {
            bool lift = def.Id.Contains("lift");
            kb.Box(root, new Vector3(x + 0.5f, y + 1.3f, z + 0.5f), new Vector3(0.9f, 0.6f, 0.9f), kb.PaintDark);
            var dim = new StandardMaterial3D { AlbedoColor = lift ? new Color(0.2f, 0.5f, 0.6f) : new Color(0.6f, 0.35f, 0.1f), EmissionEnabled = true, Emission = lift ? new Color(0.25f, 0.85f, 1f) : new Color(1f, 0.5f, 0.1f), EmissionEnergyMultiplier = 0.4f };
            kb.Box(root, new Vector3(x + 0.5f, y + 1.62f, z + 0.5f), new Vector3(0.6f, 0.04f, 0.6f), dim);
            if (lift && liftDir != 0)
            {   // 上り・下りの矢印: 縦の柱と 4 本の斜めの羽根(上り = 水色で上向き、下り = 橙で下向き)。グローで潰れないよう発光は弱め
                var c = liftDir > 0 ? new Color(0.25f, 0.85f, 1f) : new Color(1f, 0.5f, 0.1f);
                var glow = new StandardMaterial3D { AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = 0.9f };
                float dir = liftDir > 0 ? 1 : -1, cy = y + 2.1f;
                kb.Box(root, new Vector3(x + 0.5f, cy, z + 0.5f), new Vector3(0.12f, 1.0f, 0.12f), glow);
                for (int k = 0; k < 4; k++)
                {
                    float ang = k * 90;
                    var off = new Vector3(Mathf.Cos(Mathf.DegToRad(ang)) * 0.18f, 0, Mathf.Sin(Mathf.DegToRad(ang)) * 0.18f);
                    kb.Box(root, new Vector3(x + 0.5f, cy + dir * 0.32f, z + 0.5f) + off, new Vector3(0.1f, 0.5f, 0.1f), glow, new Vector3(0, -ang, dir * 45f));
                }
                kb.Box(root, new Vector3(x + 0.5f, cy + dir * 0.55f, z + 0.5f), new Vector3(0.28f, 0.06f, 0.28f), glow);
            }
        }
    }

    /// <summary>リアクターの発光部から立ち上る粒子。世代の色。</summary>
    static GpuParticles3D ReactorParticles(Color col, Vector3 at)
    {
        var mat = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.3f,
            Direction = new Vector3(0, 1, 0), Spread = 30f, InitialVelocityMin = 0.6f, InitialVelocityMax = 1.4f, Gravity = new Vector3(0, 0.4f, 0),
            ScaleMin = 0.5f, ScaleMax = 1.0f, Color = col, DampingMin = 0.3f, DampingMax = 0.3f,
        };
        var curve = new Curve(); curve.AddPoint(new Vector2(0, 1)); curve.AddPoint(new Vector2(1, 0));
        mat.ScaleCurve = new CurveTexture { Curve = curve };
        return new GpuParticles3D
        {
            Amount = 40, Lifetime = 1.8f, Position = at, ProcessMaterial = mat, Emitting = true, Preprocess = 1.5f,
            DrawPass1 = new SphereMesh { Radius = 0.1f, Height = 0.2f, Material = new StandardMaterial3D { AlbedoColor = col, EmissionEnabled = true, Emission = col, EmissionEnergyMultiplier = 3f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } },
        };
    }

    /// <summary>溜めた設備を MultiMesh として親に追加する。場面の構築の最後に 1 回呼ぶ。</summary>
    public void Flush(Node3D parent)
    {
        foreach (var (id, xforms) in _placed)
        {
            foreach (var (mesh, local) in Parts(id))
            {
                var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = xforms.Count };
                for (int i = 0; i < xforms.Count; i++) mm.SetInstanceTransform(i, xforms[i] * local);
                parent.AddChild(new MultiMeshInstance3D { Multimesh = mm, Name = $"eq_{id}_{mesh.GetInstanceId()}" });
            }
        }
        _placed.Clear();
    }

    /// <summary>部品の GLB を 1 度だけ読み、メッシュ(材質差し替え済み)と原点からの相対変換の一覧にする。</summary>
    List<(Mesh, Transform3D)> Parts(string id)
    {
        if (_parts.TryGetValue(id, out var cached)) return cached;
        var ps = GD.Load<PackedScene>($"res://assets/equipment/{id}.glb");
        var root = (Node3D)ps.Instantiate();
        var list = new List<(Mesh, Transform3D)>();
        Collect(root, Transform3D.Identity, list);
        root.QueueFree();
        return _parts[id] = list;
    }

    void Collect(Node node, Transform3D acc, List<(Mesh, Transform3D)> list)
    {
        var xf = node is Node3D n3 ? acc * n3.Transform : acc;
        if (node is MeshInstance3D mi && mi.Mesh is ArrayMesh am)
        {
            var mesh = (ArrayMesh)am.Duplicate();
            for (int i = 0; i < mesh.GetSurfaceCount(); i++)
            {
                var m = mi.GetActiveMaterial(i);
                if (m != null && _fm.Materials.TryGetValue(m.ResourceName, out var rep)) mesh.SurfaceSetMaterial(i, rep);
            }
            list.Add((mesh, xf));
        }
        foreach (var c in node.GetChildren()) Collect(c, xf, list);
    }

    /// <summary>平地 1 ブロックや建物の 1 階(w×d)に工場の見本を敷く: 5 列周期の生産ライン(ベルト・インサータ・機械・インサータ・ベルト)、20 列ごとの通路、縁に採掘機・タンク・箱・電柱。</summary>
    public void SampleFactory(int ox, float y, int oz, int w, int d, Random rnd)
    {
        for (int j = 0; j + 4 < d; j += 6)
        {
            for (int i = 0; i < w; i++)
            {
                if (i % 20 == 19 || i % 20 == 0) continue;   // 通路
                Place("belt", ox + i, y, oz + j, 90);
                Place("inserter", ox + i, y, oz + j + 1, 180);
                Place(rnd.NextDouble() < 0.5 ? "furnace" : "assembler", ox + i, y, oz + j + 2, 180);
                Place("inserter", ox + i, y, oz + j + 3, 180);
                Place("belt", ox + i, y, oz + j + 4, 270);
            }
            for (int i = 0; i < w; i += 20)
            {
                Place("pole", ox + i, y, oz + j + 2, 0);
                if (i + 19 < w) Place(rnd.NextDouble() < 0.5 ? "tank" : "chest", ox + i + 19, y, oz + j + 2, 0);
            }
        }
        for (int i = 2; i < w; i += 7) Place("miner", ox + i, y, oz + d - 1, 0);
    }

    /// <summary>平地 1 ブロック(5x5)の小さな見本(近景用)。</summary>
    public void SampleGround(int ox, float y, int oz)
    {
        Place("miner", ox + 0, y, oz + 4, 0); Place("belt", ox + 0, y, oz + 3, 0); Place("belt", ox + 0, y, oz + 2, 0); Place("belt_turn", ox + 0, y, oz + 1, 0);
        Place("belt", ox + 1, y, oz + 1, 90); Place("inserter", ox + 2, y, oz + 1, 90); Place("furnace", ox + 3, y, oz + 1, 180); Place("inserter", ox + 3, y, oz + 2, 180);
        Place("belt", ox + 3, y, oz + 3, 180); Place("inserter", ox + 3, y, oz + 4, 180); Place("assembler", ox + 4, y, oz + 4, 270); Place("chest", ox + 4, y, oz + 2, 0);
        Place("inserter", ox + 4, y, oz + 3, 0); Place("tank", ox + 2, y, oz + 4, 0); Place("pole", ox + 1, y, oz + 3, 0);
    }
}
