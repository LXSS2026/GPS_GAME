using GPS.Sim;
using Xunit;

public class FactoryTests
{
    static readonly GameData Data = GameData.Load(GameData.FindDataDir());
    static BlockRegistry Blocks() => BlockRegistry.FromJson(File.ReadAllText(Path.Combine(GameData.FindDataDir(), "blocks.json")));
    static Simulation NewSim(ulong seed = 7) { var s = new Simulation(Blocks(), Data, seed); s.Factory.Creative = true; return s; }

    static void Do(Simulation s, params ICommand[] cmds) { foreach (var c in cmds) s.Enqueue(c); s.Step(); }

    [Fact]
    public void Quality_Distribution_MatchesConfirmedRates()
    {
        var rng = new Rng(3);
        int n = 400_000; var count = new int[4];
        for (int i = 0; i < n; i++) count[(int)Quality.TierOf(Quality.Roll(rng))]++;
        Assert.InRange(count[(int)QualityTier.Top] * 10_000.0 / n, 6, 15);        // 0.1% (±)
        Assert.InRange(count[(int)QualityTier.High] * 10_000.0 / n, 85, 115);     // 1%
        Assert.InRange(count[(int)QualityTier.Normal] * 100.0 / n, 89, 91);       // 90%
        Assert.InRange(count[(int)QualityTier.Defect] * 100.0 / n, 8.4, 9.4);     // 8.9%
        // 階級の境界
        Assert.Equal(QualityTier.Defect, Quality.TierOf(39)); Assert.Equal(QualityTier.Normal, Quality.TierOf(40));
        Assert.Equal(QualityTier.High, Quality.TierOf(90)); Assert.Equal(QualityTier.Top, Quality.TierOf(99));
    }

    [Fact]
    public void Quality_NormalBias_ReducesBothTails()
    {
        var rng = new Rng(5); int n = 200_000; int tails = 0, tails2 = 0;
        for (int i = 0; i < n; i++) { var t = Quality.TierOf(Quality.Roll(rng)); if (t != QualityTier.Normal) tails++; }
        for (int i = 0; i < n; i++) { var t = Quality.TierOf(Quality.Roll(rng, normalBias: 80)); if (t != QualityTier.Normal) tails2++; }
        Assert.True(tails2 < tails / 3);
    }

    [Fact]
    public void Belt_MovesOneSlotPerStep_AndHandsOverToNextBelt()
    {
        var s = NewSim();
        Do(s, new CreateBuilding(1, "t", 5, 5, 1, false),
              new PlaceStructure(1, 0, "chest", 0, 0, 0, Direction.East), new PlaceStructure(1, 0, "inserter", 1, 0, 0, Direction.East),
              new PlaceStructure(1, 0, "belt", 2, 0, 0, Direction.East), new PlaceStructure(1, 0, "belt", 3, 0, 0, Direction.East),
              new PlaceStructure(1, 0, "reactor_g0", 0, 2, 0, Direction.East), new AddItems(1, 0, 1, "iron_ore", 50, 1));
        s.Run(30 + 1);                                           // インサータが掴んで置く
        var b1 = (Belt)s.Factory.Get(1)!.Find(0, 3)!; var b2 = (Belt)s.Factory.Get(1)!.Find(0, 4)!;
        s.Run(10 * 8);                                           // 10 ティックごとに 1 コマ、4 コマ + 次のベルトへ
        Assert.True(b2.Slots.Any(x => x != null) && b1.Slots.All(x => x == null));
        s.Run(10 * 5);
        Assert.NotNull(b2.Slots[3]);                             // 先が無いので出口で止まる
    }

    [Fact]
    public void Miner_DepletesOre_AndInserterFeedsFurnace_WhichNeedsStaff()
    {
        var s = NewSim();
        Do(s, new CreateBuilding(1, "g", 4, 4, 1, true), new SetOre(1, 0, 0, "iron_ore", 3, 55),
              new PlaceStructure(1, 0, "miner", 0, 0, 0, Direction.East), new PlaceStructure(1, 0, "inserter", 1, 0, 0, Direction.East),
              new PlaceStructure(1, 0, "furnace", 2, 0, 0, Direction.East), new PlaceStructure(1, 0, "reactor_g1", 0, 2, 0, Direction.East),
              new SetRecipe(1, 0, 3, "iron_plate"));
        var fl = s.Factory.Get(1)!.Floors[0]; var m = (Machine)fl.Structures.First(x => x is Machine);
        s.Run(300 * 3 + 200);
        Assert.Null(fl.OreAt(0, 0));                              // 3 個掘って枯渇
        Assert.True(m.Inputs.Count > 0 && m.Made == 0);          // 要員なしでは動かない
        Do(s, new AssignStaff(1, 0, 3, new Worker(1, "a", 5)));
        s.Run(180 * 4);
        Assert.True(m.Made + m.Failed >= 2);
        Assert.All(m.Outputs, o => Assert.True(o.Item == "iron_plate" || o.Item == "iron_ore"));
    }

