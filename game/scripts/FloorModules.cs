using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// 階モジュール方式。建物は階モジュール(Blender 生成の GLB)の積み重ね。
/// モジュール定義は data/modules.json、形状は assets/modules/<id>.glb。
/// 接続規則: 上のモジュールの footprint は下のモジュールの top と一致すること。
/// 切り取り表示: 指定階より下の外装(名前が ext_ で始まる節)を隠し、内部に設備を置く。
/// </summary>
public sealed class FloorModules
{
    public sealed record Module(string Id, string Kind, int Footprint, int Top);
    public readonly float FloorHeight;
    public readonly Dictionary<string, Module> Defs = new();
    readonly Dictionary<string, PackedScene> _scenes = new();
    readonly KitBash _kb;
    readonly Dictionary<string, Material> _mats;

    public FloorModules(KitBash kb)
    {
        _kb = kb;
        var doc = JsonDocument.Parse(FileAccess.GetFileAsString("res://data/modules.json"));
        FloorHeight = (float)doc.RootElement.GetProperty("floorHeight").GetDouble();
        foreach (var m in doc.RootElement.GetProperty("modules").EnumerateArray())
            Defs[m.GetProperty("id").GetString()!] = new Module(m.GetProperty("id").GetString()!, m.GetProperty("kind").GetString()!, m.GetProperty("footprint").GetInt32(), m.GetProperty("top").GetInt32());
        var winLit = new StandardMaterial3D { AlbedoColor = new Color(1.0f, 0.82f, 0.55f), EmissionEnabled = true, Emission = new Color(1.0f, 0.82f, 0.55f), EmissionEnergyMultiplier = 1.8f, Roughness = 0.2f };
        var winCool = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.85f, 1.0f), EmissionEnabled = true, Emission = new Color(0.55f, 0.85f, 1.0f), EmissionEnergyMultiplier = 1.8f, Roughness = 0.2f };
        // Blender の材質名 → Godot の PBR 材質(三平面投影)
        _mats = new()
        {
            ["M_Steel"] = kb.Steel, ["M_Concrete"] = kb.Concrete, ["M_PaintDark"] = kb.PaintDark, ["M_Paint"] = kb.Paint, ["M_Rust"] = kb.Rust,
            ["M_Glass"] = kb.Glass, ["M_WinLit"] = winLit, ["M_WinCool"] = winCool, ["M_GlowCyan"] = kb.GlowCyan, ["M_GlowOrange"] = kb.GlowOrange,
            ["M_GlowRed"] = kb.GlowRed, ["M_Rubber"] = kb.Rubber, ["M_Hazard"] = kb.Hazard, ["M_Grate"] = kb.Grate, ["M_Foliage"] = kb.Foliage,
        };
    }

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

    /// <summary>
    /// 積み上げ。stack は下から上のモジュール id。cutawayBelow を指定すると、その階より下の外装を隠し内部に設備を置く。
    /// 戻り値は建物の根。接続規則に反する積み方は例外。
    /// </summary>
    public Node3D Build(Node3D parent, float x, float y, float z, IList<string> stack, float rotDeg = 0, int cutawayBelow = -1)
    {
        var root = new Node3D { Position = new Vector3(x, y, z), RotationDegrees = new Vector3(0, rotDeg, 0) };
        parent.AddChild(root);
        int expect = -1;
        float cy = 0;
        int maxFoot = 0;
        for (int f = 0; f < stack.Count; f++)
        {
            var def = Defs[stack[f]];
            if (expect >= 0 && def.Footprint != expect) throw new InvalidOperationException($"{stack[f]}: footprint {def.Footprint} != expected {expect}");
            expect = def.Top;
            maxFoot = Math.Max(maxFoot, def.Footprint);
            var n = Instantiate(def.Id);
            n.Position = new Vector3(0, cy, 0);
            root.AddChild(n);
            bool cut = f < cutawayBelow && def.Kind != "roof";
            if (cut)
            {
                SetExteriorVisible(n, false);
                Interior(root, cy + 0.12f, def.Footprint, f);
            }
            // 襟: 階の境目の床板と縁の発光帯(継ぎ目を隠す)
            if (def.Kind != "roof")
            {
                float w = def.Top > 0 ? def.Top : def.Footprint;
                _kb.Box(root, new Vector3(0, cy + FloorHeight, 0), new Vector3(w + 0.36f, 0.08f, w + 0.36f), _kb.Steel);
            }
            cy += def.Kind == "roof" ? 0 : FloorHeight;
        }
        // 昇降機の芯(奥の角を全高で貫く)
        float core = maxFoot / 2f - 0.35f;
        _kb.Box(root, new Vector3(-core, cy / 2, -core), new Vector3(0.5f, cy, 0.5f), _kb.PaintDark);
        for (float yy = 0.3f; yy < cy; yy += 1.0f) _kb.Strip(root, new Vector3(-core, yy, -core + 0.26f), new Vector3(0.3f, 0.04f, 0.02f), _kb.GlowCyan);
        return root;
    }

    /// <summary>切り取った階の内部: 設備を格子に置く(表示の試作。計算とは無関係)。</summary>
    void Interior(Node3D root, float y, int footprint, int floor)
    {
        float half = footprint / 2f - 0.5f;
        switch (floor % 3)
        {
            case 0:
                for (int i = 0; i < footprint - 1; i++) _kb.Belt(root, -half + i, y, half, 0, i % 2 == 0);
                _kb.Furnace(root, half, y, half, 0);
                _kb.Chest(root, -half, y, -half + 0.2f, 0);
                break;
            case 1:
                _kb.Assembler(root, -half + 0.5f, y, 0, 90);
                _kb.Assembler(root, half - 0.5f, y, 0, -90);
                for (int i = 0; i < footprint - 1; i++) _kb.Belt(root, -half + 0.5f + i, y, -half, 0, i % 2 == 1);
                break;
            default:
                for (int i = 0; i < footprint; i++) _kb.Humanoid(root, -half + i, y, 0.3f, 180, true);
                _kb.Tank(root, half, y, -half, 0.3f, 1.1f);
                break;
        }
        _kb.Light(root, new Vector3(0, y + 1.5f, 0), new Color(0.9f, 0.9f, 1.0f), footprint * 1.2f, 1.2f);
    }
}
