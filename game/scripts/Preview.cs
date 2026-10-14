using Godot;
using GPS.Sim;
using System;
using System.Collections.Generic;

/// <summary>
/// 描画試作(ゲームの計算には触れない)。ボクセル地形 + 自前のキットバッシュ生成(KitBash) + 陰影。
/// 起動: godot --path game res://scenes/preview.tscn ++ --scene village|city [--screenshot path]
/// </summary>
public partial class Preview : Node3D
{
    VoxelWorld _world = new();
    BlockRegistry _blocks = null!;
    KitBash _kb = null!;
    FloorModules _fm = null!;
    MechBuilder _mb = null!;
    Random _rnd = new(7);
    Camera3D _cam = null!;
    int _frame;

    byte B(string name) => _blocks.IdOf(name);

    public override void _Ready()
    {
        _blocks = BlockRegistry.FromJson(FileAccess.GetFileAsString("res://data/blocks.json"));
        _kb = new KitBash(11);
        _fm = new FloorModules(_kb, 9);
        _mb = new MechBuilder(_fm);
        var args = OS.GetCmdlineUserArgs();
        string scene = Arg(args, "--scene") ?? "village";
        Vector3 pivot; float dist;
        if (scene == "city") { BuildCity(); pivot = new Vector3(30, 18, 30); dist = 122; }
        else if (scene == "buildings") { BuildShowcase(); pivot = new Vector3(60, 17, 12); dist = 112; }
        else if (scene == "modules") { BuildModules(); pivot = new Vector3(48, 14, 16); dist = 100; }
        else if (scene == "robots") { BuildRobots(); pivot = new Vector3(48f, 9f, 34f); dist = 80f; }
        else { BuildVillage(); pivot = new Vector3(22, 6, 27); dist = 40; }
        if (Arg(args, "--view") == "close") { pivot = scene == "city" ? new Vector3(24, 9, 20) : new Vector3(24, 6, 33); dist = scene == "city" ? 34 : 16; }
        if (Arg(args, "--pivot") is string pv) { var p = pv.Split(','); pivot = new Vector3(float.Parse(p[0]), float.Parse(p[1]), float.Parse(p[2])); }   // --view より後に適用(明示指定が勝つ)
        if (Arg(args, "--dist") is string ds) dist = float.Parse(ds);

        _kb.Flush(this);
        var tr = new TerrainRenderer();
        AddChild(tr);
        tr.Init(_world, _blocks);
        tr.BuildAll();
        AddChild(SceneSetup.Lighting());

        _cam = new Camera3D { Fov = 38, Far = 800 };
        AddChild(_cam);
        float yaw = scene == "buildings" ? 0.35f : scene == "modules" ? 0.6f : scene == "robots" ? 0.2f : 0.78f, elev = scene == "buildings" ? 0.30f : scene == "modules" ? 0.38f : scene == "robots" ? 0.16f : 0.52f;
        if (Arg(args, "--yaw") is string ys) yaw = float.Parse(ys);     // 視点の向き(ラジアン)を明示する
        if (Arg(args, "--elev") is string es) elev = float.Parse(es);
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

    int Top(int x, int z)
    {
        int y = 0;
        while (_world.IsSolid(new BlockPos(x, y, z))) y++;
        return y;
    }

    void SetTop(int x, int z, byte id)
    {
        int y = Top(x, z) - 1;
        if (y >= 0) _world.Set(x, y, z, id);
    }

    float C(int v) => v + 0.5f;   // セルの中心

    // ---------- 村(入植地) ----------
    void BuildVillage()
    {
        WorldGen.Terrain(_world, _blocks, 48, 48, 3);
        byte water = B("Water"), sand = B("Sand"), path = B("Path"), field = B("Field"), ore = B("IronOre"), concrete = B("Concrete");
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
        foreach (var (x, z) in new[] { (12, 20), (13, 20), (14, 21), (15, 22), (16, 23), (17, 24), (18, 25), (19, 26), (20, 27), (21, 28), (22, 28), (23, 28), (24, 28), (25, 28), (26, 28) })
            SetTop(x, z, path);
        for (int x = 6; x < 11; x++) for (int z = 22; z < 26; z++) SetTop(x, z, field);
        for (int x = 28; x < 31; x++) for (int z = 32; z < 35; z++) SetTop(x, z, ore);
        for (int x = 9; x < 17; x++) for (int z = 13; z < 19; z++) SetTop(x, z, concrete);   // 入植地の基礎
        for (int x = 18; x < 31; x++) for (int z = 30; z < 38; z++) if (_blocks[_world.Get(x, Top(x, z) - 1, z)].Name != "IronOre") SetTop(x, z, concrete);
        _world.Dirty.Clear();

        // 入植地: 居住モジュール 3、格納庫、通信塔、灯
        _kb.Habitat(this, 11, Top(10, 14), 14.7f, 0);
        _kb.Habitat(this, 11, Top(10, 17), 17.3f, 0);
        _kb.Habitat(this, 15, Top(14, 15), 15.5f, 90);
        _kb.Hangar(this, 13, Top(12, 11), 10.5f, 3.6f, 2.4f, 1.8f, 0);
        var mast = new Node3D { Position = new Vector3(C(16), Top(16, 18), C(18)) }; AddChild(mast);
        _kb.Box(mast, new Vector3(0, 0.1f, 0), new Vector3(0.8f, 0.2f, 0.8f), _kb.Concrete);
        _kb.Cyl(mast, new Vector3(0, 2.0f, 0), 0.08f, 4.0f, _kb.Steel, seg: 8);
        for (int i = 0; i < 3; i++) _kb.Box(mast, new Vector3(0, 1.0f + i * 1.2f, 0), new Vector3(0.5f, 0.04f, 0.5f), _kb.Rust);
        _kb.Box(mast, new Vector3(0.3f, 3.6f, 0), new Vector3(0.5f, 0.5f, 0.05f), _kb.Paint);
        _kb.Strip(mast, new Vector3(0, 4.05f, 0), new Vector3(0.1f, 0.1f, 0.1f), _kb.GlowRed);
        // 畑と柵
        for (int x = 6; x < 11; x++) for (int z = 22; z < 26; z++) _kb.Crop(this, C(x), Top(x, z), C(z));
        for (int i = 0; i < 5; i++) _kb.Box(this, new Vector3(6.5f + i, Top(6 + i, 21) + 0.3f, 21.5f), new Vector3(1.0f, 0.04f, 0.04f), _kb.Rust);
        for (int i = 0; i < 5; i += 2) _kb.Box(this, new Vector3(6.0f + i, Top(6 + i, 21) + 0.2f, 21.5f), new Vector3(0.06f, 0.4f, 0.06f), _kb.Steel);
        // 人(市民 4、自警団はロボット 2)
        _fm.Character(this, "citizen", 12.2f, Top(12, 19), 19.4f, 150); _fm.Character(this, "citizen", 13.0f, Top(13, 19), 19.8f, -30);
        _fm.Character(this, "soldier", 16.3f, Top(16, 20), 20.5f, 60); _fm.Character(this, "citizen", 14.0f, Top(14, 22), 22.2f, 200);
        _fm.Character(this, "soldier", 21.3f, Top(21, 35), 35.5f, 30); _mb.Build(this, "general", "g0", 34f, Top(34, 30), 30f, -60);
        // 木と岩
        foreach (var (x, z) in new[] { (3, 4), (17, 8), (4, 30), (22, 40), (36, 18), (40, 30), (14, 40), (38, 8), (5, 36), (33, 12) })
            _kb.Pine(this, C(x), Top(x, z), C(z), 1.8f + (float)_rnd.NextDouble() * 1.2f);
        _kb.Rock(this, C(26), Top(26, 42), C(42), 1.6f); _kb.Rock(this, C(9), Top(9, 38), C(38), 1.2f); _kb.Rock(this, C(33), Top(33, 8), C(8), 1.4f);
        // 工房と工場列: 採掘機 → ベルト → 組立機 / 炉 → 箱
        _kb.Factory(this, 27, Top(26, 25), 25, 3.0f, 2.0f, 1.6f, 0);
        _kb.Chimney(this, 28.6f, Top(28, 25), 24.3f, 2.4f);
        _kb.Miner(this, C(29), Top(29, 33), C(33));
        for (int i = 0; i < 5; i++) _kb.Belt(this, C(28 - i), Top(28 - i, 33), C(33), 0, i % 2 == 0);
        _kb.Assembler(this, C(22), Top(22, 33), C(33), 90);
        _kb.Furnace(this, C(22), Top(22, 35), C(35), 0);
        _kb.Belt(this, C(23), Top(23, 35), C(35), 0, true);
        _kb.Chest(this, C(20), Top(20, 33), C(33));
        _kb.Tank(this, C(19), Top(19, 36), C(36), 0.4f, 1.3f);
        // 上段の足場(鋼の柱 + 格子床)と機械
        var deck = new Node3D { Position = new Vector3(25, Top(24, 36), 37) }; AddChild(deck);
        for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2) _kb.Box(deck, new Vector3(sx * 0.9f, 1.1f, sz * 0.9f), new Vector3(0.12f, 2.2f, 0.12f), _kb.Steel);
        _kb.Box(deck, new Vector3(0, 2.2f, 0), new Vector3(2.1f, 0.08f, 2.1f), _kb.Grate);
        for (int sx = -1; sx <= 1; sx += 2) _kb.Box(deck, new Vector3(sx * 1.0f, 2.6f, 0), new Vector3(0.04f, 0.04f, 2.0f), _kb.Rust);
        for (int sz = -1; sz <= 1; sz += 2) _kb.Box(deck, new Vector3(0, 2.6f, sz * 1.0f), new Vector3(2.0f, 0.04f, 0.04f), _kb.Rust);
        _kb.Assembler(deck, 0, 2.24f, -0.4f, 180);
        _kb.Belt(deck, 0, 2.24f, 0.6f, 90, true);
        _kb.Pipe(deck, new Vector3(1.0f, 0.3f, 0.6f), new Vector3(1.0f, 2.2f, 0.6f), 0.06f, _kb.Rust);
        _kb.Light(this, new Vector3(25, Top(24, 36) + 3.0f, 37), new Color(0.25f, 0.85f, 1.0f), 5f, 1.5f);
    }