    [Fact]
    public void Furnace_Failure_ReturnsScrap_70Percent()
    {
        var s = NewSim(11);
        Do(s, new CreateBuilding(1, "g", 3, 3, 1, false), new PlaceStructure(1, 0, "furnace", 0, 0, 0, Direction.East),
              new PlaceStructure(1, 0, "reactor_g2", 1, 0, 0, Direction.East), new SetRecipe(1, 0, 1, "steel"),
              new AssignStaff(1, 0, 1, new Worker(1, "a", 0)));
        var m = (Machine)s.Factory.Get(1)!.Find(0, 1)!;
        int failed = 0, scrapPlates = 0, steel = 0;
        for (int round = 0; round < 400; round++)
        {
            m.Inputs.Add(new ItemStack("iron_plate", 60, 10)); m.Inputs.Add(new ItemStack("coal", 60, 5));
            s.Run(361);
            foreach (var o in m.Outputs) { if (o.Item == "iron_plate") scrapPlates += o.Count; else if (o.Item == "steel") steel += o.Count; }
            m.Outputs.Clear(); m.Inputs.Clear();
            failed = (int)m.Failed;
        }
        Assert.True(failed > 10 && steel > 300);
        Assert.Equal(failed * 1, scrapPlates);                   // 鉄板 2 枚 × 70% = 1.4 → 1 枚回収、石炭は回収不可
    }

    [Fact]
    public void Staff_Skill_LowersFailureRate()
    {
        int Fail(int skill)
        {
            var s = NewSim(21);
            Do(s, new CreateBuilding(1, "g", 3, 3, 1, false), new PlaceStructure(1, 0, "assembler", 0, 0, 0, Direction.East),
                  new PlaceStructure(1, 0, "reactor_g2", 1, 0, 0, Direction.East), new SetRecipe(1, 0, 1, "gear"), new AssignStaff(1, 0, 1, new Worker(1, "a", skill)));
            var m = (Machine)s.Factory.Get(1)!.Find(0, 1)!;
            for (int i = 0; i < 2000; i++) { m.Inputs.Add(new ItemStack("iron_plate", 60, 2)); s.Run(121); m.Outputs.Clear(); }
            return (int)m.Failed;
        }
        int f0 = Fail(0), f10 = Fail(10);
        Assert.True(f10 < f0 * 0.7, $"skill0={f0} skill10={f10}");
    }

    [Fact]
    public void Reactor_OutputByGenerationAndQuality_AndPolicies()
    {
        Assert.Equal(200, Building.ReactorOutput(Data.Structures["reactor_g1"], 100, Data));
        Assert.Equal(100, Building.ReactorOutput(Data.Structures["reactor_g1"], 0, Data));
        Assert.Equal(6400, Building.ReactorOutput(Data.Structures["reactor_g6"], 100, Data));
        var s = NewSim();
        Do(s, new CreateBuilding(1, "g", 6, 3, 1, false),
              new PlaceStructure(1, 0, "furnace", 0, 0, 0, Direction.East), new PlaceStructure(1, 0, "furnace", 1, 0, 0, Direction.East), new PlaceStructure(1, 0, "furnace", 2, 0, 0, Direction.East),
              new PlaceStructure(1, 0, "reactor_g0", 3, 0, 0, Direction.East));   // 供給 85 (品質 70)、需要 150
        var b = s.Factory.Get(1)!; var r = (Reactor)b.Find(0, 4)!; r.Quality = 100;   // 供給 100
        for (int i = 1; i <= 3; i++) { Do(s, new SetRecipe(1, 0, i, "iron_plate"), new AssignStaff(1, 0, i, new Worker(i, "a", 0))); ((Machine)b.Find(0, i)!).Inputs.Add(new ItemStack("iron_ore", 50, 2)); }
        s.Step();
        Assert.Equal(100, b.LastSupply); Assert.Equal(150, b.LastDemand);
        Assert.All(new[] { 1, 2, 3 }, i => Assert.Equal(66, b.Find(0, i)!.PowerPct));
        Do(s, new SetPolicy(1, PowerPolicy.Priority)); s.Step();
        Assert.Equal(100, b.Find(0, 1)!.PowerPct); Assert.Equal(100, b.Find(0, 2)!.PowerPct); Assert.Equal(0, b.Find(0, 3)!.PowerPct);
        Do(s, new SetPolicy(1, PowerPolicy.Stop)); s.Step();
        Assert.All(new[] { 1, 2, 3 }, i => Assert.Equal(0, b.Find(0, i)!.PowerPct));
    }

    [Fact]
    public void Ports_MoveItemsBetweenFloors()
    {
        var s = NewSim();
        Do(s, new CreateBuilding(1, "b", 4, 2, 2, false),
              new PlaceStructure(1, 0, "chest", 0, 0, 0, Direction.East), new PlaceStructure(1, 0, "inserter", 1, 0, 0, Direction.East), new PlaceStructure(1, 0, "port_lift", 2, 0, 0, Direction.East),
              new PlaceStructure(1, 1, "port_lift", 2, 0, 0, Direction.East), new PlaceStructure(1, 1, "chest", 3, 0, 0, Direction.East),
              new PlaceStructure(1, 0, "reactor_g0", 0, 1, 0, Direction.East), new LinkPorts(1, 0, 3, 1, 1, 4), new AddItems(1, 0, 1, "gear", 70, 5));
        s.Run(60 * 6);
        var up = (Chest)s.Factory.Get(1)!.Find(1, 5)!;
        Assert.Equal(5, up.Items.Count);
    }

