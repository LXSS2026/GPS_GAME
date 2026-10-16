using GPS.Sim;
using Xunit;

public class DiplomacyTests
{
    static readonly GameData Data = GameData.Load(GameData.FindDataDir());
    static BlockRegistry Blocks() => BlockRegistry.FromJson(File.ReadAllText(Path.Combine(GameData.FindDataDir(), "blocks.json")));
    static Simulation NewSim(ulong seed) => new(Blocks(), Data, seed);
    static void Do(Simulation s, params ICommand[] cmds) { foreach (var c in cmds) s.Enqueue(c); s.Step(); }
    static void Hours(Simulation s, int n) { for (int i = 0; i < n; i++) { CitySim.RefreshRoads(s); CitySim.Hour(s, s.Factory.City!); } }
    static void Days(Simulation s, int n) { for (int i = 0; i < n; i++) { Hours(s, 24); CitySim.Day(s, s.Factory.City!); } }

    /// <summary>村(拠点 = 格納庫 10)と AI 勢力 3 つ(拠点は東に 200 m)。</summary>
    static Simulation World(ulong seed, int myMechs = 4, int gen = 2)
    {
        var s = NewSim(seed); s.Factory.Creative = true;
        byte road = s.Blocks.IdOf("RoadX"); for (int x = -2; x < 400; x++) s.World.Set(x, 0, -1, road);
        var cmds = new List<ICommand> { new FoundCity("アカネ", 60, 60),
            new CreateBuilding(1, "住居", 20, 20, 2, false), new SetBuildingPosition(1, 0, 0), new SetBuildingUse(1, "residence"),
            new CreateBuilding(3, "畑", 30, 30, 1, false), new SetBuildingPosition(3, 24, 0), new SetBuildingUse(3, "farm"),
            new CreateBuilding(4, "井戸", 10, 10, 1, false), new SetBuildingPosition(4, 60, 0), new SetBuildingUse(4, "well"), new PlaceStructure(4, 0, "reactor_g0", 0, 0, 0, Direction.East),
            new CreateBuilding(10, "格納庫", 12, 12, 3, false), new SetBuildingPosition(10, 80, 0), new SetCapital(10),
            new CreateBuilding(40, "ミナト", 12, 12, 3, false), new SetBuildingPosition(40, 300, 0), new CreateBuilding(41, "クロガネ", 12, 12, 3, false), new SetBuildingPosition(41, 300, 60), new CreateBuilding(42, "山賊", 12, 12, 3, false), new SetBuildingPosition(42, 300, 120),
            new AddPower(1, 40), new AddPower(2, 41), new AddPower(3, 42) };
        for (int i = 1; i <= myMechs; i++) cmds.Add(new CreateMech(i, gen, "general", 75));
        if (myMechs > 0) cmds.Add(new FormSquad(1, 1, "第 1 小隊", Enumerable.Range(1, myMechs).ToArray(), 10));
        foreach (var c in cmds) s.Enqueue(c); s.Step(); s.Step();
        return s;
    }
    static Power P(Simulation s, int id) => s.Factory.Diplomacy!.Powers.First(p => p.Id == id);

    [Fact]
    public void Powers_StrengthRatio_StateAndProposalChance()
    {
        var s = World(1); var f = s.Factory;
        var minato = P(s, 1); var kurogane = P(s, 2); var bandits = P(s, 3);
        Assert.Equal("neutral", minato.State(s.DiplomacyData)); Assert.Equal("wary", kurogane.State(s.DiplomacyData)); Assert.Equal("hostile", bandits.State(s.DiplomacyData));
        Assert.True(DiplomacySim.PlayerStrength(f) > 0); Assert.True(DiplomacySim.Ratio(f, kurogane) > DiplomacySim.Ratio(f, minato), "好戦型は軍の比率が高い");
        double c0 = DiplomacySim.ProposalChance(s, minato, "trade"), c1 = DiplomacySim.ProposalChance(s, minato, "alliance");
        Assert.True(c0 > c1 && c0 >= 30);
        // 交渉役を置くと上がる
        f.City!.Persons.Add(new Person { Id = 1, Name = "n", Negotiate = 10, Loyalty = 5, Ambition = 5 }); f.City.NextPersonId = 2; Do(s, new Appoint("negotiate", 1));
        Assert.Equal(c0 + 30, DiplomacySim.ProposalChance(s, minato, "trade"), 6);
        Assert.Equal(1, f.Buildings.Count(b => b.Id == 10 && b.MaxHp == 12 * 12 * 3 * 400));
    }

