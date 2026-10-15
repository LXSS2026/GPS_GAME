using GPS.Sim;
using Xunit;

/// <summary>AI 勢力の戦艦と、戦艦どうしの砲撃戦(2026-10-16)。</summary>
public class PowerShipTests
{
    static readonly GameData Data = GameData.Load(GameData.FindDataDir());
    static BlockRegistry Blocks() => BlockRegistry.FromJson(File.ReadAllText(Path.Combine(GameData.FindDataDir(), "blocks.json")));
    static void Do(Simulation s, params ICommand[] cmds) { foreach (var c in cmds) s.Enqueue(c); s.Step(); }

    static Simulation World(ulong seed)
    {
        var s = new Simulation(Blocks(), Data, seed); s.Factory.Creative = true;
        byte road = s.Blocks.IdOf("RoadX"); for (int x = -2; x < 500; x++) s.World.Set(x, 0, -1, road);
        Do(s, new FoundCity("a", 60, 60), new CreateBuilding(10, "拠点", 12, 12, 1, false), new SetBuildingPosition(10, 0, 0), new SetCapital(10),
            new CreateBuilding(41, "クロガネ", 12, 12, 3, false), new SetBuildingPosition(41, 400, 0), new AddPower(2, 41));
        s.Step(); return s;
    }

    [Fact]
    public void Power_BuildsShipAtTech4_SailsToCapital_AndBombards()
    {
        var s = World(1); var f = s.Factory; var p = f.Diplomacy!.Powers.First(x => x.Id == 2);
        Assert.Equal(2, f.Get(41)!.OwnerPower);
        p.Tech = 4; p.Treasury = 100_000; f.City!.Days = 29; CitySim.Day(s, f.City);   // 30 日目 = 月次
        var ship = f.Buildings.FirstOrDefault(b => b.IsShip && b.OwnerPower == 2); Assert.NotNull(ship); Assert.Contains(f.Events, e => e.Kind == "power_ship");
        Assert.Equal(2, ship!.Floors[0].Structures.OfType<Turret>().Count());
        s.Run(60 * 5); Assert.Null(ship.MoveTargetX);   // 平時は動かない
        DiplomacySim.DeclareWar(s, p, byPlayer: false); CitySim.Hour(s, f.City);
        Assert.NotNull(ship.MoveTargetX);
        var cap = f.Get(10)!; double hp0 = cap.Hp;
        s.Run(60 * 60);
        Assert.True(cap.Hp < hp0, $"hp {cap.Hp}/{hp0}"); Assert.Contains(f.Events, e => e.Kind == "hit_building" && e.Building == -ship.Id && e.Floor == 10);
        // 和平すると拠点の隣へ戻る
        DiplomacySim.MakePeace(s, p, 0, 0); CitySim.Hour(s, f.City); Assert.NotNull(ship.MoveTargetX); Assert.True(ship.MoveTargetX > 300);
    }

    [Fact]
    public void ShipVersusShip_BothBombard_UntilOneSinks()
    {
        var s = World(2); var f = s.Factory; var p = f.Diplomacy!.Powers.First(x => x.Id == 2);
        p.Tech = 4; p.Treasury = 100_000; f.City!.Days = 29; CitySim.Day(s, f.City);
        var enemyShip = f.Buildings.First(b => b.IsShip && b.OwnerPower == 2);
        Do(s, new BuildShip(50, "ハヤテ", "small", 100, 0), new PlaceStructure(50, 0, "reactor_g3", 2, 2, 0, Direction.East));
        for (int i = 0; i < 6; i++) s.Enqueue(new PlaceStructure(50, 0, "turret_beam", 2 + (i % 2) * 13, 20 + (i / 2) * 20, 0, Direction.East)); s.Step();
        var mine = f.Get(50)!;
        DiplomacySim.DeclareWar(s, p, byPlayer: false);
        Do(s, new MoveShip(50, enemyShip.X + 60, enemyShip.Y - 60)); s.Run(60 * 40);   // 敵の拠点より敵の艦に近い位置(砲塔は最寄りの敵の建物を撃つ)
        Assert.True(enemyShip.Hp < enemyShip.MaxHp, "自分の艦が敵の艦を撃つ"); Assert.Contains(f.Events, e => e.Kind == "hit_building" && e.Building == -50 && e.Floor == enemyShip.Id);
        Assert.True(mine.Hp < mine.MaxHp, "敵の艦も撃ち返す");
        for (int i = 0; i < 160 && !enemyShip.Destroyed && !mine.Destroyed; i++) { Do(s, new MoveShip(50, enemyShip.X + 60, enemyShip.Y - 60)); s.Run(60 * 30); }   // 敵の艦は 1 時間ごとに拠点へ向かうので追いかける。船体 294 万に 6 基 × 135 / 秒 → 約 1 時間
        Assert.True(enemyShip.Destroyed, $"enemy {enemyShip.Hp} mine {mine.Hp}");   // 砲塔 6 基 vs 2 基
        Assert.Contains(f.Events, e => e.Kind == "ship_sunk" && e.Building == enemyShip.Id);
    }
}
