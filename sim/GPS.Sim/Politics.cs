using System.Text.Json;
using System.Text.Json.Serialization;

namespace GPS.Sim;

/// <summary>登用と政治のデータ(politics.json)。</summary>
public sealed class PoliticsData
{
    public sealed record OfficeDef(string Kind, string Ability, string[] Names, int FromStage, bool RequiresMech, int PerResidences, int PerSquads);
    public sealed record Stratum(string Name, double FoodWaterMul, double RobotShare, double ConscriptionExtra, double UnemployedExtra);
    public sealed record Decree(string Name, int Days, double CostPerPersonPerDay, double SupportBonus, double CostPerPerson, bool Instant);
    public List<OfficeDef> Offices = new();
    public Dictionary<string, Dictionary<string, double>> Effects = new();
    public Dictionary<string, string> Jobs = new();
    public Dictionary<string, Stratum> Strata = new();
    public Dictionary<string, double> Discontent = new();
    public Dictionary<string, double> Faction = new();
    public Dictionary<string, string> Demands = new();
    public Dictionary<string, double> DemandMet = new();
    public Dictionary<string, double> Rebellion = new();
    public Dictionary<string, double> Budget = new();
    public int RobotCapDefault = 50;
    public Dictionary<string, Decree> Decrees = new();
    public int CandidateExpireDays = 360;

    public static PoliticsData Load(string dir)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "politics.json")));
        var r = doc.RootElement; var d = new PoliticsData();
        static Dictionary<string, double> Num(JsonElement e) => e.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Number).ToDictionary(p => p.Name, p => p.Value.GetDouble());
        foreach (var o in r.GetProperty("offices").EnumerateArray())
            d.Offices.Add(new OfficeDef(o.GetProperty("kind").GetString()!, o.GetProperty("ability").GetString()!, o.GetProperty("names").EnumerateArray().Select(e => e.GetString()!).ToArray(), o.GetProperty("fromStage").GetInt32(),
                o.TryGetProperty("requiresMech", out var rm) && rm.GetBoolean(), o.TryGetProperty("perResidences", out var pr) ? pr.GetInt32() : 0, o.TryGetProperty("perSquads", out var ps) ? ps.GetInt32() : 0));
        foreach (var e in r.GetProperty("effects").EnumerateObject()) d.Effects[e.Name] = Num(e.Value);
        foreach (var j in r.GetProperty("jobs").EnumerateObject()) d.Jobs[j.Name] = j.Value.GetString()!;
        foreach (var s in r.GetProperty("strata").EnumerateObject())
        {
            var v = s.Value; double G(string k) => v.TryGetProperty(k, out var x) ? x.GetDouble() : 0;
            d.Strata[s.Name] = new Stratum(v.GetProperty("name").GetString()!, G("foodWaterMul"), G("robotShare"), G("conscriptionExtra"), G("unemployedExtra"));
        }
        d.Discontent = Num(r.GetProperty("discontent"));
        var f = r.GetProperty("faction"); d.Faction = Num(f);
        foreach (var x in f.GetProperty("demands").EnumerateObject()) d.Demands[x.Name] = x.Value.GetString()!;
        d.DemandMet = Num(f.GetProperty("demandMet"));
        d.Rebellion = Num(r.GetProperty("rebellion")); d.Budget = Num(r.GetProperty("budget"));
        d.RobotCapDefault = r.GetProperty("robotCapDefault").GetInt32();
        foreach (var x in r.GetProperty("decrees").EnumerateObject())
        {
            var v = x.Value; double G(string k) => v.TryGetProperty(k, out var y) ? y.GetDouble() : 0;
            d.Decrees[x.Name] = new Decree(v.GetProperty("name").GetString()!, v.GetProperty("days").GetInt32(), G("costPerPersonPerDay"), G("supportBonus"), G("costPerPerson"), v.TryGetProperty("instant", out var ins) && ins.GetBoolean());
        }
        d.CandidateExpireDays = r.GetProperty("candidateExpireDays").GetInt32();
        return d;
    }
}

/// <summary>役職の枠(段階と建物・隊の数で決まる)。</summary>
public sealed record OfficeSlot(string Id, string Kind, string Name, string Ability);

