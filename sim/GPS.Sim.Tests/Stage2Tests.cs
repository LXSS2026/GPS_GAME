using GPS.Sim;
using Xunit;

public class Stage2Tests
{
    static readonly GameData Data = GameData.Load(GameData.FindDataDir());
    static BlockRegistry Blocks() => BlockRegistry.FromJson(File.ReadAllText(Path.Combine(GameData.FindDataDir(), "blocks.json")));
    static Simulation NewSim(ulong seed) { var s = new Simulation(Blocks(), Data, seed); s.Factory.Creative = true; return s; }
    static void Do(Simulation s, params ICommand[] cmds) { foreach (var c in cmds) s.Enqueue(c); s.Step(); }

    [Fact]
    public void Trade_ExportSellsByTier_ImportBuysIntoOutbox()
    {
        var s = NewSim(1);
        Do(s, new CreateBuilding(1, "t", 6, 3, 1, false),
              new PlaceStructure(1, 0, "chest", 0, 0, 0, Direction.East), new PlaceStructure(1, 0, "inserter", 1, 0, 0, Direction.East), new PlaceStructure(1, 0, "trade", 2, 0, 0, Direction.East),
              new PlaceStructure(1, 0, "inserter", 4, 0, 0, Direction.East), new PlaceStructure(1, 0, "chest", 5, 0, 0, Direction.East), new PlaceStructure(1, 0, "reactor_g2", 0, 2, 0, Direction.East),
              new AddItems(1, 0, 1, "gear", 95, 2), new AddItems(1, 0, 1, "gear", 60, 3));   // 高品質 2 + 通常 3
        s.Run(60 * 5 + 40);   // インサータは 1 個に 60 ティック(掴む 30 + 置く 30)
        long expected = 2 * (12 * 300 / 100) + 3 * 12;   // 高品質は 3 倍
        Assert.Equal(expected, s.Factory.Treasury);
        var t = (Trade)s.Factory.Get(1)!.Find(0, 3)!; Assert.Equal(5, t.Sold);
        Do(s, new SetImport(1, 0, 3, "steel"));
        s.Run(120 * 3 + 60);
        var outChest = (Chest)s.Factory.Get(1)!.Find(0, 5)!;
        Assert.True(t.Bought >= 2 && outChest.Items.Count + t.Outbox.Count >= 2);
        Assert.All(outChest.Items, it => Assert.Equal("steel", it.Item));
        Assert.True(s.Factory.Treasury < expected);
        // 国庫が尽きたら買わない
        Do(s, new AddTreasury(-s.Factory.Treasury));
        long bought = t.Bought; s.Run(600); Assert.Equal(bought, t.Bought);
    }

    [Fact]
    public void Robot_ReplacesWorker_SkillFromQuality_AndBiasesToNormal()
    {
        var s = NewSim(5);
        Do(s, new CreateBuilding(1, "r", 4, 2, 1, false),
              new PlaceStructure(1, 0, "assembler", 0, 0, 0, Direction.East), new PlaceStructure(1, 0, "chest", 1, 0, 0, Direction.East), new PlaceStructure(1, 0, "reactor_g2", 2, 0, 0, Direction.East),
              new SetRecipe(1, 0, 1, "gear"), new AddItems(1, 0, 2, "factory_robot", 83, 1));
        var m = (Machine)s.Factory.Get(1)!.Find(0, 1)!;
        m.Inputs.Add(new ItemStack("iron_plate", 60, 2)); s.Run(130);
        Assert.Equal(0, m.Made + m.Failed);                       // 要員なし
        Do(s, new AssignRobot(1, 0, 1, 2));
        Assert.Single(m.Staff); Assert.True(m.Staff[0].IsRobot); Assert.Equal(8, m.Staff[0].Skill);
        int tails = 0;
        for (int i = 0; i < 3000; i++) { m.Inputs.Add(new ItemStack("iron_plate", 60, 2)); s.Run(121); foreach (var o in m.Outputs) if (o.Item == "gear" && o.Tier != QualityTier.Normal) tails++; m.Outputs.Clear(); }
        Assert.True(m.Made > 2500);
        Assert.True(tails < 3000 * 0.011 * 0.6, $"tails {tails}");   // 高・最高が 7 割減(不良は技能 8 でも減る)
        Do(s, new RemoveStaff(1, 0, 1, m.Staff[0].Id, 2));
        Assert.Empty(m.Staff); Assert.Contains(((Chest)s.Factory.Get(1)!.Find(0, 2)!).Items, it => it.Item == "factory_robot" && it.Quality == 83);
    }

