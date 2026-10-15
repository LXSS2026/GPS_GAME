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
    Equipment _eq = null!;
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
        _eq = new Equipment(_fm);
        var args = OS.GetCmdlineUserArgs();
        string scene = Arg(args, "--scene") ?? "village";
        Vector3 pivot; float dist;
        if (scene == "city") { BuildCity(); pivot = new Vector3(298, 40, 298); dist = 900; }
        else if (scene == "buildings") { BuildShowcase(); pivot = new Vector3(60, 17, 12); dist = 112; }
        else if (scene == "modules") { BuildModules(); pivot = new Vector3(48, 14, 16); dist = 100; }
        else if (scene == "micro") { BuildMicro(); pivot = new Vector3(108f, 6f, 50f); dist = 260f; }
        else if (scene == "ship") { BuildShip(); pivot = new Vector3(0f, 22f, 10f); dist = 230f; }
        else if (scene == "robots") { BuildRobots(); pivot = new Vector3(48f, 9f, 34f); dist = 80f; }
        else { BuildVillage(); pivot = new Vector3(22, 6, 27); dist = 40; }
        if (Arg(args, "--view") == "close") { pivot = scene == "city" ? new Vector3(240, 40, 180) : new Vector3(24, 6, 33); dist = scene == "city" ? 300 : 16; }
        if (Arg(args, "--pivot") is string pv) { var p = pv.Split(','); pivot = new Vector3(float.Parse(p[0]), float.Parse(p[1]), float.Parse(p[2])); }   // --view より後に適用(明示指定が勝つ)
        if (Arg(args, "--dist") is string ds) dist = float.Parse(ds);

        _kb.Flush(this);
        _eq.Flush(this);
        var tr = new TerrainRenderer();
        AddChild(tr);
        tr.Init(_world, _blocks);
        tr.BuildAll();
        AddChild(SceneSetup.Lighting());

        _cam = new Camera3D { Fov = 38, Far = 3000 };
        AddChild(_cam);
        float yaw = scene == "buildings" ? 0.35f : scene == "modules" ? 0.6f : scene == "robots" ? 0.2f : scene == "ship" ? 2.3f : scene == "micro" ? 0.5f : 0.78f, elev = scene == "buildings" ? 0.30f : scene == "modules" ? 0.38f : scene == "robots" ? 0.16f : scene == "ship" ? 0.3f : scene == "micro" ? 0.75f : 0.52f;
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
        if (Arg(OS.GetCmdlineUserArgs(), "--riflex") is string rx) MechBuilder.RifleX = float.Parse(rx);
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

    static readonly List<string> MechHangar = new() { "base_12x12_office", "hangar_mech_12x12", "roof_12x12_heli_office" };
    static readonly List<string> BigShip = new() { "ship_stern_30x16", "ship_turret_30x16", "ship_lift_30x16", "ship_mid_30x16", "ship_hangar_30x24", "ship_hangar_30x24", "ship_mid_30x16", "ship_bridge_30x16", "ship_lift_30x16", "ship_turret_30x16", "ship_bow_30x28" };   // 全長 204 m
    static readonly List<string> SmallShip = new() { "ship_stern_18x12", "ship_lift_18x12", "ship_mid_18x12", "ship_hangar_18x18", "ship_bridge_18x12", "ship_turret_18x12", "ship_bow_18x24" };   // 全長 102 m

    // ---------- ミクロの地図(平地 1 ブロック 100x100 と建物の 1 階 100x100。設備は 1 セル角) ----------
    void BuildMicro()
    {
        byte stone = B("Stone"), plaza = B("Plaza"), conc = B("Concrete"), road = B("RoadX");
        string? only = Arg(OS.GetCmdlineUserArgs(), "--only");
        const int S = 100, G = 16;   // 街区 100 m、街路 16 m
        for (int x = -G; x < S * 2 + G * 2; x++) for (int z = -G; z < S + G; z++)
        {
            _world.Set(x, 0, z, stone); _world.Set(x, 1, z, stone);
            bool inBlockA = x >= 0 && x < S && z >= 0 && z < S, inBlockB = x >= S + G && x < S * 2 + G && z >= 0 && z < S;
            _world.Set(x, 2, z, inBlockA ? conc : inBlockB ? plaza : road);
        }
        _world.Dirty.Clear();
        var rnd = new Random(3);
        // 左: 平地 1 ブロック(100x100)の工場。区画の縁を光る線で示す
        if (only != "floor")
        {
            _eq.SampleFactory(0, 3f, 0, S, S, rnd);
            for (int i = 0; i <= S; i += 10) { _kb.Strip(this, new Vector3(i, 3.02f, S / 2f), new Vector3(0.04f, 0.02f, S), _kb.GlowCyan); _kb.Strip(this, new Vector3(S / 2f, 3.02f, i), new Vector3(S, 0.02f, 0.04f), _kb.GlowCyan); }
            for (int i = 0; i < 12; i++) _fm.Character(this, "citizen", 0.5f + rnd.Next(S), 3f, 0.5f + rnd.Next(S), rnd.Next(360));
        }
        // 右: 建物(100x100 事務所・4 階)の 2 階だけを開き、設備を敷く。上の階は描かない(実装では薄く残す)
        if (only != "ground")
        {
            var stack = _fm.Plan("office", S, S, 4, false, "heli").GetRange(0, 3);
            float ox = S + G;
            _fm.Build(this, ox + S / 2f, 3f, S / 2f, stack, 0, cutawayBelow: 3, cutawayFrom: 2, interior: false);
            float fy = 3f + _fm.FloorHeight * 2 + 0.12f;
            _eq.SampleFactory((int)ox + 2, fy, 2, S - 4, S - 4, rnd);
            for (int i = 0; i < 12; i++) _fm.Character(this, "citizen", ox + 2.5f + rnd.Next(S - 4), fy, 2.5f + rnd.Next(S - 4), rnd.Next(360));
        }
        _eq.Flush(this);
        GD.Print($"[micro] equipment placed: {_eq.Count}");
        _kb.Light(this, new Vector3(S, 60f, S / 2f), new Color(0.9f, 0.92f, 1.0f), 220f, 1.4f);
    }

    // ---------- 航空戦艦と機体格納庫(機体 15〜20 m に合わせた寸法の確認) ----------
    void BuildShip()
    {
        byte stone = B("Stone"), plaza = B("Plaza");
        for (int x = -140; x < 140; x++) for (int z = -60; z < 80; z++) { _world.Set(x, 0, z, stone); _world.Set(x, 1, z, stone); _world.Set(x, 2, z, plaza); }
        _world.Dirty.Clear();
        // 大型(全長 204 m): 第 1 格納庫区画を切り取り表示し、中に機体 2 体
        _fm.Ship(this, 0, 36f, 40, BigShip, 0, cutawayIndex: 4);
        var bay = _fm.ShipBayFloor(BigShip, 4);
        _mb.Build(this, "general", "g2", bay.X - 5f, 36f + bay.Y, 40 + bay.Z, 0);
        _mb.Build(this, "heavy", "g2", bay.X + 6f, 36f + bay.Y, 40 + bay.Z, 0);
        // 小型(全長 102 m): 格納庫区画を切り取り表示
        _fm.Ship(this, -20, 24f, -20, SmallShip, 20, cutawayIndex: 3);
        // 地上: 機体格納庫の建物(切り取り表示、中に機体)と、比較用の機体・人間・7 階の塔
        _fm.Build(this, 60, 3, -10, MechHangar, 0, cutawayBelow: 2);
        _mb.Build(this, "general", "g4", 60, 3 + 3.15f, -10, 0);
        _fm.Build(this, 60, 3, -30, MechHangar, 0);   // 閉じた状態(大扉は -Z 側)
        _mb.Build(this, "heavy", "g1", 50, 3, -42, 150);
        _kb.Light(this, new Vector3(60f, 14f, -48f), new Color(1.0f, 0.85f, 0.7f), 40f, 1.5f);
        _mb.Build(this, "fast", "g3", 78, 3, -4, 20);
        _fm.Character(this, "citizen", 72, 3, 0, 20); _fm.Character(this, "soldier", 73.2f, 3, 0.5f, 0);
        _fm.Build(this, 92, 3, -10, _fm.Plan("office", 5, 5, 7, false, "heli"), 0);
        _kb.Light(this, new Vector3(0, 60f, 60f), new Color(0.9f, 0.92f, 1.0f), 160f, 1.4f);
        _kb.Light(this, new Vector3(60f, 30f, -30f), new Color(1.0f, 0.9f, 0.8f), 80f, 1.2f);
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
        // 機体格納庫の建物(基壇 + 6 階分の格納庫 + 屋上。基壇と格納庫を切り取り表示し、中に機体)
        fm.Build(this, xx + 5, 3, 8, MechHangar, 0, cutawayBelow: 2);
        _mb.Build(this, "general", "g2", xx + 5, 3 + 3.15f, 8, 0);
        // 小型の航空戦艦を上空に(格納庫区画を切り取り表示、中に機体)
        fm.Ship(this, 48, 30f, 24, SmallShip, 0, cutawayIndex: 3);
        var bay = fm.ShipBayFloor(SmallShip, 3);
        _mb.Build(this, "fast", "g3", 48 + bay.X, 30f + bay.Y, 24 + bay.Z, 0);
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
        // 街区 100 m を 5 × 5、街路 16 m(2026-10-15 決定: 1 マス = 1 m、建物と平地ブロックは 100x100 m)
        const int S = 100, G = 16, NB = 5, N = NB * S + (NB + 1) * G;   // 596 m 四方
        byte stone = B("Stone"), dirt = B("Dirt"), plaza = B("Plaza"), concrete = B("Concrete"), asphalt = B("Asphalt"), rx = B("RoadX"), rz = B("RoadY");
        byte grass = B("Grass"), water = B("Water");
        static int Lane(int v) { int p = v % (S + G); return p < G ? -1 : v / (S + G); }   // 街路なら -1、街区番号
        static int LanePos(int v) => v % (S + G);
        for (int x = 0; x < N; x++)
            for (int z = 0; z < N; z++)
            {
                _world.Set(x, 0, z, stone); _world.Set(x, 1, z, stone); _world.Set(x, 2, z, dirt);
                int bi = Lane(x), bj = Lane(z);
                bool roadX = bj < 0, roadZ = bi < 0;
                bool river = roadX && z / (S + G) == 3 && LanePos(z) >= 2 && LanePos(z) < G - 2 && !roadZ;   // 川は 4 本目の横街路に沿う(縦街路との交点が橋)
                bool centerLine = (roadX && LanePos(z) == G / 2) || (roadZ && LanePos(x) == G / 2);
                byte top = roadX && roadZ ? asphalt : roadX ? (centerLine ? asphalt : rx) : roadZ ? (centerLine ? asphalt : rz) : District(bi, bj) switch { "park" => grass, "industry" => concrete, _ => plaza };
                if (river) { _world.Set(x, 2, z, water); continue; }
                _world.Set(x, 3, z, top);
            }
        _world.Dirty.Clear();
        const float Y = 4f;
        var rnd = _rnd;

        for (int bi = 0; bi < NB; bi++)
            for (int bj = 0; bj < NB; bj++)
            {
                int x0 = G + bi * (S + G), z0 = G + bj * (S + G);
                float cx = x0 + S / 2f, cz = z0 + S / 2f;
                switch (District(bi, bj))
                {
                    case "downtown":
                    {
                        string fam = new[] { "office", "office", "exo", "svc", "concrete" }[rnd.Next(5)];
                        _fm.Build(this, cx, Y, cz, _fm.Plan(fam, S, S, rnd.Next(20, 61), false), rnd.Next(4) * 90);   // 60〜180 m
                        break;
                    }
                    case "residential":
                        _fm.Build(this, cx, Y, cz, _fm.Plan(rnd.NextDouble() < 0.6 ? "res" : "concrete", S, S, rnd.Next(3, 8), false), rnd.Next(4) * 90);
                        break;
                    case "park":
                        for (int i = 0; i < 60; i++) _kb.Pine(this, x0 + 2f + rnd.Next(S - 4), Y, z0 + 2f + rnd.Next(S - 4), 4f + (float)rnd.NextDouble() * 4f);
                        _kb.Box(this, new Vector3(cx, Y + 0.3f, cz), new Vector3(12f, 0.6f, 12f), _kb.Concrete);
                        break;
                    case "industry":
                    {   // 工場棟(コンクリート 2〜4 階)。1 階を開いて設備を敷く
                        int floors = rnd.Next(2, 5);
                        _fm.Build(this, cx, Y, cz, _fm.Plan("concrete", S, S, floors, false, "boiler"), 0, cutawayBelow: 2, cutawayFrom: 1, interior: false);
                        _eq.SampleFactory(x0 + 2, Y + _fm.FloorHeight + 0.12f, z0 + 2, S - 4, S - 4, rnd);
                        break;
                    }
                    case "military":
                        // 機体格納庫(12x12・6 階分)を 4 棟、扉の前に機体
                        foreach (var (ox, oz) in new[] { (20f, 30f), (50f, 30f), (80f, 30f), (35f, 70f) })
                        {
                            _fm.Build(this, x0 + ox, Y, z0 + oz, MechHangar, 0);
                            _mb.Build(this, rnd.NextDouble() < 0.5 ? "heavy" : "general", $"g{rnd.Next(1, 5)}", x0 + ox, Y, z0 + oz - 12f, 180);
                        }
                        _mb.BuildFighter(this, _mb.Presets["fast"], "g3", x0 + 70f, Y + 30f, z0 + 75f, 30);
                        break;
                    case "solar":
                        for (int i = 0; i < 40; i++) for (int j = 0; j < 25; j++) _kb.Solar(this, x0 + 2f + i * 2.4f, Y, z0 + 2f + j * 3.8f, 0);
                        break;
                }
            }
        _eq.Flush(this);
        // 航空戦艦: 大型が中心街の上空、小型が軍区画の上空
        _fm.Ship(this, N * 0.45f, Y + 90f, N * 0.3f, BigShip, 25);
        _fm.Ship(this, N * 0.2f, Y + 60f, N * 0.8f, SmallShip, 70);
        // 高架の軌道(2 本目の縦街路の中央)と列車
        float railX = G + (S + G) + G / 2f + 0.5f;
        for (int z = 0; z < N; z++)
        {
            if (z % 6 == 0) _kb.RailPillar(this, railX, Y, z + 0.5f, 6.4f);
            _kb.RailSegment(this, railX, Y + 6.5f, z + 0.5f);
        }
        for (int k = 0; k < 3; k++) _kb.Train(this, railX, Y + 6.6f, 120f + k * 4.2f, 4.0f);
        // 路上の市民
        for (int i = 0; i < 400; i++)
        {
            int x = rnd.Next(1, N - 1), z = rnd.Next(1, N - 1);
            if (Lane(x) >= 0 && Lane(z) >= 0) continue;
            _fm.Character(this, _fm.RandomHuman(), x + (float)rnd.NextDouble(), Y, z + (float)rnd.NextDouble(), rnd.Next(360));
        }
        GD.Print($"[city] equipment placed: {_eq.Count}");
    }

    /// <summary>街区 (bi, bj) の地区。5 × 5 の街区。</summary>
    static string District(int bi, int bj)
    {
        if (bi < 0 || bj < 0) return "road";
        if (bi == 2 && bj == 2) return "park";
        if (bj <= 1 && bi >= 1 && bi <= 3) return "downtown";
        if (bj >= 3 && bi >= 3) return "industry";
        if (bj >= 3 && bi <= 1) return "military";
        if (bi == 0 && bj == 0) return "solar";
        return "residential";
    }
}