    // ---------- 機体の展示(前列: 重装・高速・汎用の 3 系統と組み替え例、後列: 汎用の工作機 + 第 1〜6 世代 = リアクターの色) ----------
    void BuildRobots()
    {
        byte stone = B("Stone"), plaza = B("Plaza");
        for (int x = 0; x < 100; x++) for (int z = 0; z < 60; z++) { _world.Set(x, 0, z, stone); _world.Set(x, 1, z, stone); _world.Set(x, 2, z, plaza); }
        _world.Dirty.Clear();
        string? only = Arg(OS.GetCmdlineUserArgs(), "--only");
        if (only == "fighter") { _mb.BuildFighter(this, _mb.Presets["fast"], "g3", 48f, 20f, 34f, 0); return; }   // 戦闘機モードだけ
        string[] heads = { "head_twin", "head_visor", "head_fast", "head_mono", "head_quad", "head_tall", "head_twin" };   // 顔のレパートリーを兼ねる
        for (int g = 0; g < 7; g++) _mb.Build(this, _mb.Presets["general"].With(head: heads[g]), $"g{g}", 8f + g * 13.5f, 3f, 20f, 10);   // 工作機 + 第 1〜6 世代(リアクターの色)
        if (only == "gens") return;   // 世代の列だけを描く(前列が隠すため)
        _mb.Build(this, "heavy", "g2", 18f, 3f, 44f, 0);
        _mb.Build(this, "fast", "g2", 38f, 3f, 44f, 0);
        _mb.Build(this, "general", "g2", 58f, 3f, 44f, 0);
        _mb.BuildFighter(this, _mb.Presets["fast"], "g3", 40f, 24f, 34f, 0);   // 高速機の戦闘機モード(上空)
        // 組み替え例: 汎用の胴と頭に、重装の腕と高速の脚、大砲
        _mb.Build(this, _mb.Presets["general"].With(arms: "heavy", legs: "fast", back: "back_heavy", weaponR: "weapon_bazooka_beam", weaponL: "weapon_sword_live", spare: "weapon_smg_live") with { Main = new Color(0.35f, 0.45f, 0.4f) }, "g6", 78f, 3f, 44f, 0);
        _fm.Character(this, "citizen", 8f, 3f, 48f, 20); _fm.Character(this, "soldier", 9.2f, 3f, 48.5f, 0);
        _fm.Build(this, 94f, 3f, 16f, _fm.Plan("office", 5, 5, 7, false, "heli"), 0);   // 7 階 = 21 m の塔(比較用)
        _kb.Light(this, new Vector3(48f, 16f, 36f), new Color(0.9f, 0.92f, 1.0f), 60f, 1.6f);
        _kb.Light(this, new Vector3(10f, 10f, 30f), new Color(1.0f, 0.8f, 0.6f), 30f, 1.0f);
    }

