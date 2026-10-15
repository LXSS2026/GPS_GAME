using GPS.Sim;
using Xunit;

public class CombatTests
{
    static readonly GameData Data = GameData.Load(GameData.FindDataDir());
    static BlockRegistry Blocks() => BlockRegistry.FromJson(File.ReadAllText(Path.Combine(GameData.FindDataDir(), "blocks.json")));
    static Simulation NewSim(ulong seed) { var s = new Simulation(Blocks(), Data, seed); s.Factory.Creative = true; return s; }
    static void Do(Simulation s, params ICommand[] cmds) { foreach (var c in cmds) s.Enqueue(c); s.Step(); }

    static void Bases(Simulation s)
    {
        Do(s, new CreateBuilding(1, "味方の格納庫", 12, 12, 1, false), new SetBuildingPosition(1, 0, 0), new CreateBuilding(2, "敵の格納庫", 12, 12, 1, false), new SetBuildingPosition(2, 400, 0));
    }

    [Fact]
    public void Stats_FollowArchetypeGenerationAndQuality()
    {
        var s = NewSim(1); var c = s.Combat;
        Do(s, new CreateMech(1, 2, "heavy", 70), new CreateMech(2, 2, "fast", 70), new CreateMech(3, 4, "general", 90));
        var h = s.Factory.Mechs[0]; var fa = s.Factory.Mechs[1]; var g4 = s.Factory.Mechs[2];
        Assert.True(MechStats.MaxHp(h, c) > MechStats.MaxHp(fa, c) * 2.5);
        Assert.True(MechStats.Speed(fa, c) > MechStats.Speed(h, c) * 2.5);
        Assert.Equal(70 * 2 * 1.4, MechStats.Search(g4, c) / (90 / 70.0), 6);   // 索敵 = 頭 × 2 m × 世代倍率
        Assert.True(MechStats.MaxHp(g4, c) > MechStats.MaxHp(fa, c));
    }

    [Fact]
    public void Squad_FormDeployAttack_DestroysEnemy_AndLeavesLoot()
    {
        var s = NewSim(7); Bases(s);
        for (int i = 1; i <= 4; i++) s.Enqueue(new CreateMech(i, 2, "general", 75));
        for (int i = 11; i <= 12; i++) s.Enqueue(new CreateMech(i, 1, "general", 60));
        s.Step();
        Do(s, new FormSquad(1, 1, "第 1 小隊", new[] { 1, 2, 3, 4 }, 1), new FormSquad(2, 2, "敵", new[] { 11, 12 }, 2));
        Assert.Equal(SquadState.InHangar, s.Factory.Squads[0].State);
        Do(s, new Deploy(1, 10, 0), new Deploy(2, 200, 0), new OrderSquad(1, SquadOrder.Attack, TargetSquad: 2), new OrderSquad(2, SquadOrder.Hold));
        s.Run(60 * 120);   // 2 分
        var enemy = s.Factory.Squads[1]; var mine = s.Factory.Squads[0];
        Assert.Equal(0, enemy.Alive);
        Assert.True(mine.Alive >= 3, $"alive {mine.Alive}");
        Assert.Contains(s.Factory.Events, e => e.Kind == "hit"); Assert.Contains(s.Factory.Events, e => e.Kind == "destroyed");
        Assert.Equal(2, s.Factory.Loot.Count); Assert.All(s.Factory.Loot, l => Assert.Equal(3, l.Parts.Count));   // 10 部品 × 3 割
    }

