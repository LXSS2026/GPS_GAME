using System.Text.Json;
using System.Text.Json.Serialization;

namespace GPS.Sim;

/// <summary>都市のデータ(city.json)。</summary>
public sealed class CityData
{
    public sealed record Use(string Name, Dictionary<string, double> Supplies, double StaffPerCell, int MaxFloors, int Priority);
    public sealed record Band(string Id, double Min, double InflowMul, double OutflowPerDay, double ConscriptionMul, double ResignPerDay, double TaxMul, int LoyaltyPerMonth, int CandidateLoyalty, double SecurityDemandMul);
    public double LaborRate = 0.6, FoodPerPerson = 1, WaterPerPerson = 1, KwPerPerson = 0.1;
    public int CrewPerMech = 3;
    public double IncomePerWorkerPerDay = 10, SoldierPayPerDay = 2, UpkeepPerCellPerDay = 0.2;
    public int UpkeepFromStage = 3;
    public double DefaultTaxRate = 0.1, DefaultConscriptionRate = 0.05, MaxTaxRate = 0.5, MaxConscriptionRate = 0.5;
    public double BirthRatePerDay = 0.001, InflowRatePerDay = 0.02, ShortageOutflowPerDay = 0.1;
    public Dictionary<string, Use> Uses = new();
    public List<string> HousingPrerequisites = new();
    public Dictionary<string, double> Support = new();
    public List<Band> Bands = new();   // 高い順
    public int RiotEfficiencyPct = 70, CollapseDays = 30, RoadReach = 6;
    public List<int> StagePopulationMin = new();
    public double StageSupportMin = 60; public int StageDays = 30;
    public int PersonsPerMonthMin = 1, PersonsPerMonthMax = 3, AbilityBase = 5, AbilityByEducation = 5;
    public List<string> Names = new();

    public static CityData Load(string dir)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "city.json")));
        var r = doc.RootElement; var c = new CityData();
        double D(string k) => r.GetProperty(k).GetDouble(); int I(string k) => r.GetProperty(k).GetInt32();
        c.LaborRate = D("laborRate"); c.FoodPerPerson = D("foodPerPerson"); c.WaterPerPerson = D("waterPerPerson"); c.KwPerPerson = D("kwPerPerson"); c.CrewPerMech = I("crewPerMech");
        c.IncomePerWorkerPerDay = D("incomePerWorkerPerDay"); c.SoldierPayPerDay = D("soldierPayPerDay"); c.UpkeepPerCellPerDay = D("upkeepPerCellPerDay"); c.UpkeepFromStage = I("upkeepFromStage");
        c.DefaultTaxRate = D("defaultTaxRate"); c.DefaultConscriptionRate = D("defaultConscriptionRate"); c.MaxTaxRate = D("maxTaxRate"); c.MaxConscriptionRate = D("maxConscriptionRate");
        c.BirthRatePerDay = D("birthRatePerDay"); c.InflowRatePerDay = D("inflowRatePerDay"); c.ShortageOutflowPerDay = D("shortageOutflowPerDay");
        foreach (var u in r.GetProperty("uses").EnumerateObject())
            c.Uses[u.Name] = new Use(u.Value.GetProperty("name").GetString()!, u.Value.GetProperty("supplies").EnumerateObject().ToDictionary(s => s.Name, s => s.Value.GetDouble()), u.Value.GetProperty("staffPerCell").GetDouble(), u.Value.GetProperty("maxFloors").GetInt32(), u.Value.TryGetProperty("priority", out var pr) ? pr.GetInt32() : 99);
        c.HousingPrerequisites = r.GetProperty("housingPrerequisites").EnumerateArray().Select(e => e.GetString()!).ToList();
        c.Support = r.GetProperty("support").EnumerateObject().ToDictionary(s => s.Name, s => s.Value.GetDouble());
        foreach (var b in r.GetProperty("bands").EnumerateObject())
        {
            var v = b.Value;
            c.Bands.Add(new Band(b.Name, v.GetProperty("min").GetDouble(), v.GetProperty("inflowMul").GetDouble(), v.GetProperty("outflowPerDay").GetDouble(), v.GetProperty("conscriptionMul").GetDouble(), v.GetProperty("resignPerDay").GetDouble(), v.GetProperty("taxMul").GetDouble(), v.GetProperty("loyaltyPerMonth").GetInt32(), v.GetProperty("candidateLoyalty").GetInt32(), v.GetProperty("securityDemandMul").GetDouble()));
        }
        c.Bands = c.Bands.OrderByDescending(b => b.Min).ToList();
        c.RiotEfficiencyPct = I("riotEfficiencyPct"); c.CollapseDays = I("collapseDays"); c.RoadReach = I("roadReach");
        c.StagePopulationMin = r.GetProperty("stages").GetProperty("populationMin").EnumerateArray().Select(e => e.GetInt32()).ToList();
        c.StageSupportMin = r.GetProperty("stages").GetProperty("supportMin").GetDouble(); c.StageDays = r.GetProperty("stages").GetProperty("days").GetInt32();
        var ps = r.GetProperty("persons"); c.PersonsPerMonthMin = ps.GetProperty("perMonthMin").GetInt32(); c.PersonsPerMonthMax = ps.GetProperty("perMonthMax").GetInt32(); c.AbilityBase = ps.GetProperty("abilityBase").GetInt32(); c.AbilityByEducation = ps.GetProperty("abilityByEducation").GetInt32();
        c.Names = r.GetProperty("names").EnumerateArray().Select(e => e.GetString()!).ToList();
        return c;
    }
    public Band BandOf(double support) => Bands.First(b => support >= b.Min);
}

