using System.Diagnostics;
using GPS.Sim;
using Xunit;
using Xunit.Abstractions;

/// <summary>軍の粒度(G21): 隊長と司令官、軍の命令、集約の式、注視範囲の切り替え、計測。</summary>
public class ArmyTests
{
    readonly ITestOutputHelper _out;
    public ArmyTests(ITestOutputHelper o) => _out = o;
    static readonly GameData Data = GameData.Load(GameData.FindDataDir());
    static BlockRegistry Blocks() => BlockRegistry.FromJson(File.ReadAllText(Path.Combine(GameData.FindDataDir(), "blocks.json")));
    static void Do(Simulation s, params ICommand[] cmds) { foreach (var c in cmds) s.Enqueue(c); s.Step(); }

    /// <summary>町(段階 2)。味方 n 隊 × 12 機(第 3 世代 汎用)と敵 n 隊(第 2 世代 汎用、AI 勢力 = 指揮官不要)。</summary>
    static Simulation Field(ulong seed, int squadsPerSide, bool withCity = true)
    {
        var s = new Simulation(Blocks(), Data, seed); s.Factory.Creative = true;
        if (withCity) { Do(s, new FoundCity("a", 200_000, 60)); s.Factory.City!.Stage = 2; s.Factory.City.ConscriptionRate = 0.5; }
        Do(s, new CreateBuilding(1, "a", 12, 12, 1, false), new SetBuildingPosition(1, 0, 0), new CreateBuilding(2, "b", 12, 12, 1, false), new SetBuildingPosition(2, 1000, 0));
        int mid = 1, sid = 1;
        for (int side = 0; side < 2; side++)
            for (int q = 0; q < squadsPerSide; q++)
            {
                var ids = new List<int>(); for (int m = 0; m < 12; m++) { s.Enqueue(new CreateMech(mid, side == 0 ? 3 : 2, "general", 75)); ids.Add(mid++); }
                s.Enqueue(new FormSquad(sid, side == 0 ? 1 : 102, $"s{sid}", ids, side == 0 ? 1 : 2)); sid++;
            }
        s.Step();
        if (withCity) { var city = s.Factory.City!; for (int q = 1; q <= squadsPerSide; q++) { city.Persons.Add(new Person { Id = 900 + q, Name = $"隊長 {q}", Command = 5, Loyalty = 5, Ambition = 5 }); s.Enqueue(new AssignCommander("squad", q, 900 + q)); } city.NextPersonId = 2000; s.Step(); CitySim.Hour(s, city); }
        return s;
    }
    static void DeployAll(Simulation s, int squadsPerSide, double gap)
    {
        int sid = 1;
        for (int side = 0; side < 2; side++) for (int q = 0; q < squadsPerSide; q++) { s.Enqueue(new Deploy(sid, side == 0 ? 100 : 100 + gap, q * 40)); s.Enqueue(new OrderSquad(sid, SquadOrder.Attack, TargetSquad: side == 0 ? sid + squadsPerSide : sid - squadsPerSide)); sid++; }
        s.Step();
    }
    static int AliveSide(Simulation s, int faction) => s.Factory.Squads.Where(q => q.Faction == faction).Sum(q => q.Alive);

    [Fact]
    public void Commander_RequiredToDeploy_AndImprovesHit()
    {
        var s = Field(1, 1); var f = s.Factory; var sq = f.Squads[0];
        Do(s, new DismissCommander("squad", 1)); Assert.Equal(-1, sq.CommanderId); Assert.Null(f.City!.Persons.First(p => p.Id == 901).Office);
        Do(s, new Deploy(1, 100, 0)); Assert.Equal(SquadState.InHangar, sq.State); Assert.Contains(f.Events, e => e.Kind == "no_commander");
        f.City.Persons.First(p => p.Id == 901).Command = 10; Do(s, new AssignCommander("squad", 1, 901)); Assert.Equal("squad:1", f.City.Persons.First(p => p.Id == 901).Office);
        Assert.Equal(1.1, Armies.CommandMul(f, sq), 6);
        Do(s, new Deploy(1, 100, 0)); Assert.Equal(SquadState.Field, sq.State);
        // 軍の司令官 +1% / 点
        f.City.Persons.Add(new Person { Id = 950, Name = "司令官", Command = 10, Loyalty = 5, Ambition = 5 });
        Do(s, new FormArmy(1, "第 1 軍", new[] { 1 }), new AssignCommander("army", 1, 950)); Assert.Equal(1.1 * 1.05, Armies.CommandMul(f, sq), 6);
        // 人物は役職と掛け持ちできない
        Do(s, new Appoint("head", 950)); Assert.False(PoliticsSim.Of(f).Offices.ContainsKey("head"));
    }

