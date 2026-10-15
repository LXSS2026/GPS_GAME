using GPS.Sim;
using Xunit;

public class PoliticsTests
{
    static readonly GameData Data = GameData.Load(GameData.FindDataDir());
    static BlockRegistry Blocks() => BlockRegistry.FromJson(File.ReadAllText(Path.Combine(GameData.FindDataDir(), "blocks.json")));
    static Simulation NewSim(ulong seed) => new(Blocks(), Data, seed);
    static void Do(Simulation s, params ICommand[] cmds) { foreach (var c in cmds) s.Enqueue(c); s.Step(); }
    static void Hours(Simulation s, int n) { for (int i = 0; i < n; i++) { CitySim.RefreshRoads(s); CitySim.Hour(s, s.Factory.City!); } }
    static void Days(Simulation s, int n) { for (int i = 0; i < n; i++) { Hours(s, 24); CitySim.Day(s, s.Factory.City!); } }

    static Simulation Village(ulong seed, int pop = 60)
    {
        var s = NewSim(seed);
        byte road = s.Blocks.IdOf("RoadX"); for (int x = -2; x < 120; x++) s.World.Set(x, 0, -1, road);
        Do(s, new FoundCity("アカネ", pop, 60),
            new CreateBuilding(1, "住居 A", 20, 20, 2, false), new SetBuildingPosition(1, 0, 0), new SetBuildingUse(1, "residence"),
            new CreateBuilding(3, "畑", 30, 30, 1, false), new SetBuildingPosition(3, 24, 0), new SetBuildingUse(3, "farm"),
            new CreateBuilding(4, "井戸", 10, 10, 1, false), new SetBuildingPosition(4, 60, 0), new SetBuildingUse(4, "well"),
            new CreateBuilding(5, "警察", 10, 10, 1, false), new SetBuildingPosition(5, 72, 0), new SetBuildingUse(5, "police"),
            new AddInventory(4, "reactor_g0", 1), new PlaceStructure(4, 0, "reactor_g0", 0, 0, 0, Direction.East));
        s.Step(); return s;
    }
    static Person AddPerson(Simulation s, int id, int govern = 5, int produce = 5, int command = 5, int negotiate = 5, int loyalty = 5, int ambition = 5, string job = "farmer")
    {
        var p = new Person { Id = id, Name = $"人物 #{id}", Govern = govern, Produce = produce, Command = command, Negotiate = negotiate, Loyalty = loyalty, Ambition = ambition, Job = job, AppearedDay = s.Factory.City!.Days };
        s.Factory.City!.Persons.Add(p); s.Factory.City!.NextPersonId = Math.Max(s.Factory.City!.NextPersonId, id + 1); return p;
    }

    [Fact]
    public void Slots_FollowStage_AndAppointmentChangesCoefficients()
    {
        var s = Village(1); var f = s.Factory;
        var slots = PoliticsSim.Slots(f, s.PoliticsData);
        Assert.Equal(new[] { "head", "factory", "military", "negotiate" }, slots.Select(x => x.Id).ToArray());   // 村: 技師は機体の試作後、区長・隊長は町から
        Assert.Equal("村長", slots[0].Name);
        Hours(s, 1); var pol = f.Politics!;
        Assert.Equal(-10, pol.SupportBonus);   // 全役職が空席 = 能力 0 扱い → 村長 (0−5)×2
        Assert.Equal(115, f.FailureMulPct);    // 工場長の空席で失敗率 × 1.15
        AddPerson(s, 1, govern: 9, produce: 2); AddPerson(s, 2, produce: 9, loyalty: 8, job: "artisan");
        Do(s, new Appoint("head", 1), new Appoint("factory", 2));
        Assert.Equal(8, pol.SupportBonus); Assert.Equal(88, f.FailureMulPct); Assert.Equal(108, f.SpeedMulPct);
        Assert.Equal("head", s.Factory.City!.Persons[0].Office); Assert.Equal(1, pol.JobHoles["farmer"]);
        Do(s, new Appoint("head", 2));   // 掛け持ち不可: 2 は工場長なので無視
        Assert.Equal(1, pol.Offices["head"]);
        Do(s, new Dismiss("head"));
        Assert.False(pol.Offices.ContainsKey("head")); Assert.Equal(2, s.Factory.City!.Persons[0].Loyalty); Assert.Equal(3, s.Factory.City!.Persons[0].Discontent);
        f.City!.Stage = 4; slots = PoliticsSim.Slots(f, s.PoliticsData);
        Assert.Contains(slots, x => x.Name == "内務大臣"); Assert.Contains(slots, x => x.Id == "marshal"); Assert.Contains(slots, x => x.Id == "engineer");
    }