/// <summary>人物(登用候補)。登用・役職・派閥は手順 4.5。</summary>
public sealed class Person
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Govern { get; set; }
    public int Produce { get; set; }
    public int Command { get; set; }
    public int Negotiate { get; set; }
    public int Loyalty { get; set; }
    public int Ambition { get; set; }
    public int AppearedDay { get; set; }
    /// <summary>元の職(farmer / worker / militia / artisan / merchant)。</summary>
    public string Job { get; set; } = "farmer";
    public double Discontent { get; set; }
    public string? Office { get; set; }
    public int Age { get; set; } = 30;   // 死亡と年齢は未実装(前提として欄だけ)
}

/// <summary>インフラ 1 種の 1 時間の集計(供給・需要・充足率)。</summary>
public sealed class Supply { public double Have { get; set; } public double Need { get; set; } public double Ratio { get; set; } = 1; /// <summary>余剰込みの充足(上限 1.5)。食料・水の支持率の加点に使う。</summary>
    public double Surplus { get; set; } = 1; }

/// <summary>都市の台帳(G17)。人口は集計で持ち、個人として持つのは人物だけ。</summary>
public sealed class City
{
    public string Name { get; set; } = "";
    public double Population { get; set; }
    public double Support { get; set; } = 50;
    public double SupportTarget { get; set; } = 50;
    public int Stage { get; set; } = 1;
    public int StageDays { get; set; }
    public int CollapseDays { get; set; }
    public bool Defeated { get; set; }
    public double TaxRate { get; set; }
    public double ConscriptionRate { get; set; }
    public int NextWorkerId { get; set; } = 1000;
    public int NextPersonId { get; set; } = 1;
    public List<Person> Persons { get; set; } = new();
    public int Days { get; set; }
    /// <summary>拠点の建物(落ちると征服。G20 #6)。</summary>
    public int Capital { get; set; } = -1;
    // 1 時間ごとの集計(保存するが、読み込み後に再計算される)
    public double Labor { get; set; }
    public double Jobs { get; set; }
    public double Employed { get; set; }
    public double Unemployment { get; set; }
    public double MachineStaff { get; set; }
    public double CrewNeed { get; set; }
    public double Conscripts { get; set; }
    public double PopulationCap { get; set; }
    public double Pollution { get; set; }
    public bool Riot { get; set; }
    public string Band { get; set; } = "normal";
    public SortedDictionary<string, Supply> Supplies { get; set; } = new(StringComparer.Ordinal);
    public double LastTax { get; set; }
    public double LastUpkeep { get; set; }
    public double LastPay { get; set; }
    public double LastBirths { get; set; }
    public double LastInflow { get; set; }
    public double LastOutflow { get; set; }

    public double Ratio(string kind) => Supplies.TryGetValue(kind, out var s) ? s.Ratio : 1;
}

/// <summary>都市の計算。1 時間ごとに集計(雇用・供給・支持率)、1 日ごとに人口・税・段階・失脚、1 か月ごとに人物。</summary>
public static class CitySim
{
    public static void Tick(Simulation sim)
    {
        var f = sim.Factory; var city = f.City; if (city == null) return;
        if (sim.Tick % Clock.TicksPerHour != 0) return;
        Hour(sim, city);
        if (sim.Tick > 0 && sim.Tick % Clock.TicksPerDay == 0) Day(sim, city);   // ティック 0 は集計だけ(日の処理は最初の日の終わりから)
    }

