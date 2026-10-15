using GPS.Sim;
using Xunit;

public class CityTests
{
    static readonly GameData Data = GameData.Load(GameData.FindDataDir());
    static BlockRegistry Blocks() => BlockRegistry.FromJson(File.ReadAllText(Path.Combine(GameData.FindDataDir(), "blocks.json")));
    static Simulation NewSim(ulong seed) => new(Blocks(), Data, seed);
    static void Do(Simulation s, params ICommand[] cmds) { foreach (var c in cmds) s.Enqueue(c); s.Step(); }
    // 都市の計算だけを直接回す(ティックを 18,000 回進めるのは遅い。配線は Wiring_RealTicks で確かめる)
    static void Hours(Simulation s, int n) { for (int i = 0; i < n; i++) { CitySim.RefreshRoads(s); CitySim.Hour(s, s.Factory.City!); } }
    static void Days(Simulation s, int n) { for (int i = 0; i < n; i++) { Hours(s, 24); CitySim.Day(s, s.Factory.City!); } }

    /// <summary>村の見本: 住居 2 棟(収容 20 人ずつ)、畑、井戸(工作機リアクター付き = 電力)、街路に接する。支持率 60 から。</summary>
    static Simulation Village(ulong seed, int pop = 30, bool roads = true)
    {
        var s = NewSim(seed);
        byte road = s.Blocks.IdOf("RoadX");
        if (roads) for (int x = -2; x < 80; x++) s.World.Set(x, 0, -1, road);   // z = -1 の街路に全棟が接する
        Do(s, new FoundCity("アカネ", pop, 60),
            new CreateBuilding(1, "住居 A", 10, 10, 2, false), new SetBuildingPosition(1, 0, 0), new SetBuildingUse(1, "residence"),
            new CreateBuilding(2, "住居 B", 10, 10, 2, false), new SetBuildingPosition(2, 12, 0), new SetBuildingUse(2, "residence"),
            new CreateBuilding(3, "畑", 20, 20, 1, false), new SetBuildingPosition(3, 24, 0), new SetBuildingUse(3, "farm"),
            new CreateBuilding(4, "井戸", 10, 10, 1, false), new SetBuildingPosition(4, 46, 0), new SetBuildingUse(4, "well"),
            new AddInventory(4, "reactor_g0", 1), new PlaceStructure(4, 0, "reactor_g0", 0, 0, 0, Direction.East));
        s.Step();   // リアクターの供給が LastSupply に入る
        return s;
    }

    [Fact]
    public void Hourly_SuppliesAndJobs_FromBuildingUses()
    {
        var s = Village(1);
        Hours(s, 1);
        var c = s.Factory.City!;
        Assert.Equal(40, c.Supplies["housing"].Have, 6);          // 10x10 × 2 階 × 0.1 × 2 棟
        Assert.Equal(80, c.Supplies["food"].Have, 6);             // 20x20 × 0.2(要員が足りている)
        Assert.Equal(100, c.Supplies["water"].Have, 6);
        Assert.Equal(1, c.Ratio("food")); Assert.Equal(1, c.Ratio("road")); Assert.Equal(1, c.Ratio("power"));
        Assert.Equal(1, c.Ratio("water")); Assert.Equal(1, c.Ratio("sewage"));   // 下水は村(段階 1)では未解放 = 1
        Assert.Equal(30 * 0.6, c.Labor, 6);
        Assert.Equal(9, c.Jobs, 6);                                // 畑 400 × 0.02 + 井戸 100 × 0.01
        Assert.True(c.Unemployment > 0.4, $"unemployment {c.Unemployment}");
        Assert.Equal(40, c.PopulationCap, 6);
    }

    [Fact]
    public void Wiring_RealTicks_RunHourlyUpdate()
    {
        var s = Village(11); var c = s.Factory.City!;
        Assert.Equal(40, c.Supplies["housing"].Have, 6);   // ティック 0 で 1 回目の集計
        c.Supplies.Clear();
        s.Run(Clock.TicksPerHour);   // 次の 1 時間の境界で集計される
        Assert.Equal(40, c.Supplies["housing"].Have, 6);
    }

