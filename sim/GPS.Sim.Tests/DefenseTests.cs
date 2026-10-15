using GPS.Sim;
using Xunit;

/// <summary>防御壁と砲塔(G1 確定要件「建物の防御」、G16 次段階)。</summary>
public class DefenseTests
{
    static readonly GameData Data = GameData.Load(GameData.FindDataDir());
    static BlockRegistry Blocks() => BlockRegistry.FromJson(File.ReadAllText(Path.Combine(GameData.FindDataDir(), "blocks.json")));
    static Simulation NewSim(ulong seed) { var s = new Simulation(Blocks(), Data, seed); s.Factory.Creative = true; return s; }
    static void Do(Simulation s, params ICommand[] cmds) { foreach (var c in cmds) s.Enqueue(c); s.Step(); }

    /// <summary>格納庫(砲塔付き)と、200 m 東の敵の格納庫。敵は格納庫を攻めてくる。</summary>
    static Simulation Siege(ulong seed, string turret, int turrets = 4)
    {
        var s = NewSim(seed);
        Do(s, new CreateBuilding(1, "格納庫", 12, 12, 1, false), new SetBuildingPosition(1, 0, 0), new CreateBuilding(2, "敵", 12, 12, 1, false), new SetBuildingPosition(2, 200, 0));
        var cmds = new List<ICommand> { new PlaceStructure(1, 0, "reactor_g3", 0, 0, 0, Direction.East) };
        for (int i = 0; i < turrets; i++) cmds.Add(new PlaceStructure(1, 0, turret, 2 + i * 2, 11, 0, Direction.South));
        for (int i = 11; i <= 13; i++) cmds.Add(new CreateMech(i, 1, "general", 60));
        cmds.Add(new FormSquad(2, 2, "敵", new[] { 11, 12, 13 }, 2));
        foreach (var c in cmds) s.Enqueue(c); s.Step();
        Do(s, new Deploy(2, 190, 0), new OrderSquad(2, SquadOrder.Attack, TargetBuilding: 1));
        return s;
    }

    [Fact]
    public void BeamTurrets_ShootApproachingEnemies_WithBuildingPower()
    {
        var s = Siege(1, "turret_beam"); var f = s.Factory;
        s.Run(60 * 90);
        var b = f.Get(1)!; var turrets = b.Floors[0].Structures.OfType<Turret>().ToList();
        Assert.True(turrets.Sum(t => t.Shots) > 10, $"shots {turrets.Sum(t => t.Shots)}");
        Assert.Contains(f.Events, e => e.Kind == "hit" && e.Building == -1);
        Assert.True(f.Squads[0].Alive < 3, "砲塔が敵を減らす");
        // 動力が無いと撃たない
        var s2 = Siege(2, "turret_beam"); var r = s2.Factory.Get(1)!.Floors[0].Structures.OfType<Reactor>().First(); r.Enabled = false; r.OutputPct = 0;
        s2.Run(60 * 60); Assert.Equal(0, s2.Factory.Get(1)!.Floors[0].Structures.OfType<Turret>().Sum(t => t.Shots));
    }

    [Fact]
    public void LiveTurrets_NeedAmmoInCargo()
    {
        var s = Siege(3, "turret_live"); var f = s.Factory; var b = f.Get(1)!;
        s.Run(60 * 40); Assert.Equal(0, b.Floors[0].Structures.OfType<Turret>().Sum(t => t.Shots));   // 弾薬なし
        b.Cargo.Add(new ItemStack("ammo", 60, 30));
        s.Run(60 * 60);
        int shots = b.Floors[0].Structures.OfType<Turret>().Sum(t => t.Shots);
        Assert.True(shots > 0 && shots <= 30, $"shots {shots}"); Assert.Equal(30 - shots, b.Cargo.Where(x => x.Item == "ammo").Sum(x => x.Count));
        Assert.Contains(Data.Recipes.Values, r => r.Id == "ammo" && r.Outputs[0].Item == "ammo");   // 鋼 → 弾薬のレシピ
    }

    [Fact]
    public void Wall_RaisesBuildingHp_AndCostsTreasury()
    {
        var s = NewSim(4); var f = s.Factory; f.Creative = false; f.Treasury = 10_000;
        Do(s, new CreateBuilding(1, "工場棟", 30, 30, 3, false));
        var b = f.Get(1)!; double hp0 = b.MaxHp; Assert.Equal(120, b.Perimeter);
        Do(s, new BuildWall(1, 2));
        Assert.Equal(2, b.WallLevel); Assert.Equal(hp0 + 120 * 400 * 2, b.MaxHp); Assert.Equal(b.MaxHp, b.Hp); Assert.Equal(10_000 - 120 * 5 * 2, f.Treasury);
        Do(s, new BuildWall(1, 9)); Assert.Equal(3, b.WallLevel);   // 上限 3
        Do(s, new BuildWall(1, 1)); Assert.Equal(3, b.WallLevel);   // 下げられない
        f.Treasury = 0; Do(s, new CreateBuilding(2, "小屋", 10, 10, 1, false)); Do(s, new BuildWall(2, 1)); Assert.Equal(0, f.Get(2)!.WallLevel); Assert.Contains(f.Events, e => e.Kind == "build_failed");
        var loaded = Save.Load(Save.Snapshot(s), Blocks(), Data); Assert.Equal(Save.StateHash(s), Save.StateHash(loaded)); Assert.Equal(3, loaded.Factory.Get(1)!.WallLevel);
    }
}
