using GPS.Sim;
using Xunit;

public class McuTests
{
    static readonly GameData Data = GameData.Load(GameData.FindDataDir());
    static BlockRegistry Blocks() => BlockRegistry.FromJson(File.ReadAllText(Path.Combine(GameData.FindDataDir(), "blocks.json")));
    static Simulation NewSim(ulong seed) { var s = new Simulation(Blocks(), Data, seed); s.Factory.Creative = true; return s; }
    static void Do(Simulation s, params ICommand[] cmds) { foreach (var c in cmds) s.Enqueue(c); s.Step(); }

    [Fact]
    public void Mcu_ReactorOverloadGuard_LowersSettingWhenLoadHigh()
    {
        // リアクターの過負荷が 50% を超えたら出力設定を 80% に、そうでなければ 150% に(過負荷の蓄積と回復を繰り返す制御)
        var s = NewSim(1);
        Do(s, new CreateBuilding(1, "c", 8, 4, 1, false), new PlaceStructure(1, 0, "reactor_g1", 0, 0, 0, Direction.East), new PlaceStructure(1, 0, "control_room", 2, 0, 0, Direction.East));
        var prog = new Program { Name = "過負荷の見張り", Interval = 1, Nodes = {
            new McuNode { Id = 1, Type = "in.reactor.load", TargetId = 1 },
            new McuNode { Id = 2, Type = "in.const", Params = { ["value"] = 50 } },
            new McuNode { Id = 3, Type = "op.gt" },
            new McuNode { Id = 4, Type = "in.const", Params = { ["value"] = 80 } },
            new McuNode { Id = 5, Type = "in.const", Params = { ["value"] = 150 } },
            new McuNode { Id = 6, Type = "op.select" },
            new McuNode { Id = 7, Type = "out.reactor.setting", TargetId = 1 } },
            Links = { new(1, 0, 3, 0), new(2, 0, 3, 1), new(3, 0, 6, 0), new(4, 0, 6, 1), new(5, 0, 6, 2), new(6, 0, 7, 0) } };
        Do(s, new SetProgram(1, 0, 2, prog));
        var r = (Reactor)s.Factory.Get(1)!.Find(0, 1)!; var mcu = (Mcu)s.Factory.Get(1)!.Find(0, 2)!;
        s.Run(10); Assert.Equal(150, r.OutputPct);
        s.Run(Data.OverloadLimit / 50 / 2 + 20);                  // 負荷 50% に達する
        // 以後は 50% の境で 80% と 150% を行き来し(1 評価遅れの制御)、負荷は 50% 付近に留まる
        var seen = new HashSet<int>(); for (int i = 0; i < 200; i++) { s.Step(); seen.Add(r.OutputPct); Assert.InRange(r.Load * 100.0 / Data.OverloadLimit, 45, 55); }
        Assert.Contains(80, seen); Assert.Contains(150, seen);
        Assert.True(mcu.Evaluations > 100);
        s.Run(Data.OverloadLimit / 10); Assert.NotNull(s.Factory.Get(1)!.Find(0, 1));   // 爆発しない
    }

    [Fact]
    public void Mcu_StopsMachineWhenChestFull_AndNeedsPower_AndAnyBuilding()
    {
        var s = NewSim(2);
        Do(s, new CreateBuilding(1, "a", 6, 4, 1, false), new CreateBuilding(2, "b", 6, 4, 1, false),
              new PlaceStructure(1, 0, "reactor_g1", 0, 0, 0, Direction.East), new PlaceStructure(1, 0, "control_room", 2, 0, 0, Direction.East),
              new PlaceStructure(2, 0, "reactor_g1", 0, 0, 0, Direction.East), new PlaceStructure(2, 0, "chest", 2, 0, 0, Direction.East), new PlaceStructure(2, 0, "furnace", 3, 0, 0, Direction.East),
              new AddItems(2, 0, 2, "gear", 60, 90));
        // 別の建物の箱が 80 個以上なら、別の建物の炉を止める
        var prog = new Program { Interval = 1, Nodes = {
            new McuNode { Id = 1, Type = "in.chest.count", TargetBuilding = 2, TargetFloor = 0, TargetId = 2 },
            new McuNode { Id = 2, Type = "in.const", Params = { ["value"] = 80 } }, new McuNode { Id = 3, Type = "op.lt" },
            new McuNode { Id = 4, Type = "out.structure.enabled", TargetBuilding = 2, TargetFloor = 0, TargetId = 3 } },
            Links = { new(1, 0, 3, 0), new(2, 0, 3, 1), new(3, 0, 4, 0) } };
        Do(s, new SetProgram(1, 0, 2, prog));
        var furnace = s.Factory.Get(2)!.Find(0, 3)!;
        s.Run(5); Assert.False(furnace.Enabled);
        ((Chest)s.Factory.Get(2)!.Find(0, 2)!).Items.RemoveRange(0, 20); s.Run(5); Assert.True(furnace.Enabled);
        // 制御室の建物の電気を止めると評価しない
        var mcu = (Mcu)s.Factory.Get(1)!.Find(0, 2)!; long ev = mcu.Evaluations;
        Do(s, new RemoveStructure(1, 0, 1)); s.Run(10); Assert.Equal(ev, mcu.Evaluations);
    }