public sealed class Faction
{
    public int Id { get; set; }
    public int LeaderId { get; set; }
    public string Demand { get; set; } = "";
    public double Supporters { get; set; }
    public int Days { get; set; }
    public int MetDays { get; set; }
    public int CalmDays { get; set; }
}
public sealed class ActiveDecree { public string Kind { get; set; } = ""; public int DaysLeft { get; set; } }
public sealed class Exile { public string Name { get; set; } = ""; public double Count { get; set; } public int Day { get; set; } public string Demand { get; set; } = ""; }
public sealed class StratumState { public double Count { get; set; } public double Target { get; set; } }

/// <summary>政治の台帳(G19)。役職・予算・導入率・布告・派閥・反乱・層ごとの支持率・亡命者。</summary>
public sealed class Politics
{
    public SortedDictionary<string, int> Offices { get; set; } = new(StringComparer.Ordinal);   // 役職の枠 Id → 人物 Id
    public int InfraInvest { get; set; } = 100;
    public int Military { get; set; } = 50;
    public int RobotCap { get; set; } = 50;
    public List<ActiveDecree> Decrees { get; set; } = new();
    public List<Faction> Factions { get; set; } = new();
    public int NextFactionId { get; set; } = 1;
    public bool Rebellion { get; set; }
    public int RebellionCalmDays { get; set; }
    public int RebellionCooldown { get; set; }   // 鎮圧後に次の反乱が起きない日数
    public List<Exile> Exiles { get; set; } = new();
    public SortedDictionary<string, StratumState> Strata { get; set; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> JobHoles { get; set; } = new(StringComparer.Ordinal);   // 登用で元の職に空いた穴(表示用)
    // 1 時間ごとの係数(役職の効果)
    public double SupportBonus { get; set; }
    public double TaxMul { get; set; } = 1;
    public double FailureMul { get; set; } = 1;
    public double SpeedMul { get; set; } = 1;
    public double HitMul { get; set; } = 1;
    public double RetreatHp { get; set; } = 0.25;
    public double ConscriptionMul { get; set; } = 1;
    public double PriceMul { get; set; } = 1;
    public double HighQualityMul { get; set; } = 1;
    public double RobotShare { get; set; }
    public double FactionPenalty { get; set; }

    public bool HasDecree(string kind) => Decrees.Any(d => d.Kind == kind);
}

public static class PoliticsSim
{
    public static Politics Of(Factory f) => f.Politics ??= new Politics();

    /// <summary>今の段階と建物・隊の数で存在する役職の枠。</summary>
    public static List<OfficeSlot> Slots(Factory f, PoliticsData pd)
    {
        var list = new List<OfficeSlot>(); int stage = f.City?.Stage ?? 1;
        foreach (var o in pd.Offices)
        {
            if (stage < o.FromStage) continue;
            if (o.RequiresMech && f.Mechs.Count == 0 && stage < 2) continue;
            string name = o.Names[Math.Clamp(stage - 1, 0, o.Names.Length - 1)]; if (name == "") continue;
            if (o.PerResidences > 0) { int n = f.Buildings.Count(b => b.Use == "residence") / o.PerResidences; for (int i = 1; i <= n; i++) list.Add(new OfficeSlot($"{o.Kind}_{i}", o.Kind, $"{name} {i}", o.Ability)); }
            else if (o.PerSquads > 0) { int n = f.Squads.Count / o.PerSquads; for (int i = 1; i <= n; i++) list.Add(new OfficeSlot($"{o.Kind}_{i}", o.Kind, $"{name} {i}", o.Ability)); }
            else list.Add(new OfficeSlot(o.Kind, o.Kind, name, o.Ability));
        }
        return list;
    }

    /// <summary>軍事費の徴兵への係数: 50 以上で 1、0 で militaryConscriptionMin。</summary>
    public static double MilitaryMul(PoliticsData pd, int military) => military >= 50 ? 1 : pd.Budget["militaryConscriptionMin"] + (1 - pd.Budget["militaryConscriptionMin"]) * military / 50.0;

    static int Ability(Person p, string ability) => ability switch { "govern" => p.Govern, "produce" => p.Produce, "command" => p.Command, _ => p.Negotiate };