    [Fact]
    public void Gift_Propose_Treaty_TradeBonus_AndBreak()
    {
        var s = World(2); var f = s.Factory; var minato = P(s, 1); f.Treasury = 10_000;
        Do(s, new Gift(1)); Assert.Equal(30, minato.Relation); Assert.Equal(10_000 - 120, f.Treasury);
        Do(s, new Gift(1)); Assert.Equal(30, minato.Relation);   // 月 1 回
        minato.Relation = 100; f.City!.Persons.Add(new Person { Id = 1, Name = "n", Negotiate = 10, Loyalty = 5, Ambition = 5 }); f.City.NextPersonId = 2; Do(s, new Appoint("negotiate", 1));
        Assert.Equal(95, DiplomacySim.ProposalChance(s, minato, "trade"));
        int tries = 0; while (!minato.Treaties.Contains("trade") && tries++ < 10) { Do(s, new Propose(1, "trade")); f.City.Days += 31; }
        Assert.Contains("trade", minato.Treaties); Assert.Contains(f.Events, e => e.Kind == "treaty");
        Days(s, 1); Assert.Equal(10, f.TradeBonusPct);
        Do(s, new BreakTreaty(1, "trade")); Assert.DoesNotContain("trade", minato.Treaties); Days(s, 1); Assert.Equal(0, f.TradeBonusPct);
        // 不可侵の破棄は全勢力の関係を下げる
        minato.Treaties.Add("nonaggression"); int k = P(s, 2).Relation; Do(s, new BreakTreaty(1, "nonaggression")); Assert.Equal(k - 20, P(s, 2).Relation);
    }

    [Fact]
    public void Threaten_StrongPlayer_GetsTribute_AndMonthlyPayment()
    {
        var s = World(3, myMechs: 8, gen: 4); var f = s.Factory; var bandits = P(s, 3);
        Assert.True(DiplomacySim.Ratio(f, bandits) < 1 / 1.5, $"ratio {DiplomacySim.Ratio(f, bandits)}");
        Do(s, new Threaten(3)); Assert.True(bandits.TributeFromThem > 0); Assert.Equal(12, bandits.TributeMonths);
        long t0 = f.Treasury; f.City!.Days = 29; Days(s, 1);   // 30 日目 = 月次
        Assert.True(f.Treasury > t0, "貢納が国庫に入る"); Assert.Equal(11, bandits.TributeMonths);
        // 弱い相手には失敗して関係 −20
        var s2 = World(4, myMechs: 1, gen: 1); var k = P(s2, 2); int r = k.Relation; Do(s2, new Threaten(2)); Assert.Equal(r - 20, k.Relation); Assert.Equal(0, k.TributeFromThem);
    }

    [Fact]
    public void Demand_RefuseLowersRelation_AcceptSetsTribute()
    {
        var s = World(5); var f = s.Factory; var k = P(s, 2);
        k.Offers.Add(new Offer { Id = 1, Kind = "demand_tribute", DaysLeft = 7, Amount = 60 });
        int r = k.Relation; Do(s, new AnswerOffer(2, 1, false)); Assert.True(k.Relation <= r - 30 || k.AtWar);
        k.Offers.Add(new Offer { Id = 2, Kind = "demand_tribute", DaysLeft = 7, Amount = 60 }); k.AtWar = false;
        Do(s, new AnswerOffer(2, 2, true)); Assert.Equal(60, k.TributeToMe); Assert.Equal(12, k.TributeMonths);
        // 期限切れは拒否と同じ
        k.Offers.Add(new Offer { Id = 3, Kind = "demand_trade", DaysLeft = 1 }); r = k.Relation; Days(s, 1); Assert.Empty(k.Offers.Where(o => o.Id == 3)); Assert.True(k.Relation <= r - 30 || k.AtWar);
        // AI の提案を受ける
        var m = P(s, 1); m.Offers.Add(new Offer { Id = 9, Kind = "propose_nonaggression", DaysLeft = 7 }); Do(s, new AnswerOffer(1, 9, true)); Assert.Contains("nonaggression", m.Treaties);
        Do(s, new DeclareWar(1)); Assert.False(m.AtWar);   // 不可侵中は宣戦できない
    }

    [Fact]
    public void War_AiSpawnsSquad_AttacksCapital_AndConquersWeakPlayer()
    {
        var s = World(6, myMechs: 0); var f = s.Factory; var k = P(s, 2);
        Do(s, new DeclareWar(2)); Assert.True(k.AtWar); Assert.Contains(f.Events, e => e.Kind == "war_declared");
        Days(s, 1);
        var sq = f.Squads.FirstOrDefault(x => x.Faction == k.Faction); Assert.NotNull(sq); Assert.Equal(SquadState.Field, sq!.State); Assert.Equal(10, sq.TargetBuilding);
        var capital = f.Get(10)!; double hp0 = capital.Hp;
        s.Run(60 * 150);   // 2.5 分: 220 m を移動して砲撃
        Assert.True(capital.Hp < hp0, $"hp {capital.Hp}/{capital.MaxHp}"); Assert.Contains(f.Events, e => e.Kind == "hit_building");
        for (int i = 0; i < 8 && !capital.Destroyed; i++) s.Run(Clock.TicksPerHour);   // 弾切れで帰還 → 補給 → 1 時間ごとに再出撃(3 往復ほど)
        Assert.Contains(f.Events, e => e.Kind == "no_ammo");
        Assert.True(capital.Destroyed); Assert.Equal("conquered", f.Diplomacy!.Outcome); Assert.True(f.City!.Defeated); Assert.False(k.AtWar);
    }

