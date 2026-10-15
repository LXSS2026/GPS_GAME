using System.Text.Json;
using System.Text.Json.Serialization;

namespace GPS.Sim;

/// <summary>外交のデータ(diplomacy.json)。</summary>
public sealed class DiplomacyData
{
    public sealed record PowerDef(int Id, string Name, string Personality, int Population, int Support, int Tech, long Treasury, int Relation);
    public sealed record Personality(double Growth, double ArmyRatio, double InvadeRatio, double RaidRatio, double DemandRatio, double InvadePerDay, double DemandPerDay, double RaidPerDay, Dictionary<string, double> ProposePerMonth);
    public List<PowerDef> Powers = new();
    public Dictionary<string, Personality> Personalities = new();
    public Dictionary<string, double> Relation = new(), Treaty = new(), Gift = new(), Threat = new(), Demand = new(), War = new();
    public Dictionary<string, double> ProposalBase = new(), ProposalDifficulty = new();
    public double NegotiatorPerPoint = 3, StrengthBonus = 10; public int ProposalCooldownDays = 30;
    public Dictionary<int, string[]> ScrapItems = new();

    public static DiplomacyData Load(string dir)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "diplomacy.json")));
        var r = doc.RootElement; var d = new DiplomacyData();
        static Dictionary<string, double> Num(JsonElement e) => e.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Number).ToDictionary(p => p.Name, p => p.Value.GetDouble());
        foreach (var p in r.GetProperty("powers").EnumerateArray())
            d.Powers.Add(new PowerDef(p.GetProperty("id").GetInt32(), p.GetProperty("name").GetString()!, p.GetProperty("personality").GetString()!, p.GetProperty("population").GetInt32(), p.GetProperty("support").GetInt32(), p.GetProperty("tech").GetInt32(), p.GetProperty("treasury").GetInt64(), p.GetProperty("relation").GetInt32()));
        foreach (var p in r.GetProperty("personalities").EnumerateObject())
        {
            var v = p.Value; double G(string k) => v.TryGetProperty(k, out var x) ? x.GetDouble() : 0;
            d.Personalities[p.Name] = new Personality(G("growth"), G("armyRatio"), G("invadeRatio"), G("raidRatio"), G("demandRatio"), G("invadePerDay"), G("demandPerDay"), G("raidPerDay"), Num(v.GetProperty("proposePerMonth")));
        }
        d.Relation = Num(r.GetProperty("relation")); d.Treaty = Num(r.GetProperty("treaty")); d.Gift = Num(r.GetProperty("gift")); d.Threat = Num(r.GetProperty("threat")); d.Demand = Num(r.GetProperty("demand")); d.War = Num(r.GetProperty("war"));
        var pr = r.GetProperty("proposal"); d.ProposalBase = Num(pr.GetProperty("baseByState")); d.ProposalDifficulty = Num(pr.GetProperty("difficulty"));
        d.NegotiatorPerPoint = pr.GetProperty("negotiatorPerPoint").GetDouble(); d.StrengthBonus = pr.GetProperty("strengthBonus").GetDouble(); d.ProposalCooldownDays = pr.GetProperty("cooldownDays").GetInt32();
        foreach (var s in r.GetProperty("scrapItems").EnumerateObject()) d.ScrapItems[int.Parse(s.Name)] = s.Value.EnumerateArray().Select(e => e.GetString()!).ToArray();
        return d;
    }
}

/// <summary>相手からの提案・要求・和平の申し出(期限付き。プレイヤーが回答する)。</summary>
public sealed class Offer { public int Id { get; set; } public string Kind { get; set; } = ""; public int DaysLeft { get; set; } public double Amount { get; set; } }

/// <summary>AI 勢力の台帳(G20 #1)。工場は回さず概算で成長し、隊だけ実物。</summary>
public sealed class Power
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Personality { get; set; } = "trade";
    public double Population { get; set; }
    public double Support { get; set; } = 50;
    public int Tech { get; set; } = 1;
    public long Treasury { get; set; }
    public double Scrap { get; set; }   // 蓄えたスクラップ(工場の概算)。拠点を落とした側が得る
    public int Relation { get; set; }
    public bool AtWar { get; set; }
    public int WarDays { get; set; }
    public int LossesMine { get; set; }      // 戦争中に失った機体(この勢力)
    public int LossesEnemy { get; set; }     // 相手(プレイヤー)が失った機体
    public SortedSet<string> Treaties { get; set; } = new(StringComparer.Ordinal);   // trade / nonaggression / alliance
    public bool Vassal { get; set; }
    public int HomeBuilding { get; set; } = -1;
    public double Strength { get; set; }     // 残りの戦力(機体数相当)。月ごとに人口から回復
    public int SpawnCooldown { get; set; }
    public SortedDictionary<string, int> Cooldown { get; set; } = new(StringComparer.Ordinal);   // 行動 → 次に可能になる日
    public List<Offer> Offers { get; set; } = new();
    public int NextOfferId { get; set; } = 1;
    public double TributeToMe { get; set; }      // プレイヤーがこの勢力へ払う月額
    public double TributeFromThem { get; set; }  // この勢力がプレイヤーへ払う月額
    public int TributeMonths { get; set; }
    public int Faction => 100 + Id;
    public string State(DiplomacyData d) => Vassal ? "vassal" : Relation < d.Relation["hostileBelow"] ? "hostile" : Relation < d.Relation["waryBelow"] ? "wary" : Relation >= d.Relation["friendlyFrom"] ? "friendly" : "neutral";
}