    /// <summary>役職の効果を係数に直す(1 時間ごと。CitySim.Hour の先頭で呼ぶ)。</summary>
    public static void Hour(Simulation sim)
    {
        var f = sim.Factory; var city = f.City; if (city == null) return;
        foreach (var sq in f.Squads.Where(q => q.CommanderId >= 0 && q.Alive == 0).ToList()) DismissCommander.Do(f, "squad", sq.Id);   // 全滅した隊の隊長は候補に戻る(G21 #1)
        foreach (var p in city.Persons.Where(p => p.Office != null && p.Office.Contains(':'))) { var parts = p.Office!.Split(':'); int id = int.Parse(parts[1]); bool ok = parts[0] == "squad" ? f.Squads.Any(q => q.Id == id && q.CommanderId == p.Id) : f.Armies.Any(a => a.Id == id && a.CommanderId == p.Id); if (!ok) p.Office = null; }
        var pd = sim.PoliticsData; var pol = Of(f);
        var slots = Slots(f, pd); var persons = city.Persons.ToDictionary(p => p.Id);
        // 消えた枠・人物の役職を外す
        foreach (var id in pol.Offices.Keys.ToList()) if (!slots.Any(s => s.Id == id) || !persons.ContainsKey(pol.Offices[id])) pol.Offices.Remove(id);
        double A(string kind) { var s = slots.FirstOrDefault(x => x.Kind == kind); if (s == null) return 5; return pol.Offices.TryGetValue(s.Id, out var pid) ? Ability(persons[pid], s.Ability) : 0; }
        double E(string kind, string key) => pd.Effects.TryGetValue(kind, out var e) && e.TryGetValue(key, out var v) ? v : 0;
        double head = A("head") - 5, fac = A("factory") - 5, mil = A("military") - 5, neg = A("negotiate") - 5, eng = slots.Any(s => s.Kind == "engineer") ? A("engineer") - 5 : 0, mar = slots.Any(s => s.Kind == "marshal") ? A("marshal") - 5 : 0;
        int SlotNet(string kind) => slots.Where(s => s.Kind == kind).Sum(s => pol.Offices.ContainsKey(s.Id) ? 1 : -1);
        pol.SupportBonus = head * E("head", "supportPerPoint") + SlotNet("district") * E("district", "supportPerSlot");
        pol.TaxMul = Math.Clamp(1 + head * E("head", "taxPerPoint"), 0.5, 1.5);
        pol.FailureMul = Math.Clamp(1 - fac * E("factory", "failurePerPoint"), 0.5, 1.5);
        pol.SpeedMul = Math.Clamp(1 + fac * E("factory", "speedPerPoint"), 0.5, 1.5);
        pol.HitMul = Math.Clamp(1 + mil * E("military", "hitPerPoint") + mar * E("marshal", "hitPerPoint"), 0.5, 1.5);   // 隊長・司令官は隊と軍への任命(G21)で、ここでは国の係数だけ
        pol.RetreatHp = Math.Clamp(sim.Combat.DefaultRetreatHp - mil * E("military", "retreatPerPoint"), 0.05, 0.6);
        pol.ConscriptionMul = Math.Clamp(1 + mil * E("military", "conscriptionPerPoint"), 0.5, 1.5) * MilitaryMul(pd, pol.Military);
        pol.PriceMul = Math.Clamp(1 + neg * E("negotiate", "pricePerPoint"), 0.5, 1.5);
        pol.HighQualityMul = Math.Clamp(1 + eng * E("engineer", "highQualityPerPoint"), 0.5, 2);
        // 製造ロボットの割合(工場労働者層の不満)
        int robots = 0, humans = 0;
        foreach (var b in f.Buildings) foreach (var fl in b.Floors) foreach (var m in fl.Structures.OfType<Machine>()) { robots += m.Staff.Count(w => w.IsRobot); humans += m.Staff.Count(w => !w.IsRobot); }
        pol.RobotShare = robots + humans == 0 ? 0 : (double)robots / (robots + humans);
        pol.FactionPenalty = city.Population <= 0 ? 0 : pol.Factions.Sum(x => x.Supporters) / city.Population * pd.Faction["supportPenaltyMul"];
        // 工場・戦闘・交易への反映
        f.FailureMulPct = (int)Math.Round(pol.FailureMul * 100);
        f.SpeedMulPct = (int)Math.Round(pol.SpeedMul * 100);
        f.HitMulPct = (int)Math.Round(pol.HitMul * 100);
        f.RetreatHp = pol.RetreatHp;
        f.PriceMulPct = (int)Math.Round(pol.PriceMul * 100);
        f.HighQualityMulPct = (int)Math.Round(pol.HighQualityMul * 100);
        f.AmmoResupply = pol.Military >= pd.Budget["ammoResupplyMin"];
    }