    [Fact]
    public void Mcu_Memory_AndSaveReplay()
    {
        var s = NewSim(3);
        Do(s, new CreateBuilding(1, "a", 6, 4, 1, false), new PlaceStructure(1, 0, "reactor_g1", 0, 0, 0, Direction.East), new PlaceStructure(1, 0, "control_room", 2, 0, 0, Direction.East), new PlaceStructure(1, 0, "battery_g1", 4, 0, 0, Direction.East));
        // 記憶: 1 度でも供給が 100 を超えたら以後バッテリーを非常用(1)に
        var prog = new Program { Interval = 3, Nodes = {
            new McuNode { Id = 1, Type = "in.building.supply" }, new McuNode { Id = 2, Type = "in.const", Params = { ["value"] = 100 } }, new McuNode { Id = 3, Type = "op.gt" },
            new McuNode { Id = 4, Type = "in.const", Params = { ["value"] = 1 } }, new McuNode { Id = 5, Type = "op.memory" }, new McuNode { Id = 6, Type = "out.battery.mode", TargetId = 3 } },
            Links = { new(1, 0, 3, 0), new(2, 0, 3, 1), new(3, 0, 5, 0), new(4, 0, 5, 1), new(5, 0, 6, 0) } };
        Do(s, new SetProgram(1, 0, 2, prog)); s.Run(10);
        Assert.Equal(BatteryMode.Emergency, ((Battery)s.Factory.Get(1)!.Find(0, 3)!).Mode);
        var replayed = Save.Replay(3, s.Log, s.Tick, Blocks(), Data, creative: true);
        Assert.Equal(Save.StateHash(s), Save.StateHash(replayed));
        var loaded = Save.Load(Save.Snapshot(s), Blocks(), Data); loaded.Factory.Creative = true;
        Assert.Equal(Save.StateHash(s), Save.StateHash(loaded));
        Assert.Single(((Mcu)loaded.Factory.Get(1)!.Find(0, 2)!).Program.Nodes, n => n.Type == "op.memory");
    }

    [Fact]
    public void Mech_ProgramIsStored_AndCatalogCoversMechIo()
    {
        var s = NewSim(4);
        s.Factory.Mechs.Add(new Mech { Id = 1, Generation = 2 });
        var prog = new Program { Nodes = { new McuNode { Id = 1, Type = "in.enemy.distance" }, new McuNode { Id = 2, Type = "in.const", Params = { ["value"] = 300 } }, new McuNode { Id = 3, Type = "op.lt" }, new McuNode { Id = 4, Type = "out.mech.engage" } }, Links = { new(1, 0, 3, 0), new(2, 0, 3, 1), new(3, 0, 4, 0) } };
        Do(s, new SetMechProgram(1, prog));
        Assert.NotNull(s.Factory.Mechs[0].Program);
        Assert.Contains(McuCatalog.Types, t => t.Type == "out.mech.target");
        Assert.Throws<InvalidOperationException>(() => new SetProgram(1, 0, 1, new Program { Nodes = { new McuNode { Id = 1, Type = "in.nothing" } } }).Apply(s));
    }
}