    [Fact]
    public void Beam_ConsumesEnergy_AndIdleReactorMechChargesBatteryMech()
    {
        var s = NewSim(3); Bases(s);
        Do(s, new CreateMech(1, 2, "general", 70, "battery"), new CreateMech(2, 2, "general", 70, "reactor"), new CreateMech(11, 1, "heavy", 60));
        Do(s, new FormSquad(1, 1, "a", new[] { 1, 2 }, 1), new FormSquad(2, 2, "e", new[] { 11 }, 2));
        var bat = s.Factory.Squads[0].Members[0]; var rea = s.Factory.Squads[0].Members[1];
        Do(s, new Deploy(1, 0, 0), new Deploy(2, 300, 0));   // 敵は索敵外(300 m)
        bat.Energy = 0; s.Run(60);
        Assert.True(bat.Energy > 0, "屋外で待機中のリアクター機がバッテリー機に給電する");
        // 敵を接近させて撃つ: ビームは電力を消費
        Do(s, new Deploy(2, 60, 0), new OrderSquad(1, SquadOrder.Attack, TargetSquad: 2));
        bool dipped = false; for (int i = 0; i < 120; i++) { s.Step(); dipped |= rea.Energy < rea.EnergyCap; }
        Assert.True(s.Factory.Events.Any(e => e.Kind is "hit" or "miss"));
        Assert.True(dipped, "撃つと電力が減る(リアクターで回復するが、撃った直後は上限より下)");
    }

    [Fact]
    public void HangarMechs_SupplyBuildingPower()
    {
        var s = NewSim(4); Bases(s);
        Do(s, new PlaceStructure(1, 0, "furnace", 0, 0, 0, Direction.East), new SetRecipe(1, 0, 1, "iron_plate"), new AssignStaff(1, 0, 1, new Worker(1, "a", 3)));
        ((Machine)s.Factory.Get(1)!.Find(0, 1)!).Inputs.Add(new ItemStack("iron_ore", 50, 4));
        s.Step(); Assert.Equal(0, s.Factory.Get(1)!.LastSupply);
        Do(s, new CreateMech(1, 3, "general", 70), new FormSquad(1, 1, "a", new[] { 1 }, 1));
        s.Step(); s.Step();
        Assert.True(s.Factory.Get(1)!.LastSupply > 0, "格納庫で待機中の機体のリアクターが建物に給電する");
        Do(s, new Deploy(1, 5, 5)); s.Step(); s.Step();
        Assert.Equal(0, s.Factory.Get(1)!.LastSupply);
    }

    [Fact]
    public void Stealth_G6_OnlyVisualRange_AndMissilesMiss_UnlessBarrage()
    {
        var s = NewSim(5); Bases(s);
        Do(s, new CreateMech(1, 2, "general", 70), new CreateMech(11, 6, "fast", 99));
        Do(s, new FormSquad(1, 1, "a", new[] { 1 }, 1), new FormSquad(2, 2, "s", new[] { 11 }, 2));
        Do(s, new Deploy(1, 0, 0), new Deploy(2, 100, 0), new OrderSquad(1, SquadOrder.Hold), new OrderSquad(2, SquadOrder.Hold));
        s.Run(10);
        Assert.Equal(-1, s.Factory.Squads[0].Members[0].TargetMech);   // 索敵 140 m でも第 6 世代は見えない(目視 50 m のみ)
        Do(s, new Deploy(2, 40, 0)); s.Run(10);
        Assert.Equal(11, s.Factory.Squads[0].Members[0].TargetMech);
        // 重装 1 機のバズーカ(誘導弾)は第 6 世代にほぼ当たらない
        var s2 = NewSim(6); Bases(s2);
        Do(s2, new CreateMech(1, 3, "heavy", 70), new CreateMech(11, 6, "heavy", 99));
        Do(s2, new FormSquad(1, 1, "h", new[] { 1 }, 1), new FormSquad(2, 2, "s", new[] { 11 }, 2));
        Do(s2, new Deploy(1, 0, 0), new Deploy(2, 30, 0), new OrderSquad(1, SquadOrder.Attack, TargetSquad: 2), new OrderSquad(2, SquadOrder.Hold));
        s2.Run(60 * 60);
        int shots = s2.Factory.Events.Count(e => e.Kind is "hit" or "miss" && e.Building == 1); int hits = s2.Factory.Events.Count(e => e.Kind == "hit" && e.Building == 1);
        Assert.True(shots >= 10 && hits <= shots * 0.2, $"shots {shots} hits {hits}");   // 敵の反撃で撃破されるまで約 11 発
    }

