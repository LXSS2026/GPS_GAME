using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// 階モジュール方式。建物は階モジュール(Blender 生成の GLB)の積み重ね、戦艦は船体部品の並び。
/// モジュール定義は data/modules.json(Blender のスクリプトが書く)、形状は assets/modules/<id>.glb。
/// 接続規則: 上のモジュールの床面積(w, d)は下のモジュールの上端面積(topW, topD)と一致すること。
/// 切り取り表示: 指定階より下の外装(名前が ext_ で始まる節)を隠し、内部に設備を置く。
/// </summary>
public sealed class FloorModules
{
    public sealed record Module(string Id, string Kind, string Family, int W, int D, int TopW, int TopD, int Length);
    public readonly float FloorHeight;
    public readonly Dictionary<string, Module> Defs = new();
    readonly Dictionary<string, PackedScene> _scenes = new();
    readonly KitBash _kb;
    readonly Dictionary<string, Material> _mats;
    readonly Random _rnd;

    public FloorModules(KitBash kb, int seed = 5)
    {
        _kb = kb;
        _rnd = new Random(seed);
        var doc = JsonDocument.Parse(FileAccess.GetFileAsString("res://data/modules.json"));
        FloorHeight = (float)doc.RootElement.GetProperty("floorHeight").GetDouble();
        foreach (var m in doc.RootElement.GetProperty("modules").EnumerateArray())
            Defs[m.GetProperty("id").GetString()!] = new Module(m.GetProperty("id").GetString()!, m.GetProperty("kind").GetString()!, m.GetProperty("family").GetString()!,
                m.GetProperty("w").GetInt32(), m.GetProperty("d").GetInt32(), m.GetProperty("topW").GetInt32(), m.GetProperty("topD").GetInt32(), m.GetProperty("length").GetInt32());
        var winLit = new StandardMaterial3D { AlbedoColor = new Color(1.0f, 0.82f, 0.55f), EmissionEnabled = true, Emission = new Color(1.0f, 0.82f, 0.55f), EmissionEnergyMultiplier = 1.8f, Roughness = 0.2f };
        var winCool = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.85f, 1.0f), EmissionEnabled = true, Emission = new Color(0.55f, 0.85f, 1.0f), EmissionEnergyMultiplier = 1.8f, Roughness = 0.2f };
        var armor = (StandardMaterial3D)kb.Paint.Duplicate(); armor.AlbedoColor = new Color(0.42f, 0.45f, 0.5f); armor.Metallic = 0.6f;
        // 部品の発光帯は数が多いので控えめにする(グローで白飛びしないように)
        var glowCyan = (StandardMaterial3D)kb.GlowCyan.Duplicate(); glowCyan.EmissionEnergyMultiplier = 1.6f;
        var glowOrange = (StandardMaterial3D)kb.GlowOrange.Duplicate(); glowOrange.EmissionEnergyMultiplier = 2.2f;
        _mats = new()
        {
            ["M_Steel"] = kb.Steel, ["M_Concrete"] = kb.Concrete, ["M_PaintDark"] = kb.PaintDark, ["M_Paint"] = kb.Paint, ["M_Rust"] = kb.Rust,
            ["M_Glass"] = kb.Glass, ["M_WinLit"] = winLit, ["M_WinCool"] = winCool, ["M_GlowCyan"] = glowCyan, ["M_GlowOrange"] = glowOrange,
            ["M_GlowRed"] = kb.GlowRed, ["M_Rubber"] = kb.Rubber, ["M_Hazard"] = kb.Hazard, ["M_Grate"] = kb.Grate, ["M_Foliage"] = kb.Foliage, ["M_Armor"] = armor,
        };
    }

    public bool Has(string id) => Defs.ContainsKey(id);

    Node3D Instantiate(string id)
    {
        if (!_scenes.TryGetValue(id, out var ps))
            _scenes[id] = ps = GD.Load<PackedScene>($"res://assets/modules/{id}.glb");
        var n = (Node3D)ps.Instantiate();
        Swap(n);
        return n;
    }

    void Swap(Node node)
    {
        foreach (var c in node.GetChildren()) Swap(c);
        if (node is not MeshInstance3D mi || mi.Mesh == null) return;
        for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
        {
            var m = mi.GetActiveMaterial(i);
            if (m != null && _mats.TryGetValue(m.ResourceName, out var rep)) mi.SetSurfaceOverrideMaterial(i, rep);
        }
    }

    static void SetExteriorVisible(Node node, bool visible)
    {
        foreach (var c in node.GetChildren()) SetExteriorVisible(c, visible);
        if (node is Node3D n3 && node.Name.ToString().StartsWith("ext_")) n3.Visible = visible;
    }

    // ---------- 積み上げ ----------
    public Node3D Build(Node3D parent, float x, float y, float z, IList<string> stack, float rotDeg = 0, int cutawayBelow = -1)
    {
        var root = new Node3D { Position = new Vector3(x, y, z), RotationDegrees = new Vector3(0, rotDeg, 0) };
        parent.AddChild(root);
        (int w, int d) expect = (-1, -1);
        float cy = 0;
        int maxW = 0, maxD = 0;
        for (int f = 0; f < stack.Count; f++)
        {
            var def = Defs[stack[f]];
            if (expect.w >= 0 && (def.W, def.D) != expect) throw new InvalidOperationException($"{stack[f]}: {def.W}x{def.D} != expected {expect.w}x{expect.d}");
            expect = (def.TopW, def.TopD);
            maxW = Math.Max(maxW, def.W); maxD = Math.Max(maxD, def.D);
            var n = Instantiate(def.Id);
            n.Position = new Vector3(0, cy, 0);
            root.AddChild(n);
            if (f < cutawayBelow && def.Kind != "roof")
            {
                SetExteriorVisible(n, false);
                Interior(root, cy + 0.12f, def.W, def.D, f);
            }
            if (def.Kind != "roof")
            {
                float w = def.TopW > 0 ? def.TopW : def.W, d = def.TopD > 0 ? def.TopD : def.D;
                _kb.Box(root, new Vector3(0, cy + FloorHeight, 0), new Vector3(w + 0.36f, 0.08f, d + 0.36f), _kb.Steel);
            }
            cy += def.Kind == "roof" ? 0 : FloorHeight;
        }
        float cx = maxW / 2f - 0.35f, cz = maxD / 2f - 0.35f;
        _kb.Box(root, new Vector3(-cx, cy / 2, -cz), new Vector3(0.5f, cy, 0.5f), _kb.PaintDark);
        for (float yy = 0.3f; yy < cy; yy += 1.0f) _kb.Strip(root, new Vector3(-cx, yy, -cz + 0.26f), new Vector3(0.3f, 0.04f, 0.02f), _kb.GlowCyan);
        return root;
    }

    /// <summary>積み方の計画。family: office / res / concrete / deco / exo / svc。後退階があれば途中で細くなる。</summary>
    public List<string> Plan(string family, int w, int d, int floors, bool allowTransfer = true, string? roofKind = null)
    {
        string fam2 = family == "concrete" ? "concrete" : "office";
        var stack = new List<string> { $"base_{w}x{d}_{fam2}" };
        string Std(int ww, int dd) => family switch
        {
            "office" => _rnd.NextDouble() < 0.5 ? $"office_{ww}x{dd}" : $"office_{ww}x{dd}_c",
            _ => $"{family}_{ww}x{dd}",
        };
        int transferAt = allowTransfer && floors >= 8 ? (int)(floors * 0.6f) : -1;
        int cw = w, cd = d;
        for (int f = 1; f < floors; f++)
        {
            if (f == transferAt)
            {
                var cands = new[] { ((5, 5), (3, 3)), ((5, 5), (4, 6)), ((4, 6), (3, 3)), ((3, 3), (2, 2)) };
                foreach (var ((a, b), (c, e)) in cands)
                {
                    string id = $"transfer_{a}x{b}_{c}x{e}_{fam2}";
                    if ((cw, cd) == (a, b) && Has(id)) { stack.Add(id); cw = c; cd = e; goto next; }
                }
            }
            stack.Add(f % 6 == 5 ? $"mech_{cw}x{cd}_{fam2}" : Std(cw, cd));
        next:;
        }
        roofKind ??= family == "concrete" ? (_rnd.NextDouble() < 0.5 ? "boiler" : "mech") : new[] { "mech", "heli", "spire" }[_rnd.Next(3)];
        stack.Add($"roof_{cw}x{cd}_{roofKind}_{fam2}");
        return stack;
    }

    /// <summary>双塔: 3x3 の 2 塔を x 方向に gap セル離して建て、floor 階に渡り廊下を架ける。</summary>
    public void Twin(Node3D parent, float x, float y, float z, string family, int floors, int gap, int bridgeFloor, float rotDeg = 0)
    {
        var root = new Node3D { Position = new Vector3(x, y, z), RotationDegrees = new Vector3(0, rotDeg, 0) };
        parent.AddChild(root);
        float off = (3 + gap) / 2f;
        Build(root, -off, 0, 0, Plan(family, 3, 3, floors, false, "spire"));
        Build(root, off, 0, 0, Plan(family, 3, 3, floors, false, "spire"));
        var b = Instantiate($"bridge_{gap}");
        b.Position = new Vector3(0, bridgeFloor * FloorHeight + 0.14f, 0);
        root.AddChild(b);
    }

    /// <summary>戦艦: 船体部品を x 方向(艦尾 → 艦首)に並べる。幅 W。stern/mid/hangar/turret/bridge/bow の id を順に。</summary>
    public Node3D Ship(Node3D parent, float x, float y, float z, IList<string> segments, float rotDeg = 0, int cutawayIndex = -1)
    {
        var root = new Node3D { Position = new Vector3(x, y, z), RotationDegrees = new Vector3(0, rotDeg, 0) };
        parent.AddChild(root);
        float total = 0;
        foreach (var id in segments) total += Defs[id].Length;
        float cursor = -total / 2;
        for (int i = 0; i < segments.Count; i++)
        {
            var def = Defs[segments[i]];
            var n = Instantiate(def.Id);
            n.Position = new Vector3(cursor + def.Length / 2f, 0, 0);
            root.AddChild(n);
            if (i == cutawayIndex)
            {
                SetExteriorVisible(n, false);
                Interior(root, 0.45f, def.Length, def.W, 1, cursor + def.Length / 2f);
            }
            cursor += def.Length;
        }
        // 船底の支持(地上に据える台)
        for (float sx = -total / 2 + 2; sx < total / 2 - 1; sx += 4) _kb.Box(root, new Vector3(sx, -0.3f, 0), new Vector3(0.8f, 0.6f, Defs[segments[1]].W + 1.0f), _kb.Concrete);
        return root;
    }

    void Interior(Node3D root, float y, int w, int d, int floor, float ox = 0)
    {
        float hw = w / 2f - 0.5f, hd = d / 2f - 0.5f;
        switch (floor % 3)
        {
            case 0:
                for (int i = 0; i < w - 1; i++) _kb.Belt(root, ox - hw + i, y, hd, 0, i % 2 == 0);
                _kb.Furnace(root, ox + hw, y, hd, 0);
                _kb.Chest(root, ox - hw, y, -hd + 0.2f, 0);
                if (w >= 5) { _kb.Assembler(root, ox, y, 0, 0); _kb.Tank(root, ox - hw, y, 0, 0.3f, 1.1f); }
                break;
            case 1:
                _kb.Assembler(root, ox - hw + 0.5f, y, 0, 90);
                _kb.Assembler(root, ox + hw - 0.5f, y, 0, -90);
                for (int i = 0; i < w - 1; i++) _kb.Belt(root, ox - hw + 0.5f + i, y, -hd, 0, i % 2 == 1);
                break;
            default:
                for (int i = 0; i < w; i++) _kb.Humanoid(root, ox - hw + i, y, 0.3f, 180, true);
                _kb.Tank(root, ox + hw, y, -hd, 0.3f, 1.1f);
                break;
        }
        _kb.Light(root, new Vector3(ox, y + 1.5f, 0), new Color(0.9f, 0.9f, 1.0f), Math.Max(w, d) * 1.2f, 1.2f);
    }
}