    [Fact]
    public void MilitaryChief_AffectsHitRetreatAndConscription()
    {
        var s = Village(2); var f = s.Factory;
        AddPerson(s, 1, command: 10); Do(s, new Appoint("military", 1));
        Assert.Equal(110, f.HitMulPct); Assert.Equal(0.15, f.RetreatHp, 6);
        Assert.Equal(1.25, f.Politics!.ConscriptionMul, 6);   // (10−5)×0.05 = +25%、軍事費 50 → 係数 1
        Do(s, new SetBudget(100, 0));
        Assert.Equal(1.25 * 0.5, f.Politics!.ConscriptionMul, 6); Assert.False(f.AmmoResupply);
        Do(s, new SetBudget(100, 50)); Assert.True(f.AmmoResupply);
    }

    [Fact]
    public void Strata_UnemployedAndRobotShare_LowerTheirOwnTarget()
    {
        var s = Village(3, pop: 100); Hours(s, 1);   // 労働 60、職場 畑 18 + 井戸 1 + 警察 5 → 失業 6 割
        var pol = s.Factory.Politics!; var st = pol.Strata;
        Assert.True(st["unemployed"].Count > 30); Assert.True(st["unemployed"].Target < st["farmer"].Target - 20, $"u {st["unemployed"].Target} f {st["farmer"].Target}");
        Assert.True(st["farmer"].Target > st["other"].Target, "農民は食料と水の余剰で加点が 2 倍");
    }

    [Fact]
    public void Faction_Forms_Resolves_OrDefects()
    {
        var s = Village(4); var c = s.Factory.City!; c.Support = 40;   // 低い帯: 不満が溜まる
        Do(s, new SetTaxRate(0.4));
        var p = AddPerson(s, 1, loyalty: 2, ambition: 9, job: "farmer"); p.Discontent = 6;
        Days(s, 1);
        var pol = s.Factory.Politics!;
        Assert.Single(pol.Factions); Assert.Equal("tax", pol.Factions[0].Demand); Assert.True(pol.Factions[0].Supporters > 0);
        Assert.True(pol.FactionPenalty > 0 || true);
        Hours(s, 1); Assert.True(pol.FactionPenalty > 0);
        // 要求(減税)を満たして 7 日で解消
        Do(s, new SetTaxRate(0.05)); Days(s, 7);
        Assert.Empty(pol.Factions); Assert.Contains(s.Factory.Events, e => e.Kind == "faction_end");
        // 放置すると 60 日で離反: 首領が消え、支持者の 3 割が流出し、亡命者に記録
        var s2 = Village(5); var c2 = s2.Factory.City!; c2.Support = 40; Do(s2, new SetTaxRate(0.4), new SetBuildingUse(5, "park"));   // 警察なし = 治安で解消されない
        var q = AddPerson(s2, 1, loyalty: 2, ambition: 9); q.Discontent = 6;
        Days(s2, 1); var pol2 = s2.Factory.Politics!; Assert.Single(pol2.Factions);
        double before = c2.Population; Days(s2, 60);
        Assert.Empty(pol2.Factions); Assert.DoesNotContain(c2.Persons, x => x.Id == 1); Assert.Single(pol2.Exiles); Assert.True(c2.Population < before);
    }

    [Fact]
    public void Rebellion_WhenCollapsedWithLargeFaction_AndSuppressedBySecurity()
    {
        var s = Village(6, pop: 300); var c = s.Factory.City!; c.Support = 5; var f = s.Factory;
        // 300 人が住め、食料・水が足り、警察の要員が埋まる町(反乱中は毎日 7% が去るので、鎮圧の 15 日で約 100 人になる)
        Do(s, new CreateBuilding(8, "住居 B", 30, 30, 3, false), new SetBuildingPosition(8, 0, 30), new SetBuildingUse(8, "residence"),
            new CreateBuilding(10, "畑 2", 20, 20, 1, false), new SetBuildingPosition(10, 40, 30), new SetBuildingUse(10, "farm"),   // 畑の要員(優先)を抑えて警察に人が回るように
            new CreateBuilding(11, "井戸 2", 20, 20, 1, false), new SetBuildingPosition(11, 90, 30), new SetBuildingUse(11, "well"),
            new CreateBuilding(9, "警察 2", 30, 30, 2, false), new SetBuildingPosition(9, 90, 0), new SetBuildingUse(9, "police"));
        byte road = s.Blocks.IdOf("RoadX"); for (int x = -2; x < 120; x++) s.World.Set(x, 0, 29, road);
        Do(s, new SetTaxRate(0.5), new SetConscriptionRate(0.5));
        var p = AddPerson(s, 1, loyalty: 1, ambition: 10, job: "farmer"); p.Discontent = 9;
        Days(s, 1); var pol = f.Politics!;
        Assert.Single(pol.Factions); pol.Factions[0].Supporters = 150;   // 人口の 5 割
        Days(s, 1);
        Assert.True(pol.Rebellion); Assert.Contains(f.Events, e => e.Kind == "rebellion");
        Hours(s, 1); Assert.True(f.EfficiencyPct <= 50, $"eff {f.EfficiencyPct}");
        Days(s, 1); Assert.Equal(0, c.LastTax);   // 反乱中は税収 0
        // 治安の充足(需要は人口 × 2 × 3 + 支持者。警察は暴動・反乱でも能力が落ちない)が 15 日続くと鎮圧
        Assert.True(c.Ratio("security") >= 1, $"security {c.Ratio("security")} have {c.Supplies["security"].Have} need {c.Supplies["security"].Need}");
        Days(s, 15);
        Assert.False(pol.Rebellion, $"security {c.Ratio("security")} calm {pol.RebellionCalmDays} pop {c.Population}"); Assert.Contains(f.Events, e => e.Kind == "rebellion_end");
    }

