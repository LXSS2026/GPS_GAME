using GPS.Sim;
using Xunit;

/// <summary>修理と部品の回収の工場連携(G16 #9 の残り)。</summary>
public class RecoveryTests
{
    static readonly GameData Data = GameData.Load(GameData.FindDataDir());
    static BlockRegistry Blocks() => BlockRegistry.FromJson(File.ReadAllText(Path.Combine(GameData.FindDataDir(), "blocks.json")));
    static Simulation NewSim(ulong seed) { var s = new Simulation(Blocks(), Data, seed); s.Factory.Creative = true; return s; }
    static void Do(Simulation s, params ICommand[] cmds) { foreach (var c in cmds) s.Enqueue(c); s.Step(); }

    static Simulation Battle(ulong seed)
    {
        var s = NewSim(seed);
        Do(s, new CreateBuilding(1, "味方の格納庫", 12, 12, 1, false), new SetBuildingPosition(1, 0, 0), new CreateBuilding(2, "敵の格納庫", 12, 12, 1, false), new SetBuildingPosition(2, 400, 0));
        for (int i = 1; i <= 4; i++) s.Enqueue(new CreateMech(i, 3, "general", 80));
        for (int i = 11; i <= 12; i++) s.Enqueue(new CreateMech(i, 1, "general", 60));
        s.Step();
        Do(s, new FormSquad(1, 1, "第 1 小隊", new[] { 1, 2, 3, 4 }, 1), new FormSquad(2, 2, "敵", new[] { 11, 12 }, 2));
        Do(s, new Deploy(1, 10, 0), new Deploy(2, 120, 0), new OrderSquad(1, SquadOrder.Attack, TargetSquad: 2), new OrderSquad(2, SquadOrder.Attack, TargetSquad: 1));
        s.Run(60 * 90);
        return s;
    }

    [Fact]
    public void Collect_PicksUpLoot_ReturnsHome_AndCargoLandsInBuilding()
    {
        var s = Battle(1); var f = s.Factory;
        Assert.Equal(0, f.Squads[1].Alive); Assert.Equal(2, f.Loot.Count);
        int parts = f.Loot.Sum(l => l.Parts.Count);
        Do(s, new OrderSquad(1, SquadOrder.Collect));
        for (int i = 0; i < 20 && f.Squads[0].State != SquadState.InHangar; i++) s.Run(60 * 10);
        Assert.Empty(f.Loot); Assert.Contains(f.Events, e => e.Kind == "collected");
        Assert.Equal(SquadState.InHangar, f.Squads[0].State); Assert.Empty(f.Squads[0].Cargo);
        var home = f.Get(1)!; Assert.Equal(parts, home.Cargo.Sum(x => x.Count)); Assert.All(home.Cargo, x => Assert.StartsWith("mech_", x.Item.Replace("reactor_core", "mech_x").Replace("battery_pack", "mech_x")));
    }

    [Fact]
    public void Repair_UsesCargoParts_QuarterPerPartPerHour()
    {
        var s = Battle(2); var f = s.Factory; var sq = f.Squads[0];
        Do(s, new OrderSquad(1, SquadOrder.Retreat));
        for (int i = 0; i < 20 && sq.State != SquadState.InHangar; i++) s.Run(60 * 10);
        var hurt = sq.Members.Where(u => u.Alive && u.Hp < u.MaxHp).ToList(); Assert.NotEmpty(hurt);
        var u = hurt.OrderBy(x => x.Hp / x.MaxHp).First(); double hp0 = u.Hp;
        var home = f.Get(1)!; home.Cargo.Add(new ItemStack("armor_plate", 70, 2));
        Combat.RepairHour(f, s.Combat);
        Assert.Equal(Math.Min(u.MaxHp, hp0 + u.MaxHp * 0.25), u.Hp, 6);
        Assert.Contains(f.Events, e => e.Kind == "repaired");
        Combat.RepairHour(f, s.Combat); Combat.RepairHour(f, s.Combat);   // 部品が尽きたら止まる
        Assert.Empty(home.Cargo.Where(x => x.Item == "armor_plate"));
        // 1 時間の境界で自動で動く
        home.Cargo.Add(new ItemStack("mech_arm", 80, 1)); double before = sq.Members.Where(x => x.Alive).Sum(x => x.Hp);
        long toHour = Clock.TicksPerHour - s.Tick % Clock.TicksPerHour; s.Run(toHour + 1);
        Assert.True(sq.Members.Where(x => x.Alive).Sum(x => x.Hp) > before || sq.Members.All(x => !x.Alive || x.Hp >= x.MaxHp));
    }

    [Fact]
    public void UnloadCargo_MovesToChest_ForFactoryUse()
    {
        var s = NewSim(3); var f = s.Factory;
        Do(s, new CreateBuilding(1, "格納庫", 12, 12, 1, false), new PlaceStructure(1, 0, "chest", 0, 0, 0, Direction.East));
        var b = f.Get(1)!; b.Cargo.Add(new ItemStack("iron_plate", 60, 5)); b.Cargo.Add(new ItemStack("mech_leg", 85, 1));
        Do(s, new UnloadCargo(1, 0, 1));
        var ch = (Chest)b.Find(0, 1)!; Assert.Equal(6, ch.Items.Count); Assert.Empty(b.Cargo); Assert.Contains(ch.Items, x => x.Item == "mech_leg" && x.Quality == 85);
        var loaded = Save.Load(Save.Snapshot(s), Blocks(), Data); Assert.Equal(Save.StateHash(s), Save.StateHash(loaded));
    }
}
