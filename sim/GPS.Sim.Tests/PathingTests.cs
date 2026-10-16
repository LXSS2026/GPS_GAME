using GPS.Sim;
using Xunit;

/// <summary>経路探索(G21 第 7 節): 障害物格子、直線の判定、A*、隊が建物を避けて目標に着くこと、写しで同じ進行。</summary>
public class PathingTests
{
    static readonly GameData Data = GameData.Load(GameData.FindDataDir());
    static BlockRegistry Blocks() => BlockRegistry.FromJson(File.ReadAllText(Path.Combine(GameData.FindDataDir(), "blocks.json")));
    static void Do(Simulation s, params ICommand[] cmds) { foreach (var c in cmds) s.Enqueue(c); s.Step(); }

    /// <summary>町。格納庫(1)の東 60 m に 40x40 m の建物(9)、その先 (200, 20) が目標。味方 1 隊(12 機)。</summary>
    static Simulation Scene(ulong seed, bool aggregated)
    {
        var s = new Simulation(Blocks(), Data, seed); s.Factory.Creative = true;
        Do(s, new FoundCity("a", 200_000, 60)); s.Factory.City!.Stage = 2; s.Factory.City.ConscriptionRate = 0.5;
        Do(s, new CreateBuilding(1, "a", 12, 12, 1, false), new SetBuildingPosition(1, 0, 0), new CreateBuilding(9, "壁", 40, 40, 2, false), new SetBuildingPosition(9, 80, 0));
        var ids = new List<int>(); for (int m = 0; m < 12; m++) { s.Enqueue(new CreateMech(m + 1, 3, "general", 75)); ids.Add(m + 1); }
        s.Enqueue(new FormSquad(1, 1, "s1", ids, 1)); s.Step();
        var city = s.Factory.City!; city.Persons.Add(new Person { Id = 901, Name = "隊長", Command = 5, Loyalty = 5, Ambition = 5 }); city.NextPersonId = 2000;
        Do(s, new AssignCommander("squad", 1, 901)); CitySim.Hour(s, city);
        if (aggregated) Do(s, new SetBattleFocus(5000, 5000, 10));
        return s;
    }

    [Fact]
    public void Grid_LineClear_And_FindAroundBuilding()
    {
        var s = Scene(1, false); var f = s.Factory; s.Step();   // Tick で格子ができる
        Assert.NotNull(f.PathGrid);
        Assert.True(Pathing.Blocked(f, 100, 20)); Assert.False(Pathing.Blocked(f, 100, 60)); Assert.False(Pathing.Blocked(f, 60, 20));
        Assert.False(Pathing.LineClear(f, 40, 20, 200, 20)); Assert.True(Pathing.LineClear(f, 40, 60, 200, 60));
        var path = Pathing.Find(f, 40, 20, 200, 20); Assert.NotNull(path);
        Assert.Equal(200, path![^1].X, 6); Assert.Equal(20, path[^1].Y, 6);
        Assert.All(path, p => Assert.False(Pathing.Blocked(f, p.X, p.Y)));
        double px = 40, py = 20; foreach (var p in path) { Assert.True(Pathing.LineClear(f, px, py, p.X, p.Y), "中継点どうしは直線が通る"); px = p.X; py = p.Y; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Squad_GoesAroundBuilding_AndArrives(bool aggregated)
    {
        var s = Scene(2, aggregated); var f = s.Factory; var sq = f.Squads[0];
        Do(s, new Deploy(1, 40, 20), new OrderSquad(1, SquadOrder.Move, 200, 20));
        int inside = 0;
        for (int t = 0; t < 60 * 60; t++)
        {
            s.Step();
            foreach (var u in sq.Members) if (u.Alive && u.X >= 82 && u.X < 118 && u.Y >= 2 && u.Y < 38) inside++;   // 床の内側(縁 2 m は陣形のはみ出しとして除く)
            if (Math.Abs(sq.X - 200) < 6 && Math.Abs(sq.Y - 20) < 6) break;
        }
        if (aggregated) Assert.True(sq.Aggregated);
        Assert.True(Math.Abs(sq.X - 200) < 6 && Math.Abs(sq.Y - 20) < 6, $"目標に着く: ({sq.X:F0}, {sq.Y:F0})");
        Assert.Equal(0, inside);
    }

    [Fact]
    public void Path_IsSaved_AndContinuesIdentically()
    {
        var s = Scene(3, true); Do(s, new Deploy(1, 40, 20), new OrderSquad(1, SquadOrder.Move, 200, 20)); s.Run(300);
        Assert.NotNull(s.Factory.Squads[0].Route.Path);
        var loaded = Save.Load(Save.Snapshot(s), Blocks(), Data); loaded.Factory.Creative = true;
        s.Run(900); loaded.Run(900);
        Assert.Equal(Save.StateHash(s), Save.StateHash(loaded));
    }
}
