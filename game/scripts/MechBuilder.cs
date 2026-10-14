using System.Collections.Generic;
using System.Text.Json;
using Godot;

/// <summary>機体のキャラクリ: 部位部品(胴・頭・腕・脚・背部・武装)を取り付け口で組み、配色とリアクターの色(世代)を与える。
/// 部品は tools/blender/make_mech_parts.py が assets/mech/*.glb と data/mech_parts.json に書く。
/// 座標は GLB(Y 上、Blender の +Y 正面が -Z)。Character() と同じく全体を +180 度回して +Z を正面にする。</summary>
public sealed class MechBuilder
{
    public sealed record Part(string Id, string Slot, string Archetype, Dictionary<string, Vector3> Sockets, float Length);
    public sealed record Loadout(string Torso, string Head, string ArmL, string ArmR, string LegL, string LegR, string Back, string? WeaponR, string? WeaponL,
                                 Color Main, Color Sub, Color Trim, Color Glow)
    {
        public Loadout With(string? torso = null, string? head = null, string? arms = null, string? legs = null, string? back = null, string? weaponR = null, string? weaponL = null)
            => this with { Torso = torso ?? Torso, Head = head ?? Head, ArmL = arms != null ? $"arm_{arms}_l" : ArmL, ArmR = arms != null ? $"arm_{arms}_r" : ArmR,
                           LegL = legs != null ? $"leg_{legs}_l" : LegL, LegR = legs != null ? $"leg_{legs}_r" : LegR, Back = back ?? Back, WeaponR = weaponR ?? WeaponR, WeaponL = weaponL ?? WeaponL };
    }

    public readonly Dictionary<string, Part> Parts = new();
    public readonly Dictionary<string, Loadout> Presets = new();
    public readonly Dictionary<string, Color> Generations = new();   // 世代はリアクターの色で表す
    readonly Dictionary<string, PackedScene> _scenes = new();
    readonly FloorModules _fm;

    public MechBuilder(FloorModules fm)
    {
        _fm = fm;
        using var doc = JsonDocument.Parse(FileAccess.GetFileAsString("res://data/mech_parts.json"));
        foreach (var p in doc.RootElement.GetProperty("parts").EnumerateArray())
        {
            var sockets = new Dictionary<string, Vector3>();
            foreach (var s in p.GetProperty("sockets").EnumerateObject())
                sockets[s.Name] = new Vector3(s.Value[0].GetSingle(), s.Value[1].GetSingle(), s.Value[2].GetSingle());
            string id = p.GetProperty("id").GetString()!;
            Parts[id] = new Part(id, p.GetProperty("slot").GetString()!, p.GetProperty("archetype").GetString()!, sockets, p.TryGetProperty("length", out var l) ? l.GetSingle() : 0);
        }
        foreach (var pr in doc.RootElement.GetProperty("presets").EnumerateObject())
        {
            var v = pr.Value; var c = v.GetProperty("colors");
            Presets[pr.Name] = new Loadout(S(v, "torso"), S(v, "head"), S(v, "arm_l"), S(v, "arm_r"), S(v, "leg_l"), S(v, "leg_r"), S(v, "back"), S(v, "weapon_r"), S(v, "weapon_l"),
                                           C(c, "main"), C(c, "sub"), C(c, "trim"), C(c, "glow"));
        }
        foreach (var g in doc.RootElement.GetProperty("generations").EnumerateObject()) Generations[g.Name] = C(g.Value, "reactor");
        static string S(JsonElement e, string k) => e.GetProperty(k).GetString()!;
        static Color C(JsonElement e, string k) { var a = e.GetProperty(k); return new Color(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle()); }
    }

    /// <summary>機体を 1 体組む。(x, y, z) は足元の中心。構えは腕を少し外へ、右腕を少し前へ、肘を曲げる。</summary>
    public Node3D Build(Node3D parent, Loadout lo, string generation, float x, float y, float z, float rotDeg)
    {
        var mats = Materials(lo, Generations[generation]);
        var root = new Node3D { Name = "mech", Position = new Vector3(x, y, z), RotationDegrees = new Vector3(0, rotDeg + 180, 0) };
        parent.AddChild(root);
        var torsoPart = Parts[lo.Torso];
        float legLen = Mathf.Max(Parts[lo.LegL].Length, Parts[lo.LegR].Length);
        var torso = Attach(root, lo.Torso, new Vector3(0, legLen, 0), mats);
        Attach(torso, lo.Head, torsoPart.Sockets["neck"], mats);
        Attach(torso, lo.Back, torsoPart.Sockets["back"], mats);
        var legL = Attach(torso, lo.LegL, torsoPart.Sockets["hip_l"], mats);
        var legR = Attach(torso, lo.LegR, torsoPart.Sockets["hip_r"], mats);
        legL.RotationDegrees = new Vector3(0, 6, -3); legR.RotationDegrees = new Vector3(0, -6, 3);   // 足先を少し外へ開く(内股にしない)
        var armL = Attach(torso, lo.ArmL, torsoPart.Sockets["shoulder_l"], mats);
        var armR = Attach(torso, lo.ArmR, torsoPart.Sockets["shoulder_r"], mats);
        armL.RotationDegrees = new Vector3(10, 0, 12); armR.RotationDegrees = new Vector3(-18, 0, -12);
        Bend(armL, 22); Bend(armR, 30);
        if (lo.WeaponR != null) Attach(armR.GetNodeOrNull<Node3D>("elbow") ?? armR, lo.WeaponR, Parts[lo.ArmR].Sockets["hand"], mats);
        if (lo.WeaponL != null) Attach(armL.GetNodeOrNull<Node3D>("elbow") ?? armL, lo.WeaponL, Parts[lo.ArmL].Sockets["hand"], mats);
        return root;
    }

