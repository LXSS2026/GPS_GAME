using System.Collections.Generic;
using System.Text.Json;
using Godot;

/// <summary>機体のキャラクリ: 部位部品(胴・頭・腕・脚・背部・武装)を取り付け口で組み、配色とリアクターの色(世代)を与える。
/// 部品は tools/blender/make_mech_parts.py が assets/mech/*.glb と data/mech_parts.json に書く。
/// 座標は GLB(Y 上、Blender の +Y 正面が -Z)。Character() と同じく全体を +180 度回して +Z を正面にする。</summary>
public sealed class MechBuilder
{
    public sealed record Part(string Id, string Slot, string Archetype, Dictionary<string, Vector3> Sockets, float Length);
    /// <summary>武装の口: 手(左右)・肩上(重装のみ)・腰左右(近接武器)・腰背部(予備 1、上限に数えない)。装備数の上限は data/mech_parts.json の weapons にある(計算は未実装)。</summary>
    public sealed record Loadout(string Torso, string Head, string ArmL, string ArmR, string LegL, string LegR, string Back, string? Wing, string? WeaponR, string? WeaponL,
                                 string? ShoulderL, string? ShoulderR, string? MeleeL, string? MeleeR, string? Spare, Color Main, Color Sub, Color Trim, Color Glow)
    {
        public Loadout With(string? torso = null, string? head = null, string? arms = null, string? legs = null, string? back = null, string? weaponR = null, string? weaponL = null,
                            string? shoulderL = null, string? shoulderR = null, string? meleeL = null, string? meleeR = null, string? spare = null)
            => this with { Torso = torso ?? Torso, Head = head ?? Head, ArmL = arms != null ? $"arm_{arms}_l" : ArmL, ArmR = arms != null ? $"arm_{arms}_r" : ArmR,
                           LegL = legs != null ? $"leg_{legs}_l" : LegL, LegR = legs != null ? $"leg_{legs}_r" : LegR, Back = back ?? Back, WeaponR = weaponR ?? WeaponR, WeaponL = weaponL ?? WeaponL,
                           ShoulderL = shoulderL ?? ShoulderL, ShoulderR = shoulderR ?? ShoulderR, MeleeL = meleeL ?? MeleeL, MeleeR = meleeR ?? MeleeR, Spare = spare ?? Spare };
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
            Presets[pr.Name] = new Loadout(S(v, "torso"), S(v, "head"), S(v, "arm_l"), S(v, "arm_r"), S(v, "leg_l"), S(v, "leg_r"), S(v, "back"), N(v, "wing"), N(v, "weapon_r"), N(v, "weapon_l"),
                                           N(v, "shoulder_l"), N(v, "shoulder_r"), N(v, "melee_l"), N(v, "melee_r"), N(v, "spare"), C(c, "main"), C(c, "sub"), C(c, "trim"), C(c, "glow"));
        }
        foreach (var g in doc.RootElement.GetProperty("generations").EnumerateObject()) Generations[g.Name] = C(g.Value, "reactor");
        static string S(JsonElement e, string k) => e.GetProperty(k).GetString()!;
        static string? N(JsonElement e, string k) => e.TryGetProperty(k, out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() : null;
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
        armL.RotationDegrees = new Vector3(lo.WeaponL != null ? -8 : 10, 0, 12); armR.RotationDegrees = new Vector3(-18, 0, -12);   // 武器を持つ腕は少し前へ
        Bend(armL, 22); Bend(armR, 30);
        if (lo.WeaponR != null) Hold(armR, lo.ArmR, lo.WeaponR, mats);
        if (lo.WeaponL != null) Hold(armL, lo.ArmL, lo.WeaponL, mats);
        if (lo.Wing != null) { var wg = Attach(torso, lo.Wing, torsoPart.Sockets["back"] + new Vector3(0, -0.3f, -0.2f), mats); wg.RotationDegrees = new Vector3(10, 0, 0); FoldWings(wg, 80); }   // 人型では翼を下向きに畳む
        Pauldron(armL, 70); Pauldron(armR, 70);   // 肩の羽根を立てて肩当てにする
        Holsters(torso, lo, torsoPart, mats);
        return root;
    }

    /// <summary>腰の武装: 近接武器を腰左右に柄を上にして吊り、予備を腰背部に銃口を上にして格納。重装は肩上の架にバズーカ。</summary>
    void Holsters(Node3D torso, Loadout lo, Part tp, Dictionary<string, Material> mats, bool fighter = false)
    {
        float hang = fighter ? 45 : 180;   // 人型は刃を下に、戦闘機モードは脚に沿って後ろに(刃は +Y なので 45 度で後ろ向き)
        if (lo.MeleeL != null) Attach(torso, Stowed(lo.MeleeL), tp.Sockets["waist_l"], mats).RotationDegrees = new Vector3(hang, 0, 10);
        if (lo.MeleeR != null) Attach(torso, Stowed(lo.MeleeR), tp.Sockets["waist_r"], mats).RotationDegrees = new Vector3(hang, 0, -10);
        if (lo.Spare != null) Attach(torso, lo.Spare, tp.Sockets["waist_rear"], mats).RotationDegrees = new Vector3(fighter ? 135 : 90, 0, 0);   // 人型は銃口を上に、戦闘機モードは脚に沿って後ろに
        if (lo.ShoulderL != null && tp.Sockets.ContainsKey("shoulder_mount_l")) Attach(torso, lo.ShoulderL, tp.Sockets["shoulder_mount_l"] + new Vector3(0, -0.6f, -1.5f), mats);
        if (lo.ShoulderR != null && tp.Sockets.ContainsKey("shoulder_mount_r")) Attach(torso, lo.ShoulderR, tp.Sockets["shoulder_mount_r"] + new Vector3(0, -0.6f, -1.5f), mats);
    }