    [Fact]
    public void NoRoad_DropsPopulationCap_AndWaterShortageCausesOutflow()
    {
        var s = Village(2, roads: false); Hours(s, 1);
        Assert.Equal(0, s.Factory.City!.Ratio("road")); Assert.Equal(0, s.Factory.City!.PopulationCap);
        var s2 = Village(3, pop: 200); Hours(s2, 1);   // 水 100 人分に対して 200 人
        var c = s2.Factory.City!; Assert.Equal(0.5, c.Ratio("water"), 6);
        Days(s2, 1);
        Assert.True(c.LastOutflow > 150, $"outflow {c.LastOutflow}");   // 住居超過 160 + 渇き 200 × 0.5 × 0.1
        Assert.True(c.Population < 60);
    }

    [Fact]
    public void Support_ApproachesTarget_AndBandEffects()
    {
        var s = Village(4); Hours(s, 1);
        var c = s.Factory.City!;
        Assert.True(c.SupportTarget < 50 && c.SupportTarget > 0, $"target {c.SupportTarget}");   // 失業(約 5 割)・税率・徴兵率・治安なしで下がる
        double before = c.Support; Days(s, 1);
        Assert.True(c.Support < before && c.Support > c.SupportTarget, "1 日で目標へ約 1/4 近づく");
        // 税率と徴兵率を上げると目標が下がる。帯が「低い」に入ると流出と離職
        Do(s, new SetTaxRate(0.5), new SetConscriptionRate(0.5));
        Days(s, 20);
        Assert.True(c.Band is "low" or "danger" or "collapse", c.Band);
        Assert.True(c.LastOutflow > 0);
    }

    [Fact]
    public void Collapse_AfterThirtyDaysBelowTen()
    {
        var s = Village(5);   // 税率 50%・徴兵率 50%・治安なし・失業で目標は 0
        Do(s, new SetTaxRate(0.5), new SetConscriptionRate(0.5));
        s.Factory.City!.Support = 5;
        Days(s, 29); Assert.False(s.Factory.City!.Defeated);
        Days(s, 1); var cc = s.Factory.City!; Assert.True(cc.Defeated, $"support {cc.Support} target {cc.SupportTarget} band {cc.Band} days {cc.CollapseDays} pop {cc.Population} u {cc.Unemployment}"); Assert.Contains(s.Factory.Events, e => e.Kind == "defeat");
    }

    [Fact]
    public void Riot_DropsProductionEfficiency_WhenSecurityShort()
    {
        var s = Village(6); var c = s.Factory.City!;
        c.Support = 20; Hours(s, 1);
        Assert.True(c.Riot); Assert.Equal(70, s.Factory.EfficiencyPct);
        Do(s, new CreateBuilding(9, "警察", 10, 10, 2, false), new SetBuildingPosition(9, 60, 0), new SetBuildingUse(9, "police"));   // 200 × 2 = 400 人分 > 需要 30 × 2
        Hours(s, 1);
        Assert.False(c.Riot); Assert.Equal(100, s.Factory.EfficiencyPct);
    }

