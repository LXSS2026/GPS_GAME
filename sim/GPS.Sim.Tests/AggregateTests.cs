using GPS.Sim;
using Xunit;

public class AggregateTests
{
    static readonly GameData Data = GameData.Load(GameData.FindDataDir());
    static BlockRegistry Blocks() => BlockRegistry.FromJson(File.ReadAllText(Path.Combine(GameData.FindDataDir(), "blocks.json")));
    static Simulation NewSim(ulong seed) { var s = new Simulation(Blocks(), Data, seed); s.Factory.Creative = true; return s; }
    static void Do(Simulation s, params ICommand[] cmds) { foreach (var c in cmds) s.Enqueue(c); s.Step(); }

    static (long made, long failed, int chest, long ore) Totals(Simulation s)
    {
        var fl = s.Factory.Get(1)!.Floors[0];
        return (fl.Structures.OfType<Machine>().Sum(m => m.Made), fl.Structures.OfType<Machine>().Sum(m => m.Failed),
                fl.Structures.OfType<Chest>().Sum(c => c.Items.Count), Enumerable.Range(0, fl.W * fl.D).Sum(i => fl.Ore![i]?.Amount ?? 0));
    }

    [Fact]
    public void Aggregate_TracksFullSimulation_WithinTolerance_AndIsMuchCheaper()
    {
        const int ticks = 60 * 60 * 2;   // 2 ゲーム分(再記録を 1 回含む長さではないが、窓 30 秒 + 積算)
        var full = NewSim(42); var agg = NewSim(42);
        foreach (var c in SampleLines.Build(1, 24, reactors: 6)) { full.Enqueue(c); agg.Enqueue(c); }
        full.Step(); agg.Step();
        full.Run(1800); agg.Run(1800);                 // 立ち上がりを揃える
        Do(agg, new SetViewed(1, false));
        var sw1 = System.Diagnostics.Stopwatch.StartNew(); full.Run(ticks); sw1.Stop();
        var sw2 = System.Diagnostics.Stopwatch.StartNew(); agg.Run(ticks); sw2.Stop();
        var a = Totals(full); var b = Totals(agg);
        Console.WriteLine($"[agg] full made={a.made} failed={a.failed} chest={a.chest} ore={a.ore}  ms={sw1.ElapsedMilliseconds}");
        Console.WriteLine($"[agg] aggr made={b.made} failed={b.failed} chest={b.chest} ore={b.ore}  ms={sw2.ElapsedMilliseconds}");
        Assert.InRange(b.made, a.made * 0.93, a.made * 1.07);
        Assert.InRange(b.chest, a.chest * 0.9, a.chest * 1.1);
        Assert.InRange(b.ore, a.ore - (a.ore == 0 ? 0 : 50), a.ore + 50);
        Assert.True(a.made > 100);
        Assert.True(sw2.ElapsedMilliseconds * 3 < sw1.ElapsedMilliseconds, $"aggregate {sw2.ElapsedMilliseconds} ms vs full {sw1.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Aggregate_ReprofilesWhenChestFills_AndStopsAtCapacity()
    {
        var s = NewSim(3);
        Do(s, new CreateBuilding(1, "g", 4, 2, 1, true), new SetOre(1, 0, 0, "iron_ore", 1_000_000, 60),
              new PlaceStructure(1, 0, "miner", 0, 0, 0, Direction.East), new PlaceStructure(1, 0, "inserter", 1, 0, 0, Direction.East),
              new PlaceStructure(1, 0, "chest", 2, 0, 0, Direction.East), new PlaceStructure(1, 0, "reactor_g3", 3, 0, 0, Direction.East));
        s.Run(600);
        Do(s, new SetViewed(1, false));
        s.Run(300 * 100 + 2000);   // 100 個 × 300 ティック + 記録の窓
        var chest = (Chest)s.Factory.Get(1)!.Find(0, 3)!;
        Assert.Equal(100, chest.Items.Count);
        Assert.All(chest.Items, it => Assert.Equal(60, it.Quality));   // 窓内の平均品質で置くので鉱石の品質 60 がそのまま残る
    }

    [Fact]
    public void Aggregate_IsDeterministic_ThroughSaveAndReplay()
    {
        var s = NewSim(9);
        foreach (var c in SampleFactory.Ground(1, 20, 20, reactors: 4)) s.Enqueue(c);
        s.Step(); s.Run(500);
        Do(s, new SetViewed(1, false));
        s.Run(4000);
        var loaded = Save.Load(Save.Snapshot(s), Blocks(), Data); loaded.Factory.Creative = true;
        Assert.Equal(Save.StateHash(s), Save.StateHash(loaded));
        var replayed = Save.Replay(9, s.Log, s.Tick, Blocks(), Data, creative: true);
        Assert.Equal(Save.StateHash(s), Save.StateHash(replayed));
        s.Run(700); loaded.Run(700);
        Assert.Equal(Save.StateHash(s), Save.StateHash(loaded));
    }

    [Fact]
    public void Battery_ChargesOnSurplus_DischargesByMode()
    {
        var s = NewSim(1);
        Do(s, new CreateBuilding(1, "g", 6, 2, 1, false),
              new PlaceStructure(1, 0, "reactor_g1", 0, 0, 0, Direction.East),       // 供給 170 (品質 70)
              new PlaceStructure(1, 0, "battery_s", 1, 0, 0, Direction.East),
              new PlaceStructure(1, 0, "furnace", 2, 0, 0, Direction.East), new PlaceStructure(1, 0, "furnace", 3, 0, 0, Direction.East), new PlaceStructure(1, 0, "furnace", 4, 0, 0, Direction.East), new PlaceStructure(1, 0, "furnace", 5, 0, 0, Direction.East));
        var b = s.Factory.Get(1)!; var bat = (Battery)b.Find(0, 2)!;
        s.Run(600);
        Assert.True(bat.Stored >= 100 * 600 * 0.9, $"stored {bat.Stored}");          // 余剰 170 kW のうち充電速度 100 kW
        // 需要 200 kW(炉 4 台)> 供給 170: ピーク用なら放電して 100%
        for (int i = 3; i <= 6; i++) { Do(s, new SetRecipe(1, 0, i, "iron_plate"), new AssignStaff(1, 0, i, new Worker(i, "a", 0))); ((Machine)b.Find(0, i)!).Inputs.Add(new ItemStack("iron_ore", 50, 4)); }
        s.Step();
        Assert.Equal(100, b.Find(0, 3)!.PowerPct); Assert.Equal(200, b.LastSupply);
        long before = bat.Stored; s.Run(60); Assert.True(bat.Stored < before);
        // 非常用: リアクターが生きている間は放電しない
        Do(s, new SetBatteryMode(1, 0, 2, BatteryMode.Emergency)); s.Step();
        Assert.Equal(85, b.Find(0, 3)!.PowerPct);
        // リアクターを撤去 → 非常用が放電
        Do(s, new RemoveStructure(1, 0, 1)); s.Step();
        Assert.Equal(100, b.Find(0, 3)!.PowerPct);
        // 停止: 放電しない
        Do(s, new SetBatteryMode(1, 0, 2, BatteryMode.Off)); s.Step();
        Assert.Equal(0, b.Find(0, 3)!.PowerPct);
    }
}