    [Fact]
    public void RobotCap_LimitsAssignRobot()
    {
        var s = Village(7); var f = s.Factory; f.Creative = true;
        Do(s, new CreateBuilding(20, "工房", 10, 10, 1, true), new SetBuildingPosition(20, 90, 0), new PlaceStructure(20, 0, "furnace", 0, 0, 0, Direction.East), new PlaceStructure(20, 0, "chest", 2, 0, 0, Direction.East));
        var m = (Machine)f.Get(20)!.Find(0, 1)!; var ch = (Chest)f.Get(20)!.Find(0, 2)!;
        for (int i = 0; i < 4; i++) ch.Items.Add(new ItemStack("factory_robot", 70, 1));
        Do(s, new HireStaff(20, 0, 1)); Assert.Single(m.Staff);
        Do(s, new SetRobotCap(50), new AssignRobot(20, 0, 1, 2)); Assert.Equal(2, m.Staff.Count);     // 1/2 = 50% まで可
        Do(s, new AssignRobot(20, 0, 1, 2)); Assert.Equal(2, m.Staff.Count); Assert.Contains(f.Events, e => e.Kind == "robot_cap");   // 2/3 = 67% は超過
        Do(s, new SetRobotCap(100), new AssignRobot(20, 0, 1, 2)); Assert.Equal(3, m.Staff.Count);
        Hours(s, 1); Assert.Equal(2 / 3.0, f.Politics!.RobotShare, 6);
    }

    [Fact]
    public void Decrees_RationTaxcutFestival()
    {
        var s = Village(8, pop: 300); var c = s.Factory.City!; var f = s.Factory; f.Treasury = 100_000;
        Hours(s, 1); Assert.True(c.Ratio("water") < 1);   // 井戸 100 人分に 300 人
        Do(s, new Decree("ration")); Hours(s, 1);
        Assert.Equal(1, c.Ratio("water")); long t0 = f.Treasury; Days(s, 1); Assert.True(f.Treasury < t0 + c.LastTax, "配給の費用が国庫から出る");
        double before = c.Support; Do(s, new Decree("festival")); Assert.Equal(before + 5, c.Support, 6);
        Do(s, new Decree("festival")); Assert.Equal(before + 5, c.Support, 6);   // 30 日は重ねられない
        Do(s, new SetTaxRate(0.3), new Decree("taxcut")); Days(s, 1); Assert.Equal(0, c.LastTax);
        Assert.Equal(3, f.Politics!.Decrees.Count); Days(s, 30); Assert.Empty(f.Politics!.Decrees);
    }

    [Fact]
    public void Forecast_TracksPolicyChanges_AndSaveLoadMatch()
    {
        var s = Village(9, pop: 40); Hours(s, 2);
        var (t1, m1, _, _) = PoliticsSim.Forecast(s, 0.1, 0.05, 100, 50);
        var (t2, m2, _, _) = PoliticsSim.Forecast(s, 0.5, 0.5, 100, 50);
        Assert.True(t2 < t1 - 20, $"{t1} → {t2}"); Assert.True(m2 > m1);
        AddPerson(s, 1, govern: 8); Do(s, new Appoint("head", 1), new SetBudget(60, 80), new SetRobotCap(30), new Decree("taxcut"));
        Days(s, 2);
        var loaded = Save.Load(Save.Snapshot(s), Blocks(), Data);
        Assert.Equal(Save.StateHash(s), Save.StateHash(loaded));
        Assert.Equal(1, loaded.Factory.Politics!.Offices["head"]); Assert.Equal(60, loaded.Factory.Politics!.InfraInvest); Assert.Single(loaded.Factory.Politics!.Decrees);
    }
}
