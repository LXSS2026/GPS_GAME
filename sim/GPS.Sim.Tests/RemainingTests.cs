using GPS.Sim;
using Xunit;

/// <summary>残る粗の実装(2026-10-16): 建物の修復、制御室からの砲塔と戦艦、遮蔽、航空戦の第 1 段階。</summary>
public class RemainingTests
{
    static readonly GameData Data = GameData.Load(GameData.FindDataDir());
    static BlockRegistry Blocks() => BlockRegistry.FromJson(File.ReadAllText(Path.Combine(GameData.FindDataDir(), "blocks.json")));
    static Simulation NewSim(ulong seed) { var s = new Simulation(Blocks(), Data, seed); s.Factory.Creative = true; return s; }
    static void Do(Simulation s, params ICommand[] cmds) { foreach (var c in cmds) s.Enqueue(c); s.Step(); }

    [Fact]
    public void RepairBuilding_RestoresHp_AndRevivesFallen()
    {
        var s = NewSim(1); var f = s.Factory; f.Creative = false; f.Treasury = 10_000;
        Do(s, new FoundCity("a", 50, 60), new CreateBuilding(1, "工場棟", 20, 20, 2, true));
        var b = f.Get(1)!; b.Hp = 0; b.Destroyed = true;
        Do(s, new RepairBuilding(1));
        Assert.False(b.Destroyed); Assert.Equal(b.MaxHp, b.Hp); Assert.Equal(10_000 - 20 * 20 * 2 * 3 / 2, f.Treasury);   // 全損なので建設費の半分
        f.Treasury = 0; b.Hp = b.MaxHp / 2; Do(s, new RepairBuilding(1)); Assert.Equal(b.MaxHp / 2, b.Hp); Assert.Contains(f.Events, e => e.Kind == "build_failed");
    }

    [Fact]
    public void Mcu_ReadsTurret_AndMovesShip()
    {
        var s = NewSim(2); var f = s.Factory;
        Do(s, new BuildShip(50, "艦", "small", 0, 0), new PlaceStructure(50, 0, "reactor_g3", 2, 2, 0, Direction.East), new PlaceStructure(50, 0, "turret_beam", 2, 10, 0, Direction.East), new PlaceStructure(50, 0, "control_room", 6, 6, 0, Direction.East));
        var ship = f.Get(50)!; var mcu = ship.Floors[0].Structures.OfType<Mcu>().First(); var turret = ship.Floors[0].Structures.OfType<Turret>().First();
        // 砲塔の準備(1)なら戦艦を (120, 0) へ動かす
        var prog = new Program { Interval = 6, Nodes = { new McuNode { Id = 1, Type = "in.turret.ready", TargetId = turret.Id, TargetFloor = 0 }, new McuNode { Id = 2, Type = "out.ship.go", Params = { ["x"] = 120, ["y"] = 0 } } }, Links = { new(1, 0, 2, 0) } };
        Do(s, new SetProgram(50, 0, mcu.Id, prog));
        s.Run(60 * 3); Assert.NotNull(ship.MoveTargetX); Assert.True(ship.X > 0);
        s.Run(60 * 12); Assert.Equal(120, ship.X, 3);
        // 建物の耐久 % を読める
        var prog2 = new Program { Interval = 6, Nodes = { new McuNode { Id = 1, Type = "in.building.hp" }, new McuNode { Id = 2, Type = "out.structure.enabled", TargetId = turret.Id, TargetFloor = 0 } }, Links = { new(1, 0, 2, 0) } };
        Do(s, new SetProgram(50, 0, mcu.Id, prog2)); s.Run(60); Assert.True(turret.Enabled);
        ship.Hp = 0.0; s.Run(60); Assert.True(ship.Destroyed);   // 耐久 0 で沈む(読みは 0 → 砲塔 停止)
    }

    [Fact]
    public void Cover_TallBuildingBlocksSight_BeyondCloseRange()
    {
        var s = NewSim(3); var f = s.Factory;
        Do(s, new CreateBuilding(1, "a", 12, 12, 1, false), new SetBuildingPosition(1, 0, 200), new CreateBuilding(2, "b", 12, 12, 1, false), new SetBuildingPosition(2, 300, 200),
            new CreateBuilding(9, "ビル", 30, 30, 4, false), new SetBuildingPosition(9, 100, 0),   // x 100〜130, y 0〜30 の 12 m の建物
            new CreateMech(1, 3, "general", 80), new CreateMech(11, 3, "general", 80));
        Do(s, new FormSquad(1, 1, "a", new[] { 1 }, 1), new FormSquad(2, 2, "e", new[] { 11 }, 2));
        Do(s, new Deploy(1, 80, 10), new Deploy(2, 150, 10), new OrderSquad(1, SquadOrder.Hold), new OrderSquad(2, SquadOrder.Hold));
        s.Run(5);
        Assert.Equal(-1, f.Squads[0].Members[0].TargetMech);   // 70 m 先だが間にビル
        Do(s, new Deploy(2, 150, 110)); s.Run(5);   // ビルの南を回り込めば見える(射線 (80,10)-(150,110) は x 96 以降で y 32 以上 = ビルの粗格子の区画(x 96〜143, y 0〜31)を通らない)
        Assert.Equal(11, f.Squads[0].Members[0].TargetMech);
        Do(s, new Deploy(2, 95, 10)); s.Run(5); Assert.Equal(11, f.Squads[0].Members[0].TargetMech);   // 15 m なら見える
    }