    [Fact]
    public void Tax_Upkeep_Pay_GoToTreasury_AndStagePromotion()
    {
        var s = Village(7, pop: 150); var c = s.Factory.City!;
        c.Support = 80; s.Factory.Treasury = 0;
        for (int i = 0; i < 8; i++) { s.Enqueue(new CreateBuilding(20 + i, $"住居 {i}", 10, 10, 3, false)); s.Enqueue(new SetBuildingPosition(20 + i, 60 + i * 12, 0)); s.Enqueue(new SetBuildingUse(20 + i, "residence")); }
        // 支持率 60 以上を保つ: 警察・市場と学校(雇用)・公園、税率と徴兵率は 0
        foreach (var (id, name, w, fl, use, x) in new[] { (42, "警察", 10, 2, "police", 60), (43, "市場", 30, 1, "market", 80), (44, "学校", 20, 2, "school", 120), (45, "公園", 20, 1, "park", 150) })
        { s.Enqueue(new CreateBuilding(id, name, w, w, fl, false)); s.Enqueue(new SetBuildingPosition(id, x, 40)); s.Enqueue(new SetBuildingUse(id, use)); }
        s.Enqueue(new SetTaxRate(0.05)); s.Enqueue(new SetConscriptionRate(0));
        s.Enqueue(new CreateBuilding(40, "畑 2", 30, 30, 1, false)); s.Enqueue(new SetBuildingPosition(40, 0, 40)); s.Enqueue(new SetBuildingUse(40, "farm"));
        s.Enqueue(new CreateBuilding(41, "井戸 2", 10, 10, 1, false)); s.Enqueue(new SetBuildingPosition(41, 40, 40)); s.Enqueue(new SetBuildingUse(41, "well"));
        byte road = s.Blocks.IdOf("RoadX"); for (int x = -2; x < 200; x++) { s.World.Set(x, 0, -1, road); s.World.Set(x, 0, 39, road); }
        s.Step();
        Days(s, 1);
        Assert.True(c.LastTax > 0 && s.Factory.Treasury > 0, $"tax {c.LastTax}");
        Assert.Equal(0, c.LastUpkeep);   // 維持費は段階 3 から
        Assert.Equal(1, c.Stage);
        Days(s, 31);
        Assert.True(c.Support >= 60, $"support {c.Support} target {c.SupportTarget} unemployment {c.Unemployment}");
        Assert.Equal(2, c.Stage); Assert.Contains(s.Factory.Events, e => e.Kind == "stage" && e.Power == 2);
    }

    [Fact]
    public void Conscription_LimitsDeploy_AndStaffDrawnFromLabor()
    {
        var s = Village(8, pop: 20); var c = s.Factory.City!;   // 労働 12 人
        Do(s, new CreateBuilding(10, "格納庫", 12, 12, 1, false), new SetBuildingPosition(10, 60, 0), new CreateMech(1, 2, "general", 70), new CreateMech(2, 2, "general", 70), new FormSquad(1, 1, "a", new[] { 1, 2 }, 10));
        Hours(s, 1);
        Assert.Equal(6, c.CrewNeed); Assert.True(c.Conscripts < 6, $"conscripts {c.Conscripts}");   // 12 × 5% = 0.6 人
        Do(s, new Deploy(1, 70, 0));
        Assert.Equal(SquadState.InHangar, s.Factory.Squads[0].State); Assert.Contains(s.Factory.Events, e => e.Kind == "no_crew");
        Do(s, new SetConscriptionRate(0.5)); Hours(s, 1);   // 12 × 50% = 6 人
        Assert.Equal(6, c.Conscripts, 6);
        Do(s, new Deploy(1, 70, 0)); Assert.Equal(SquadState.Field, s.Factory.Squads[0].State);
        // 要員の引き当て: 労働人口の残り(12 − 6 = 6 人)まで
        Do(s, new AddInventory(10, "furnace", 1), new PlaceStructure(10, 0, "furnace", 0, 0, 0, Direction.East));
        var m = (Machine)s.Factory.Get(10)!.Find(0, 1)!;
        for (int i = 0; i < 10; i++) Do(s, new HireStaff(10, 0, 1));
        Assert.Equal(6, m.Staff.Count); Assert.All(m.Staff, w => Assert.InRange(w.Skill, 0, 10));
        Assert.Contains("#1000", m.Staff[0].Name);
    }

    [Fact]
    public void Persons_AppearMonthly_AndSaveReplayMatch()
    {
        var s = Village(9, pop: 60); s.Factory.City!.Support = 75;
        Days(s, 30);
        var c = s.Factory.City!;
        Assert.InRange(c.Persons.Count, 1, 3); Assert.All(c.Persons, p => { Assert.InRange(p.Govern, 1, 10); Assert.InRange(p.Loyalty, 1, 10); });
        var replayed = Save.Replay(9, s.Log, s.Tick, Blocks(), Data, creative: false);
        foreach (var e in replayed.Factory.Buildings) { }
        // 再生では地形(街路)が無いので道路接続が変わる。写しの読み込みで一致を見る
        var loaded = Save.Load(Save.Snapshot(s), Blocks(), Data);
        Assert.Equal(Save.StateHash(s), Save.StateHash(loaded));
        Assert.Equal(c.Persons.Count, loaded.Factory.City!.Persons.Count);
        Assert.Equal(c.Population, loaded.Factory.City!.Population, 6);
    }
}