    /// <summary>段階で未解放のインフラは充足率 1 として扱う(村に下水処理は無い)。食料と水(井戸・畑)は段階を問わず効く。</summary>
    static bool Unlocked(string kind, City city, GameData data) => kind is "food" or "water" || !data.Infrastructure.TryGetValue(kind, out var lv) || city.Stage >= lv;

    /// <summary>設備の要員のうち人間の数(製造ロボットは数えない)。</summary>
    public static int HumanStaff(Factory f) => f.Buildings.Sum(b => b.Floors.Sum(fl => fl.Structures.OfType<Machine>().Sum(m => m.Staff.Count(w => !w.IsRobot))));

    public static void Hour(Simulation sim, City city)
    {
        var f = sim.Factory; var cd = sim.CityData; var data = sim.Data;
        var band = cd.BandOf(city.Support); city.Band = band.Id;
        PoliticsSim.Hour(sim); DiplomacySim.Hour(sim); var pol = PoliticsSim.Of(f); var pd = sim.PoliticsData;
        // 雇用: 設備の要員 → 軍の要員(徴兵) → 都市の供給設備の要員 の順に労働人口を使う
        city.Labor = city.Population * cd.LaborRate;
        city.MachineStaff = HumanStaff(f);
        city.CrewNeed = f.Squads.Where(s => s.Faction == 1).Sum(s => s.Alive) * cd.CrewPerMech;   // 自軍の隊だけ(AI 勢力の隊は数えない)
        city.Conscripts = Math.Min(city.CrewNeed, Math.Max(0, city.Labor - city.MachineStaff) * city.ConscriptionRate * band.ConscriptionMul * pol.ConscriptionMul);   // 軍の長と軍事費の係数(G19)
        // 都市の供給設備の要員は用途の優先順(井戸 → 畑 → 警察 …)に埋める。余った人が失業
        double left = Math.Max(0, city.Labor - city.MachineStaff - city.Conscripts), useJobs = 0, useFilled = 0;
        var fillOf = new Dictionary<string, double>();
        foreach (var (uid, u) in cd.Uses.OrderBy(kv => kv.Value.Priority))
        {
            double jobs = f.Buildings.Where(b => b.Use == uid).Sum(b => Cells(b, u) * u.StaffPerCell);
            double filled = Math.Min(jobs, left); left -= filled; useJobs += jobs; useFilled += filled;
            fillOf[uid] = jobs <= 0 ? 1 : filled / jobs;
        }
        city.Jobs = city.MachineStaff + city.CrewNeed + useJobs;
        city.Employed = Math.Min(city.Labor, city.MachineStaff + city.Conscripts + useFilled);
        city.Unemployment = city.Labor <= 0 ? 0 : Math.Clamp(1 - city.Employed / city.Labor, 0, 1);
        // 供給と需要
        var sup = new SortedDictionary<string, Supply>(StringComparer.Ordinal);
        Supply S(string k) { if (!sup.TryGetValue(k, out var s)) sup[k] = s = new Supply(); return s; }
        double riotMul = (city.Riot ? cd.RiotEfficiencyPct / 100.0 : 1) * (pol.Rebellion ? pd.Rebellion["efficiencyPct"] / 100.0 : 1) * (pd.Budget["infraSupplyMin"] + (1 - pd.Budget["infraSupplyMin"]) * pol.InfraInvest / 100.0);   // 暴動・反乱・インフラ投資
        int residences = 0, connected = 0;
        foreach (var b in f.Buildings)
        {
            var u = cd.Uses.GetValueOrDefault(b.Use); if (u == null) continue;
            foreach (var (k, per) in u.Supplies) S(k).Have += Cells(b, u) * per * (u.StaffPerCell > 0 ? fillOf[b.Use] : 1) * (k is "housing" or "security" ? 1 : riotMul);   // 暴動・反乱は治安(警察)の能力を落とさない
            if (b.Use == "residence") { residences++; if (b.RoadConnected) connected++; }
        }
        S("food").Need = city.Population * cd.FoodPerPerson; S("water").Need = city.Population * cd.WaterPerPerson;
        foreach (var k in new[] { "sewage", "waste", "medical", "education", "park" }) S(k).Need = city.Population;
        S("security").Need = city.Population * Math.Max(band.SecurityDemandMul, pol.Rebellion ? pd.Rebellion["securityDemandMul"] : 1) + pol.Factions.Sum(x => x.Supporters);   // 反乱中は 3 倍(帯の 2 倍と重ねない)+ 派閥の支持者
        S("power").Have = f.Buildings.Sum(b => Math.Max(0, b.LastSupply - b.LastDemand)); S("power").Need = city.Population * cd.KwPerPerson;
        S("road").Have = connected; S("road").Need = residences;
        S("housing").Need = city.Population;
        foreach (var (k, s) in sup) { s.Ratio = !Unlocked(k, city, data) ? 1 : s.Need <= 0 ? 1 : Math.Min(1, s.Have / s.Need); s.Surplus = s.Need <= 0 ? 1 : Math.Min(1.5, s.Have / s.Need); }
        if (pol.HasDecree("ration")) foreach (var k in new[] { "food", "water" }) { sup[k].Ratio = 1; sup[k].Surplus = Math.Max(1, sup[k].Surplus); }   // 配給の布告
        city.Supplies = sup;
        double prereq = cd.HousingPrerequisites.Min(k => city.Ratio(k));
        city.PopulationCap = S("housing").Have * prereq;
        city.Pollution = Math.Min(cd.Support["pollutionCap"], f.Buildings.Sum(b => b.Floors.Sum(fl => fl.Structures.OfType<Machine>().Count())) * cd.Support["pollutionPerMachine"]);
        // 暴動: 危険の帯で治安が足りないと生産効率が落ちる
        city.Riot = band.SecurityDemandMul > 1 && city.Ratio("security") < 1;
        f.EfficiencyPct = (int)Math.Round((city.Riot ? cd.RiotEfficiencyPct : 100) * (pol.Rebellion ? pd.Rebellion["efficiencyPct"] / 100.0 : 1) * pol.SpeedMul);   // 暴動・反乱・工場長
        // 支持率の目標と接近
        var w = cd.Support;
        // 共通項 + 層ごとの項(G19 #7)。食料・水は余剰(1.5 倍まで)で加点、不足で減点
        double common = CommonTarget(sim, city, pol, city.TaxRate, city.Ratio("security"), city.Ratio("park"), city.Ratio("waste"));
        double target = PoliticsSim.StrataTarget(sim, city, common, w["food"] * (sup["food"].Surplus - 1), w["water"] * (sup["water"].Surplus - 1), city.Unemployment, city.ConscriptionRate);
        city.SupportTarget = Math.Clamp(target, 0, 100);
        city.Support = Math.Clamp(city.Support + (city.SupportTarget - city.Support) * w["approachPerHour"], 0, 100);
    }