public sealed class Diplomacy
{
    public List<Power> Powers { get; set; } = new();
    public string Outcome { get; set; } = "";   // conquest / alliance / conquered(征服された)。達しても計算は止めない
    public int LastDay { get; set; } = -1;
}

public static class DiplomacySim
{
    public static Diplomacy Of(Factory f) => f.Diplomacy ??= new Diplomacy();

    /// <summary>勢力を置く(見本・開始時)。拠点の建物は先に作っておく。</summary>
    public static Power AddPower(Simulation sim, DiplomacyData.PowerDef def, int homeBuilding)
    {
        var dip = Of(sim.Factory);
        var p = new Power { Id = def.Id, Name = def.Name, Personality = def.Personality, Population = def.Population, Support = def.Support, Tech = def.Tech, Treasury = def.Treasury, Relation = def.Relation, HomeBuilding = homeBuilding };
        p.Strength = p.Population * sim.DiplomacyData.War["mechsPerPopulation"] * sim.DiplomacyData.Personalities[p.Personality].ArmyRatio;
        p.Scrap = p.Population * p.Tech * sim.DiplomacyData.War["scrapPerPopulationTech"];
        var hb = sim.Factory.Get(homeBuilding); if (hb != null) hb.OwnerPower = p.Id;
        dip.Powers.Add(p); return p;
    }

    /// <summary>戦力 = Σ 生存機 × (1 + 0.1 × 世代) × 品質 / 70(G20 #7)。AI は台帳の残り戦力も足す。</summary>
    public static double PlayerStrength(Factory f) => f.Squads.Where(s => s.Faction == 1).SelectMany(s => s.Members.Where(u => u.Alive).Select(u => (u, s))).Sum(x => { var m = f.Mechs.FirstOrDefault(mm => mm.Id == x.u.MechId); return m == null ? 0 : (1 + 0.1 * m.Generation) * m.Quality / 70.0; });
    public static double PowerStrength(Factory f, Power p) => p.Vassal ? 0 : p.Strength * (1 + 0.1 * p.Tech) + f.Squads.Where(s => s.Faction == p.Faction).Sum(s => s.Alive * (1 + 0.1 * s.Generation));
    /// <summary>兵力比 = 相手 / 自分(G7 の表示)。</summary>
    public static double Ratio(Factory f, Power p) { double mine = PlayerStrength(f); return mine <= 0 ? 99 : PowerStrength(f, p) / mine; }

    static int Negotiator(Simulation sim)
    {
        var pol = PoliticsSim.Of(sim.Factory); var city = sim.Factory.City; if (city == null) return 0;
        var slot = PoliticsSim.Slots(sim.Factory, sim.PoliticsData).FirstOrDefault(s => s.Kind == "negotiate");
        return slot != null && pol.Offices.TryGetValue(slot.Id, out var pid) && city.Persons.FirstOrDefault(x => x.Id == pid) is Person pr ? pr.Negotiate : 0;
    }

    /// <summary>提案の成功率(%): 関係の状態 + 交渉役 × 3 + 兵力比 ± 10 + 条約の難度。</summary>
    public static double ProposalChance(Simulation sim, Power p, string kind)
    {
        var d = sim.DiplomacyData; double ratio = Ratio(sim.Factory, p);
        double c = d.ProposalBase[p.State(d)] + Negotiator(sim) * d.NegotiatorPerPoint + (ratio < 0.8 ? d.StrengthBonus : ratio > 1.25 ? -d.StrengthBonus : 0) + d.ProposalDifficulty.GetValueOrDefault(kind);
        return Math.Clamp(c, 0, 95);
    }
    public static double PeaceChance(Simulation sim, Power p)
    {
        var d = sim.DiplomacyData; double lossRatio = (p.LossesMine + 1.0) / (p.LossesEnemy + 1.0);
        return lossRatio >= d.War["peaceBaseLossRatio"] ? d.War["peaceWinPct"] : lossRatio <= 1 / d.War["peaceBaseLossRatio"] ? d.War["peaceLosePct"] : d.War["peaceEvenPct"];
    }

    public static void Relate(Simulation sim, Power p, int delta) { if (!p.Vassal) p.Relation = Math.Clamp(p.Relation + delta, -100, 100); }

    static void Notice(Factory f, string kind, Power p, int v = 0) => f.Events.Add(new FactoryEvent { Kind = kind, Building = p.Id, Power = v });