    /// <summary>層ごとの支持率の目標(G19 #7)。共通項 common に層ごとの項を足し、人数で加重平均したものを返す。</summary>
    public static double StrataTarget(Simulation sim, City city, double common, double foodTerm, double waterTerm, double unemployment, double conscriptionRate)
    {
        var f = sim.Factory; var pd = sim.PoliticsData; var pol = Of(f);
        double farmers = Math.Max(0, city.Employed - city.MachineStaff - city.Conscripts), workers = city.MachineStaff, military = city.Conscripts;
        double unemployed = Math.Max(0, city.Labor - city.Employed), other = Math.Max(0, city.Population - city.Labor);
        var counts = new Dictionary<string, double> { ["farmer"] = farmers, ["worker"] = workers, ["military"] = military, ["unemployed"] = unemployed, ["other"] = other };
        double sum = 0, n = 0;
        foreach (var (k, st) in pd.Strata)
        {
            double t = common + (foodTerm + waterTerm) * st.FoodWaterMul - st.RobotShare * pol.RobotShare - st.ConscriptionExtra * conscriptionRate - (k == "unemployed" ? st.UnemployedExtra : 0);
            t = Math.Clamp(t, 0, 100);
            if (!pol.Strata.TryGetValue(k, out var s)) pol.Strata[k] = s = new StratumState();
            s.Count = counts[k]; s.Target = t; sum += t * counts[k]; n += counts[k];
        }
        return n <= 0 ? Math.Clamp(common + foodTerm + waterTerm, 0, 100) : sum / n;
    }

