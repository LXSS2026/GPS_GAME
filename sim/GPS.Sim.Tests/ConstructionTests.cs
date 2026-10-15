using GPS.Sim;
using Xunit;

public class ConstructionTests
{
    static readonly GameData Data = GameData.Load(GameData.FindDataDir());
    static BlockRegistry Blocks() => BlockRegistry.FromJson(File.ReadAllText(Path.Combine(GameData.FindDataDir(), "blocks.json")));
    static Simulation NewSim(ulong seed) => new(Blocks(), Data, seed);
    static void Do(Simulation s, params ICommand[] cmds) { foreach (var c in cmds) s.Enqueue(c); s.Step(); }

    static Simulation Town(ulong seed)
    {
        var s = NewSim(seed);
        byte plaza = s.Blocks.IdOf("Plaza"), road = s.Blocks.IdOf("RoadX");
        for (int x = 0; x < 120; x++) for (int z = 0; z < 120; z++) s.World.Set(x, 2, z, z >= 50 && z < 54 ? road : plaza);   // z = 50〜53 に街路
        Do(s, new FoundCity("アカネ", 50, 60)); s.Factory.Treasury = 10_000;
        return s;
    }

    [Fact]
    public void Build_CostsTreasury_RespectsStageLimits_AndOverlap()
    {
        var s = Town(1); var f = s.Factory;
        Do(s, new BuildBuilding(0, "住居 A", "residence", 10, 10, 20, 2));
        var b = f.Buildings.Single(); Assert.Equal("residence", b.Use); Assert.Equal(20, b.Floors[0].W); Assert.Equal(2, b.Floors.Count);
        Assert.Equal(10_000 - 20 * 20 * 2 * 2, f.Treasury); Assert.Contains(f.Events, e => e.Kind == "built");
        Do(s, new BuildBuilding(0, "大きすぎ", "residence", 60, 10, 30, 1)); Assert.Single(f.Buildings);   // 村は 20 m 角まで
        Do(s, new BuildBuilding(0, "高すぎ", "residence", 60, 10, 20, 3)); Assert.Single(f.Buildings);   // 村は 2 階まで
        Do(s, new BuildBuilding(0, "重なる", "farm", 20, 20, 10, 1)); Assert.Single(f.Buildings);
        Do(s, new BuildBuilding(0, "街路の上", "farm", 10, 48, 10, 1)); Assert.Single(f.Buildings);
        Assert.Equal(4, f.Events.Count(e => e.Kind == "build_failed"));
        f.City!.Stage = 3;
        Do(s, new BuildBuilding(0, "工場棟", "factory", 60, 10, 30, 3)); Assert.Equal(2, f.Buildings.Count); Assert.NotNull(f.Buildings[1].Floors[0].Ore);
        f.Treasury = 10; Do(s, new BuildBuilding(0, "金欠", "farm", 60, 60, 10, 1)); Assert.Equal(2, f.Buildings.Count); Assert.Contains(f.Events, e => e.Kind == "build_failed" && e.Power == 50);
    }

    [Fact]
    public void Road_ConnectsBuilding_AndDemolishRefunds()
    {
        var s = Town(2); var f = s.Factory;
        Do(s, new BuildBuilding(5, "住居 B", "residence", 10, 100, 10, 1));   // 街路(z 50〜53)から 46 m 離れている
        var b = f.Get(5)!; Assert.False(b.RoadConnected);
        long t0 = f.Treasury;
        Do(s, new BuildRoad(4, 54, 4, 98));   // 縦の街路(幅 4)を住居の隣まで
        Assert.Equal(t0 - 4 * 45, f.Treasury); Assert.True(b.RoadConnected); Assert.Contains(f.Events, e => e.Kind == "road_built" && e.Destroyed == 180);
        Assert.Equal(s.Blocks.IdOf("Asphalt"), s.World.Get(5, 2, 80));
        Do(s, new BuildRoad(4, 54, 4, 98, Remove: true)); Assert.Equal(s.Blocks.IdOf("Plaza"), s.World.Get(5, 2, 80));
        t0 = f.Treasury; Do(s, new Demolish(5)); Assert.Null(f.Get(5)); Assert.Equal(t0 + 100, f.Treasury);   // 10x10 × 2 × 0.5
        Do(s, new BuildBuilding(6, "格納庫", "factory", 10, 100, 10, 1), new SetCapital(6), new Demolish(6)); Assert.NotNull(f.Get(6));   // 拠点は撤去できない
        var loaded = Save.Load(Save.Snapshot(s), Blocks(), Data); Assert.Equal(Save.StateHash(s), Save.StateHash(loaded));
    }
}