    [Fact]
    public void PlayerConquers_PowerBase_BecomesVassal_WithScrap_AndVictory()
    {
        var s = World(7, myMechs: 6, gen: 3); var f = s.Factory; var m = P(s, 1);
        f.Diplomacy!.Powers.RemoveAll(p => p.Id != 1);   // 勢力はミナトだけ(制覇の判定用)
        double scrap = m.Scrap; Assert.True(scrap > 0);
        f.City!.Population = 200; f.City.ConscriptionRate = 0.5; Hours(s, 1);   // 隊の要員(6 機 × 3 人)を徴兵で満たす
        f.City.Persons.Add(new Person { Id = 800, Name = "隊長", Command = 5, Loyalty = 5, Ambition = 5 }); f.City.NextPersonId = 801; Do(s, new AssignCommander("squad", 1, 800));   // 隊長(G21)
        Do(s, new DeclareWar(1), new Deploy(1, 90, 0), new OrderSquad(1, SquadOrder.Attack, TargetBuilding: 40));
        Assert.Equal(SquadState.Field, f.Squads[0].State);
        for (int i = 0; i < 80 && !m.Vassal; i++) s.Run(60 * 30);   // 汎用機のビームは電力で撃ち続けられる
        Assert.True(m.Vassal, $"hp {f.Get(40)!.Hp}"); Assert.Equal(50, m.Relation); Assert.False(m.AtWar); Assert.Empty(f.Squads.Where(x => x.Faction == m.Faction));
        var home = f.Get(10)!; Assert.True(home.Cargo.Any(x => x.Item == "iron_plate") && home.Cargo.Any(x => x.Item == "gear"), "スクラップが帰還先の荷下ろし場に入る");
        Assert.Equal((int)Math.Round(scrap), home.Cargo.Sum(x => x.Count));
        Days(s, 1); Assert.Equal("conquest", f.Diplomacy.Outcome); Assert.Contains(f.Events, e => e.Kind == "victory" && e.Power == 1);
        Assert.Equal(0, DiplomacySim.PowerStrength(f, m));
    }

    [Fact]
    public void Peace_AiOffersAfterLongWar_AndAllianceReinforcement()
    {
        var s = World(8, myMechs: 4); var f = s.Factory; var k = P(s, 2); var m = P(s, 1);
        m.Treaties.Add("alliance");
        DiplomacySim.DeclareWar(s, k, byPlayer: false);
        Assert.Contains(f.Squads, x => x.Faction == 1 && x.Name.Contains("援軍"));   // 同盟国の援軍
        k.Strength = 0;   // 相手は隊を出せない
        f.City!.Days = 0; for (int d = 0; d < 91; d++) { k.WarDays = d; CitySim.Day(s, f.City); if (k.Offers.Any(o => o.Kind == "peace")) break; }
        var offer = k.Offers.FirstOrDefault(o => o.Kind == "peace"); Assert.NotNull(offer);
        Do(s, new AnswerOffer(2, offer!.Id, true)); Assert.False(k.AtWar); Assert.Contains(f.Events, e => e.Kind == "peace");
        // 勝利: 生存する勢力が全て同盟
        foreach (var p in f.Diplomacy!.Powers) p.Treaties.Add("alliance");
        Days(s, 1); Assert.Equal("alliance", f.Diplomacy.Outcome);
    }

    [Fact]
    public void SupportWave_AndSaveLoadMatch()
    {
        var s = World(9); var f = s.Factory; f.City!.Support = 10;   // 低支持: 要求と侵攻が増える
        int demands = 0; for (int d = 0; d < 60; d++) { Days(s, 1); demands += f.Events.Count(e => e.Kind == "offer"); f.Events.Clear(); }
        Assert.True(demands > 0, "低支持率で要求か提案が来る");
        var loaded = Save.Load(Save.Snapshot(s), Blocks(), Data);
        Assert.Equal(Save.StateHash(s), Save.StateHash(loaded));
        Assert.Equal(f.Diplomacy!.Powers.Count, loaded.Factory.Diplomacy!.Powers.Count); Assert.Equal(P(s, 2).Relation, loaded.Factory.Diplomacy.Powers.First(p => p.Id == 2).Relation);
    }
}