    /// <summary>1 日ごと: 不満・派閥・反乱・布告・候補の入れ替わり。CitySim.Day の最後で呼ぶ。</summary>
    public static void Day(Simulation sim, City city, CityData.Band band)
    {
        var f = sim.Factory; var pd = sim.PoliticsData; var pol = Of(f); var rng = sim.Rng; var dc = pd.Discontent; var fc = pd.Faction;
        bool lowBand = band.Id is "low" or "danger" or "collapse", okBand = band.Id is "high" or "normal";
        // 不満
        foreach (var p in city.Persons)
        {
            bool inOffice = pol.Offices.ContainsValue(p.Id) || p.Office != null;   // 隊長・司令官も役職
            double d = band.Id == "low" ? dc["lowPerDay"] : lowBand ? dc["dangerPerDay"] : 0;
            if (!inOffice && p.Ambition >= dc["ambitiousMin"]) d += dc["ambitiousIdlePerDay"];
            if (inOffice && okBand) d -= dc["officeCalmPerDay"];
            p.Discontent = Math.Clamp(p.Discontent + d, 0, 10);
        }
        // 派閥の発生
        foreach (var p in city.Persons)
        {
            if (pol.Factions.Count >= fc["max"]) break;
            if (pol.Factions.Any(x => x.LeaderId == p.Id) || pol.Offices.ContainsValue(p.Id) || p.Office != null) continue;
            if (p.Loyalty > fc["loyaltyMax"] || p.Ambition < fc["ambitionMin"] || p.Discontent < fc["discontentMin"]) continue;
            string demand = WorstDemand(sim, city, pol);
            double layer = p.Job switch { "farmer" => pol.Strata.GetValueOrDefault("farmer")?.Count ?? 0, "worker" or "artisan" => pol.Strata.GetValueOrDefault("worker")?.Count ?? 0, "militia" => pol.Strata.GetValueOrDefault("military")?.Count ?? 0, _ => pol.Strata.GetValueOrDefault("other")?.Count ?? 0 };
            pol.Factions.Add(new Faction { Id = pol.NextFactionId++, LeaderId = p.Id, Demand = demand, Supporters = Math.Max(1, layer * fc["supporterShare"]) });
            f.Events.Add(new FactoryEvent { Kind = "faction", Power = p.Id });
        }
        // 派閥の解消・離反
        bool secure = city.Ratio("security") >= 1;
        foreach (var x in pol.Factions.ToList())
        {
            x.Days++; x.Supporters = Math.Min(x.Supporters, city.Population * fc["supporterShare"]);   // 支持者は人口の半分を超えない(流出で減る)
            x.MetDays = DemandMet(x.Demand, sim, city, pol) ? x.MetDays + 1 : 0;
            x.CalmDays = secure ? x.CalmDays + 1 : 0;
            if (x.MetDays >= fc["resolveDays"] || x.CalmDays >= fc["securityDays"])
            {
                var leader = city.Persons.FirstOrDefault(p => p.Id == x.LeaderId);
                if (leader != null) { leader.Discontent = Math.Max(0, leader.Discontent - fc["discontentMin"]); leader.Loyalty = Math.Min(10, leader.Loyalty + 1); }   // 要求が通ると落ち着く
                pol.Factions.Remove(x); f.Events.Add(new FactoryEvent { Kind = "faction_end", Power = x.LeaderId }); continue;
            }
            if (x.Days >= fc["defectDays"])
            {
                var leader = city.Persons.FirstOrDefault(p => p.Id == x.LeaderId);
                double leaving = x.Supporters * fc["defectShare"];
                city.Population = Math.Max(0, city.Population - leaving);
                pol.Exiles.Add(new Exile { Name = leader?.Name ?? $"#{x.LeaderId}", Count = leaving, Day = city.Days, Demand = x.Demand });
                var dest = f.Diplomacy?.Powers.Where(pw => !pw.Vassal).OrderBy(pw => pw.Relation).FirstOrDefault();   // 最も敵対的な勢力へ亡命(G20 #5)
                if (dest != null) { DiplomacySim.Relate(sim, dest, (int)sim.DiplomacyData.Relation["exile"]); dest.Strength += leaving * 0.01; dest.Population += leaving; }
                if (leader != null) city.Persons.Remove(leader);
                pol.Factions.Remove(x); f.Events.Add(new FactoryEvent { Kind = "defect", Power = x.LeaderId, Destroyed = (int)leaving });
            }
        }
        // 反乱(G19 #6): 失脚の帯で支持者が人口の 3 割を超えたら。治安の充足が続くか派閥が消えると鎮圧
        double supporters = pol.Factions.Sum(x => x.Supporters);
        if (pol.RebellionCooldown > 0) pol.RebellionCooldown--;
        if (!pol.Rebellion && pol.RebellionCooldown == 0 && band.Id == "collapse" && city.Population > 0 && supporters / city.Population > pd.Rebellion["supportersShare"]) { pol.Rebellion = true; pol.RebellionCalmDays = 0; f.Events.Add(new FactoryEvent { Kind = "rebellion" }); }
        if (pol.Rebellion)
        {
            city.Population = Math.Max(0, city.Population * (1 - pd.Rebellion["outflowPerDay"]));
            pol.RebellionCalmDays = secure ? pol.RebellionCalmDays + 1 : 0;
            if (pol.RebellionCalmDays >= pd.Rebellion["calmDays"] || pol.Factions.Count == 0)
            {   // 鎮圧: 派閥の支持者は半減し、cooldownDays は次の反乱が起きない
                pol.Rebellion = false; pol.RebellionCooldown = (int)pd.Rebellion["cooldownDays"];
                foreach (var x in pol.Factions) x.Supporters *= 0.5;
                f.Events.Add(new FactoryEvent { Kind = "rebellion_end" });
            }
        }
        // 布告の残り日数と配給の費用
        foreach (var d in pol.Decrees.ToList())
        {
            var def = pd.Decrees[d.Kind];
            if (def.CostPerPersonPerDay > 0) f.Treasury -= (long)Math.Round(city.Population * def.CostPerPersonPerDay);
            if (--d.DaysLeft <= 0) pol.Decrees.Remove(d);
        }
        // 候補の入れ替わり(役職に就いていない候補は一定日数で去る)
        city.Persons.RemoveAll(p => !pol.Offices.ContainsValue(p.Id) && p.Office == null && city.Days - p.AppearedDay >= pd.CandidateExpireDays && !pol.Factions.Any(x => x.LeaderId == p.Id));
    }

    /// <summary>1 か月ごと: 不満が高い人物の忠誠が下がる。</summary>
    public static void Month(Simulation sim, City city)
    {
        var dc = sim.PoliticsData.Discontent;
        foreach (var p in city.Persons) if (p.Discontent >= dc["loyaltyDropAt"]) p.Loyalty = Math.Max(1, p.Loyalty - 1);
    }