    public static void DeclareWar(Simulation sim, Power p, bool byPlayer)
    {
        var f = sim.Factory; var d = sim.DiplomacyData;
        if (p.AtWar || p.Vassal) return;
        p.AtWar = true; p.WarDays = 0; p.LossesMine = 0; p.LossesEnemy = 0; p.Treaties.Remove("trade"); p.Treaties.Remove("alliance");
        if (p.Treaties.Remove("nonaggression") && byPlayer) { Relate(sim, p, (int)d.Relation["breakNonAggression"]); foreach (var o in Of(f).Powers.Where(x => x != p)) Relate(sim, o, (int)d.Relation["breakNonAggressionOthers"]); }
        Relate(sim, p, (int)d.Relation["warDeclared"]);
        Notice(f, byPlayer ? "war_declared" : "war_received", p);
        // 同盟国の参戦(援軍 1 隊をプレイヤーの拠点の前に)
        foreach (var ally in Of(f).Powers.Where(x => x != p && x.Treaties.Contains("alliance") && !x.Vassal)) SpawnSquad(sim, ally, forPlayer: true, target: p);
    }

    public static void MakePeace(Simulation sim, Power p, double tributeFromThem, double tributeToThem)
    {
        var f = sim.Factory; var d = sim.DiplomacyData;
        p.AtWar = false; p.TributeFromThem = tributeFromThem; p.TributeToMe = tributeToThem; if (tributeFromThem > 0 || tributeToThem > 0) p.TributeMonths = (int)d.Threat["tributeMonths"];
        if (tributeFromThem > 0 && f.City != null && p.Population >= 50)
        {   // 割譲(2026-10-16): 優勢な和平では相手の人口の 3 割が、相手の拠点の隣に置く新しい都市としてこちらに付く。相手の蓄えの 3 割も荷下ろし場へ
            var home = f.Get(p.HomeBuilding); double ceded = Math.Round(p.Population * 0.3); p.Population -= ceded;
            var st = new Settlement { Id = f.City.NextSettlementId++, Name = $"{p.Name} からの割譲地", X = (home?.X ?? 0) + 40, Y = (home?.Y ?? 0), Population = ceded };
            f.City.Settlements.Add(st);
            var cap = f.Get(f.City.Capital); if (cap != null) { var items = d.ScrapItems.GetValueOrDefault(Math.Clamp(p.Tech, 1, 4)) ?? d.ScrapItems[1]; int n = (int)Math.Round(p.Scrap * 0.3); p.Scrap -= n; foreach (var it in items) cap.Cargo.Add(new ItemStack(it, 60, Math.Max(1, n / items.Length))); }
            f.Events.Add(new FactoryEvent { Kind = "ceded", Building = p.Id, Power = st.Id, Destroyed = (int)ceded });
        }
        foreach (var s in f.Squads.Where(s => s.Faction == p.Faction && s.State == SquadState.Field)) { s.Order = SquadOrder.Retreat; }
        foreach (var s in f.Squads.Where(s => s.Faction == 1 && s.TargetBuilding == p.HomeBuilding)) { s.TargetBuilding = -1; s.Order = SquadOrder.Hold; }
        Notice(f, "peace", p);
    }

    /// <summary>AI の隊を作って出す(台帳の戦力を消費)。forPlayer = 同盟の援軍(プレイヤー側の隊)。</summary>
    public static Squad? SpawnSquad(Simulation sim, Power p, bool forPlayer, Power? target, bool raid = false)
    {
        var f = sim.Factory; var d = sim.DiplomacyData; var home = f.Get(p.HomeBuilding); if (home == null) return null;
        int n = (int)Math.Min(d.War["squadMechsMax"], Math.Floor(forPlayer ? d.Treaty["allianceSquadMechs"] : p.Strength));
        if (n <= 0) return null;
        if (!forPlayer) p.Strength -= n;
        string arch = p.Personality == "warlike" ? "heavy" : p.Personality == "raider" ? "fast" : "general";
        var ids = new List<int>();
        for (int i = 0; i < n; i++) { int id = f.NextMechId; new CreateMech(id, Math.Clamp(p.Tech, 1, 6), arch, 60 + sim.Rng.Next(21)).Apply(sim); ids.Add(id); }
        int sid = f.NextSquadId; int faction = forPlayer ? 1 : p.Faction;
        var capital = f.Get(f.City?.Capital ?? -1);
        int homeId = forPlayer && capital != null ? capital.Id : p.HomeBuilding;
        new FormSquad(sid, faction, forPlayer ? $"{p.Name} の援軍" : raid ? $"{p.Name} の襲撃隊" : $"{p.Name} の隊", ids, homeId).Apply(sim);
        var sq = f.Squads.First(s => s.Id == sid); var hb = f.Get(homeId)!;
        new Deploy(sid, hb.X + 14, hb.Y + 6).Apply(sim);
        // 目標: 侵攻は相手の拠点、襲撃は最寄りの工場の建物、援軍は敵の隊
        if (forPlayer && target != null) { var ts = f.Squads.FirstOrDefault(s => s.Faction == target.Faction && s.State == SquadState.Field); sq.Order = SquadOrder.Attack; sq.TargetSquad = ts?.Id ?? -1; sq.TargetBuilding = ts == null ? target.HomeBuilding : -1; }
        else if (raid) { var tb = f.Buildings.Where(b => b.Use == "factory" && b.Id != p.HomeBuilding && !b.Destroyed && !Of(f).Powers.Any(o => o.HomeBuilding == b.Id)).OrderBy(b => Math.Abs(b.X - hb.X) + Math.Abs(b.Y - hb.Y)).FirstOrDefault(); sq.Order = SquadOrder.Attack; sq.TargetBuilding = tb?.Id ?? -1; }
        else { sq.Order = SquadOrder.Attack; sq.TargetBuilding = f.City?.Capital ?? -1; }
        return sq;
    }

