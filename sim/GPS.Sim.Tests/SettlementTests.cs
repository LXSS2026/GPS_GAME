using GPS.Sim;
using Xunit;

/// <summary>複数都市(G4 段階 3)と割譲(2026-10-16)。</summary>
public class SettlementTests
{
    static readonly GameData Data = GameData.Load(GameData.FindDataDir());
    static BlockRegistry Blocks() => BlockRegistry.FromJson(File.ReadAllText(Path.Combine(GameData.FindDataDir(), "blocks.json")));
    static void Do(Simulation s, params ICommand[] cmds) { foreach (var c in cmds) s.Enqueue(c); s.Step(); }
    static void Hours(Simulation s, int n) { for (int i = 0; i < n; i++) { CitySim.RefreshRoads(s); CitySim.Hour(s, s.Factory.City!); } }
    static void Days(Simulation s, int n) { for (int i = 0; i < n; i++) { Hours(s, 24); CitySim.Day(s, s.Factory.City!); } }

    /// <summary>都市 A(井戸と畑あり)と、400 m 東の都市 B(住居だけ)。</summary>
    static Simulation Two(ulong seed)
    {
        var s = new Simulation(Blocks(), Data, seed); s.Factory.Creative = true;
        byte road = s.Blocks.IdOf("RoadX"); for (int x = -2; x < 600; x++) s.World.Set(x, 0, -1, road);
        Do(s, new FoundCity("アカネ", 40, 70, 0, 0), new FoundSettlement("ミナミ", 400, 0, 40),
            new CreateBuilding(1, "住居 A", 20, 20, 2, false), new SetBuildingPosition(1, 0, 0), new SetBuildingUse(1, "residence"),
            new CreateBuilding(3, "畑", 30, 30, 1, false), new SetBuildingPosition(3, 24, 0), new SetBuildingUse(3, "farm"),
            new CreateBuilding(4, "井戸", 10, 10, 1, false), new SetBuildingPosition(4, 60, 0), new SetBuildingUse(4, "well"), new PlaceStructure(4, 0, "reactor_g0", 0, 0, 0, Direction.East),
            new CreateBuilding(5, "住居 B", 20, 20, 2, false), new SetBuildingPosition(5, 400, 0), new SetBuildingUse(5, "residence"));
        s.Step(); return s;
    }

    [Fact]
    public void Settlements_HaveOwnSupplies_AndPopulationMovesSeparately()
    {
        var s = Two(1); var c = s.Factory.City!;
        Assert.Equal(2, c.Settlements.Count); Assert.Equal(80, c.Population);
        Hours(s, 1);
        var a = c.Settlements[0]; var b = c.Settlements[1];
        Assert.Equal(1, a.Food); Assert.Equal(1, a.Water); Assert.Equal(0, b.Food); Assert.Equal(0, b.Water);   // 村の段階では融通しない
        Assert.Equal(80, a.PopulationCap, 6); Assert.Equal(0, b.PopulationCap);   // 住居 20x20x2 = 80 人分。B は水が無いので住居の前提を満たさない
        Assert.True(c.Ratio("water") < 1, "国の充足は都市の加重平均");
        Days(s, 1);
        Assert.True(b.Population < 40 && a.Population >= 39, $"a {a.Population} b {b.Population}");
        Assert.Equal(c.Population, a.Population + b.Population, 6);
        // 国家の段階になると融通する
        c.Stage = 4; Hours(s, 1); Assert.Equal(b.Food, a.Food); Assert.True(b.Water > 0);   // (住居の上限は下水処理が無いので 0 のまま)
    }

    [Fact]
    public void FoundSettlement_RequiresStage3_AndNearestBuildingAssignment()
    {
        var s = Two(2); var c = s.Factory.City!; s.Factory.Creative = false;
        Do(s, new FoundSettlement("キタ", 0, 400)); Assert.Equal(2, c.Settlements.Count); Assert.Contains(s.Factory.Events, e => e.Kind == "build_failed");
        c.Stage = 3; Do(s, new FoundSettlement("キタ", 0, 400)); Assert.Equal(3, c.Settlements.Count); Assert.Contains(s.Factory.Events, e => e.Kind == "settlement");
        Assert.Equal("ミナミ", c.SettlementOf(s.Factory.Get(5)!)!.Name); Assert.Equal("アカネ", c.SettlementOf(s.Factory.Get(3)!)!.Name);
        var loaded = Save.Load(Save.Snapshot(s), Blocks(), Data); Assert.Equal(Save.StateHash(s), Save.StateHash(loaded)); Assert.Equal(3, loaded.Factory.City!.Settlements.Count); Assert.Equal(c.Population, loaded.Factory.City!.Population, 6);
    }

    [Fact]
    public void Peace_WhenWinning_CedesTerritory()
    {
        var s = Two(3); var f = s.Factory; var c = f.City!;
        Do(s, new CreateBuilding(10, "拠点", 12, 12, 1, false), new SetBuildingPosition(10, 80, 0), new SetCapital(10), new CreateBuilding(41, "クロガネ", 12, 12, 3, false), new SetBuildingPosition(41, 300, 200), new AddPower(2, 41));
        var p = f.Diplomacy!.Powers.First(x => x.Id == 2); double pop0 = p.Population; double scrap0 = p.Scrap;
        DiplomacySim.DeclareWar(s, p, true); p.LossesMine = 10; p.LossesEnemy = 1;
        DiplomacySim.MakePeace(s, p, p.Population, 0);   // 優勢な和平(相手から貢納)
        Assert.Equal(3, c.Settlements.Count); var st = c.Settlements[^1];
        Assert.Contains("割譲地", st.Name); Assert.Equal(Math.Round(pop0 * 0.3), st.Population); Assert.Equal(pop0 - st.Population, p.Population);
        Assert.True(f.Get(10)!.Cargo.Count > 0); Assert.True(p.Scrap < scrap0); Assert.Contains(f.Events, e => e.Kind == "ceded");
    }
}