    // ---------- 階モジュール方式の確認(系統・床面積・双塔・戦艦) ----------
    void BuildModules()
    {
        const int W = 96, D = 30;
        byte stone = B("Stone"), plaza = B("Plaza"), road = B("RoadX");
        for (int x = 0; x < W; x++) for (int z = 0; z < D; z++) { _world.Set(x, 0, z, stone); _world.Set(x, 1, z, stone); _world.Set(x, 2, z, z == 15 || z == 16 ? road : plaza); }
        _world.Dirty.Clear();
        var fm = _fm;
        float xx = 5f;
        // 1 列目(奥): 系統ごとの塔。5x5 と 4x6、後退あり
        fm.Build(this, xx, 3, 8, fm.Plan("office", 5, 5, 14), 0); xx += 9;
        fm.Build(this, xx, 3, 8, fm.Plan("deco", 5, 5, 13, true, "spire"), 0); xx += 9;
        fm.Build(this, xx, 3, 8, fm.Plan("exo", 4, 6, 12, true, "mech"), 0); xx += 9;
        fm.Build(this, xx, 3, 8, fm.Plan("svc", 5, 5, 11, true, "heli"), 0); xx += 9;
        fm.Build(this, xx, 3, 8, fm.Plan("concrete", 5, 5, 10), 0); xx += 9;
        fm.Build(this, xx, 3, 8, fm.Plan("res", 4, 6, 9, true, "mech"), 0); xx += 10;
        fm.Twin(this, xx + 1, 3, 8, "office", 12, 3, 7, 0); xx += 12;
        // 格納庫付きの塔(下 4 階を切り取り表示)
        var hangarTower = new List<string> { "base_5x5_office", "hangar_5x5", "office_5x5", "office_5x5_c", "mech_5x5_office", "office_5x5", "transfer_5x5_3x3_office", "office_3x3_c", "office_3x3", "roof_3x3_heli_office" };
        fm.Build(this, xx + 1, 3, 8, hangarTower, 0, cutawayBelow: 4);
        // 手前: 戦艦(艦尾 → 艦首)。中央の 1 区画を切り取り表示
        // 航空戦艦 2 隻を上空に(大型は中央区画を切り取り表示)
        fm.Ship(this, 40, 24f, 24, new List<string> { "ship_stern_7x4", "ship_turret_7x4", "ship_lift_7x4", "ship_mid_7x4", "ship_hangar_7x4", "ship_bridge_7x4", "ship_lift_7x4", "ship_turret_7x4", "ship_bow_7x7" }, 0, cutawayIndex: 3);
        fm.Ship(this, 18, 16f, 27, new List<string> { "ship_stern_5x3", "ship_lift_5x3", "ship_mid_5x3", "ship_bridge_5x3", "ship_turret_5x3", "ship_bow_5x6" }, 0);
        // 空中の接続: 奥の塔どうしを異なる階で結び(普通幅と広幅)、4 棟の中心に結節点を置いて放射状に結ぶ
        var b = fm.Buildings;
        fm.Connect(this, b[0], b[1], 5, false);
        fm.Connect(this, b[1], b[2], 8, true);
        fm.Connect(this, b[2], b[3], 4, false);
        fm.Connect(this, b[3], b[4], 6, true);
        fm.Connect(this, b[4], b[5], 3, false);
        // 4 棟と結節点(手前右)
        float cx = 62, cz = 25;
        for (int i = 0; i < 4; i++)
        {
            float ox = (i % 2 == 0 ? -1 : 1) * 4.0f, oz = (i < 2 ? -1 : 1) * 4.0f;
            fm.Build(this, cx + ox, 3, cz + oz, fm.Plan(i % 2 == 0 ? "office" : "exo", 3, 3, 9, false, "mech"), 0);
        }
        fm.SkyNode(this, new Vector3(cx, 0, cz), 5, b.GetRange(b.Count - 4, 4), true);
        for (int i = 0; i < 30; i++) _fm.Character(this, _fm.RandomHuman(), 3f + (float)_rnd.NextDouble() * 90f, 3f, 15f + (float)_rnd.NextDouble() * 2f, _rnd.Next(360));
        _mb.Build(this, "general", "g2", 30f, 3f, 24f, 40);
    }