    /// <summary>1 時間ごと: 戦争中の AI は補給で戻った隊を再出撃させ、戦艦をプレイヤーの拠点の手前(砲塔の射程の 8 割)へ進める。</summary>
    public static void Hour(Simulation sim)
    {
        var f = sim.Factory; var dip = f.Diplomacy; if (dip == null) return;
        foreach (var p in dip.Powers)
            foreach (var ship in f.Buildings.Where(b => b.IsShip && b.OwnerPower == p.Id && !b.Destroyed))
            {
                var home = f.Get(p.HomeBuilding); var cap = f.Get(f.City?.Capital ?? -1);
                if (p.AtWar && !p.Vassal && cap != null)
                {
                    double range = ship.Floors.SelectMany(fl => fl.Structures).OfType<Turret>().Select(t => sim.Data.Structures[t.DefId].Range).DefaultIfEmpty(100).Min() * 0.8;
                    double cx = cap.X + cap.Floors[0].W / 2.0, cy = cap.Y + cap.Floors[0].D / 2.0, sx = ship.X + ship.Floors[0].W / 2.0, sy = ship.Y + ship.Floors[0].D / 2.0;
                    double dx = cx - sx, dy = cy - sy, d = Math.Sqrt(dx * dx + dy * dy);
                    if (d > range && ship.MoveTargetX == null) { ship.MoveTargetX = cx - dx / d * range - ship.Floors[0].W / 2.0; ship.MoveTargetZ = cy - dy / d * range - ship.Floors[0].D / 2.0; }
                }
                else if (home != null && ship.MoveTargetX == null && (Math.Abs(ship.X - (home.X + 20)) > 1 || Math.Abs(ship.Y - (home.Y - 40)) > 1)) { ship.MoveTargetX = home.X + 20; ship.MoveTargetZ = home.Y - 40; }
            }
        foreach (var p in dip.Powers.Where(p => p.AtWar && !p.Vassal))
            foreach (var sq in f.Squads.Where(s => s.Faction == p.Faction && s.State == SquadState.InHangar && s.Alive > 0).ToList())
            {
                var hb = f.Get(sq.HomeBuilding); if (hb == null) continue;
                new Deploy(sq.Id, hb.X + 14, hb.Y + 6).Apply(sim);
                sq.Order = SquadOrder.Attack; sq.TargetBuilding = sq.Name.Contains("襲撃") ? sq.TargetBuilding : (f.City?.Capital ?? -1);
                if (sq.TargetBuilding < 0 || (f.Get(sq.TargetBuilding)?.Destroyed ?? true)) { sq.TargetBuilding = -1; sq.Order = SquadOrder.Hold; }
            }
    }

