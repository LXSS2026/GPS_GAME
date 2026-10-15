using GPS.Sim;
using Xunit;

/// <summary>航空戦艦の第 1 段階(建造・移動・隊の出撃と帰還・砲塔・撃沈)。</summary>
public class ShipTests
{
    static readonly GameData Data = GameData.Load(GameData.FindDataDir());
    static BlockRegistry Blocks() => BlockRegistry.FromJson(File.ReadAllText(Path.Combine(GameData.FindDataDir(), "blocks.json")));
    static Simulation NewSim(ulong seed) { var s = new Simulation(Blocks(), Data, seed); s.Factory.Creative = true; return s; }
    static void Do(Simulation s, params ICommand[] cmds) { foreach (var c in cmds) s.Enqueue(c); s.Step(); }

    [Fact]
    public void Build_RequiresStage_CostsTreasury_AndMoves()
    {
        var s = NewSim(1); var f = s.Factory; f.Creative = false; f.Treasury = 100_000;
        Do(s, new FoundCity("a", 100, 60), new BuildShip(50, "ハヤテ", "small", 0, 0));
        Assert.Null(f.Get(50)); Assert.Contains(f.Events, e => e.Kind == "build_failed");   // 村では建造できない
        f.City!.Stage = 4; Do(s, new BuildShip(50, "ハヤテ", "small", 0, 0));
        var ship = f.Get(50)!; Assert.True(ship.IsShip); Assert.Equal(2, ship.Floors.Count); Assert.Equal(18, ship.Floors[0].W); Assert.Equal(102, ship.Floors[0].D);
        Assert.Equal(100_000 - 18 * 102 * 2 * 8, f.Treasury); Assert.Equal(18 * 102 * 2 * 400 * 2, ship.MaxHp);
        Do(s, new MoveShip(50, 120, 0));
        s.Run(60 * 5); Assert.True(ship.X > 50 && ship.X < 70, $"x {ship.X}");   // 12 m/s × 5 s
        s.Run(60 * 6); Assert.Equal(120, ship.X); Assert.Null(ship.MoveTargetX); Assert.Contains(f.Events, e => e.Kind == "ship_arrived");
    }

    [Fact]
    public void Squads_DeployFromShip_ReturnToIt_AndTurretsFire_UntilSunk()
    {
        var s = NewSim(2); var f = s.Factory;
        Do(s, new BuildShip(50, "ハヤテ", "small", 0, 0), new PlaceStructure(50, 0, "reactor_g3", 2, 2, 0, Direction.East), new PlaceStructure(50, 0, "turret_beam", 2, 10, 0, Direction.East), new PlaceStructure(50, 0, "turret_beam", 14, 10, 0, Direction.East),
            new CreateBuilding(2, "敵", 12, 12, 1, false), new SetBuildingPosition(2, 260, 40));
        for (int i = 1; i <= 3; i++) s.Enqueue(new CreateMech(i, 3, "general", 80)); for (int i = 11; i <= 16; i++) s.Enqueue(new CreateMech(i, 2, "heavy", 70)); s.Step();
        Do(s, new FormSquad(1, 1, "艦載隊", new[] { 1, 2, 3 }, 50), new FormSquad(2, 2, "敵", Enumerable.Range(11, 6).ToArray(), 2));
        var ship = f.Get(50)!;
        Do(s, new MoveShip(50, 100, 40)); s.Run(60 * 12); Assert.Equal(100, ship.X, 3);
        Assert.All(f.Squads[0].Members, u => Assert.Equal(100, u.X, 3));   // 待機中の隊は船と一緒に動く
        Do(s, new Deploy(1, 110, 50), new OrderSquad(1, SquadOrder.Attack, TargetSquad: 2), new Deploy(2, 250, 40), new OrderSquad(2, SquadOrder.Attack, TargetBuilding: 50));
        s.Run(60 * 60);
        Assert.True(ship.Floors[0].Structures.OfType<Turret>().Sum(t => t.Shots) > 0, "艦の砲塔が撃つ");
        Assert.True(ship.Hp < ship.MaxHp, "敵が艦を撃つ");
        Do(s, new OrderSquad(1, SquadOrder.Retreat)); s.Run(60 * 30);
        Assert.Equal(SquadState.InHangar, f.Squads[0].State);
        ship.Hp = 1; s.Run(60 * 10);
        Assert.True(ship.Destroyed); Assert.Contains(f.Events, e => e.Kind == "ship_sunk"); Assert.DoesNotContain(f.Squads, q => q.HomeBuilding == 50);
        var loaded = Save.Load(Save.Snapshot(s), Blocks(), Data); Assert.Equal(Save.StateHash(s), Save.StateHash(loaded));
    }
}