    [Fact]
    public void CopyPaste_ReproducesLayoutWithRecipes_AndNeedsInventory()
    {
        var s = NewSim(2);
        foreach (var c in SampleLines.Build(1, 4, reactors: 1)) s.Enqueue(c);
        s.Step();
        Do(s, new CopyRegion(1, 0, 0, 0, 3, 5, 1, "生産線 4 本"));
        var bp = s.Factory.Blueprints[1];
        Assert.Equal(4 * 5 + 1, bp.Entries.Count);
        Assert.Contains(bp.Entries, e => e.Def == "furnace" && e.Recipe == "iron_plate");
        // 在庫が無い建物には貼れない、在庫があれば貼れる
        s.Factory.Creative = false;
        Do(s, new CreateBuilding(2, "b", 10, 10, 2, false), new PasteBlueprint(2, 1, 2, 2, 1));
        Assert.Empty(s.Factory.Get(2)!.Floors[1].Structures);
        foreach (var e in bp.Entries) s.Enqueue(new AddInventory(2, e.Def, 1));
        Do(s, new PasteBlueprint(2, 1, 2, 2, 1));
        var fl = s.Factory.Get(2)!.Floors[1];
        Assert.Equal(bp.Entries.Count, fl.Structures.Count);
        Assert.NotNull(fl.At(2, 2, 0)); Assert.Equal("iron_plate", ((Machine)fl.At(2, 4, 0)!).Recipe);
        Assert.All(s.Factory.Get(2)!.Inventory.Values, v => Assert.Equal(0, v));
    }

    [Fact]
    public void Move_RelocatesWithStaffAndRecipes_AcrossBuildings()
    {
        var s = NewSim(3);
        foreach (var c in SampleLines.Build(1, 3, reactors: 1)) s.Enqueue(c);
        s.Enqueue(new CreateBuilding(2, "新棟", 8, 8, 1, false));
        s.Step();
        s.Factory.Creative = false;
        var m0 = s.Factory.Get(1)!.Floors[0].Structures.OfType<Machine>().First();
        Do(s, new MoveRegion(1, 0, 0, 0, 2, 5, 2, 0, 3, 1));
        Assert.Empty(s.Factory.Get(1)!.Floors[0].Structures);
        var fl = s.Factory.Get(2)!.Floors[0];
        Assert.Equal(3 * 5 + 1, fl.Structures.Count);
        var m = (Machine)fl.At(3, 3, 0)!;
        Assert.Equal("iron_plate", m.Recipe); Assert.Single(m.Staff); Assert.Equal(m0.Staff[0].Name, m.Staff[0].Name);
        Assert.All(s.Factory.Get(1)!.Inventory.Values, v => Assert.Equal(0, v));
        Assert.All(s.Factory.Get(2)!.Inventory.Values, v => Assert.Equal(0, v));
    }

    [Fact]
    public void Stage2_Commands_SurviveSaveAndReplay()
    {
        var s = NewSim(8);
        foreach (var c in SampleLines.Build(1, 4, reactors: 2)) s.Enqueue(c);
        s.Step();
        Do(s, new PlaceStructure(1, 0, "trade", 5, 0, 0, Direction.East), new SetImport(1, 0, s.Factory.Get(1)!.NextStructureId - 1, "coal"), new AddTreasury(5000),
              new CopyRegion(1, 0, 0, 0, 1, 5, 1, "x"), new CreateBuilding(2, "b", 8, 8, 1, false), new PasteBlueprint(2, 0, 1, 1, 1));
        s.Run(2000);
        var replayed = Save.Replay(8, s.Log, s.Tick, Blocks(), Data, creative: true);
        Assert.Equal(Save.StateHash(s), Save.StateHash(replayed));
        var loaded = Save.Load(Save.Snapshot(s), Blocks(), Data); loaded.Factory.Creative = true;
        Assert.Equal(Save.StateHash(s), Save.StateHash(loaded));
    }