    [Fact]
    public void Fluids_PumpFillsTank_AndFurnaceUsesWaterForConcrete()
    {
        var s = NewSim();
        Do(s, new CreateBuilding(1, "b", 5, 2, 1, false),
              new PlaceStructure(1, 0, "pump", 0, 0, 0, Direction.East), new PlaceStructure(1, 0, "pipe", 1, 0, 0, Direction.East), new PlaceStructure(1, 0, "tank", 2, 0, 0, Direction.East),
              new PlaceStructure(1, 0, "pipe", 3, 0, 0, Direction.East), new PlaceStructure(1, 0, "furnace", 4, 0, 0, Direction.East), new PlaceStructure(1, 0, "reactor_g2", 0, 1, 0, Direction.East),
              new SetRecipe(1, 0, 5, "concrete"), new AssignStaff(1, 0, 5, new Worker(1, "a", 5)));
        var m = (Machine)s.Factory.Get(1)!.Find(0, 5)!; var t = (Tank)s.Factory.Get(1)!.Find(0, 3)!;
        s.Run(600);
        Assert.Equal("water", t.Fluid); Assert.True(t.Amount > 0);
        m.Inputs.Add(new ItemStack("stone", 50, 4));
        s.Run(300);
        Assert.True(m.Made + m.Failed >= 1);
    }

    [Fact]
    public void Inventory_PlaceNeedsStock_RemoveReturnsIt()
    {
        var s = new Simulation(Blocks(), Data, 1);
        Do(s, new CreateBuilding(1, "b", 3, 3, 1, false), new PlaceStructure(1, 0, "chest", 0, 0, 0, Direction.East));
        Assert.Empty(s.Factory.Get(1)!.Floors[0].Structures);
        Do(s, new AddInventory(1, "chest", 1), new PlaceStructure(1, 0, "chest", 0, 0, 0, Direction.East));
        Assert.Single(s.Factory.Get(1)!.Floors[0].Structures); Assert.Equal(0, s.Factory.Get(1)!.Inventory["chest"]);
        Do(s, new RemoveStructure(1, 0, 1));
        Assert.Empty(s.Factory.Get(1)!.Floors[0].Structures); Assert.Equal(1, s.Factory.Get(1)!.Inventory["chest"]);
    }

    [Fact]
    public void Clock_FiveRealMinutesIsOneGameHour()
    {
        Assert.Equal(18_000, Clock.TicksPerHour); Assert.Equal(5 * 60 * 60, Clock.TicksPerHour);
        var (y, d, h, mi) = Clock.Of(Clock.TicksPerYear + Clock.TicksPerDay * 2 + Clock.TicksPerHour * 3 + Clock.TicksPerHour / 2);
        Assert.Equal((2, 3, 3, 30), (y, d, h, mi));
    }

    [Fact]
    public void Save_SnapshotAndReplay_AreIdentical()
    {
        var s = NewSim(99);
        foreach (var c in SampleFactory.Ground(1, 20, 20, reactors: 4)) s.Enqueue(c);
        s.Step();
        s.Run(2_000);
        Do(s, new SetPolicy(1, PowerPolicy.Priority));
        s.Run(1_000);
        string json = Save.Snapshot(s);
        var loaded = Save.Load(json, Blocks(), Data); loaded.Factory.Creative = true;
        Assert.Equal(Save.StateHash(s), Save.StateHash(loaded));
        var replayed = Save.Replay(99, s.Log, s.Tick, Blocks(), Data, creative: true);
        Assert.Equal(Save.StateHash(s), Save.StateHash(replayed));
        loaded.Run(500); s.Run(500);
        Assert.Equal(Save.StateHash(s), Save.StateHash(loaded));   // 読み込み後も同じ軌道
    }

    [Fact]
    public void SampleFactory_100x100_Runs_AndReportsTickCost()
    {
        var s = NewSim(5);
        var cmds = SampleFactory.Ground(1, 100, 100, reactors: 40, reactor: "reactor_g4");
        foreach (var c in cmds) s.Enqueue(c);
        s.Step();
        int n = s.Factory.Get(1)!.Floors[0].Structures.Count;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        s.Run(600);
        sw.Stop();
        double perTick = sw.Elapsed.TotalMilliseconds / 600;
        Console.WriteLine($"[perf] structures={n} ms/tick={perTick:F3}");
        Assert.True(n > 6000);
        Assert.True(perTick < 16.6, $"1 ティックが {perTick:F2} ms(60 ティック/秒の予算 16.6 ms を超過)");
    }
}