    /// <summary>1 日ごと(CitySim.Day の後)。関係・AI の判定・要求の期限・戦争の進み・勝敗。</summary>
    public static void Day(Simulation sim)
    {
        var f = sim.Factory; var dip = f.Diplomacy; if (dip == null || f.City == null) return;
        var d = sim.DiplomacyData; var city = f.City; var rng = sim.Rng; int day = city.Days;
        dip.LastDay = day;
        f.TradeBonusPct = dip.Powers.Any(p => !p.Vassal && p.Treaties.Contains("trade")) ? (int)d.Treaty["tradePriceBonusPct"] : 0;
        double support = city.Support, supportWave = support < d.War["supportWaveBelow"] ? 1 + (d.War["supportWaveBelow"] - support) / d.War["supportWaveBelow"] : 1;
        foreach (var p in dip.Powers)
        {
            if (p.Vassal) continue;
            var pers = d.Personalities[p.Personality]; double ratio = Ratio(f, p);
            // 期限切れの申し出
            foreach (var o in p.Offers.ToList()) if (--o.DaysLeft <= 0) { p.Offers.Remove(o); if (o.Kind == "demand_tribute" || o.Kind == "demand_trade") Refuse(sim, p, o); }
            if (p.AtWar)
            {
                p.WarDays++;
                // 隊が無ければ出す(台帳の戦力がある限り)
                if (!f.Squads.Any(s => s.Faction == p.Faction && s.State == SquadState.Field && s.Alive > 0) && --p.SpawnCooldown <= 0 && p.Strength >= 1) { SpawnSquad(sim, p, false, null, raid: p.Personality == "raider"); p.SpawnCooldown = (int)d.War["spawnEveryDays"]; }
                // 和平の申し出(長引く・損耗が大きい)
                double lossShare = p.LossesMine / Math.Max(1.0, p.LossesMine + p.Strength);
                if ((p.WarDays >= d.War["aiPeaceAfterDays"] || lossShare >= d.War["aiPeaceLossShare"]) && !p.Offers.Any(o => o.Kind == "peace") && p.Cooldown.GetValueOrDefault("peace") <= day)
                { p.Offers.Add(new Offer { Id = p.NextOfferId++, Kind = "peace", DaysLeft = (int)d.Demand["answerDays"], Amount = lossShare >= d.War["aiPeaceLossShare"] ? p.Population * d.Threat["tributePerMonthPerPopulation"] * 0.5 : 0 }); p.Cooldown["peace"] = day + 30; Notice(f, "offer", p, 3); }
                continue;
            }
            // 侵攻(好戦型)・襲撃(略奪型)・要求・提案
            bool nonAgg = p.Treaties.Contains("nonaggression"), ally = p.Treaties.Contains("alliance");
            if (!nonAgg && !ally && ratio >= pers.InvadeRatio && rng.Next(10_000) < pers.InvadePerDay * supportWave * 10_000) { DeclareWar(sim, p, false); continue; }
            if (!nonAgg && !ally && ratio >= pers.RaidRatio && rng.Next(10_000) < pers.RaidPerDay * supportWave * 10_000) { p.AtWar = true; p.WarDays = 0; Relate(sim, p, (int)d.Relation["warDeclared"] / 2); Notice(f, "raid", p); SpawnSquad(sim, p, false, null, raid: true); continue; }
            if (!ally && ratio >= pers.DemandRatio && p.Offers.Count == 0 && p.Cooldown.GetValueOrDefault("demand") <= day && rng.Next(10_000) < pers.DemandPerDay * supportWave * 10_000)
            {
                string kind = p.Treaties.Contains("trade") && rng.Next(2) == 0 ? "demand_trade" : "demand_tribute";
                p.Offers.Add(new Offer { Id = p.NextOfferId++, Kind = kind, DaysLeft = (int)d.Demand["answerDays"], Amount = city.Population * d.Demand["tributePerMonthPerPopulation"] });
                p.Cooldown["demand"] = day + 60; Notice(f, "offer", p, kind == "demand_trade" ? 2 : 1);
            }
            if (ratio >= 1.3 && p.Personality == "warlike" && !nonAgg && day % 30 == 0) Notice(f, "warning", p);
        }
        if (day % 30 == 0) Month(sim);
        Victory(sim);
    }

    static void Refuse(Simulation sim, Power p, Offer o)
    {
        var d = sim.DiplomacyData; Relate(sim, p, (int)d.Relation["demandRefused"]);
        if (p.Personality == "warlike" && sim.Rng.Next(100) < d.Demand["warOnRefusePct"]) DeclareWar(sim, p, false);
    }

    /// <summary>1 か月ごと: 勢力の成長、関係の自然回復、条約の効き、貢納、AI の提案。</summary>
    static void Month(Simulation sim)
    {
        var f = sim.Factory; var d = sim.DiplomacyData; var dip = f.Diplomacy!; var city = f.City!; var rng = sim.Rng; int day = city.Days;
        foreach (var p in dip.Powers)
        {
            var pers = d.Personalities[p.Personality];
            if (!p.Vassal)
            {
                p.Population *= 1 + pers.Growth;
                if (day % 360 == 0) p.Tech = Math.Min(4, p.Tech + 1);
                double maxStrength = p.Population * d.War["mechsPerPopulation"] * pers.ArmyRatio;
                p.Strength = Math.Min(maxStrength, p.Strength + maxStrength * 0.25);
                p.Scrap += p.Population * p.Tech * d.War["scrapPerPopulationTech"] * 0.1;
                p.Treasury += (long)(p.Population * 2);
                // 戦艦(G2 第 5 節): 技術 L4 で国庫が足りれば小型を 1 隻(拠点の隣に置き、リアクターとビーム砲塔 2 基)
                if (p.Tech >= 4 && !f.Buildings.Any(b => b.IsShip && b.OwnerPower == p.Id && !b.Destroyed))
                {
                    var k = Ships.Kinds["small"]; long cost = (long)Math.Round(k.W * k.D * k.Floors * k.CostPerCell);
                    var home = f.Get(p.HomeBuilding);
                    if (home != null && p.Treasury >= cost)
                    {
                        int sid = f.Buildings.Max(b => b.Id) + 1; bool cre = f.Creative; f.Creative = true;
                        new BuildShip(sid, $"{p.Name} の戦艦", "small", home.X + 20, home.Y - 40).Apply(sim);
                        new PlaceStructure(sid, 0, "reactor_g3", 2, 2, 0, Direction.East).Apply(sim); new PlaceStructure(sid, 0, "turret_beam", 2, 50, 0, Direction.East).Apply(sim); new PlaceStructure(sid, 0, "turret_beam", 15, 50, 0, Direction.East).Apply(sim);
                        f.Creative = cre; p.Treasury -= cost; var ship = f.Get(sid)!; ship.OwnerPower = p.Id;
                        f.Events.Add(new FactoryEvent { Kind = "power_ship", Building = p.Id, Power = sid });
                    }
                }
                // 関係の自然回復と条約
                if (p.Relation > 0) p.Relation = Math.Max(0, p.Relation - (int)d.Relation["decayPerMonth"]); else if (p.Relation < 0) p.Relation = Math.Min(0, p.Relation + (int)d.Relation["decayPerMonth"]);
                if (p.Treaties.Contains("trade")) { Relate(sim, p, (int)d.Treaty["tradeRelationPerMonth"]); p.Treasury += (long)(city.Population); }
                // AI の提案(期限付きの申し出)
                if (!p.AtWar && p.Offers.Count == 0)
                    foreach (var (kind, prob) in pers.ProposePerMonth)
                    {
                        if (p.Treaties.Contains(kind) || prob <= 0) continue;
                        if (kind == "alliance" && !(p.Relation >= d.Relation["friendlyFrom"] && dip.Powers.Any(o => o != p && o.AtWar))) continue;
                        if (p.Relation < d.Relation["waryBelow"]) continue;
                        if (rng.Next(10_000) < prob * 10_000) { p.Offers.Add(new Offer { Id = p.NextOfferId++, Kind = "propose_" + kind, DaysLeft = (int)d.Demand["answerDays"] }); Notice(f, "offer", p, 4); break; }
                    }
            }
            // 貢納
            if (p.TributeMonths > 0)
            {
                if (p.TributeToMe > 0) { f.Treasury -= (long)p.TributeToMe; p.Treasury += (long)p.TributeToMe; Relate(sim, p, (int)d.Relation["tributePaidPerMonth"]); }
                if (p.TributeFromThem > 0) { f.Treasury += (long)p.TributeFromThem; p.Treasury -= (long)p.TributeFromThem; }
                if (--p.TributeMonths == 0) { p.TributeToMe = 0; p.TributeFromThem = 0; }
            }
        }
    }