    // ---------- 建物の展示(10 様式を一列に) ----------
    void BuildShowcase()
    {
        const int W = 120, D = 24;
        byte stone = B("Stone"), plaza = B("Plaza"), road = B("RoadX");
        for (int x = 0; x < W; x++) for (int z = 0; z < D; z++) { _world.Set(x, 0, z, stone); _world.Set(x, 1, z, stone); _world.Set(x, 2, z, z == 17 || z == 18 ? road : plaza); }
        _world.Dirty.Clear();
        int[] floors = { 30, 26, 28, 24, 36, 40, 34, 18, 16, 30 };
        for (int i = 0; i < KitBash.RefStyles; i++)
            _kb.RefBuilding(this, i, 5.5f + i * 7.2f, 3f, 11f, 3.6f, 3.6f, floors[i], 0);
        int[] lfloors = { 36, 32, 30, 34, 26, 28 };
        for (int i = 0; i < KitBash.LoftStyles; i++)
            _kb.LoftBuilding(this, i, 5.5f + (KitBash.RefStyles + i) * 7.2f, 3f, 11f, 3.6f, lfloors[i], 0);
        for (int i = 0; i < 40; i++) _fm.Character(this, _fm.RandomHuman(), 3f + (float)_rnd.NextDouble() * 112f, 3f, 17f + (float)_rnd.NextDouble() * 2f, _rnd.Next(360));
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
                bool roadX = z % 7 == 0 || z == 28 || z == 29;
                bool roadZ = x % 7 == 0;
                byte top = roadX && roadZ ? asphalt : roadX ? rx : roadZ ? rz : District(x, z) switch { "park" => grass, "industry" => concrete, _ => plaza };
                if (river && !(z == 28 || z == 29)) { _world.Set(x, 2, z, water); continue; }
                _world.Set(x, 3, z, top);
            }
        _world.Dirty.Clear();
        const float Y = 4f;