    public Node3D Build(Node3D parent, string preset, string generation, float x, float y, float z, float rotDeg) => Build(parent, Presets[preset], generation, x, y, z, rotDeg);

    /// <summary>戦闘機モード(高速機の変形)。胴を前へ倒して頭を機首に、脚を後ろへ伸ばしてエンジンに、腕を胴の下へ畳み、背部の翼を上面に広げる。(x, y, z) は機体の中心。</summary>
    public Node3D BuildFighter(Node3D parent, Loadout lo, string generation, float x, float y, float z, float rotDeg)
    {
        var mats = Materials(lo, Generations[generation]);
        var root = new Node3D { Name = "fighter", Position = new Vector3(x, y, z), RotationDegrees = new Vector3(0, rotDeg + 180, 0) };
        parent.AddChild(root);
        var tp = Parts[lo.Torso];
        var torso = Attach(root, lo.Torso, new Vector3(0, 0, -tp.Sockets["neck"].Y * 0.5f), mats);
        torso.RotationDegrees = new Vector3(-90, 0, 0);   // 頭が前(-Z)を向き、胸が下を向く
        var head = Attach(torso, lo.Head, tp.Sockets["neck"], mats);
        head.RotationDegrees = new Vector3(20, 0, 0);     // 機首: 少し下げる
        var back = Attach(torso, lo.Back, tp.Sockets["back"], mats);
        back.RotationDegrees = new Vector3(0, 0, 180);   // 背部を上下反転し、立ち姿で上へ開く翼を後方へ水平に広げる
        var legL = Attach(torso, lo.LegL, tp.Sockets["hip_l"], mats);
        var legR = Attach(torso, lo.LegR, tp.Sockets["hip_r"], mats);
        legL.RotationDegrees = new Vector3(-8, 0, -2); legR.RotationDegrees = new Vector3(-8, 0, 2);   // 後方へ伸びたエンジン
        var armL = Attach(torso, lo.ArmL, tp.Sockets["shoulder_l"], mats);
        var armR = Attach(torso, lo.ArmR, tp.Sockets["shoulder_r"], mats);
        armL.RotationDegrees = new Vector3(0, 0, 0); armR.RotationDegrees = new Vector3(0, 0, 0);
        Bend(armL, 10); Bend(armR, 10);   // 腕は脚と同じく後方へ伸ばす(畳むと機首を隠す)
        if (lo.WeaponR != null) Attach(armR.GetNodeOrNull<Node3D>("elbow") ?? armR, lo.WeaponR, Parts[lo.ArmR].Sockets["hand"], mats);
        return root;
    }

    static void Bend(Node3D arm, float deg)
    {
        var e = arm.GetNodeOrNull<Node3D>("elbow");
        if (e != null) e.RotationDegrees = new Vector3(-deg, 0, 0);
    }

    Node3D Attach(Node3D parent, string id, Vector3 at, Dictionary<string, Material> mats)
    {
        if (!_scenes.TryGetValue(id, out var ps)) _scenes[id] = ps = GD.Load<PackedScene>($"res://assets/mech/{id}.glb");
        var n = (Node3D)ps.Instantiate();
        n.Name = id;
        Swap(n, mats);
        n.Position = at;
        parent.AddChild(n);
        return n;
    }

    void Swap(Node node, Dictionary<string, Material> mats)
    {
        foreach (var c in node.GetChildren()) Swap(c, mats);
        if (node is not MeshInstance3D mi || mi.Mesh == null) return;
        for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
        {
            var m = mi.GetActiveMaterial(i);
            if (m == null) continue;
            if (mats.TryGetValue(m.ResourceName, out var rep) || _fm.Materials.TryGetValue(m.ResourceName, out rep)) mi.SetSurfaceOverrideMaterial(i, rep);
        }
    }

    static Dictionary<string, Material> Materials(Loadout lo, Color reactor) => new()
    {
        ["M_Main"] = Paint(lo.Main, 0.35f, 0.45f),
        ["M_Sub"] = Paint(lo.Sub, 0.35f, 0.5f),
        ["M_Trim"] = Paint(lo.Trim, 0.3f, 0.5f),
        ["M_GlowMech"] = Glow(lo.Glow, 1.2f),
        ["M_Reactor"] = Glow(reactor, 0.5f),   // 世代の色が読めるようグローで白飛びしない強さ
    };

    static StandardMaterial3D Paint(Color c, float metallic, float rough) => new() { AlbedoColor = c, Metallic = metallic, Roughness = rough };
    static StandardMaterial3D Glow(Color c, float energy) => new() { AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = energy, Roughness = 0.4f };
}