    /// <summary>建物が落ちたとき(Combat から)。AI の拠点なら属国 + スクラップ、プレイヤーの拠点なら征服、工場なら略奪。</summary>
    public static void BuildingFell(Simulation sim, Building b, Squad? by)
    {
        var f = sim.Factory; var dip = f.Diplomacy; if (dip == null) return; var d = sim.DiplomacyData;
        var owner = dip.Powers.FirstOrDefault(p => p.HomeBuilding == b.Id);
        if (owner != null)
        {
            owner.Vassal = true; owner.AtWar = false; owner.Relation = (int)d.Relation["vassal"]; owner.Treaties.Clear(); owner.Offers.Clear();
            f.Squads.RemoveAll(s => s.Faction == owner.Faction);
            var home = f.Get(by?.HomeBuilding ?? f.City?.Capital ?? -1);
            if (home != null)
            {   // スクラップ(技術レベルに応じた品)を帰還先の建物の在庫へ(G20 #1・#6)
                var items = d.ScrapItems.GetValueOrDefault(Math.Clamp(owner.Tech, 1, 4)) ?? d.ScrapItems[1];
                int n = (int)Math.Round(owner.Scrap); foreach (var it in items) home.Cargo.Add(new ItemStack(it, 60, Math.Max(1, n / items.Length)));   // 荷下ろし場へ(品質は通常)
            }
            foreach (var s in f.Squads.Where(s => s.Faction == 1 && s.TargetBuilding == b.Id)) { s.TargetBuilding = -1; s.Order = SquadOrder.Retreat; }
            f.Events.Add(new FactoryEvent { Kind = "vassal", Building = owner.Id, Power = (int)owner.Scrap });
            b.Hp = b.MaxHp;   // 拠点は残る(属国の建物)
            return;
        }
        if (f.City != null && b.Id == f.City.Capital)
        {
            f.City.Defeated = true; dip.Outcome = "conquered";
            foreach (var p in dip.Powers.Where(p => p.AtWar)) { p.AtWar = false; foreach (var s in f.Squads.Where(s => s.Faction == p.Faction)) s.Order = SquadOrder.Retreat; }
            f.Events.Add(new FactoryEvent { Kind = "conquered", Building = b.Id });
            return;
        }
        // 工場の建物: 襲撃隊は略奪して帰る
        var raider = by != null ? dip.Powers.FirstOrDefault(p => p.Faction == by.Faction) : null;
        if (raider != null)
        {
            long loot = b.Cargo.Sum(x => x.Count); b.Cargo.Clear(); raider.Scrap += loot; raider.AtWar = false;   // 荷下ろし場の品を奪う
            foreach (var s in f.Squads.Where(s => s.Faction == raider.Faction)) { s.Order = SquadOrder.Retreat; s.TargetBuilding = -1; }
            f.Events.Add(new FactoryEvent { Kind = "raided", Building = b.Id, Power = raider.Id, Destroyed = (int)loot });
        }
    }