    static string WorstDemand(Simulation sim, City city, Politics pol)
    {
        var pd = sim.PoliticsData;
        var bad = new Dictionary<string, double>
        {
            ["robot"] = pol.RobotShare,
            ["tax"] = city.TaxRate / Math.Max(0.01, sim.CityData.MaxTaxRate),
            ["conscription"] = city.ConscriptionRate / Math.Max(0.01, sim.CityData.MaxConscriptionRate),
            ["foodwater"] = 1 - Math.Min(city.Ratio("food"), city.Ratio("water")),
            ["security"] = 1 - city.Ratio("security"),
        };
        return bad.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).First().Key;
    }

    public static bool DemandMet(string demand, Simulation sim, City city, Politics pol)
    {
        var m = sim.PoliticsData.DemandMet;
        return demand switch
        {
            "robot" => pol.RobotCap <= m["robotCapMax"],
            "tax" => city.TaxRate <= m["taxMax"] || pol.HasDecree("taxcut"),
            "conscription" => city.ConscriptionRate <= m["conscriptionMax"],
            "foodwater" => (city.Ratio("food") >= 1 && city.Ratio("water") >= 1) || pol.HasDecree("ration"),
            _ => city.Ratio("security") >= 1,
        };
    }

    /// <summary>次の月の見込み(G19 #12)。乱数なしで、与えた政策での支持率の目標・国庫の増減(30 日)・徴兵の充足を出す。</summary>
    public static (double supportTarget, double treasuryDelta, double conscripts, double crewNeed) Forecast(Simulation sim, double taxRate, double conscriptionRate, int infra, int military)
    {
        var f = sim.Factory; var city = f.City!; var cd = sim.CityData; var pd = sim.PoliticsData; var pol = Of(f);
        var band = cd.BandOf(city.Support);
        double conscripts = Math.Min(city.CrewNeed, Math.Max(0, city.Labor - city.MachineStaff) * conscriptionRate * band.ConscriptionMul * (pol.ConscriptionMul / MilitaryMul(pd, pol.Military)) * MilitaryMul(pd, military));
        double supplyMul = pd.Budget["infraSupplyMin"] + (1 - pd.Budget["infraSupplyMin"]) * infra / 100.0;
        double curMul = pd.Budget["infraSupplyMin"] + (1 - pd.Budget["infraSupplyMin"]) * pol.InfraInvest / 100.0;
        double Adj(string k) { var s = city.Supplies.GetValueOrDefault(k); if (s == null || s.Need <= 0) return 1; return Math.Min(1.5, s.Have / curMul * supplyMul / s.Need); }
        var w = cd.Support;
        double common = CitySim.CommonTarget(sim, city, pol, taxRate, Math.Min(1, Adj("security")), Math.Min(1, Adj("park")), Math.Min(1, Adj("waste")));
        double target = StrataTargetPreview(sim, city, common, w["food"] * (Adj("food") - 1), w["water"] * (Adj("water") - 1), conscriptionRate);
        double tax = city.Employed * cd.IncomePerWorkerPerDay * (pol.HasDecree("taxcut") ? 0 : taxRate) * band.TaxMul * pol.TaxMul;
        double upkeep = city.Stage >= cd.UpkeepFromStage ? f.Buildings.Sum(b => cd.Uses.TryGetValue(b.Use, out var u) && b.Use != "factory" ? CitySim.CellsOf(b, u) * cd.UpkeepPerCellPerDay : 0) * infra / 100.0 : 0;
        double pay = conscripts * cd.SoldierPayPerDay * military / 50.0;
        double ration = pol.Decrees.Sum(d => pd.Decrees[d.Kind].CostPerPersonPerDay) * city.Population;
        return (Math.Clamp(target, 0, 100), (tax - upkeep - pay - ration) * 30, conscripts, city.CrewNeed);
    }
    static double StrataTargetPreview(Simulation sim, City city, double common, double foodTerm, double waterTerm, double conscriptionRate)
    {
        var pd = sim.PoliticsData; var pol = Of(sim.Factory); double sum = 0, n = 0;
        foreach (var (k, st) in pd.Strata)
        {
            double cnt = pol.Strata.GetValueOrDefault(k)?.Count ?? 0;
            double t = Math.Clamp(common + (foodTerm + waterTerm) * st.FoodWaterMul - st.RobotShare * pol.RobotShare - st.ConscriptionExtra * conscriptionRate - (k == "unemployed" ? st.UnemployedExtra : 0), 0, 100);
            sum += t * cnt; n += cnt;
        }
        return n <= 0 ? Math.Clamp(common + foodTerm + waterTerm, 0, 100) : sum / n;
    }
}

