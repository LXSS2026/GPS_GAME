using Godot;
using GPS.Sim;
using System;
using System.Collections.Generic;

/// <summary>
/// 描画試作(ゲームの計算には触れない)。ボクセル地形 + 格子に置く低ポリゴンのモデル + 陰影。
/// 起動: godot --path game res://scenes/preview.tscn ++ --scene village|city [--screenshot path]
/// </summary>
public partial class Preview : Node3D
{
    const string Kits = "res://assets/kenney";
    VoxelWorld _world = new();
    BlockRegistry _blocks = null!;
    Random _rnd = new(7);
    Camera3D _cam = null!;
    int _frame;
    StandardMaterial3D _beltMat = new() { AlbedoColor = new Color(0.85f, 0.70f, 0.31f), Roughness = 0.8f };
    StandardMaterial3D _beltStripe = new() { AlbedoColor = new Color(0.20f, 0.20f, 0.22f), Roughness = 0.9f };

    byte B(string name) => _blocks.IdOf(name);

    public override void _Ready()
    {
        _blocks = BlockRegistry.FromJson(FileAccess.GetFileAsString("res://data/blocks.json"));
        var args = OS.GetCmdlineUserArgs();
        string scene = Arg(args, "--scene") ?? "village";
        Vector3 pivot; float dist;
        if (scene == "city") { BuildCity(); pivot = new Vector3(32, 4, 34); dist = 72; }
        else { BuildVillage(); pivot = new Vector3(22, 6, 27); dist = 40; }

        var tr = new TerrainRenderer();
        AddChild(tr);
        tr.Init(_world, _blocks);
        tr.BuildAll();
        AddChild(SceneSetup.Lighting());

        _cam = new Camera3D { Fov = 38, Far = 800 };
        AddChild(_cam);
        float yaw = 0.78f, elev = 0.62f;
        _cam.Position = pivot + new Vector3(Mathf.Cos(elev) * Mathf.Sin(yaw), Mathf.Sin(elev), Mathf.Cos(elev) * Mathf.Cos(yaw)) * dist;
        _cam.LookAt(pivot);
    }

    public override void _Process(double delta)
    {
        var args = OS.GetCmdlineUserArgs();
        string? path = Arg(args, "--screenshot");
        if (path == null) return;
        if (++_frame == 40)
        {
            GetViewport().GetTexture().GetImage().SavePng(path);
            GetTree().Quit();
        }
    }

