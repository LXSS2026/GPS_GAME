using GPS.Sim;
using Xunit;

/// <summary>士気・練度(G21 第 6 節): 損耗で下がり撃破で上がる、崩れると帰還、格納庫で回復、命中の倍率の範囲。</summary>
public class MoraleTests
{
    static readonly GameData Data = GameData.Load(GameData.FindDataDir());
    static BlockRegistry Blocks() => BlockRegistry.FromJson(File.ReadAllText(Path.Combine(GameData.FindDataDir(), "blocks.json")));
    static void Do(Simulation s, params ICommand[] cmds) { foreach (var c in cmds) s.Enqueue(c); s.Step(); }

    /// <summary>町。味方 1 隊(第 3 世代 × 12)と敵 1 隊(第 2 世代 × 12、AI 勢力)。</summary>
    static Simulation Field(ulong seed)
    {
        var s = new Simulation(Blocks(), Data, seed); s.Factory.Creative = true;
        Do(s, new FoundCity("a", 200_000, 60)); s.Factory.City!.Stage = 2; s.Factory.City.ConscriptionRate = 0.5;
        Do(s, new CreateBuilding(1, "a", 12, 12, 1, false), new SetBuildingPosition(1, 0, 0), new CreateBuilding(2, "b", 12, 12, 1, false), new SetBuildingPosition(2, 1000, 0));
        int mid = 1;
        for (int side = 0; side < 2; side++)
        {
            var ids = new List<int>(); for (int m = 0; m < 12; m++) { s.Enqueue(new CreateMech(mid, side == 0 ? 3 : 2, "general", 75)); ids.Add(mid++); }
            s.Enqueue(new FormSquad(side + 1, side == 0 ? 1 : 102, $"s{side + 1}", ids, side + 1));
        }
        s.Step();
        var city = s.Factory.City!; city.Persons.Add(new Person { Id = 901, Name = "隊長", Command = 5, Loyalty = 5, Ambition = 5 }); city.NextPersonId = 2000;
        Do(s, new AssignCommander("squad", 1, 901)); CitySim.Hour(s, city);
        return s;
    }

    [Fact]
    public void Start_FromData_AndHitMultiplierRange()
    {
        var s = Field(1); var c = s.Combat; var sq = s.Factory.Squads[0];
        Assert.Equal(c.MoraleStart, sq.Morale); Assert.Equal(0, sq.Exp);
        sq.Morale = 0; sq.Exp = 0; Assert.Equal(c.MoraleHitAt0, Combat.MoraleMul(sq, c), 6);
        sq.Morale = 100; sq.Exp = 100; Assert.Equal(c.MoraleHitAt100 * (1 + c.ExpHitBonus), Combat.MoraleMul(sq, c), 6);
        sq.Morale = 0; Assert.Equal(s.Factory.RetreatHp + c.MoraleRetreatExtra, Combat.RetreatFrac(s.Factory, sq, c), 6);
        sq.Morale = 50; Assert.Equal(s.Factory.RetreatHp, Combat.RetreatFrac(s.Factory, sq, c), 6);
        sq.Morale = 100; Assert.Equal(s.Factory.RetreatHp, Combat.RetreatFrac(s.Factory, sq, c), 6);
    }

    [Fact]
    public void Losses_LowerMorale_Kills_RaiseMoraleAndExp()
    {
        var s = Field(2); var f = s.Factory;
        Do(s, new Deploy(1, 100, 0), new Deploy(2, 200, 0), new OrderSquad(1, SquadOrder.Attack, TargetSquad: 2), new OrderSquad(2, SquadOrder.Attack, TargetSquad: 1));
        s.Run(60 * 90);
        var mine = f.Squads[0]; var enemy = f.Squads[1];
        Assert.True(enemy.Alive < 12, "敵が減る場面");
        Assert.True(enemy.Morale < s.Combat.MoraleStart, $"損耗で下がる: {enemy.Morale}");
        Assert.True(mine.Morale > s.Combat.MoraleStart || mine.Alive < 12, $"撃破で上がる: {mine.Morale}");
        Assert.True(mine.Exp > 0, $"撃破と交戦で練度: {mine.Exp}");
    }

    [Fact]
    public void BelowBreak_SquadRetreats()
    {
        var s = Field(3); var f = s.Factory; var enemy = f.Squads[1]; enemy.Morale = s.Combat.MoraleBreakBelow + 2;
        Do(s, new Deploy(1, 100, 0), new Deploy(2, 200, 0), new OrderSquad(1, SquadOrder.Attack, TargetSquad: 2), new OrderSquad(2, SquadOrder.Attack, TargetSquad: 1));
        s.Run(60 * 120);
        Assert.Contains(f.Events, e => e.Kind == "broken" && e.Building == 2);
        Assert.True(enemy.Order == SquadOrder.Retreat || enemy.State == SquadState.InHangar || enemy.Alive == 0);
    }

    [Fact]
    public void Hangar_RecoversPerHour_Commander_Adds()
    {
        var s = Field(4); var f = s.Factory; var c = s.Combat; var sq = f.Squads[0]; sq.Morale = 50;
        Combat.MoraleHour(f, c); Assert.Equal(50 + c.MoraleHangarRecover, sq.Morale, 6);   // 隊長の指揮 5 = ±0
        f.City!.Persons.First(p => p.Id == 901).Command = 10; Combat.MoraleHour(f, c);
        Assert.Equal(50 + 2 * c.MoraleHangarRecover + 5 * c.MoraleCommanderPerPoint, sq.Morale, 6);
        // 写しに残る
        var loaded = Save.Load(Save.Snapshot(s), Blocks(), Data); Assert.Equal(sq.Morale, loaded.Factory.Squads[0].Morale, 6);
    }
}
