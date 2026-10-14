using Godot;
using GPS.Sim;
using System;

/// <summary>
/// 描画試作(ゲームの計算には触れない)。ボクセル地形 + 自前のキットバッシュ生成(KitBash) + 陰影。
/// 起動: godot --path game res://scenes/preview.tscn ++ --scene village|city [--screenshot path]
/// </summary>
public partial class Preview : Node3D
{
    VoxelWorld _world = new();
    BlockRegistry _blocks = null!;
    KitBash _kb = null!;
    Random _rnd = new(7);
    Camera3D _cam = null!;
    int _frame;

    byte B(string name) => _blocks.IdOf(name);

    public override void _Ready()
    {
        _blocks = BlockRegistry.FromJson(FileAccess.GetFileAsString("res://data/blocks.json"));
        _kb = new KitBash(11);
        var args = OS.GetCmdlineUserArgs();
        string scene = Arg(args, "--scene") ?? "village";
        Vector3 pivot; float dist;
        if (scene == "city") { BuildCity(); pivot = new Vector3(30, 18, 30); dist = 122; }
        else { BuildVillage(); pivot = new Vector3(22, 6, 27); dist = 40; }
        if (Arg(args, "--view") == "close") { pivot = scene == "city" ? new Vector3(24, 9, 20) : new Vector3(24, 6, 33); dist = scene == "city" ? 34 : 16; }

        _kb.Flush(this);
        var tr = new TerrainRenderer();
        AddChild(tr);
        tr.Init(_world, _blocks);
        tr.BuildAll();
        AddChild(SceneSetup.Lighting());

        _cam = new Camera3D { Fov = 38, Far = 800 };
        AddChild(_cam);
        float yaw = 0.78f, elev = 0.52f;
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
        _kb.Humanoid(this, 12.2f, Top(12, 19), 19.4f, 150, false); _kb.Humanoid(this, 13.0f, Top(13, 19), 19.8f, -30, false);
        _kb.Humanoid(this, 16.3f, Top(16, 20), 20.5f, 60, false); _kb.Humanoid(this, 14.0f, Top(14, 22), 22.2f, 200, false);
        _kb.Humanoid(this, 21.3f, Top(21, 35), 35.5f, 30, true); _kb.Humanoid(this, 26.5f, Top(26, 31), 31.5f, -90, true);
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
                        if ((x0, z0) is (15, 8) or (22, 15) or (29, 8))
                            _kb.MegaTower(this, x0 + 3f, Y, z0 + 3f, 3.6f, 3.6f, (x0, z0) == (22, 15) ? 8 : 6, 6, 0);
                        else if (_rnd.NextDouble() < 0.4)
                            _kb.Tower(this, x0 + 3f, Y, z0 + 3f, 3.8f, 3.8f, _rnd.Next(24, 49), _rnd.Next(4) * 90);
                        else
                            foreach (var (ox, oz) in new[] { (1.6f, 1.6f), (4.4f, 1.6f), (1.6f, 4.4f), (4.4f, 4.4f) })
                                _kb.Tower(this, x0 + ox, Y, z0 + oz, 2.2f, 2.2f, _rnd.Next(8, 26), _rnd.Next(4) * 90);
                        break;
                    case "residential":
                        foreach (var (ox, oz) in new[] { (1.6f, 1.6f), (4.4f, 1.6f), (1.6f, 4.4f), (4.4f, 4.4f) })
                        {
                            if (_rnd.NextDouble() < 0.8) _kb.Building(this, x0 + ox, Y, z0 + oz, 2.2f, 2.2f, _rnd.Next(3, 10), _rnd.NextDouble() < 0.6 ? KitBash.Style.Slab : KitBash.Style.Podium, _rnd.Next(4) * 90);
                            else _kb.Pine(this, x0 + ox, Y, z0 + oz, 2.2f);
                        }
                        break;
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
                        _kb.Humanoid(this, x0 + 5.5f, Y, z0 + 2.6f, -90, true);
                        break;
                    case "military":
                        _kb.Hangar(this, x0 + 3f, Y, z0 + 1.4f, 4.6f, 2.2f, 1.9f, 0);
                        for (int i = 0; i < 6; i++) for (int j = 0; j < 3; j++) _kb.Humanoid(this, x0 + 0.5f + i, Y, z0 + 3.6f + j * 0.9f, 180, true);
                        _kb.Turret(this, x0 + 0.6f, Y, z0 + 0.6f, 45);
                        _kb.Turret(this, x0 + 5.4f, Y, z0 + 5.4f, -135);
                        break;
                    case "solar":
                        for (int i = 0; i < 5; i++) for (int j = 0; j < 3; j++) _kb.Solar(this, x0 + 0.8f + i * 1.1f, Y, z0 + 1.0f + j * 1.8f, 0);
                        break;
                }
            }
        // メガタワー間の空中回廊(2 層目のデッキ高さ)
        _kb.SkyBridge(this, new Vector3(18 + 2.4f, Y + 0.3f + 0.9f * 2 + 0.2f + (0.9f * 6 + 0.5f) * 2 + 0.3f, 11), new Vector3(25 - 2.4f, Y + 0.3f + 0.9f * 2 + 0.2f + (0.9f * 6 + 0.5f) * 2 + 0.3f, 18));
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
            _kb.Humanoid(this, x + (float)_rnd.NextDouble(), Y, z + (float)_rnd.NextDouble(), _rnd.Next(360), _rnd.NextDouble() < 0.5);
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