    static string? Arg(string[] args, string key)
    {
        int i = Array.IndexOf(args, key);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    // ---------- 共通 ----------
    int Top(int x, int z)
    {
        int y = 0;
        while (_world.IsSolid(new BlockPos(x, y, z))) y++;
        return y; // 最初の空気の高さ = 地表に置く y
    }

    void SetTop(int x, int z, byte id)
    {
        int y = Top(x, z) - 1;
        if (y >= 0) _world.Set(x, y, z, id);
    }

    Node3D Place(string kit, string model, float x, float y, float z, float scale = 1f, float rotDeg = 0f, float yOffset = 0f, Vector3? scale3 = null)
    {
        var ps = GD.Load<PackedScene>($"{Kits}/{kit}/models/{model}.glb");
        var n = (Node3D)ps.Instantiate();
        n.Position = new Vector3(x, y + yOffset, z);
        n.Scale = scale3 ?? Vector3.One * scale;
        n.RotationDegrees = new Vector3(0, rotDeg, 0);
        AddChild(n);
        return n;
    }

    /// <summary>ベルト 1 セル: 薄い板と進行方向の縞(自前の簡易メッシュ)。</summary>
    void Belt(float x, float y, float z, float rotDeg)
    {
        var root = new Node3D { Position = new Vector3(x + 0.5f, y, z + 0.5f), RotationDegrees = new Vector3(0, rotDeg, 0) };
        root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.9f, 0.12f, 0.8f) }, MaterialOverride = _beltMat, Position = new Vector3(0, 0.06f, 0) });
        for (int i = -1; i <= 1; i++)
            root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.08f, 0.02f, 0.6f) }, MaterialOverride = _beltStripe, Position = new Vector3(i * 0.28f, 0.13f, 0) });
        AddChild(root);
    }

    void Person(float x, float y, float z, float rot)
    {
        string[] m = { "character-male-a", "character-female-a", "character-male-c", "character-female-d", "character-male-e" };
        Place("mini-characters", m[_rnd.Next(m.Length)], x, y, z, 2.4f, rot);
    }

    void Robot(float x, float y, float z, float rot) => Place("space-kit", "astronautA", x, y, z, 1.1f, rot, yOffset: 0.43f);

    // ---------- 村 ----------
    void BuildVillage()
    {
        WorldGen.Terrain(_world, _blocks, 48, 48, 3);
        byte water = B("Water"), sand = B("Sand"), path = B("Path"), field = B("Field"), ore = B("IronOre");
        // 川: 高さ 5 以上を削り、底を水にする
        for (int x = 0; x < 48; x++)
            for (int z = 0; z < 48; z++)
            {
                int d = x - z;
                if (d >= 12 && d <= 13)
                {
                    for (int y = 5; y < 16; y++) _world.Set(x, y, z, BlockRegistry.Air);
                    _world.Set(x, 4, z, water);
                }
                else if (d == 11 || d == 14) SetTop(x, z, sand);
            }
        // 道
        foreach (var (x, z) in new[] { (12, 20), (13, 20), (14, 21), (15, 22), (16, 23), (17, 24), (18, 25), (19, 26), (20, 27), (21, 28), (22, 28), (23, 28), (24, 28), (25, 28), (26, 28) })
            SetTop(x, z, path);
        // 畑と鉱脈
        for (int x = 6; x < 11; x++) for (int z = 22; z < 26; z++) SetTop(x, z, field);
        for (int x = 28; x < 31; x++) for (int z = 32; z < 35; z++) SetTop(x, z, ore);
        _world.Dirty.Clear();

        // 家と柵、井戸代わりの樽、人
        var houses = new[] { ("building-type-a", 9, 14, 0f), ("building-type-c", 13, 13, 90f), ("building-type-f", 10, 18, 0f), ("building-type-g", 15, 17, 180f), ("building-type-b", 7, 10, 90f) };
        foreach (var (m, x, z, r) in houses) Place("city-kit-suburban", m, x + 1, Top(x, z), z + 1, 2.0f, r);
        for (int i = 0; i < 5; i++) Place("nature-kit", "fence_simple", 6.5f + i, Top(6 + i, 21), 21.5f, 1.0f, 0);
        for (int i = 0; i < 4; i++) Place("nature-kit", "fence_simple", 5.5f, Top(5, 22 + i), 22.5f + i, 1.0f, 90);
        for (int x = 6; x < 11; x++) for (int z = 22; z < 26; z++) Place("nature-kit", "crops_wheatStageB", x + 0.5f, Top(x, z), z + 0.5f, 1.6f, _rnd.Next(4) * 90);
        Place("space-kit", "machine_barrel", 12.5f, Top(12, 16), 16.5f, 1.2f);
        Person(11.5f, Top(11, 16), 16.5f, 120); Person(12.3f, Top(12, 15), 15.2f, -40); Person(16.5f, Top(16, 20), 20.5f, 60); Person(14.0f, Top(14, 21), 21.5f, 200);
        // 木と岩
        var trees = new[] { (3, 4), (5, 7), (17, 8), (20, 11), (4, 30), (8, 33), (22, 36), (33, 14), (36, 18), (40, 30), (34, 38), (14, 40), (19, 44), (30, 8), (38, 8) };
        string[] tm = { "tree_default", "tree_pineDefaultA", "tree_oak", "tree_default", "tree_pineTallA" };
        foreach (var (x, z) in trees) Place("nature-kit", tm[_rnd.Next(tm.Length)], x + 0.5f, Top(x, z), z + 0.5f, 1.6f, _rnd.Next(360));
        Place("nature-kit", "rock_largeA", 26.5f, Top(26, 40), 40.5f, 1.5f); Place("nature-kit", "rock_largeB", 9.5f, Top(9, 38), 38.5f, 1.3f);
        // 工房(小さな工場)と煙突
        Place("city-kit-industrial", "building-f", 29, Top(28, 26), 27, 1.6f, 0);
        Place("city-kit-industrial", "chimney-small", 31.3f, Top(31, 26), 26.5f, 1.6f);
        // 採掘機(やぐら) → ベルト → 機械 → 炉(樽型の機械) → 箱
        Place("space-kit", "machine_wireless", 29.5f, Top(29, 33), 33.5f, 2.2f);
        for (int i = 0; i < 5; i++) Belt(28 - i, Top(28 - i, 33), 33, 90);
        Place("space-kit", "machine_generator", 22.5f, Top(22, 33), 33.5f, 2.2f, 90);
        Place("space-kit", "pipe_straight", 22.5f, Top(22, 34), 34.5f, 1.2f, 0);
        Place("space-kit", "pipe_cornerRound", 22.5f, Top(22, 35), 35.5f, 1.2f, 0);
        Place("space-kit", "machine_barrelLarge", 20.5f, Top(20, 33), 33.5f, 2.0f, 0);
        Place("city-kit-industrial", "shipping-container-a", 18.5f, Top(18, 33), 33.5f, 1.2f, 90);
        // 上段の足場と縦の搬送(構造体 + 足場 + 機械)
        for (int i = 0; i < 2; i++) for (int j = 0; j < 2; j++) Place("space-kit", "structure", 24.5f + i, Top(24 + i, 36 + j), 36.5f + j, 1.0f);
        for (int i = 0; i < 2; i++) for (int j = 0; j < 2; j++) Place("space-kit", "platform_center", 24.5f + i, Top(24 + i, 36 + j) + 1.0f, 36.5f + j, 1.0f);
        Place("space-kit", "machine_generatorLarge", 25.5f, Top(25, 36) + 1.0f, 36.5f, 1.6f, 180);
        Place("space-kit", "pipe_rampLarge", 23.5f, Top(23, 36), 36.5f, 1.0f, 90);
        Belt(24, Top(24, 38) + 1.0f, 38, 0);
        // 人型ロボット(試作では宇宙服のモデルで代用)
        Robot(21.3f, Top(21, 35), 35.5f, 30); Robot(26.5f, Top(26, 31), 31.5f, -90);
    }

    // ---------- 大都市 ----------
    void BuildCity()
    {
        const int N = 64;
        byte stone = B("Stone"), dirt = B("Dirt"), plaza = B("Plaza"), concrete = B("Concrete"), asphalt = B("Asphalt"), rx = B("RoadX"), rz = B("RoadY");
        byte grass = B("Grass"), water = B("Water");
        for (int x = 0; x < N; x++)
            for (int z = 0; z < N; z++)
            {
                _world.Set(x, 0, z, stone); _world.Set(x, 1, z, stone); _world.Set(x, 2, z, dirt);
                bool river = x - z >= 16 && x - z <= 17;
                bool roadX = z % 7 == 0 || z == 28 || z == 29;   // x 方向に走る道
                bool roadZ = x % 7 == 0;
                byte top = roadX && roadZ ? asphalt : roadX ? rx : roadZ ? rz : District(x, z) switch { "park" => grass, "industry" => concrete, _ => plaza };
                if (river && !(z == 28 || z == 29)) { _world.Set(x, 2, z, water); continue; }
                _world.Set(x, 3, z, top);
            }
        _world.Dirty.Clear();
        const float Y = 4f;

        // 街区ごとに配置
        for (int bx = 0; bx < N; bx += 7)
            for (int bz = 0; bz < N; bz += 7)
            {
                int x0 = bx + 1, z0 = bz + 1;
                if (x0 + 5 >= N || z0 + 5 >= N) continue;
                if (z0 <= 29 && z0 + 5 >= 28) continue; // 大通りにかかる街区は空ける
                bool river = Enumerable_Any(x0, z0);
                if (river) continue;
                string d = District(x0 + 2, z0 + 2);
                switch (d)
                {
                    case "downtown":
                        foreach (var (ox, oz) in new[] { (1.5f, 1.5f), (4.5f, 1.5f), (1.5f, 4.5f), (4.5f, 4.5f) })
                        {
                            if (_rnd.NextDouble() < 0.55)
                                Place("city-kit-commercial", $"building-skyscraper-{"abcde"[_rnd.Next(5)]}", x0 + ox, Y, z0 + oz, 2.0f, _rnd.Next(4) * 90);
                            else
                                Place("city-kit-commercial", $"low-detail-building-{"abcdefghijklmn"[_rnd.Next(14)]}", x0 + ox, Y, z0 + oz, 2.6f, _rnd.Next(4) * 90);
                        }
                        break;
                    case "residential":
                        foreach (var (ox, oz) in new[] { (1.5f, 1.5f), (4.5f, 1.5f), (1.5f, 4.5f), (4.5f, 4.5f) })
                        {
                            if (_rnd.NextDouble() < 0.75)
                                Place("city-kit-commercial", $"building-{"abcdefghijklmn"[_rnd.Next(14)]}", x0 + ox, Y, z0 + oz, 2.0f, _rnd.Next(4) * 90);
                            else
                                Place("nature-kit", "tree_default", x0 + ox, Y, z0 + oz, 1.6f, _rnd.Next(360));
                        }
                        break;
                    case "park":
                        for (int i = 0; i < 7; i++) Place("nature-kit", _rnd.NextDouble() < 0.5 ? "tree_default" : "tree_oak", x0 + 0.5f + _rnd.Next(6), Y, z0 + 0.5f + _rnd.Next(6), 1.7f, _rnd.Next(360));
                        Place("city-kit-suburban", "planter", x0 + 3f, Y, z0 + 3f, 2.0f);
                        break;
                    case "industry":
                        Place("city-kit-industrial", $"building-{"abcdeghjk"[_rnd.Next(9)]}", x0 + 2.2f, Y, z0 + 1.6f, 1.8f, 0);
                        Place("city-kit-industrial", "chimney-large", x0 + 5.2f, Y, z0 + 1.2f, 1.5f);
                        Place("city-kit-industrial", "detail-tank-large", x0 + 1.5f, Y, z0 + 4.8f, 1.5f);
                        Place("city-kit-industrial", $"shipping-container-{"abc"[_rnd.Next(3)]}", x0 + 4.5f, Y, z0 + 4.8f, 1.4f, 90);
                        for (int i = 0; i < 3; i++) Belt(x0 + 3 + i, Y, z0 + 3, 90);
                        Place("space-kit", "machine_generator", x0 + 2.0f, Y, z0 + 3.5f, 2.0f, 90);
                        Robot(x0 + 5.5f, Y, z0 + 3.0f, -90);
                        break;
                    case "military":
                        Place("space-kit", "hangar_smallA", x0 + 3f, Y, z0 + 1.5f, 1.5f, 0);
                        for (int i = 0; i < 6; i++) for (int j = 0; j < 3; j++) Robot(x0 + 0.5f + i, Y, z0 + 3.6f + j * 0.9f, 180);
                        Place("space-kit", "turret_single", x0 + 0.6f, Y, z0 + 0.6f, 1.2f, 45);
                        break;
                    case "solar":
                        for (int i = 0; i < 3; i++) for (int j = 0; j < 2; j++) Place("city-kit-industrial", "solar-panel-landscape-group", x0 + 1f + i * 2f, Y, z0 + 1.5f + j * 3f, 1.6f, 0);
                        break;
                }
            }
        // 高架の軌道(x = 21 の道路の上) と列車
        for (int z = 0; z < N; z++)
        {
            if (z % 3 == 0) Place("space-kit", "monorail_trackSupport", 21.5f, Y, z + 0.5f, scale3: new Vector3(2.2f, 7.0f, 2.2f));
            Place("space-kit", "monorail_trackStraight", 21.5f, Y + 3.5f, z + 0.5f, scale3: new Vector3(2.2f, 2.2f, 1.0f));
        }
        Place("space-kit", "monorail_trainFront", 21.5f, Y + 3.7f, 17.5f, scale3: new Vector3(2.2f, 2.2f, 1.0f));
        Place("space-kit", "monorail_trainPassenger", 21.5f, Y + 3.7f, 18.5f, scale3: new Vector3(2.2f, 2.2f, 1.0f));
        Place("space-kit", "monorail_trainPassenger", 21.5f, Y + 3.7f, 19.5f, scale3: new Vector3(2.2f, 2.2f, 1.0f));
        Place("space-kit", "monorail_trainEnd", 21.5f, Y + 3.7f, 20.5f, scale3: new Vector3(2.2f, 2.2f, 1.0f), rotDeg: 180);
        // 路上の市民と人型ロボット
        for (int i = 0; i < 70; i++)
        {
            int x = _rnd.Next(1, N - 1), z = _rnd.Next(1, N - 1);
            bool road = x % 7 == 0 || z % 7 == 0 || z == 28 || z == 29;
            if (!road || (x - z >= 16 && x - z <= 17)) continue;
            float px = x + (float)_rnd.NextDouble(), pz = z + (float)_rnd.NextDouble();
            if (_rnd.NextDouble() < 0.5) Robot(px, Y, pz, _rnd.Next(360)); else Person(px, Y, pz, _rnd.Next(360));
        }
        // 川の橋(大通り)の欄干
        for (int x = 44; x <= 47; x++) { Place("nature-kit", "fence_simple", x + 0.5f, Y, 28.05f, 1.0f, 0); Place("nature-kit", "fence_simple", x + 0.5f, Y, 29.95f, 1.0f, 0); }
    }

    static bool Enumerable_Any(int x0, int z0)
    {
        for (int x = x0 - 1; x <= x0 + 6; x++) for (int z = z0 - 1; z <= z0 + 6; z++) if (x - z >= 16 && x - z <= 17) return true;
        return false;
    }

    static string District(int x, int z)
    {
        if (x >= 22 && x <= 27 && z >= 31 && z <= 36) return "park";
        if (x >= 8 && x <= 34 && z <= 27) return "downtown";
        if (x >= 36 && z >= 31) return "industry";
        if (x >= 22 && x <= 34 && z >= 38 && z <= 55) return "military";
        if (x <= 13 && z >= 45) return "solar";
        return "residential";
    }
}