    [Fact]
    public void FastMechs_FlyWhenFar_AndShipTurretsBombardEnemyBase()
    {
        var s = NewSim(4); var f = s.Factory;
        Do(s, new CreateBuilding(1, "a", 12, 12, 1, false), new SetBuildingPosition(1, 0, 0), new CreateMech(1, 3, "fast", 80), new CreateMech(2, 3, "general", 80));
        Do(s, new FormSquad(1, 1, "f", new[] { 1 }, 1), new FormSquad(2, 1, "g", new[] { 2 }, 1), new Deploy(1, 0, 0), new Deploy(2, 0, 20), new OrderSquad(1, SquadOrder.Move, 300, 0), new OrderSquad(2, SquadOrder.Move, 300, 20));
        s.Run(60 * 5);
        var fast = f.Squads[0].Members[0]; var gen = f.Squads[1].Members[0];
        Assert.True(fast.Flying); Assert.True(fast.X > gen.X * 2.5, $"fast {fast.X} general {gen.X}");   // 高速 1.6 × 戦闘機 2.0
        // 戦艦の砲塔が戦争中の勢力の拠点を砲撃する
        var s2 = NewSim(5); var f2 = s2.Factory;
        byte road = s2.Blocks.IdOf("RoadX"); for (int x = -2; x < 400; x++) s2.World.Set(x, 0, -1, road);
        Do(s2, new FoundCity("a", 60, 60), new CreateBuilding(10, "拠点", 12, 12, 1, false), new SetBuildingPosition(10, 0, 0), new SetCapital(10), new CreateBuilding(41, "クロガネ", 12, 12, 3, false), new SetBuildingPosition(41, 300, 0), new AddPower(2, 41),
            new BuildShip(50, "艦", "small", 180, 0), new PlaceStructure(50, 0, "reactor_g3", 2, 2, 0, Direction.East), new PlaceStructure(50, 0, "turret_beam", 2, 50, 0, Direction.East), new PlaceStructure(50, 0, "turret_beam", 15, 50, 0, Direction.East));
        var ship = f2.Get(50)!; var baseB = f2.Get(41)!; double hp0 = baseB.Hp;
        s2.Run(60 * 5); Assert.Equal(hp0, baseB.Hp);   // 戦争前は撃たない
        Do(s2, new DeclareWar(2), new MoveShip(50, 240, 0)); s2.Run(60 * 40);
        Assert.True(baseB.Hp < hp0, $"hp {baseB.Hp}/{hp0}"); Assert.Contains(f2.Events, e => e.Kind == "hit_building" && e.Building == -50);
    }
}

public class TerrainTests
{
    static readonly GameData Data = GameData.Load(GameData.FindDataDir());
    static BlockRegistry Blocks() => BlockRegistry.FromJson(File.ReadAllText(Path.Combine(GameData.FindDataDir(), "blocks.json")));
    static void Do(Simulation s, params ICommand[] cmds) { foreach (var c in cmds) s.Enqueue(c); s.Step(); }

    [Fact]
    public void Hill_BlocksSight_FlyingSeesOver()
    {
        var s = new Simulation(Blocks(), Data, 11); s.Factory.Creative = true; var f = s.Factory;
        byte dirt = s.Blocks.IdOf("Dirt"); for (int x = 0; x < 200; x++) for (int z = 0; z < 40; z++) s.World.Set(x, 0, z, dirt);
        for (int x = 90; x < 110; x++) for (int z = 0; z < 40; z++) for (int y = 1; y <= 8; y++) s.World.Set(x, y, z, dirt);   // 高さ 8 m の尾根
        Do(s, new CreateBuilding(1, "a", 12, 12, 1, false), new SetBuildingPosition(1, 0, 100), new CreateBuilding(2, "b", 12, 12, 1, false), new SetBuildingPosition(2, 300, 100),
            new CreateMech(1, 3, "general", 80), new CreateMech(11, 3, "general", 80), new CreateMech(12, 3, "fast", 80));
        Do(s, new FormSquad(1, 1, "a", new[] { 1 }, 1), new FormSquad(2, 2, "e", new[] { 11 }, 2), new FormSquad(3, 2, "f", new[] { 12 }, 2));
        Do(s, new Deploy(1, 60, 20), new Deploy(2, 140, 20), new OrderSquad(1, SquadOrder.Hold), new OrderSquad(2, SquadOrder.Hold));
        s.Run(6); Assert.Equal(-1, f.Squads[0].Members[0].TargetMech);   // 尾根の向こうは見えない
        Do(s, new Deploy(2, 91, 20)); s.Run(6); Assert.Equal(11, f.Squads[0].Members[0].TargetMech);   // 尾根の手前の縁に立てば見える(奥に立つと崖の縁に隠れる)
        Assert.Equal(8, Combat.GroundHeight(s, 100, 20)); Assert.Equal(0, Combat.GroundHeight(s, 60, 20));
    }
}
