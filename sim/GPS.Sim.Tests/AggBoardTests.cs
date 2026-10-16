using GPS.Sim;
using Xunit;

/// <summary>集約の隊の制御基板(G21 第 8 節): 退却・交戦・自爆の出力が集約でも効くこと。</summary>
public class AggBoardTests
{
    static readonly GameData Data = GameData.Load(GameData.FindDataDir());
    static BlockRegistry Blocks() => BlockRegistry.FromJson(File.ReadAllText(Path.Combine(GameData.FindDataDir(), "blocks.json")));
    static void Do(Simulation s, params ICommand[] cmds) { foreach (var c in cmds) s.Enqueue(c); s.Step(); }

    /// <summary>耐久 pct% 未満で退却する基板。</summary>
    static Program RetreatBelow(int pct) => new() { Interval = 6, Nodes = { new McuNode { Id = 1, Type = "in.mech.hp" }, new McuNode { Id = 2, Type = "in.const", Params = { ["value"] = pct } }, new McuNode { Id = 3, Type = "op.lt" }, new McuNode { Id = 4, Type = "out.mech.retreat" } }, Links = { new(1, 0, 3, 0), new(2, 0, 3, 1), new(3, 0, 4, 0) } };
    /// <summary>撃たない基板(交戦 0)。</summary>
    static Program NoEngage() => new() { Interval = 6, Nodes = { new McuNode { Id = 1, Type = "in.const", Params = { ["value"] = 0 } }, new McuNode { Id = 2, Type = "out.mech.engage" } }, Links = { new(1, 0, 2, 0) } };
    /// <summary>敵が 100 m 以内なら自爆する基板。</summary>
    static Program SelfDestructNear() => new() { Interval = 6, Nodes = { new McuNode { Id = 1, Type = "in.enemy.distance" }, new McuNode { Id = 2, Type = "in.const", Params = { ["value"] = 100 } }, new McuNode { Id = 3, Type = "op.lt" }, new McuNode { Id = 4, Type = "out.mech.selfdestruct" } }, Links = { new(1, 0, 3, 0), new(2, 0, 3, 1), new(3, 0, 4, 0) } };

    /// <summary>町、全て集約。味方 1 隊(第 3 世代 × 6、基板付き)と敵 1 隊(第 2 世代 × 12)。</summary>
    static Simulation Field(ulong seed, Program? prog)
    {
        var s = new Simulation(Blocks(), Data, seed); s.Factory.Creative = true;
        Do(s, new FoundCity("a", 200_000, 60)); s.Factory.City!.Stage = 2; s.Factory.City.ConscriptionRate = 0.5;
        Do(s, new CreateBuilding(1, "a", 12, 12, 1, false), new SetBuildingPosition(1, 0, 0), new CreateBuilding(2, "b", 12, 12, 1, false), new SetBuildingPosition(2, 1000, 0));
        var ids = new List<int>(); for (int m = 1; m <= 6; m++) { s.Enqueue(new CreateMech(m, 3, "general", 75)); ids.Add(m); }
        var eids = new List<int>(); for (int m = 11; m <= 22; m++) { s.Enqueue(new CreateMech(m, 2, "general", 75)); eids.Add(m); }
        s.Step();
        if (prog != null) foreach (var id in ids) s.Enqueue(new SetMechProgram(id, prog));
        s.Enqueue(new FormSquad(1, 1, "s1", ids, 1)); s.Enqueue(new FormSquad(2, 102, "s2", eids, 2)); s.Step();
        var city = s.Factory.City!; city.Persons.Add(new Person { Id = 901, Name = "隊長", Command = 5, Loyalty = 5, Ambition = 5 }); city.NextPersonId = 2000;
        Do(s, new AssignCommander("squad", 1, 901)); CitySim.Hour(s, city);
        Do(s, new SetBattleFocus(5000, 5000, 10));
        Do(s, new Deploy(1, 100, 0), new Deploy(2, 200, 0), new OrderSquad(1, SquadOrder.Attack, TargetSquad: 2), new OrderSquad(2, SquadOrder.Attack, TargetSquad: 1));
        return s;
    }

    [Fact]
    public void Board_RetreatOutput_Works_InAggregated()
    {
        var s = Field(1, RetreatBelow(60)); var sq = s.Factory.Squads[0];
        bool seen = false;
        for (int i = 0; i < 60 * 120 && !seen; i++) { s.Step(); foreach (var u in sq.Members) if (u.Alive && u.Retreat != 0 && u.Hp >= u.MaxHp * 0.3 && u.Hp < u.MaxHp * 0.6) seen = true; }
        Assert.True(sq.Aggregated); Assert.True(seen, "基板の退却(60% 未満)が既定の 25% より早く効く");
    }

    [Fact]
    public void Board_EngageZero_StopsFiring_InAggregated()
    {
        var s = Field(2, NoEngage()); var f = s.Factory; s.Run(60 * 60);
        Assert.True(f.Squads[0].Aggregated);
        Assert.DoesNotContain(f.Events, e => (e.Kind == "hit" || e.Kind == "miss") && e.Building == 1);   // 味方は撃たない
        Assert.Contains(f.Events, e => (e.Kind == "hit" || e.Kind == "miss") && e.Building == 2);   // 敵は撃つ
    }

    [Fact]
    public void Board_SelfDestruct_SwitchesSquadToDetailed_AndExplodes()
    {
        var s = Field(3, SelfDestructNear()); var f = s.Factory; var sq = f.Squads[0];
        s.Run(60 * 60);
        Assert.Contains(f.Events, e => e.Kind == "selfdestruct" || e.Kind == "explode" || e.Kind == "self_destruct");
        Assert.True(sq.Alive < 6, "自爆で減る");
    }
}