    [Fact]
    public void Reactor_Overload_AccumulatesAndRecovers_ThenExplodesAndDestroysNearby()
    {
        var s = NewSim(4);
        Do(s, new CreateBuilding(1, "x", 20, 20, 1, false),
              new PlaceStructure(1, 0, "reactor_g1", 10, 10, 0, Direction.East),       // 品質 70 → 170 kW、150% で 255 kW → 半径 5.1 セル
              new PlaceStructure(1, 0, "chest", 12, 10, 0, Direction.East),           // 2 セル: 消える
              new PlaceStructure(1, 0, "chest", 10, 17, 0, Direction.East),           // 7 セル: 残る
              new PlaceStructure(1, 0, "furnace", 0, 0, 0, Direction.East));         // 遠い: 残る
        var b = s.Factory.Get(1)!; var r = (Reactor)b.Find(0, 1)!;
        Do(s, new SetReactorOutput(1, 0, 1, 200));
        Assert.Equal(150, r.OutputPct);                                               // 上限 150
        s.Step(); Assert.Equal(255, b.LastSupply);                                    // 170 × 1.5
        s.Run(999); Assert.Equal(50L * 1001, r.Load);                                  // Do の 1 ティック + Step + 999
        Do(s, new SetReactorOutput(1, 0, 1, 100)); s.Run(100); Assert.Equal(50L * 1001, r.Load);   // 100% では回復しない
        Do(s, new SetReactorOutput(1, 0, 1, 50)); s.Run(100); Assert.Equal(50L * 1001 - 50 * 101, r.Load);   // 100% 未満で回復(101 ティック × 50)
        Do(s, new SetReactorOutput(1, 0, 1, 150));
        s.Run(Data.OverloadLimit / 50 + 10);
        Assert.Null(b.Find(0, 1)); Assert.Null(b.Find(0, 2));                        // リアクターと近い箱が消える
        Assert.NotNull(b.Find(0, 3)); Assert.NotNull(b.Find(0, 4));
        var ev = Assert.Single(s.Factory.Events); Assert.Equal("explosion", ev.Kind); Assert.Equal(255, ev.Power); Assert.Equal(2, ev.Destroyed);
        Assert.Equal(0, b.Inventory.GetValueOrDefault("reactor_g1"));                 // 在庫には戻らない
    }

    static void Feed(Machine m, int q, string power = "reactor_core")
    {
        foreach (var (it, n) in new[] { ("mech_head", 1), ("mech_torso", 1), ("mech_arm", 2), ("mech_leg", 2), ("mech_back", 1), ("mech_weapon", 1), (power, 1) })
            m.Inputs.Add(new ItemStack(it, q, n));
    }

    [Fact]
    public void Mech_AssemblyByGeneration_TierGates_ArchetypeAndPower()
    {
        var s = NewSim(6);
        Do(s, new CreateBuilding(1, "m", 8, 8, 1, false), new PlaceStructure(1, 0, "mech_assembler", 0, 0, 0, Direction.East), new PlaceStructure(1, 0, "reactor_g3", 6, 0, 0, Direction.East),
              new SetRecipe(1, 0, 1, "mech_g2_reactor"), new SetArchetype(1, 0, 1, "heavy"));
        var m = (Machine)s.Factory.Get(1)!.Find(0, 1)!;
        for (int i = 1; i <= 4; i++) s.Enqueue(new AssignStaff(1, 0, 1, new Worker(i, "a", 5)));
        s.Step();
        Feed(m, 65); s.Run(4800 + 5);
        var mech = Assert.Single(s.Factory.Mechs);
        Assert.Equal(2, mech.Generation); Assert.Equal("heavy", mech.Archetype); Assert.Equal("reactor", mech.PowerSource); Assert.Equal(65, mech.Quality); Assert.Equal(9, mech.Parts.Count);
        // 第 5 世代は高品質以上: 通常品質の部品では着手しない
        Do(s, new SetRecipe(1, 0, 1, "mech_g5_battery")); Feed(m, 65, "battery_pack"); s.Run(600);
        Assert.Single(s.Factory.Mechs); Assert.True(m.Progress < 0);
        m.Inputs.Clear(); Feed(m, 92, "battery_pack"); s.Run(9000 + 5);
        Assert.Equal(2, s.Factory.Mechs.Count); Assert.Equal("battery", s.Factory.Mechs[1].PowerSource); Assert.Equal(5, s.Factory.Mechs[1].Generation);
        // 第 6 世代は最高品質のみ
        Do(s, new SetRecipe(1, 0, 1, "mech_g6_reactor")); m.Inputs.Clear(); Feed(m, 95); s.Run(600);
        Assert.Equal(2, s.Factory.Mechs.Count);
        m.Inputs.Clear(); Feed(m, 99); s.Run(14400 + 5);
        Assert.Equal(3, s.Factory.Mechs.Count); Assert.Equal(6, s.Factory.Mechs[2].Generation);
        Assert.Contains(s.Factory.Events, e => e.Kind == "mech");
    }
}