    public Node3D Build(Node3D parent, string preset, string generation, float x, float y, float z, float rotDeg) => Build(parent, Presets[preset], generation, x, y, z, rotDeg);

    /// <summary>戦闘機モード(高速機の変形。フラッグの変形法 2026-10-14 指示): 立ち姿から股関節で脚を 90 度後ろへ折って水平に伸ばし、
    /// 胸を後ろへ仰け反らせ、背負った翼を水平に展開する。顔は前を向き、腕は肘を折ってライフルを前に構える。(x, y, z) は腰の位置。</summary>
    public Node3D BuildFighter(Node3D parent, Loadout lo, string generation, float x, float y, float z, float rotDeg)
    {
        const float lean = 45f;   // 胸の仰け反り(鉛直からの角度)
        var mats = Materials(lo, Generations[generation]);
        var root = new Node3D { Name = "fighter", Position = new Vector3(x, y, z), RotationDegrees = new Vector3(0, rotDeg + 180, 0) };
        parent.AddChild(root);
        var tp = Parts[lo.Torso];
        var torso = Attach(root, lo.Torso, new Vector3(0, 0, -2f), mats);
        torso.RotationDegrees = new Vector3(lean, 0, 0);                                            // 胸を後ろへ仰け反らせる
        var head = Attach(torso, lo.Head, tp.Sockets["neck"], mats);
        head.RotationDegrees = new Vector3(-lean, 0, 0);                                            // 顔は前を向く
        Attach(torso, lo.Back, tp.Sockets["back"], mats);
        if (lo.Wing != null) { var wg = Attach(torso, lo.Wing, tp.Sockets["back"] + new Vector3(0, -0.2f, 0.2f), mats); wg.RotationDegrees = new Vector3(-lean, 0, 0); FoldWings(wg, 0); }   // 背負った翼を水平に展開
        var legL = Attach(torso, lo.LegL, tp.Sockets["hip_l"], mats);
        var legR = Attach(torso, lo.LegR, tp.Sockets["hip_r"], mats);
        legL.RotationDegrees = new Vector3(-90 - lean, 0, -2); legR.RotationDegrees = new Vector3(-90 - lean, 0, 2);   // 股関節で 90 度後ろへ折る(腰の傾き分を足して水平に)
        Knee(legL, 0); Knee(legR, 0);
        var armL = Attach(torso, lo.ArmL, tp.Sockets["shoulder_l"], mats);
        var armR = Attach(torso, lo.ArmR, tp.Sockets["shoulder_r"], mats);
        const float armX = -lean - 10;   // 上腕は鉛直より少し前(仰け反り分を打ち消す)
        armL.RotationDegrees = new Vector3(armX, 0, 8); armR.RotationDegrees = new Vector3(armX, 0, -8);
        Bend(armL, 90); Bend(armR, 90);                                                     // 肘を 90 度折り、前腕を前へ(腰だめの構え)
        Pauldron(armL, -armX - lean); Pauldron(armR, -armX - lean);                         // 肩の羽根を水平にして前翼に
        if (lo.WeaponR != null) Hold(armR, lo.ArmR, lo.WeaponR, mats).RotationDegrees = new Vector3(30, 0, 0);   // 銃口を機体の前へ(手が 90 度前へ回った分を戻す)
        if (lo.WeaponL != null) Hold(armL, lo.ArmL, lo.WeaponL, mats).RotationDegrees = new Vector3(30, 0, 0);
        Holsters(torso, lo, tp, mats, fighter: true);
        return root;
    }

    /// <summary>手に武器を持たせる。手のひらの節(palm)の子にして、その中心から握りの位置へ少しずらす(肘の節の座標で計算すると肘を曲げた分ずれるため)。</summary>
    Node3D Hold(Node3D arm, string armId, string weaponId, Dictionary<string, Material> mats)
    {
        var palm = arm.FindChild("palm", true, false) as Node3D;
        return palm != null ? Attach(palm, weaponId, new Vector3(0, -0.17f, 0.34f), mats) : Attach(arm, weaponId, Parts[armId].Sockets["hand"], mats);
    }

    /// <summary>背部の翼を左右の節(wing_l / wing_r)で畳む。deg = 0 で水平、80 で下向き。</summary>
    static void FoldWings(Node3D wing, float deg)
    {
        if (wing.FindChild("wing_l", true, false) is Node3D l) l.RotationDegrees = new Vector3(0, 0, deg);
        if (wing.FindChild("wing_r", true, false) is Node3D r) r.RotationDegrees = new Vector3(0, 0, -deg);
    }

    /// <summary>肩の羽根の節(高速機のみ)。deg は腕の座標での傾き。</summary>
    static void Pauldron(Node3D arm, float deg)
    {
        if (arm.FindChild("pauldron_pivot", true, false) is Node3D p) p.RotationDegrees = new Vector3(deg, 0, 0);
    }

    static void Knee(Node3D leg, float deg)
    {
        var k = leg.GetNodeOrNull<Node3D>("knee");
        if (k != null) k.RotationDegrees = new Vector3(deg, 0, 0);
    }

    /// <summary>腰に吊る形。鞭は伸びた形のままでは向きが崩れるので、巻いた部品に差し替える。</summary>
    static string Stowed(string id) => id.Contains("whip") && !id.EndsWith("_coiled") ? id + "_coiled" : id;

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