    /// <summary>支持率の目標の全層共通の項(基準・失業・税率・徴兵率・治安・公園・廃棄物・公害・国庫・首長の効果・派閥・布告)。</summary>
    public static double CommonTarget(Simulation sim, City city, Politics pol, double taxRate, double security, double park, double waste)
    {
        var w = sim.CityData.Support; var f = sim.Factory;
        double decree = pol.Decrees.Where(d => !sim.PoliticsData.Decrees[d.Kind].Instant).Sum(d => sim.PoliticsData.Decrees[d.Kind].SupportBonus);
        if (pol.HasDecree("taxcut")) taxRate = 0;
        return w["base"] - w["unemployment"] * city.Unemployment - w["tax"] * taxRate - w["conscription"] * city.ConscriptionRate + w["security"] * (security - 1) + w["park"] * park
               - w["waste"] * (1 - waste) - city.Pollution - (f.Treasury < 0 ? w["treasuryNegative"] : 0) + pol.SupportBonus - pol.FactionPenalty + decree;
    }
    public static double CellsOf(Building b, CityData.Use u) => Cells(b, u);

    static double Cells(Building b, CityData.Use u)
    {
        if (b.Floors.Count == 0 || b.Destroyed) return 0;
        int floors = u.MaxFloors > 0 ? Math.Min(u.MaxFloors, b.Floors.Count) : b.Floors.Count;
        return (double)b.Floors[0].W * b.Floors[0].D * floors;
    }