        for (int bx = 0; bx < N; bx += 7)
            for (int bz = 0; bz < N; bz += 7)
            {
                int x0 = bx + 1, z0 = bz + 1;
                if (x0 + 5 >= N || z0 + 5 >= N) continue;
                if (z0 <= 29 && z0 + 5 >= 28) continue;
                if (NearRiver(x0, z0)) continue;
                string d = District(x0 + 2, z0 + 2);
                switch (d)
                {
                    case "downtown":
                    {
                        string fam = new[] { "office", "office", "deco", "exo", "svc", "concrete" }[_rnd.Next(6)];
                        double r = _rnd.NextDouble();
                        if (r < 0.5) _fm.Build(this, x0 + 3f, Y, z0 + 3f, _fm.Plan(fam, 5, 5, _rnd.Next(10, 23)), _rnd.Next(4) * 90);
                        else if (r < 0.7) _fm.Build(this, x0 + 3f, Y, z0 + 3f, _fm.Plan(fam, 4, 6, _rnd.Next(8, 20)), _rnd.Next(2) * 90);
                        else if (r < 0.85) _fm.Twin(this, x0 + 3f, Y, z0 + 3f, fam == "concrete" ? "office" : fam, _rnd.Next(8, 16), 3, _rnd.Next(3, 7), _rnd.Next(2) * 90);
                        else foreach (var (ox, oz) in new[] { (1.5f, 1.5f), (4.5f, 1.5f), (1.5f, 4.5f), (4.5f, 4.5f) })
                            _fm.Build(this, x0 + ox, Y, z0 + oz, _fm.Plan(fam, 3, 3, _rnd.Next(5, 14)), _rnd.Next(4) * 90);
                        break;
                    }
                    case "residential":
                    {
                        string fam = _rnd.NextDouble() < 0.6 ? "res" : "concrete";
                        if (_rnd.NextDouble() < 0.4) _fm.Build(this, x0 + 3f, Y, z0 + 3f, _fm.Plan(fam, 4, 6, _rnd.Next(3, 7), false), _rnd.Next(2) * 90);
                        else foreach (var (ox, oz) in new[] { (1.5f, 1.5f), (4.5f, 1.5f), (1.5f, 4.5f), (4.5f, 4.5f) })
                        {
                            if (_rnd.NextDouble() < 0.85) _fm.Build(this, x0 + ox, Y, z0 + oz, _fm.Plan(fam, 3, 3, _rnd.Next(2, 6), false), _rnd.Next(4) * 90);
                            else _kb.Pine(this, x0 + ox, Y, z0 + oz, 2.2f);
                        }
                        break;
                    }
                    case "park":
                        for (int i = 0; i < 8; i++) _kb.Pine(this, x0 + 0.5f + _rnd.Next(6), Y, z0 + 0.5f + _rnd.Next(6), 1.8f + (float)_rnd.NextDouble() * 1.4f);
                        _kb.Box(this, new Vector3(x0 + 3f, Y + 0.2f, z0 + 3f), new Vector3(1.4f, 0.4f, 1.4f), _kb.Concrete);
                        break;
                    case "industry":
                        _kb.Factory(this, x0 + 2.2f, Y, z0 + 1.7f, 3.4f, 2.2f, 1.6f, 0);
                        _kb.Chimney(this, x0 + 5.0f, Y, z0 + 1.0f, 2.8f);
                        _kb.Tank(this, x0 + 1.2f, Y, z0 + 4.8f, 0.5f, 1.5f);
                        _kb.Tank(this, x0 + 2.4f, Y, z0 + 4.8f, 0.4f, 1.2f);
                        _kb.Chest(this, x0 + 5.3f, Y, z0 + 5.2f, 90);
                        for (int i = 0; i < 3; i++) _kb.Belt(this, x0 + 3.5f + i, Y, z0 + 3.6f, 0, i != 1);
                        _kb.Assembler(this, x0 + 2.0f, Y, z0 + 3.6f, 90);
                        _fm.Character(this, "citizen", x0 + 5.5f, Y, z0 + 2.6f, -90);
                        break;
                    case "military":
                        _fm.Build(this, x0 + 3f, Y, z0 + 3f, new List<string> { "base_5x5_office", "hangar_5x5", "hangar_5x5", "mech_5x5_office", "roof_5x5_heli_office" }, 0);
                        _mb.Build(this, _rnd.NextDouble() < 0.5 ? "heavy" : "general", _rnd.NextDouble() < 0.5 ? "g1" : "g2", x0 + 3f, Y, z0 + 8.5f, 180);
                        break;
                    case "solar":
                        for (int i = 0; i < 5; i++) for (int j = 0; j < 3; j++) _kb.Solar(this, x0 + 0.8f + i * 1.1f, Y, z0 + 1.0f + j * 1.8f, 0);
                        break;
                }
            }
        // メガタワー間の空中回廊(2 層目のデッキ高さ)
        _fm.Ship(this, 28.5f, Y + 22f, 46f, new List<string> { "ship_stern_7x4", "ship_turret_7x4", "ship_lift_7x4", "ship_mid_7x4", "ship_hangar_7x4", "ship_bridge_7x4", "ship_lift_7x4", "ship_turret_7x4", "ship_bow_7x7" }, 90);
        _fm.Ship(this, 48f, Y + 14f, 20f, new List<string> { "ship_stern_5x3", "ship_lift_5x3", "ship_mid_5x3", "ship_bridge_5x3", "ship_turret_5x3", "ship_bow_5x6" }, 30);
        // 空中の物流路: 近い建物どうしを自動で結ぶ
        _fm.AutoConnect(this, _rnd);
        // 高架の軌道(x = 21 の道路上)と列車
        for (int z = 0; z < N; z++)
        {
            if (z % 3 == 0) _kb.RailPillar(this, 21.5f, Y, z + 0.5f, 3.4f);
            _kb.RailSegment(this, 21.5f, Y + 3.5f, z + 0.5f);
        }
        _kb.Train(this, 21.5f, Y + 3.6f, 19.0f, 4.0f);
        // 路上の市民と人型ロボット
        for (int i = 0; i < 80; i++)
        {
            int x = _rnd.Next(1, N - 1), z = _rnd.Next(1, N - 1);
            bool road = x % 7 == 0 || z % 7 == 0 || z == 28 || z == 29;
            if (!road || (x - z >= 16 && x - z <= 17)) continue;
            _fm.Character(this, _fm.RandomHuman(), x + (float)_rnd.NextDouble(), Y, z + (float)_rnd.NextDouble(), _rnd.Next(360));
        }
        // 橋の欄干
        for (int x = 44; x <= 47; x++) { _kb.Box(this, new Vector3(x + 0.5f, Y + 0.3f, 28.05f), new Vector3(1.0f, 0.04f, 0.04f), _kb.Rust); _kb.Box(this, new Vector3(x + 0.5f, Y + 0.3f, 29.95f), new Vector3(1.0f, 0.04f, 0.04f), _kb.Rust); }
    }

    static bool NearRiver(int x0, int z0)
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