    /// <summary>撃破の記録(損耗比)。Combat の Kill から。</summary>
    public static void MechLost(Factory f, int faction)
    {
        var dip = f.Diplomacy; if (dip == null) return;
        foreach (var p in dip.Powers.Where(p => p.AtWar)) { if (faction == p.Faction) p.LossesMine++; else if (faction == 1) p.LossesEnemy++; }
    }

    static void Victory(Simulation sim)
    {
        var f = sim.Factory; var dip = f.Diplomacy!; if (dip.Outcome != "" || dip.Powers.Count == 0) return;
        if (dip.Powers.All(p => p.Vassal)) { dip.Outcome = "conquest"; f.Events.Add(new FactoryEvent { Kind = "victory", Power = 1 }); }
        else if (dip.Powers.Where(p => !p.Vassal).All(p => p.Treaties.Contains("alliance"))) { dip.Outcome = "alliance"; f.Events.Add(new FactoryEvent { Kind = "victory", Power = 2 }); }
    }
}

// ---------- 命令 ----------
/// <summary>条約を提案(trade / nonaggression / alliance)。成功率は ProposalChance。失敗で関係 −5、30 日は同じ提案ができない。</summary>
public sealed record Propose(int PowerId, string Kind) : ICommand
{
    public void Apply(Simulation s)
    {
        var f = s.Factory; var d = s.DiplomacyData; var p = DiplomacySim.Of(f).Powers.FirstOrDefault(x => x.Id == PowerId); if (p == null || p.Vassal || p.AtWar || p.Treaties.Contains(Kind) || !d.ProposalDifficulty.ContainsKey(Kind)) return;
        int day = f.City?.Days ?? 0; if (p.Cooldown.GetValueOrDefault("propose_" + Kind) > day) return;
        p.Cooldown["propose_" + Kind] = day + d.ProposalCooldownDays;
        if (s.Rng.Next(100) < DiplomacySim.ProposalChance(s, p, Kind)) { p.Treaties.Add(Kind); DiplomacySim.Relate(s, p, (int)d.Relation["treatySigned"]); f.Events.Add(new FactoryEvent { Kind = "treaty", Building = p.Id, Power = Array.IndexOf(new[] { "trade", "nonaggression", "alliance" }, Kind) }); }
        else { DiplomacySim.Relate(s, p, (int)d.Relation["proposalFail"]); f.Events.Add(new FactoryEvent { Kind = "proposal_failed", Building = p.Id }); }
    }
}

/// <summary>贈り物: 国庫から 人口 × 2、関係 +10。月 1 回。</summary>
public sealed record Gift(int PowerId) : ICommand
{
    public void Apply(Simulation s)
    {
        var f = s.Factory; var d = s.DiplomacyData; var p = DiplomacySim.Of(f).Powers.FirstOrDefault(x => x.Id == PowerId); if (p == null || p.Vassal) return;
        int day = f.City?.Days ?? 0; if (p.Cooldown.GetValueOrDefault("gift") > day) return;
        long cost = (long)(p.Population * d.Gift["costPerPopulation"]); if (f.Treasury < cost) return;
        f.Treasury -= cost; p.Treasury += cost; p.Cooldown["gift"] = day + (int)d.Gift["cooldownDays"]; DiplomacySim.Relate(s, p, (int)d.Relation["gift"]);
    }
}

/// <summary>威圧: 兵力比(相手 / 自分)が 1 / 1.5 以下なら相手が貢納を飲む。足りなければ関係 −20。</summary>
public sealed record Threaten(int PowerId) : ICommand
{
    public void Apply(Simulation s)
    {
        var f = s.Factory; var d = s.DiplomacyData; var p = DiplomacySim.Of(f).Powers.FirstOrDefault(x => x.Id == PowerId); if (p == null || p.Vassal || p.AtWar) return;
        int day = f.City?.Days ?? 0; if (p.Cooldown.GetValueOrDefault("threat") > day) return; p.Cooldown["threat"] = day + 30;
        if (DiplomacySim.Ratio(f, p) <= 1 / d.Threat["ratioNeeded"]) { p.TributeFromThem = p.Population * d.Threat["tributePerMonthPerPopulation"]; p.TributeMonths = (int)d.Threat["tributeMonths"]; DiplomacySim.Relate(s, p, (int)d.Relation["threatFail"] / 2); f.Events.Add(new FactoryEvent { Kind = "threat_ok", Building = p.Id, Power = (int)p.TributeFromThem }); }
        else { DiplomacySim.Relate(s, p, (int)d.Relation["threatFail"]); f.Events.Add(new FactoryEvent { Kind = "threat_failed", Building = p.Id }); }
    }
}

public sealed record DeclareWar(int PowerId) : ICommand
{
    public void Apply(Simulation s) { var p = DiplomacySim.Of(s.Factory).Powers.FirstOrDefault(x => x.Id == PowerId); if (p == null || p.Treaties.Contains("nonaggression")) return; DiplomacySim.DeclareWar(s, p, true); }
}

