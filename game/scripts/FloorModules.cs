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
    /// <summary>Floors: 部品が占める階数(格納庫は 6)。戦艦の部品は Belly(船腹の高さ)・Decks(甲板数)・BayH(格納庫の高さ、格納庫区画のみ)を持つ。</summary>
    public sealed record Module(string Id, string Kind, string Family, int W, int D, int TopW, int TopD, int Length, int Floors = 1, float Belly = 0, int Decks = 0, float BayH = 0);
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
                m.GetProperty("w").GetInt32(), m.GetProperty("d").GetInt32(), m.GetProperty("topW").GetInt32(), m.GetProperty("topD").GetInt32(), m.GetProperty("length").GetInt32(),
                m.TryGetProperty("floors", out var fl) ? fl.GetInt32() : 1, m.TryGetProperty("belly", out var be) ? be.GetSingle() : 0, m.TryGetProperty("decks", out var dk) ? dk.GetInt32() : 0, m.TryGetProperty("bayH", out var bh) ? bh.GetSingle() : 0);
        var winLit = new StandardMaterial3D { AlbedoColor = new Color(1.0f, 0.82f, 0.55f), EmissionEnabled = true, Emission = new Color(1.0f, 0.82f, 0.55f), EmissionEnergyMultiplier = 1.8f, Roughness = 0.2f };
        var winCool = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.85f, 1.0f), EmissionEnabled = true, Emission = new Color(0.55f, 0.85f, 1.0f), EmissionEnergyMultiplier = 1.8f, Roughness = 0.2f };
        var armor = (StandardMaterial3D)kb.Paint.Duplicate(); armor.AlbedoColor = new Color(0.42f, 0.45f, 0.5f); armor.Metallic = 0.6f;
        // 部品の発光帯は数が多いので控えめにする(グローで白飛びしないように)
        var glowCyan = (StandardMaterial3D)kb.GlowCyan.Duplicate(); glowCyan.EmissionEnergyMultiplier = 0.9f;
        var glowOrange = (StandardMaterial3D)kb.GlowOrange.Duplicate(); glowOrange.EmissionEnergyMultiplier = 1.6f;
        _mats = new()
        {
            ["M_Steel"] = kb.Steel, ["M_Concrete"] = kb.Concrete, ["M_PaintDark"] = kb.PaintDark, ["M_Paint"] = kb.Paint, ["M_Rust"] = kb.Rust,
            ["M_Glass"] = kb.Glass, ["M_WinLit"] = winLit, ["M_WinCool"] = winCool, ["M_GlowCyan"] = glowCyan, ["M_GlowOrange"] = glowOrange,
            ["M_GlowRed"] = kb.GlowRed, ["M_Rubber"] = kb.Rubber, ["M_Hazard"] = kb.Hazard, ["M_Grate"] = kb.Grate, ["M_Foliage"] = kb.Foliage, ["M_Armor"] = armor,
            ["M_Skin"] = new StandardMaterial3D { AlbedoColor = new Color(0.72f, 0.58f, 0.48f), Roughness = 0.7f },
            ["M_GlowGreen"] = kb.GlowGreen,
            ["M_Cloth"] = new StandardMaterial3D { AlbedoColor = new Color(0.22f, 0.24f, 0.28f), Roughness = 0.95f },
        };
    }

    public bool Has(string id) => Defs.ContainsKey(id);

    // ---------- 人型(Blender 生成: ロボット 5 世代と人間 2 種) ----------
    readonly Dictionary<string, PackedScene> _chars = new();

    /// <summary>人型を置く。id: robot_g0〜robot_g4 / citizen / soldier。原点は足元、正面は +Z。</summary>
    public Node3D Character(Node3D parent, string id, float x, float y, float z, float rotDeg)
    {
        if (!_chars.TryGetValue(id, out var ps))
            _chars[id] = ps = GD.Load<PackedScene>($"res://assets/characters/{id}.glb");
        var n = (Node3D)ps.Instantiate();
        Swap(n);
        n.Position = new Vector3(x, y, z);
        n.RotationDegrees = new Vector3(0, rotDeg + 180, 0);   // Blender の +Y 正面は GLB で -Z になるため、+Z を正面に揃える
        parent.AddChild(n);
        return n;
    }

    public IReadOnlyDictionary<string, Material> Materials => _mats;
    public string RandomHuman() => _rnd.NextDouble() < 0.8 ? "citizen" : "soldier";

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

    static void SetNamedVisible(Node node, string name, bool visible)
    {
        foreach (var c in node.GetChildren()) SetNamedVisible(c, name, visible);
        if (node is Node3D n3 && node.Name.ToString().StartsWith(name)) n3.Visible = visible;
    }

    static void SetExteriorVisible(Node node, bool visible)
    {
        foreach (var c in node.GetChildren()) SetExteriorVisible(c, visible);
        if (node is Node3D n3 && node.Name.ToString().StartsWith("ext_")) n3.Visible = visible;
    }

    /// <summary>建てた建物の情報: 中心、階ごとの床面積(渡り廊下の取り付き位置に使う)。</summary>
    public sealed class BuildingInfo
    {
        public Vector3 Center;              // 基壇の中心(世界座標)
        public List<(int w, int d)> Floors = new();   // 階ごとの床面積(屋上は含まない)
        public int Bridges;                 // 取り付いた渡り廊下の数
        public int Count => Floors.Count;
        public (int w, int d) At(int floor) => Floors[Math.Clamp(floor, 0, Floors.Count - 1)];
    }
    public readonly List<BuildingInfo> Buildings = new();

    // ---------- 積み上げ ----------
    /// <summary>cutawayFrom〜cutawayBelow-1 の階を切り取り表示(外装を隠す)。interior = false なら切り取った階に設備を置かない(呼び出し側が置く)。</summary>
    public Node3D Build(Node3D parent, float x, float y, float z, IList<string> stack, float rotDeg = 0, int cutawayBelow = -1, int cutawayFrom = 0, bool interior = true)
    {
        var root = new Node3D { Position = new Vector3(x, y, z), RotationDegrees = new Vector3(0, rotDeg, 0) };
        parent.AddChild(root);
        (int w, int d) expect = (-1, -1);
        float cy = 0;
        int maxW = 0, maxD = 0;
        var info = new BuildingInfo { Center = root.GlobalPosition };
        Buildings.Add(info);
        for (int f = 0; f < stack.Count; f++)
        {
            var def = Defs[stack[f]];
            if (def.Kind != "roof") info.Floors.Add((def.W, def.D));
            if (expect.w >= 0 && (def.W, def.D) != expect) throw new InvalidOperationException($"{stack[f]}: {def.W}x{def.D} != expected {expect.w}x{expect.d}");
            expect = (def.TopW, def.TopD);
            maxW = Math.Max(maxW, def.W); maxD = Math.Max(maxD, def.D);
            var n = Instantiate(def.Id);
            n.Position = new Vector3(0, cy, 0);
            root.AddChild(n);
            if (f >= cutawayFrom && f < cutawayBelow && def.Kind != "roof")
            {
                SetExteriorVisible(n, false);
                if (f == cutawayBelow - 1) SetNamedVisible(n, "int_ceiling", false);   // 開いた最上階は天井も外す(上から見下ろすため)
                if (interior && def.Kind != "hangar") Interior(root, cy + 0.12f, def.W, def.D, f);   // 格納庫は設備を置かず(機体が入る)
            }
            float h = FloorHeight * def.Floors;
            if (def.Kind != "roof" && f != cutawayBelow - 1)   // 開いた最上階は襟板(蓋)を置かない
            {
                float w = def.TopW > 0 ? def.TopW : def.W, d = def.TopD > 0 ? def.TopD : def.D;
                _kb.Box(root, new Vector3(0, cy + h, 0), new Vector3(w + 0.36f, 0.08f, d + 0.36f), _kb.Steel);
            }
            cy += def.Kind == "roof" ? 0 : h;
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
        Connect(parent, Buildings[^2], Buildings[^1], bridgeFloor, false);
    }

    // ---------- 空中の接続(物流路: 中にベルトを通し、両端は階の内部格子につながる) ----------
    /// <summary>a から b へ渡り廊下を架ける(世界座標、床面の高さ)。長さに最も近い部品を選び、x 方向に伸縮して合わせる。中にベルトを通す。</summary>
    public Node3D Bridge(Node3D parent, Vector3 a, Vector3 b, bool wide)
    {
        var dlt = b - a; dlt.Y = 0;
        float len = dlt.Length();
        int L = Math.Clamp((int)MathF.Round(len), 2, 8);
        var n = Instantiate($"bridge{(wide ? "w" : "")}_{L}");
        float yaw = MathF.Atan2(-dlt.Z, dlt.X);   // 部品の x 軸を a → b の向きへ
        n.Position = (a + b) / 2;
        n.Rotation = new Vector3(0, yaw, 0);
        n.Scale = new Vector3(len / L, 1, 1);
        parent.AddChild(n);
        // ベルト: 1 セルごと。広幅は 2 列(往復)
        var dir = dlt / len;
        var side = new Vector3(-dir.Z, 0, dir.X);
        int cells = Math.Max(1, (int)MathF.Round(len));
        float yawDeg = Mathf.RadToDeg(yaw);
        for (int i = 0; i < cells; i++)
        {
            var c = a + dir * ((i + 0.5f) * len / cells);
            if (wide)
            {
                _kb.Belt(parent, c.X + side.X * 0.55f - 0.5f, c.Y + 0.2f, c.Z + side.Z * 0.55f - 0.5f, yawDeg, i % 2 == 0);
                _kb.Belt(parent, c.X - side.X * 0.55f - 0.5f, c.Y + 0.2f, c.Z - side.Z * 0.55f - 0.5f, yawDeg + 180, i % 2 == 1);
            }
            else _kb.Belt(parent, c.X - 0.5f, c.Y + 0.2f, c.Z - 0.5f, yawDeg, i % 2 == 0);
        }
        return n;
    }

    /// <summary>2 棟の向き合う面どうしを floor 階で結ぶ。取り付き位置はその階の床面積から求める。</summary>
    public bool Connect(Node3D parent, BuildingInfo A, BuildingInfo B, int floor, bool wide)
    {
        if (floor >= A.Count || floor >= B.Count) return false;
        var pa = Anchor(A, B.Center, floor);
        var pb = Anchor(B, A.Center, floor);
        var dlt = pb - pa; dlt.Y = 0;
        if (dlt.Length() < 1.5f || dlt.Length() > 8.6f) return false;
        Bridge(parent, pa, pb, wide);
        A.Bridges++; B.Bridges++;
        return true;
    }

    /// <summary>建物の外周上で、target の方向に最も近い点(床の高さ)。</summary>
    Vector3 Anchor(BuildingInfo bi, Vector3 target, int floor)
    {
        var (w, d) = bi.At(floor);
        var dir = target - bi.Center; dir.Y = 0; dir = dir.Normalized();
        float tx = MathF.Abs(dir.X) < 1e-4f ? float.MaxValue : (w / 2f) / MathF.Abs(dir.X);
        float tz = MathF.Abs(dir.Z) < 1e-4f ? float.MaxValue : (d / 2f) / MathF.Abs(dir.Z);
        float t = MathF.Min(tx, tz) + 0.05f;
        return bi.Center + dir * t + new Vector3(0, floor * FloorHeight + 0.14f, 0);
    }

    /// <summary>空中の結節点: 台を置き、各建物から台へ渡り廊下を架ける(3 棟以上の接続)。</summary>
    public void SkyNode(Node3D parent, Vector3 center, int floor, IEnumerable<BuildingInfo> targets, bool wide = false)
    {
        var n = Instantiate("sky_node");
        var pos = new Vector3(center.X, floor * FloorHeight + 0.14f, center.Z);
        n.Position = pos;
        parent.AddChild(n);
        foreach (var bi in targets)
        {
            if (floor >= bi.Count) continue;
            var pa = Anchor(bi, center, floor);
            var dir = pa - pos; dir.Y = 0;
            if (dir.Length() < 1.2f) continue;
            var pn = pos + dir.Normalized() * 1.1f;
            Bridge(parent, pn, pa, wide);
            bi.Bridges++;
        }
    }

    /// <summary>線分 a-b が建物 skip 以外の足元の矩形を横切るか(自動接続の経路判定)。</summary>
    public bool Crosses(Vector3 a, Vector3 b, BuildingInfo skipA, BuildingInfo skipB)
    {
        foreach (var bi in Buildings)
        {
            if (bi == skipA || bi == skipB) continue;
            var (w, d) = bi.At(0);
            float hw = w / 2f + 0.4f, hd = d / 2f + 0.4f;
            for (int i = 0; i <= 10; i++)
            {
                var p = a.Lerp(b, i / 10f);
                if (MathF.Abs(p.X - bi.Center.X) < hw && MathF.Abs(p.Z - bi.Center.Z) < hd) return true;
            }
        }
        return false;
    }

    /// <summary>自動接続: 近い建物どうしを、他を横切らない範囲で渡り廊下で結ぶ。</summary>
    public int AutoConnect(Node3D parent, Random rnd, float maxDist = 13f, int maxPerBuilding = 2, double chance = 0.6)
    {
        int made = 0;
        for (int i = 0; i < Buildings.Count; i++)
            for (int j = i + 1; j < Buildings.Count; j++)
            {
                var A = Buildings[i]; var B = Buildings[j];
                if (A.Bridges >= maxPerBuilding || B.Bridges >= maxPerBuilding) continue;
                var dlt = B.Center - A.Center; dlt.Y = 0;
                if (dlt.Length() > maxDist || rnd.NextDouble() > chance) continue;
                int maxFloor = Math.Min(A.Count, B.Count) - 2;
                if (maxFloor < 3) continue;
                int floor = rnd.Next(2, maxFloor + 1);
                var pa = Anchor(A, B.Center, floor); var pb = Anchor(B, A.Center, floor);
                if (Crosses(pa, pb, A, B)) continue;
                if (Connect(parent, A, B, floor, rnd.NextDouble() < 0.3)) made++;
            }
        return made;
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
                if (def.BayH <= 0)   // 格納庫区画は設備を置かず(機体が入る)
                    for (int k = 0; k < def.Decks; k++) Interior(root, def.Belly + k * FloorHeight + 0.16f, def.Length, def.W, k + 1, cursor + def.Length / 2f);
            }
            cursor += def.Length;
        }
        // 航空戦艦: 空中に浮かぶ。下面の噴射口の光を地面側へ
        _kb.Light(root, new Vector3(0, -0.5f, 0), new Color(0.25f, 0.85f, 1.0f), total * 0.6f, 1.5f);
        return root;
    }

    /// <summary>戦艦の区画 i の格納庫の床の世界座標(x 中心, y, z 中心)。機体を置くのに使う。</summary>
    public Vector3 ShipBayFloor(IList<string> segments, int i)
    {
        float total = 0; foreach (var id in segments) total += Defs[id].Length;
        float cursor = -total / 2;
        for (int k = 0; k < i; k++) cursor += Defs[segments[k]].Length;
        var def = Defs[segments[i]];
        return new Vector3(cursor + def.Length / 2f, def.Belly + 0.3f, 0);
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
                for (int i = 0; i < w; i++) Character(root, i % 3 == 0 ? "soldier" : "citizen", ox - hw + i, y, 0.3f, 180);
                _kb.Tank(root, ox + hw, y, -hd, 0.3f, 1.1f);
                break;
        }
        _kb.Light(root, new Vector3(ox, y + 1.5f, 0), new Color(0.9f, 0.9f, 1.0f), Math.Max(w, d) * 1.2f, 1.2f);
    }
}