// ---------- 命令 ----------
/// <summary>登用: 役職の枠に人物を置く。枠が無い・人物が候補でない・既に役職に就いているときは何もしない。</summary>
public sealed record Appoint(string Office, int PersonId) : ICommand
{
    public void Apply(Simulation s)
    {
        var f = s.Factory; var city = f.City; if (city == null) return; var pol = PoliticsSim.Of(f);
        var slot = PoliticsSim.Slots(f, s.PoliticsData).FirstOrDefault(x => x.Id == Office); if (slot == null) return;
        var p = city.Persons.FirstOrDefault(x => x.Id == PersonId); if (p == null || pol.Offices.ContainsValue(PersonId) || p.Office != null) return;   // 隊長・司令官との掛け持ちも不可(G21)
        if (pol.Offices.TryGetValue(Office, out var prev)) Dismiss.Do(s, Office, prev);
        pol.Offices[Office] = PersonId; p.Office = Office;
        pol.JobHoles[p.Job] = pol.JobHoles.GetValueOrDefault(p.Job) + 1;
        pol.Factions.RemoveAll(x => x.LeaderId == PersonId);   // 首領を登用すると派閥は消える
        PoliticsSim.Hour(s);
    }
}

/// <summary>解任: 人物は候補に戻り、忠誠 −3・不満 +3。</summary>
public sealed record Dismiss(string Office) : ICommand
{
    public void Apply(Simulation s) { var pol = PoliticsSim.Of(s.Factory); if (pol.Offices.TryGetValue(Office, out var pid)) { Do(s, Office, pid); PoliticsSim.Hour(s); } }
    internal static void Do(Simulation s, string office, int pid)
    {
        var pol = PoliticsSim.Of(s.Factory); pol.Offices.Remove(office);
        var p = s.Factory.City?.Persons.FirstOrDefault(x => x.Id == pid); if (p == null) return;
        p.Office = null; p.Loyalty = Math.Max(1, p.Loyalty - (int)s.PoliticsData.Discontent["dismiss"]); p.Discontent = Math.Min(10, p.Discontent + s.PoliticsData.Discontent["dismiss"]);
        pol.JobHoles[p.Job] = Math.Max(0, pol.JobHoles.GetValueOrDefault(p.Job) - 1);
    }
}

/// <summary>予算: インフラ投資と軍事費(0〜100)。</summary>
public sealed record SetBudget(int InfraInvest, int Military) : ICommand
{
    public void Apply(Simulation s) { var pol = PoliticsSim.Of(s.Factory); pol.InfraInvest = Math.Clamp(InfraInvest, 0, 100); pol.Military = Math.Clamp(Military, 0, 100); PoliticsSim.Hour(s); }
}

/// <summary>製造ロボットの導入率(設備の要員のうちロボットに置き換えてよい上限 %)。</summary>
public sealed record SetRobotCap(int Pct) : ICommand
{
    public void Apply(Simulation s) => PoliticsSim.Of(s.Factory).RobotCap = Math.Clamp(Pct, 0, 100);
}

/// <summary>布告: 配給 / 減税の布告 / 祭り。効果中の同じ布告は重ねられない。</summary>
public sealed record Decree(string Kind) : ICommand
{
    public void Apply(Simulation s)
    {
        var f = s.Factory; var city = f.City; if (city == null || !s.PoliticsData.Decrees.TryGetValue(Kind, out var def)) return;
        var pol = PoliticsSim.Of(f); if (pol.HasDecree(Kind)) return;
        if (def.CostPerPerson > 0) f.Treasury -= (long)Math.Round(city.Population * def.CostPerPerson);
        if (def.Instant) city.Support = Math.Clamp(city.Support + def.SupportBonus, 0, 100);   // 祭りは即時(Days は重ねられない期間)
        if (def.Days > 0) pol.Decrees.Add(new ActiveDecree { Kind = Kind, DaysLeft = def.Days });
        f.Events.Add(new FactoryEvent { Kind = "decree", Building = Array.IndexOf(s.PoliticsData.Decrees.Keys.ToArray(), Kind) });
    }
}