/// <summary>和平の提案(戦争中)。損耗比で成功率。優勢なら相手から貢納。</summary>
public sealed record ProposePeace(int PowerId) : ICommand
{
    public void Apply(Simulation s)
    {
        var f = s.Factory; var d = s.DiplomacyData; var p = DiplomacySim.Of(f).Powers.FirstOrDefault(x => x.Id == PowerId); if (p == null || !p.AtWar) return;
        int day = f.City?.Days ?? 0; if (p.Cooldown.GetValueOrDefault("peace") > day) return; p.Cooldown["peace"] = day + 30;
        if (s.Rng.Next(100) < DiplomacySim.PeaceChance(s, p)) DiplomacySim.MakePeace(s, p, p.LossesMine > p.LossesEnemy * d.War["peaceBaseLossRatio"] ? p.Population * d.Threat["tributePerMonthPerPopulation"] : 0, 0);
        else f.Events.Add(new FactoryEvent { Kind = "peace_failed", Building = p.Id });
    }
}

/// <summary>相手の申し出(提案・要求・和平)に回答する。</summary>
public sealed record AnswerOffer(int PowerId, int OfferId, bool Accept) : ICommand
{
    public void Apply(Simulation s)
    {
        var f = s.Factory; var d = s.DiplomacyData; var p = DiplomacySim.Of(f).Powers.FirstOrDefault(x => x.Id == PowerId); if (p == null) return;
        var o = p.Offers.FirstOrDefault(x => x.Id == OfferId); if (o == null) return; p.Offers.Remove(o);
        if (!Accept) { if (o.Kind.StartsWith("demand")) { DiplomacySim.Relate(s, p, (int)d.Relation["demandRefused"]); if (p.Personality == "warlike" && s.Rng.Next(100) < d.Demand["warOnRefusePct"]) DiplomacySim.DeclareWar(s, p, false); } return; }
        switch (o.Kind)
        {
            case "demand_tribute": p.TributeToMe = o.Amount; p.TributeMonths = (int)d.Demand["tributeMonths"]; DiplomacySim.Relate(s, p, 10); break;
            case "demand_trade": p.Treaties.Remove("trade"); DiplomacySim.Relate(s, p, 5); break;
            case "peace": DiplomacySim.MakePeace(s, p, 0, o.Amount); break;
            default: if (o.Kind.StartsWith("propose_")) { var k = o.Kind[8..]; p.Treaties.Add(k); DiplomacySim.Relate(s, p, (int)d.Relation["treatySigned"]); f.Events.Add(new FactoryEvent { Kind = "treaty", Building = p.Id, Power = Array.IndexOf(new[] { "trade", "nonaggression", "alliance" }, k) }); } break;
        }
    }
}

/// <summary>条約の破棄。不可侵の破棄は関係 −50、他の勢力 −20。</summary>
public sealed record BreakTreaty(int PowerId, string Kind) : ICommand
{
    public void Apply(Simulation s)
    {
        var f = s.Factory; var d = s.DiplomacyData; var p = DiplomacySim.Of(f).Powers.FirstOrDefault(x => x.Id == PowerId); if (p == null || !p.Treaties.Remove(Kind)) return;
        if (Kind == "nonaggression") { DiplomacySim.Relate(s, p, (int)d.Relation["breakNonAggression"]); foreach (var o in DiplomacySim.Of(f).Powers.Where(x => x != p)) DiplomacySim.Relate(s, o, (int)d.Relation["breakNonAggressionOthers"]); }
        else DiplomacySim.Relate(s, p, (int)d.Relation["proposalFail"] * 2);
    }
}

/// <summary>荷下ろし場の品を箱へ移す(箱の上限 100 個まで)。</summary>
public sealed record UnloadCargo(int Building, int Floor, int ChestId) : ICommand
{
    public void Apply(Simulation s)
    {
        var b = s.Factory.Get(Building); if (b == null || b.Find(Floor, ChestId) is not Chest ch) return;
        while (b.Cargo.Count > 0 && ch.Items.Count < 100)
        {
            var st = b.Cargo[0]; ch.Items.Add(st with { Count = 1 });
            if (st.Count <= 1) b.Cargo.RemoveAt(0); else b.Cargo[0] = st with { Count = st.Count - 1 };
        }
    }
}

/// <summary>都市の拠点(落ちると征服)。</summary>
public sealed record SetCapital(int Building) : ICommand
{
    public void Apply(Simulation s) { if (s.Factory.City != null && s.Factory.Get(Building) != null) s.Factory.City.Capital = Building; }
}

/// <summary>AI 勢力を置く(見本・開始時)。</summary>
public sealed record AddPower(int PowerId, int HomeBuilding) : ICommand
{
    public void Apply(Simulation s)
    {
        var def = s.DiplomacyData.Powers.FirstOrDefault(p => p.Id == PowerId); if (def == null || DiplomacySim.Of(s.Factory).Powers.Any(p => p.Id == PowerId) || s.Factory.Get(HomeBuilding) == null) return;
        DiplomacySim.AddPower(s, def, HomeBuilding);
    }
}