    [Fact]
    public void Army_OrdersSpreadToSquads_AndNeedCommander()
    {
        var s = Field(2, 3); var f = s.Factory;
        Do(s, new FormArmy(1, "第 1 軍", new[] { 1, 2, 3 }));
        Do(s, new DeployArmy(1, 100, 0)); Assert.All(f.Squads.Where(q => q.Faction == 1), q => Assert.Equal(SquadState.InHangar, q.State)); Assert.Contains(f.Events, e => e.Kind == "no_commander" && e.Destroyed == 1);
        f.City!.Persons.Add(new Person { Id = 950, Name = "司令官", Command = 7, Loyalty = 5, Ambition = 5 });
        Do(s, new AssignCommander("army", 1, 950), new DeployArmy(1, 100, 0));
        Assert.All(f.Squads.Where(q => q.Faction == 1), q => Assert.Equal(SquadState.Field, q.State));
        Do(s, new OrderArmy(1, SquadOrder.Move, 300, 200, Formation: Formation.Line));
        var xs = f.Squads.Where(q => q.Faction == 1).Select(q => q.OrderX).OrderBy(x => x).ToList();
        Assert.Equal(3, xs.Count); Assert.Equal(30, xs[1] - xs[0], 6); Assert.All(f.Squads.Where(q => q.Faction == 1), q => Assert.Equal(SquadOrder.Move, q.Order));
        Do(s, new OrderArmy(1, SquadOrder.Attack, TargetSquad: 4)); Assert.All(f.Squads.Where(q => q.Faction == 1), q => Assert.Equal(4, q.TargetSquad));
        Do(s, new DisbandArmy(1)); Assert.Empty(f.Armies); Assert.Null(f.City.Persons.First(p => p.Id == 950).Office);
    }

    [Fact]
    public void Aggregated_LossesMatchDetailed_WithinTolerance()
    {
        // 同じ場面を 個機(注視範囲なし) と 集約(注視範囲を遠くに) で 90 秒解き、損耗が近いこと
        int d1, d2, a1, a2;
        { var s = Field(3, 4); DeployAll(s, 4, 100); s.Run(60 * 90); d1 = AliveSide(s, 1); d2 = AliveSide(s, 102); Assert.All(s.Factory.Squads, q => Assert.False(q.Aggregated)); }
        { var s = Field(3, 4); Do(s, new SetBattleFocus(5000, 5000, 100)); DeployAll(s, 4, 100); s.Run(60 * 90); a1 = AliveSide(s, 1); a2 = AliveSide(s, 102); Assert.All(s.Factory.Squads.Where(q => q.State == SquadState.Field), q => Assert.True(q.Aggregated)); }
        _out.WriteLine($"detailed: mine {d1} enemy {d2} / aggregated: mine {a1} enemy {a2} (of 48 each)");
        Assert.True(d2 < 48 && a2 < 48, "どちらも敵を減らす");
        Assert.True(Math.Abs(d2 - a2) <= 14 && Math.Abs(d1 - a1) <= 14, $"損耗の差が大きい: {d1}/{d2} vs {a1}/{a2}");
    }

    [Fact]
    public void Focus_SwitchesPerSquad_AndRecordsContinue()
    {
        var s = Field(4, 2); var f = s.Factory;
        Do(s, new SetBattleFocus(100, 0, 30)); DeployAll(s, 2, 100);   // 味方の隊 1(y 0)は注視の中、隊 2(y 40)は境界の外
        s.Run(10);
        Assert.False(f.Squads[0].Aggregated); Assert.True(f.Squads[1].Aggregated);
        s.Run(60 * 20); double hp = f.Squads[1].Members.Sum(u => u.Hp);
        Do(s, new SetBattleFocus(f.Squads[1].X, f.Squads[1].Y, 150)); s.Run(10);   // 隊 2 の今いる所を注視
        Assert.False(f.Squads[1].Aggregated); double hp2 = f.Squads[1].Members.Sum(u => u.Hp); Assert.True(hp2 <= hp && hp2 > hp * 0.8, $"切り替えで記録が連続していない: {hp} → {hp2}");   // 10 ティック分の損耗だけ
        // (再生の一致は、この試験が段階と人物を命令なしで直接置いているので見ない。写しの読み込みでの一致を見る)
        var loaded = Save.Load(Save.Snapshot(s), Blocks(), Data); loaded.Factory.Creative = true; s.Run(300); loaded.Run(300); Assert.Equal(Save.StateHash(s), Save.StateHash(loaded));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    public void Aggregated_TickCost(int squadsPerSide)
    {
        var s = Field(5, squadsPerSide); Do(s, new SetBattleFocus(-5000, -5000, 100)); DeployAll(s, squadsPerSide, 100);
        s.Run(60);
        var sw = Stopwatch.StartNew(); s.Run(300); sw.Stop(); double perTick = sw.Elapsed.TotalMilliseconds / 300;
        _out.WriteLine($"aggregated mechs {squadsPerSide * 24} (squads {squadsPerSide * 2}): {perTick:F2} ms/tick, realtime x{1000.0 / 60 / perTick:F1}; events {s.Factory.Events.Count}");
        Assert.True(perTick < 1000.0 / 60, $"実時間に追いつかない: {perTick:F2} ms/tick");
    }
}