    public static void Day(Simulation sim, City city)
    {
        var f = sim.Factory; var cd = sim.CityData; var rng = sim.Rng;
        var band = cd.BandOf(city.Support); city.Days++;
        // 人口の増減(飢えと渇きは流出で表す)
        double pop = city.Population;
        city.LastBirths = pop * cd.BirthRatePerDay * city.Ratio("medical");
        double freeHousing = Math.Max(0, city.PopulationCap - pop), freeJobs = Math.Max(0, city.Jobs - city.Employed) / cd.LaborRate;
        city.LastInflow = band.InflowMul > 0 ? Math.Min(Math.Max(1, pop * cd.InflowRatePerDay * band.InflowMul), Math.Min(freeHousing, freeJobs)) : 0;
        double outflow = pop * band.OutflowPerDay + pop * (1 - city.Ratio("food")) * cd.ShortageOutflowPerDay + pop * (1 - city.Ratio("water")) * cd.ShortageOutflowPerDay + Math.Max(0, pop - city.PopulationCap);
        city.LastOutflow = Math.Min(pop, outflow);
        city.Population = Math.Max(0, pop + city.LastBirths + city.LastInflow - city.LastOutflow);
        // 税と支出
        var pol = PoliticsSim.Of(f); var pd = sim.PoliticsData;
        city.LastTax = pol.Rebellion ? 0 : city.Employed * cd.IncomePerWorkerPerDay * (pol.HasDecree("taxcut") ? 0 : city.TaxRate) * band.TaxMul * pol.TaxMul;   // 反乱中は税収 0、減税の布告、首長の係数
        city.LastUpkeep = city.Stage >= cd.UpkeepFromStage ? f.Buildings.Sum(b => cd.Uses.TryGetValue(b.Use, out var u) && b.Use != "factory" ? Cells(b, u) * cd.UpkeepPerCellPerDay : 0) * pol.InfraInvest / 100.0 : 0;   // インフラ投資の係数
        city.LastPay = city.Conscripts * cd.SoldierPayPerDay * pol.Military / 50.0;   // 軍事費の係数
        f.Treasury += (long)Math.Round(city.LastTax - city.LastUpkeep - city.LastPay);
        // 離職: 低い帯では設備の要員が外れる
        if (band.ResignPerDay > 0)
        {
            int p = (int)(band.ResignPerDay * 10_000);
            foreach (var b in f.Buildings) foreach (var fl in b.Floors) foreach (var m in fl.Structures.OfType<Machine>())
                m.Staff.RemoveAll(w => !w.IsRobot && rng.Next(10_000) < p);
        }
        // 段階の昇格(降格はしない)
        if (city.Stage - 1 < cd.StagePopulationMin.Count && city.Population >= cd.StagePopulationMin[city.Stage - 1] && city.Support >= cd.StageSupportMin) city.StageDays++; else city.StageDays = 0;
        if (city.Stage - 1 < cd.StagePopulationMin.Count && city.StageDays >= cd.StageDays) { city.Stage++; city.StageDays = 0; f.Events.Add(new FactoryEvent { Kind = "stage", Power = city.Stage }); }
        // 失脚
        if (band.Id == "collapse") { if (++city.CollapseDays >= cd.CollapseDays && !city.Defeated) { city.Defeated = true; f.Events.Add(new FactoryEvent { Kind = "defeat" }); } }
        else city.CollapseDays = 0;
        // 人物(1 か月ごと)
        if (city.Days % 30 == 0)
        {
            foreach (var p in city.Persons) p.Loyalty = Math.Clamp(p.Loyalty + band.LoyaltyPerMonth, 1, 10);
            PoliticsSim.Month(sim, city);
            int n = cd.PersonsPerMonthMin + rng.Next(cd.PersonsPerMonthMax - cd.PersonsPerMonthMin + 1);
            int cap = cd.AbilityBase + (int)Math.Round(cd.AbilityByEducation * city.Ratio("education"));
            for (int i = 0; i < n; i++)
            {
                int A() => 1 + rng.Next(Math.Max(1, cap));
                city.Persons.Add(new Person { Id = city.NextPersonId, Name = $"{cd.Names[rng.Next(cd.Names.Count)]} #{city.NextPersonId}", Govern = A(), Produce = A(), Command = A(), Negotiate = A(),
                    Loyalty = Math.Clamp(1 + rng.Next(10) + band.CandidateLoyalty, 1, 10), Ambition = 1 + rng.Next(10), AppearedDay = city.Days,
                    Job = sim.PoliticsData.Jobs.Keys.ElementAt(rng.Next(sim.PoliticsData.Jobs.Count)), Age = 20 + rng.Next(31) });
                city.NextPersonId++;
            }
        }
        PoliticsSim.Day(sim, city, band);
        DiplomacySim.Day(sim);
    }