    [Fact]
    public void Board_RetreatsOnLowHp_AndSelfDestructDamagesNearby()
    {
        var s = NewSim(8); Bases(s);
        Do(s, new CreateMech(1, 2, "general", 70), new CreateMech(11, 2, "heavy", 70));
        // 制御基板: 耐久 60% 未満で退却
        var prog = new Program { Interval = 6, Nodes = { new McuNode { Id = 1, Type = "in.mech.hp" }, new McuNode { Id = 2, Type = "in.const", Params = { ["value"] = 60 } }, new McuNode { Id = 3, Type = "op.lt" }, new McuNode { Id = 4, Type = "out.mech.retreat" } }, Links = { new(1, 0, 3, 0), new(2, 0, 3, 1), new(3, 0, 4, 0) } };
        Do(s, new SetMechProgram(1, prog), new FormSquad(1, 1, "a", new[] { 1 }, 1), new FormSquad(2, 2, "e", new[] { 11 }, 2));
        Do(s, new Deploy(1, 100, 0), new Deploy(2, 150, 0), new OrderSquad(1, SquadOrder.Attack, TargetSquad: 2), new OrderSquad(2, SquadOrder.Attack, TargetSquad: 1));   // 格納庫(0,0)から 100 m 離れた所で交戦
        var u = s.Factory.Squads[0].Members[0];
        for (int i = 0; i < 60 * 60 && u.Alive && u.Retreat == 0; i++) s.Step();
        Assert.True(u.Alive && u.Retreat == 1 && u.Hp < u.MaxHp * 0.6, $"alive {u.Alive} retreat {u.Retreat} hp {u.Hp}/{u.MaxHp}");
        s.Run(60 * 30);   // 退却して格納庫に戻ると待機になり、弾薬が補給される
        Assert.Equal(SquadState.InHangar, s.Factory.Squads[0].State); Assert.True(u.Alive);
        // 自爆: 第 3 世代・品質 100(800 kW × 150% = 1,200 kW → 半径 24 セル)。隣の敵 2 機に被害
        var s3 = NewSim(9); Bases(s3);
        Do(s3, new CreateMech(1, 3, "general", 100), new CreateMech(11, 1, "general", 60), new CreateMech(12, 1, "general", 60));
        Do(s3, new FormSquad(1, 1, "a", new[] { 1 }, 1), new FormSquad(2, 2, "e", new[] { 11, 12 }, 2));
        Do(s3, new Deploy(1, 0, 0), new Deploy(2, 10, 0), new OrderSquad(1, SquadOrder.SelfDestruct));
        s3.Run(3);
        Assert.Equal(0, s3.Factory.Squads[0].Alive);
        Assert.Contains(s3.Factory.Events, e => e.Kind == "selfdestruct" && e.Power == 1200 && e.Destroyed == 2);
        Assert.Equal(0, s3.Factory.Squads[1].Alive);   // 1,200 × 5 = 6,000 の被害 > 第 1 世代の耐久
    }

    [Fact]
    public void Combat_IsDeterministic_ThroughSaveAndReplay()
    {
        var s = NewSim(10); Bases(s);
        for (int i = 1; i <= 3; i++) s.Enqueue(new CreateMech(i, 2, "fast", 70)); for (int i = 11; i <= 13; i++) s.Enqueue(new CreateMech(i, 2, "general", 70)); s.Step();
        Do(s, new FormSquad(1, 1, "a", new[] { 1, 2, 3 }, 1), new FormSquad(2, 2, "e", new[] { 11, 12, 13 }, 2), new Deploy(1, 0, 0), new Deploy(2, 150, 0), new OrderSquad(1, SquadOrder.Attack, TargetSquad: 2), new OrderSquad(2, SquadOrder.Attack, TargetSquad: 1));
        s.Run(1500);
        var replayed = Save.Replay(10, s.Log, s.Tick, Blocks(), Data, creative: true);
        Assert.Equal(Save.StateHash(s), Save.StateHash(replayed));
        var loaded = Save.Load(Save.Snapshot(s), Blocks(), Data); loaded.Factory.Creative = true;
        Assert.Equal(Save.StateHash(s), Save.StateHash(loaded));
        s.Run(300); loaded.Run(300);
        Assert.Equal(Save.StateHash(s), Save.StateHash(loaded));
    }
}