    /// <summary>隊の要員が徴兵で埋まっているか(Id の順に充てる)。都市が無ければ常に可。</summary>
    public static bool CanDeploy(Factory f, Squad sq, CityData cd)
    {
        var city = f.City; if (city == null) return true;
        double cover = city.Conscripts;
        foreach (var s in f.Squads.OrderBy(s => s.Id))
        {
            double need = s.Alive * cd.CrewPerMech;
            if (s.Id == sq.Id) return cover + 1e-9 >= need;
            cover -= need;
        }
        return false;
    }

    /// <summary>道路の接続: 建物の外周 roadReach セル(既定 6 m)以内に街路のブロックがあるか(高さは 0〜8 を見る)。</summary>
    public static void RefreshRoads(Simulation sim)
    {
        var f = sim.Factory; if (f.City == null) return; int reach = sim.CityData.RoadReach;
        var roads = new HashSet<byte>(new[] { "RoadX", "RoadY", "Asphalt" }.Where(sim.Blocks.Has).Select(sim.Blocks.IdOf));
        foreach (var b in f.Buildings)
        {
            if (b.Floors.Count == 0) continue;
            int x0 = (int)Math.Round(b.X), z0 = (int)Math.Round(b.Y), w = b.Floors[0].W, d = b.Floors[0].D; bool ok = false;
            for (int i = -reach; i < w + reach && !ok; i++) for (int j = -reach; j < d + reach && !ok; j++)
            {
                if (i >= 0 && i < w && j >= 0 && j < d) continue;
                for (int y = 0; y <= 8 && !ok; y++) if (roads.Contains(sim.World.Get(x0 + i, y, z0 + j))) ok = true;
            }
            b.RoadConnected = ok;
        }
    }
}

// ---------- 命令 ----------
/// <summary>都市を置く(開始時の村)。人口と初期の支持率。</summary>
public sealed record FoundCity(string Name, int Population, double Support = 50) : ICommand
{
    public void Apply(Simulation s)
    {
        if (s.Factory.City != null) return;
        s.Factory.City = new City { Name = Name, Population = Population, Support = Support, SupportTarget = Support, TaxRate = s.CityData.DefaultTaxRate, ConscriptionRate = s.CityData.DefaultConscriptionRate };
    }
}

/// <summary>建物の用途(工場 / 住居 / 畑 / 井戸 / 市場 / 学校 / 診療所 / 警察 / 公園 / 下水処理 / 廃棄物処理)。</summary>
public sealed record SetBuildingUse(int Building, string Use) : ICommand
{
    public void Apply(Simulation s) { var b = s.Factory.Get(Building); if (b != null && s.CityData.Uses.ContainsKey(Use)) b.Use = Use; }
}

public sealed record SetTaxRate(double Rate) : ICommand
{
    public void Apply(Simulation s) { if (s.Factory.City != null) s.Factory.City.TaxRate = Math.Clamp(Rate, 0, s.CityData.MaxTaxRate); }
}

public sealed record SetConscriptionRate(double Rate) : ICommand
{
    public void Apply(Simulation s) { if (s.Factory.City != null) s.Factory.City.ConscriptionRate = Math.Clamp(Rate, 0, s.CityData.MaxConscriptionRate); }
}

/// <summary>都市の労働人口から要員を 1 人引き当てて設備に付ける(名前と技能は都市が決める。技能は教育の充足で上がる)。空きが無ければ何もしない。</summary>
public sealed record HireStaff(int Building, int Floor, int Id) : ICommand
{
    public void Apply(Simulation s)
    {
        var city = s.Factory.City; if (city == null) return;
        if (s.Factory.Get(Building)?.Find(Floor, Id) is not Machine m) return;
        if (!s.Factory.Creative && CitySim.HumanStaff(s.Factory) + city.Conscripts >= city.Population * s.CityData.LaborRate) return;
        int skill = s.Rng.Next(4) + (int)(city.Ratio("education") * s.Rng.Next(7));
        m.Staff.Add(new Worker(city.NextWorkerId, $"{s.CityData.Names[s.Rng.Next(s.CityData.Names.Count)]} #{city.NextWorkerId}", Math.Min(10, skill)));
        city.NextWorkerId++;
        city.MachineStaff = CitySim.HumanStaff(s.Factory);
    }
}
