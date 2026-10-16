using System.Text.Json;
using System.Text.Json.Serialization;

namespace GPS.Sim;

/// <summary>戦闘のデータ(combat.json)。</summary>
public sealed class CombatData
{
    public sealed record Arch(double Hp, double Speed, double Weight, string Primary, string Melee);
    public sealed record Weapon(string Kind, double Damage, double Range, double Accuracy, int Cooldown, int AmmoPerShot = 0, long EnergyPerShot = 0, bool Missile = false, string? Melee = null, double Mul = 0);
    public Dictionary<string, Arch> Archetype { get; set; } = new();
    public double GenerationMul { get; set; } = 0.1;
    public double HpPerQuality { get; set; } = 10;
    public double SpeedAtQ70 { get; set; } = 8;
    public double SearchPerQuality { get; set; } = 2;
    public double VisualRange { get; set; } = 50;
    public Dictionary<int, double> StealthSearchMul { get; set; } = new();
    public int MissileBarrage { get; set; } = 3;
    public double MissileLuck { get; set; } = 0.05;
    public double FarMul { get; set; } = 2;
    public double FarHit { get; set; } = 0.2;
    public double SpeedRef { get; set; } = 24;
    public double MinHit { get; set; } = 0.3;
    public Dictionary<string, Weapon> Weapons { get; set; } = new();
    public int AmmoCapacity { get; set; } = 60;
    public int ReactorEnergySeconds { get; set; } = 10;
    public double FieldChargeMul { get; set; } = 1;
    public double SelfDestructRadiusPerKw { get; set; } = 0.02;
    public double SelfDestructDamagePerKw { get; set; } = 5;
    public double LootRate { get; set; } = 0.3;
    public double CollectRange { get; set; } = 3;
    public int CoverCell { get; set; } = 16; public double CoverMinHeight { get; set; } = 6; public double CoverCloseRange { get; set; } = 20; public int SenseEvery { get; set; } = 3;
    public double FighterMinDist { get; set; } = 60; public double FighterSpeedMul { get; set; } = 2; public double FlyingHitMul { get; set; } = 0.7;
    public double WallHpPerCell { get; set; } = 400;
    public double WallCostPerCell { get; set; } = 5;
    public int WallMaxLevel { get; set; } = 3;
    public double RepairPerPart { get; set; } = 0.25;
    public int RepairPartsPerHour { get; set; } = 1;
    public List<string> RepairItems { get; set; } = new();
    public double DefaultRetreatHp { get; set; } = 0.25;
    // 士気・練度(G21 第 6 節)。数値は combat.json の morale
    public double MoraleStart { get; set; } = 70;
    public double MoraleLossPerMech { get; set; } = 4;
    public double MoraleKillGain { get; set; } = 2;
    public double MoraleHangarRecover { get; set; } = 3;
    public double MoraleFieldNoSupply { get; set; } = -2;
    public double MoraleNoCommander { get; set; } = -1;
    public double MoraleCommanderPerPoint { get; set; } = 0.2;
    public double MoraleHitAt0 { get; set; } = 0.8;
    public double MoraleHitAt100 { get; set; } = 1.1;
    public double MoraleRetreatExtra { get; set; } = 0.25;
    public double MoraleBreakBelow { get; set; } = 20;
    public double ExpPerKill { get; set; } = 3;
    public double ExpPerCombatHour { get; set; } = 1;
    public double ExpHitBonus { get; set; } = 0.15;
    public int SquadMax { get; set; } = 12;
    public double FormationSpacing { get; set; } = 6;

    public static CombatData Load(string dir)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "combat.json")));
        var r = doc.RootElement; var c = new CombatData();
        foreach (var a in r.GetProperty("archetype").EnumerateObject())
            c.Archetype[a.Name] = new Arch(a.Value.GetProperty("hp").GetDouble(), a.Value.GetProperty("speed").GetDouble(), a.Value.GetProperty("weight").GetDouble(), a.Value.GetProperty("primary").GetString()!, a.Value.GetProperty("melee").GetString()!);
        c.GenerationMul = r.GetProperty("generationMul").GetDouble(); c.HpPerQuality = r.GetProperty("hpPerQuality").GetDouble(); c.SpeedAtQ70 = r.GetProperty("speedAtQ70").GetDouble();
        c.SearchPerQuality = r.GetProperty("searchPerQuality").GetDouble(); c.VisualRange = r.GetProperty("visualRange").GetDouble();
        foreach (var s in r.GetProperty("stealth").EnumerateObject()) c.StealthSearchMul[int.Parse(s.Name)] = s.Value.GetProperty("searchMul").GetDouble();
        c.MissileBarrage = r.GetProperty("missileVsStealth").GetProperty("barrage").GetInt32(); c.MissileLuck = r.GetProperty("missileVsStealth").GetProperty("luck").GetDouble();
        c.FarMul = r.GetProperty("rangeDecay").GetProperty("farMul").GetDouble(); c.FarHit = r.GetProperty("rangeDecay").GetProperty("farHit").GetDouble();
        c.SpeedRef = r.GetProperty("evasion").GetProperty("speedRef").GetDouble(); c.MinHit = r.GetProperty("evasion").GetProperty("minHit").GetDouble();
        foreach (var w in r.GetProperty("weapons").EnumerateObject())
        {
            var v = w.Value;
            c.Weapons[w.Name] = new Weapon(v.GetProperty("kind").GetString()!, v.TryGetProperty("damage", out var dm) ? dm.GetDouble() : 0, v.GetProperty("range").GetDouble(), v.GetProperty("accuracy").GetDouble(), v.GetProperty("cooldown").GetInt32(),
                v.TryGetProperty("ammoPerShot", out var ap) ? ap.GetInt32() : 0, v.TryGetProperty("energyPerShot", out var ep) ? ep.GetInt64() : 0, v.TryGetProperty("missile", out var mi) && mi.GetBoolean(),
                v.TryGetProperty("melee", out var me) ? me.GetString() : null, v.TryGetProperty("mul", out var mu) ? mu.GetDouble() : 0);
        }
        c.AmmoCapacity = r.GetProperty("ammoCapacity").GetInt32(); c.ReactorEnergySeconds = r.GetProperty("reactorEnergySeconds").GetInt32(); c.FieldChargeMul = r.GetProperty("fieldChargeMul").GetDouble();
        c.SelfDestructRadiusPerKw = r.GetProperty("selfDestruct").GetProperty("radiusPerKw").GetDouble(); c.SelfDestructDamagePerKw = r.GetProperty("selfDestruct").GetProperty("damagePerKw").GetDouble();
        c.LootRate = r.GetProperty("lootRate").GetDouble(); c.CollectRange = r.GetProperty("collectRange").GetDouble();
        var cv = r.GetProperty("cover"); c.CoverCell = cv.GetProperty("cellSize").GetInt32(); c.CoverMinHeight = cv.GetProperty("minHeight").GetDouble(); c.CoverCloseRange = cv.GetProperty("closeRange").GetDouble(); c.SenseEvery = cv.TryGetProperty("senseEvery", out var se) ? se.GetInt32() : 3;
        var fy = r.GetProperty("flight"); c.FighterMinDist = fy.GetProperty("fighterMinDist").GetDouble(); c.FighterSpeedMul = fy.GetProperty("speedMul").GetDouble(); c.FlyingHitMul = fy.GetProperty("hitMul").GetDouble();
        var wl = r.GetProperty("wall"); c.WallHpPerCell = wl.GetProperty("hpPerCell").GetDouble(); c.WallCostPerCell = wl.GetProperty("costPerCell").GetDouble(); c.WallMaxLevel = wl.GetProperty("maxLevel").GetInt32();
        var rp = r.GetProperty("repair"); c.RepairPerPart = rp.GetProperty("perPart").GetDouble(); c.RepairPartsPerHour = rp.GetProperty("partsPerHour").GetInt32(); c.RepairItems = rp.GetProperty("items").EnumerateArray().Select(e => e.GetString()!).ToList(); c.DefaultRetreatHp = r.GetProperty("defaultRetreatHp").GetDouble(); c.SquadMax = r.GetProperty("squadMax").GetInt32(); c.FormationSpacing = r.GetProperty("formationSpacing").GetDouble();
        if (r.TryGetProperty("morale", out var mo))
        {
            c.MoraleStart = mo.GetProperty("start").GetDouble(); c.MoraleLossPerMech = mo.GetProperty("lossPerMech").GetDouble(); c.MoraleKillGain = mo.GetProperty("killGain").GetDouble();
            c.MoraleHangarRecover = mo.GetProperty("hangarRecoverPerHour").GetDouble(); c.MoraleFieldNoSupply = mo.GetProperty("noSupplyPerHour").GetDouble(); c.MoraleNoCommander = mo.GetProperty("noCommanderPerHour").GetDouble(); c.MoraleCommanderPerPoint = mo.GetProperty("commanderPerPointPerHour").GetDouble();
            c.MoraleHitAt0 = mo.GetProperty("hitAt0").GetDouble(); c.MoraleHitAt100 = mo.GetProperty("hitAt100").GetDouble(); c.MoraleRetreatExtra = mo.GetProperty("retreatExtraAt0").GetDouble(); c.MoraleBreakBelow = mo.GetProperty("breakBelow").GetDouble();
            var ex = mo.GetProperty("exp"); c.ExpPerKill = ex.GetProperty("perKill").GetDouble(); c.ExpPerCombatHour = ex.GetProperty("perCombatHour").GetDouble(); c.ExpHitBonus = ex.GetProperty("hitBonusAt100").GetDouble();
        }
        return c;
    }
}

public enum SquadOrder { Hold = 0, Move = 1, Attack = 2, Retreat = 3, SelfDestruct = 4, Collect = 5 }
public enum Formation { Line = 0, Column = 1, Spread = 2 }
public enum SquadState { InHangar = 0, Field = 1 }

/// <summary>隊の中の 1 機の状態。</summary>
public sealed class MechUnit
{
    public int MechId { get; set; }
    public double Hp { get; set; }
    public double MaxHp { get; set; }
    public long Energy { get; set; }
    public long EnergyCap { get; set; }
    public int Ammo { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public bool Alive { get; set; } = true;
    public int Cooldown { get; set; }
    public int TargetMech { get; set; } = -1;
    // 制御基板の出力(既定: 交戦する、耐久 25% で退却、最寄り、主武器)
    public int Engage { get; set; } = 1;
    public int Retreat { get; set; }
    public int TargetPrio { get; set; }
    public int WeaponSel { get; set; }
    public double SearchOverride { get; set; }
    public int SelfDestruct { get; set; }
    public int McuTimer { get; set; }
    public bool Flying { get; set; }   // 写しに残す(命中に効くので決定論に要る)
    [JsonIgnore] public Mech? MechRef { get; set; }   // 機体の設計への参照(索引の版が変わるまで有効。10 万件の辞書引きを避ける)
    [JsonIgnore] public int MechRefVersion { get; set; } = -1;
    [JsonIgnore] public byte PowerKind { get; set; }   // 1 = リアクター、2 = バッテリー(MechRef と一緒に更新)
    [JsonIgnore] public int LastEnemyCount { get; set; }
    [JsonIgnore] public double LastEnemyDist { get; set; } = 1e9;
    [JsonIgnore] public int LastEnemyGen { get; set; }
}

/// <summary>隊(G2 の Unit)。同じ系統・同じ世代の機体 1〜12 機。戦場では代表機を描く。</summary>
public sealed class Squad
{
    public int Id { get; set; }
    public int Faction { get; set; } = 1;
    public string Name { get; set; } = "";
    public string Archetype { get; set; } = "general";
    public int Generation { get; set; }
    public List<MechUnit> Members { get; set; } = new();
    public SquadState State { get; set; }
    public int HomeBuilding { get; set; } = -1;
    public SquadOrder Order { get; set; }
    public double OrderX { get; set; }
    public double OrderY { get; set; }
    public int TargetSquad { get; set; } = -1;
    /// <summary>攻撃する建物(拠点・工場)。機体の目標が無いときに撃つ(G20 #6)。</summary>
    public int TargetBuilding { get; set; } = -1;
    /// <summary>回収した部品(格納庫に戻ると建物の荷下ろし場へ)。</summary>
    public List<MechPart> Cargo { get; set; } = new();
    /// <summary>隊長(人物)。自軍の隊は隊長がいないと出撃できない(G21 #1)。</summary>
    public int CommanderId { get; set; } = -1;
    /// <summary>集約の射撃の端数(G21 #2)。保存する(決定論)。</summary>
    public double FireAccum { get; set; }
    public int AutoTarget { get; set; } = -1;
    /// <summary>士気 0〜100(G21 第 6 節): 損耗で下がり、撃破・格納庫・指揮官で上がる。命中と下がる閾値に効き、崩れると帰還。</summary>
    public double Morale { get; set; } = 70;
    /// <summary>練度 0〜100: 撃破と交戦した時間で上がる。命中に効く。</summary>
    public double Exp { get; set; }
    public bool FoughtHour { get; set; }   // この 1 時間に撃ったか(練度)。写しに残す(決定論)
    /// <summary>経路(G21 第 7 節): 命令の目標向けと帰還先向け。写しに残す。</summary>
    public PathCache Route { get; set; } = new();
    public PathCache HomeRoute { get; set; } = new();
    public bool Aggregated { get; set; }   // 写しに残す(切り替わりの処理を読み込み後も同じにする)
    public double CX { get; set; }   // 隊の中心(集約は置いた位置から保つ)。写しに残す(読み込み後に平均から求め直すと丸めで進行がずれる)
    public double CY { get; set; }
    [JsonIgnore] public List<MechUnit> AliveBuf { get; } = new();
    [JsonIgnore] public List<MechUnit> FightBuf { get; } = new();
    [JsonIgnore] public List<MechUnit> RunBuf { get; } = new();   // 下がって帰っている生存機(事前走査)
    [JsonIgnore] public List<MechUnit> BoardDue { get; } = new();   // このティックに制御基板を評価する機体(事前走査)
    [JsonIgnore] public int ShootCache { get; set; }   // 戦う機体のうち交戦(engage)の機体
    /// <summary>制御基板が自爆を出した機体がいる(集約では爆発を解かないので個機に戻す)。写しに残す。</summary>
    public bool AnySelfDestruct { get; set; }
    [JsonIgnore] public int AliveCache { get; set; }   // 集約の事前走査で数えた生存機(同じティックの中で撃破のたびに減らす)
    [JsonIgnore] public int FightCache { get; set; }   // 同じく戦う機体(下がっていない生存機)
    public bool CenterKept { get; set; }   // 集約が中心(CX/CY)を更新済み(次のティックの分類で機体を走査しない)。写しに残す
    [JsonIgnore] public int FieldIdx { get; set; }   // そのティックの出撃中の隊の並びの番号(配列の添字)
    [JsonIgnore] public List<Squad>? SelfUnit { get; set; }   // 軍に属さない隊の単位(自分 1 つ)
    [JsonIgnore] public bool LastFly { get; set; }
    public Formation Formation { get; set; }
    public double X => Members.Where(m => m.Alive).Select(m => m.X).DefaultIfEmpty(OrderX).Average();
    public double Y => Members.Where(m => m.Alive).Select(m => m.Y).DefaultIfEmpty(OrderY).Average();
    public int Alive => Members.Count(m => m.Alive);
}

/// <summary>機体の性能値(部品の品質と系統、世代から)。</summary>
public static class MechStats
{
    /// <summary>部品の品質の平均(種類ごと)。部品は製造時に決まるので機体ごとに 1 回だけ求めて持つ(10 万規模で毎射撃の LINQ が重かった)。</summary>
    static double Q(Mech m, string item)
    {
        if (m.QualityCache == null || m.QualityCacheParts != m.Parts.Count)
        {
            var d = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var g in m.Parts.GroupBy(p => p.Item)) d[g.Key] = g.Average(p => (double)p.Quality);
            m.QualityCache = d; m.QualityCacheParts = m.Parts.Count; m.ReactorKwCache = -1;
        }
        return m.QualityCache.TryGetValue(item, out var q) ? q : 70;
    }
    public static double GenMul(Mech m, CombatData c) => 1 + c.GenerationMul * m.Generation;
    public static double MaxHp(Mech m, CombatData c)
    {
        double sum = Q(m, "mech_torso") + Q(m, "mech_arm") * 2 + Q(m, "mech_leg") * 2;
        return sum * c.HpPerQuality * c.Archetype[m.Archetype].Hp * GenMul(m, c);
    }
    /// <summary>速度 m/s。</summary>
    public static double Speed(Mech m, CombatData c) => (Q(m, "mech_leg") + Q(m, "mech_back")) / 2 / 70 * c.SpeedAtQ70 * c.Archetype[m.Archetype].Speed * GenMul(m, c);
    public static double Search(Mech m, CombatData c) => Q(m, "mech_head") * c.SearchPerQuality * GenMul(m, c);
    public static double Weight(Mech m, CombatData c) => c.Archetype[m.Archetype].Weight;
    public static double WeaponQuality(Mech m) => Q(m, "mech_weapon");
    public static int ReactorOutputKw(Mech m, GameData data) { if (m.PowerSource != "reactor") return 0; if (m.ReactorKwCache < 0) m.ReactorKwCache = Building.ReactorOutput(data.Structures["reactor_g" + m.Generation], (int)Q(m, "reactor_core"), data); return m.ReactorKwCache; }   // 機体ごとに 1 回だけ求める
    public static long EnergyCap(Mech m, GameData data, CombatData c) => m.PowerSource == "reactor" ? (long)ReactorOutputKw(m, data) * Clock.TicksPerSecond * c.ReactorEnergySeconds : data.MechBatteryCapacity;
    public static string Primary(Mech m, CombatData c) => c.Archetype[m.Archetype].Primary;
    public static string Melee(Mech m, CombatData c) => c.Archetype[m.Archetype].Melee;
}

/// <summary>戦闘の計算(手順 4 第 1 段階)。毎ティック、屋外の隊について 索敵 → 制御基板 → 移動 → 射撃と近接 → 給電 の順。</summary>
public static class Combat
{
    public static void Tick(Factory f, GameData data, CombatData c, Rng rng, Simulation? sim = null)
    {
        Ships.Step(f); RefreshCover(f, c); if (sim != null) RefreshHeights(sim); Pathing.Refresh(f, sim?.CityData.MapSize ?? 0);
        var field = f.Squads.Where(s => s.State == SquadState.Field).ToList();
        StepTurrets(f, data, c, rng, field);
        if (field.Count == 0) return;
        var mechs = f.MechById();
        // ティックごとの索引(10 万規模の計測で、線形探索が n² になっていたため): 機体 Id → ユニット、空間格子(64 m)
        // 注視範囲の外の隊は集約(隊どうしの式)で解く(G21 #3)。村(段階 1)と、自爆の命令は個機
        int stage = f.City?.Stage ?? 1;
        foreach (var s in field) { if (s.CenterKept) s.CenterKept = false; else { double sx = 0, sy = 0; int n = 0; foreach (var u in s.Members) if (u.Alive) { sx += u.X; sy += u.Y; n++; } if (n > 0) { s.CX = sx / n; s.CY = sy / n; } else { s.CX = s.OrderX; s.CY = s.OrderY; } } bool was = s.Aggregated; if (s.AnySelfDestruct && !was) s.AnySelfDestruct = s.Members.Any(u => u.Alive && u.SelfDestruct != 0); s.Aggregated = stage >= 2 && f.FocusR > 0 && s.Order != SquadOrder.SelfDestruct && !s.AnySelfDestruct && Math.Sqrt((s.CX - f.FocusX) * (s.CX - f.FocusX) + (s.CY - f.FocusY) * (s.CY - f.FocusY)) > f.FocusR; if (s.Aggregated && !was) foreach (var u in s.Members) { u.TargetMech = -1; u.Flying = false; } }   // 個機 → 集約の切り替わりで機体の目標を消す(集約は隊の目標で解く)
        var detail = field.Where(s => !s.Aggregated).ToList(); var agg = field.Where(s => s.Aggregated).ToList();
        f.CombatUnits.Clear(); f.CombatGrid.Clear();
        if (detail.Count > 0)   // 個機の索敵・目標の索引(個機の隊が無ければ要らない)
            foreach (var s in field) foreach (var u in s.Members) { f.CombatUnits[u.MechId] = u; if (u.Alive) { long k = CellKey(u.X, u.Y); if (!f.CombatGrid.TryGetValue(k, out var l)) f.CombatGrid[k] = l = new List<(MechUnit, Squad)>(); l.Add((u, s)); } }
        StepAggregated(f, data, c, rng, agg, field, mechs, sim);
        // 索敵と制御基板
        foreach (var s in detail)
            foreach (var u in s.Members.Where(u => u.Alive))
            {
                var me = mechs[u.MechId];
                if ((f.TickCount + u.MechId) % c.SenseEvery == 0 || (u.TargetMech >= 0 && FindUnit(f, field, u.TargetMech) is not { Alive: true })) Sense(f, s, u, me, field, mechs, data, c);   // 索敵は間引く(遮蔽の射線が重いため)
                if (me.Program != null && ++u.McuTimer >= Math.Max(1, me.Program.Interval)) { u.McuTimer = 0; RunBoard(s, u, me, f, data); }
                else if (me.Program == null) { u.Retreat = u.Hp < u.MaxHp * RetreatFrac(f, s, c) ? 1 : 0; u.Engage = 1; }
            }
        // 移動
        foreach (var s in field)
        {
            if (s.Aggregated) goto squadLevel;
            if (s.Order == SquadOrder.SelfDestruct) { foreach (var u in s.Members.Where(u => u.Alive)) u.SelfDestruct = 1; }
            int idx = 0;
            foreach (var u in s.Members.Where(u => u.Alive))
            {
                var me = mechs[u.MechId];
                double spd = MechStats.Speed(me, c) / Clock.TicksPerSecond;
                (double tx, double ty)? goal = null;
                if (u.Retreat != 0 || s.Order == SquadOrder.Retreat) { var hb = f.Get(s.HomeBuilding); if (hb != null) goal = Pathing.Waypoint(f, s.HomeRoute, s.Id, u.X, u.Y, hb.X, hb.Y, f.TickCount, s.HomeBuilding); }
                else if (s.Order == SquadOrder.Move) { var (wx, wy) = Pathing.Waypoint(f, s.Route, s.Id, s.CX, s.CY, s.OrderX, s.OrderY, f.TickCount); goal = Slot(s, idx, wx, wy, c); }
                else if (s.Order == SquadOrder.Collect) { var lt = NearestLoot(f, s); if (lt != null) { var (wx, wy) = Pathing.Waypoint(f, s.Route, s.Id, s.CX, s.CY, lt.X, lt.Y, f.TickCount); goal = Slot(s, idx, wx, wy, c); } }
                else if (s.Order == SquadOrder.Attack && u.Engage != 0 && u.TargetMech >= 0)
                {
                    var t = FindUnit(f, field, u.TargetMech); var w = c.Weapons[WeaponId(u, me, c)];
                    if (t != null && Dist(u, t) > w.Range * 0.8) goal = (t.X, t.Y);
                }
                else if (s.Order == SquadOrder.Attack && s.TargetSquad >= 0) { var ts = f.Squads.FirstOrDefault(x => x.Id == s.TargetSquad); if (ts != null && ts.Alive > 0) { var (wx, wy) = Pathing.Waypoint(f, s.Route, s.Id, s.CX, s.CY, ts.CX, ts.CY, f.TickCount); goal = Slot(s, idx, wx, wy, c); } }
                else if (s.Order == SquadOrder.Attack && s.TargetBuilding >= 0) { var tb = f.Get(s.TargetBuilding); if (tb != null && !tb.Destroyed) { var (bx, by) = Center(tb); var w = c.Weapons[WeaponId(u, me, c)]; double dd = Math.Sqrt((bx - u.X) * (bx - u.X) + (by - u.Y) * (by - u.Y)); if (dd > w.Range * 0.7) { var (wx, wy) = Pathing.Waypoint(f, s.Route, s.Id, s.CX, s.CY, bx, by - w.Range * 0.6, f.TickCount); goal = Slot(s, idx, wx, wy, c); } } }
                u.Flying = false;
                if (goal is var (gx, gy)) { double dx = gx - u.X, dy = gy - u.Y, d = Math.Sqrt(dx * dx + dy * dy); if (me.Archetype == "fast" && d > c.FighterMinDist) { u.Flying = true; spd *= c.FighterSpeedMul; } if (d > 0.5) { double k = Math.Min(1, spd / d); (u.X, u.Y) = u.Flying ? (u.X + dx * k, u.Y + dy * k) : Pathing.Step(f, u.X, u.Y, dx * k, dy * k, gx, gy, u.Retreat != 0 || s.Order == SquadOrder.Retreat ? s.HomeBuilding : 0); } }   // 建物の床は避けて歩く(飛行中は越える)
                idx++;
            }
            squadLevel:
            // 回収: 部品の落ちている所に着いたら拾う。無くなれば帰還
            if (s.Order == SquadOrder.Collect)
            {
                var lt = NearestLoot(f, s);
                if (lt == null) s.Order = SquadOrder.Retreat;
                else if (s.Members.Any(u => u.Alive && Math.Sqrt((u.X - lt.X) * (u.X - lt.X) + (u.Y - lt.Y) * (u.Y - lt.Y)) <= c.CollectRange))
                { s.Cargo.AddRange(lt.Parts); f.Loot.Remove(lt); f.Events.Add(new FactoryEvent { Kind = "collected", Building = s.Id, X = lt.X, Y = lt.Y, Destroyed = lt.Parts.Count }); }
            }
            // 弾薬が尽きた隊(実弾の主武器)は帰還して補給する(建物の攻略中)
            if (s.TargetBuilding >= 0 && s.Order == SquadOrder.Attack && s.Alive > 0 && s.Members.Where(u => u.Alive).All(u => { var wd = c.Weapons[WeaponId(u, mechs[u.MechId], c)]; return wd.Kind == "live" && wd.AmmoPerShot > 0 && u.Ammo < wd.AmmoPerShot; }))
            { s.Order = SquadOrder.Retreat; f.Events.Add(new FactoryEvent { Kind = "no_ammo", Building = s.Id }); }
            // 退却して帰還先に着いたら格納庫へ
            var home = f.Get(s.HomeBuilding);
            bool anyAlive = false, allRetreat = true, allHome = true;
            if (home != null) foreach (var u in s.Members) { if (!u.Alive) continue; anyAlive = true; if (u.Retreat == 0) allRetreat = false; if (Math.Abs(u.X - home.X) + Math.Abs(u.Y - home.Y) >= 3) { allHome = false; break; } }
            if (home != null && anyAlive && allHome && (s.Order == SquadOrder.Retreat || allRetreat))
            {
                s.State = SquadState.InHangar; s.Order = SquadOrder.Hold; s.TargetBuilding = -1; foreach (var u in s.Members) { u.Retreat = 0; if (f.AmmoResupply) u.Ammo = c.AmmoCapacity; }   // 軍事費が低いと補給されない
                foreach (var p in s.Cargo) home.Cargo.Add(new ItemStack(p.Item, p.Quality)); s.Cargo.Clear();   // 回収した部品は建物の荷下ろし場へ
            }
        }
        // 射撃・近接・自爆
        foreach (var s in detail)
            foreach (var u in s.Members.Where(u => u.Alive).ToList())
            {
                var me = mechs[u.MechId];
                if (u.SelfDestruct != 0) { Explode(f, s, u, me, field, mechs, data, c); continue; }
                if (u.Cooldown > 0) { u.Cooldown--; continue; }
                if (u.Engage == 0 || u.Retreat != 0) continue;
                if (u.TargetMech < 0)
                {   // 機体の目標が無ければ建物を撃つ(建物は避けないので精度どおり当たる)
                    if (s.TargetBuilding < 0) continue;
                    var tb = f.Get(s.TargetBuilding); if (tb == null || tb.Destroyed) { s.TargetBuilding = -1; continue; }
                    string bw = WeaponId(u, me, c); var bwd = c.Weapons[bw]; var (bx, by) = Center(tb);
                    double bd = Math.Sqrt((bx - u.X) * (bx - u.X) + (by - u.Y) * (by - u.Y)); if (bd > bwd.Range) continue;
                    if (bwd.Kind == "beam") { if (u.Energy < bwd.EnergyPerShot) continue; } else if (bwd.AmmoPerShot > 0) { if (u.Ammo < bwd.AmmoPerShot) continue; }
                    u.Cooldown = bwd.Cooldown; if (bwd.Kind == "beam") u.Energy -= bwd.EnergyPerShot; else u.Ammo -= bwd.AmmoPerShot;
                    double bdmg = (bwd.Melee != null ? (bwd.Melee == "sword" ? MechStats.Speed(me, c) * MechStats.Weight(me, c) : MechStats.Speed(me, c)) * bwd.Mul : bwd.Damage * MechStats.WeaponQuality(me) / 70) * MechStats.GenMul(me, c) * bwd.Accuracy;
                    tb.Hp = Math.Max(0, tb.Hp - bdmg);
                    f.Events.Add(new FactoryEvent { Kind = "hit_building", Building = s.Id, Floor = tb.Id, X = (int)u.X, Y = (int)u.Y, Power = (int)bdmg, Radius = bd, Destroyed = 1 });
                    if (tb.Hp <= 0) { tb.Destroyed = true; f.Events.Add(new FactoryEvent { Kind = "building_destroyed", Building = tb.Id, Power = s.Id }); if (tb.IsShip) Ships.Sunk(f, tb); else if (sim != null) DiplomacySim.BuildingFell(sim, tb, s); }
                    continue;
                }
                var t = FindUnit(f, field, u.TargetMech); if (t == null || !t.Alive) { u.TargetMech = -1; continue; }
                string wid = WeaponId(u, me, c); var w = c.Weapons[wid];
                double dist = Dist(u, t);
                if (dist > w.Range * c.FarMul) continue;
                if (w.Kind == "beam") { if (u.Energy < w.EnergyPerShot) continue; } else if (w.AmmoPerShot > 0) { if (u.Ammo < w.AmmoPerShot) continue; }
                u.Cooldown = w.Cooldown; s.FoughtHour = true;
                if (w.Kind == "beam") u.Energy -= w.EnergyPerShot; else u.Ammo -= w.AmmoPerShot;
                var tm = mechs[t.MechId];
                if (t.Flying && w.Melee != null) continue;   // 飛んでいる相手に近接は届かない
                double hit = w.Accuracy * (dist <= w.Range ? 1 : 1 - (dist - w.Range) / (w.Range * (c.FarMul - 1)) * (1 - c.FarHit)) * Math.Max(c.MinHit, 1 - MechStats.Speed(tm, c) / c.SpeedRef) * f.HitMulPct / 100.0 * (t.Flying ? c.FlyingHitMul : 1) * Armies.CommandMul(f, s) * MoraleMul(s, c);   // 軍の長の効果(G19)、飛行中は当たりにくい、隊長と司令官(G21)、士気と練度
                if (w.Missile && tm.Generation >= 5)
                {   // 誘導弾はステルスに当たらない(弾幕か偶然を除く)
                    int inbound = s.Members.Count(x => x.Alive && x.TargetMech == t.MechId && c.Weapons[WeaponId(x, mechs[x.MechId], c)].Missile);
                    if (inbound < c.MissileBarrage && rng.Next(10_000) >= (int)(c.MissileLuck * 10_000)) { f.Events.Add(new FactoryEvent { Kind = "miss", Building = s.Id, X = (int)u.X, Y = (int)u.Y, Power = t.MechId }); continue; }
                }
                bool landed = rng.Next(10_000) < (int)(hit * 10_000);
                double dmg = w.Melee != null ? (w.Melee == "sword" ? MechStats.Speed(me, c) * MechStats.Weight(me, c) : MechStats.Speed(me, c)) * w.Mul * MechStats.GenMul(me, c)
                                             : w.Damage * MechStats.WeaponQuality(me) / 70 * MechStats.GenMul(me, c);
                f.Events.Add(new FactoryEvent { Kind = landed ? "hit" : "miss", Building = s.Id, Floor = t.MechId, X = (int)u.X, Y = (int)u.Y, Power = (int)dmg, Radius = dist, Destroyed = landed ? 1 : 0 });
                if (!landed) continue;
                t.Hp -= dmg;
                if (t.Hp <= 0) Kill(f, t, tm, c, field, null, s);
            }
        // 給電: 屋外では、撃っていないリアクター機がバッテリー機に給電。リアクター機は毎ティック回復
        foreach (var s in field)
        {
            if (s.Aggregated) continue;   // 集約の隊は StepAggregated の走査の中で済ませてある
            var alive = s.AliveBuf; alive.Clear();
            if (Recharge(s.Members, alive, f, mechs, data)) FieldCharge(alive, 0, alive.Count, f, mechs, data, c);
        }
    }
    /// <summary>機体の設計への参照(索引の版が変わるまで持つ。10 万機で毎ティックの辞書引きを避ける)。</summary>
    static Mech MechOf(MechUnit u, Factory f, Dictionary<int, Mech> mechs)
    {
        if (u.MechRef == null || u.MechRefVersion != f.MechIndexVersion || u.MechRef.Id != u.MechId) { u.MechRef = mechs[u.MechId]; u.MechRefVersion = f.MechIndexVersion; }
        return u.MechRef;
    }
    /// <summary>生存機を alive に足しつつ、リアクター機を回復。戻り値 = バッテリー機がいる(給電の計算が要る)。</summary>
    static bool Recharge(List<MechUnit> members, List<MechUnit> alive, Factory f, Dictionary<int, Mech> mechs, GameData data)
    {
        bool hasBattery = false;
        foreach (var u in members)
        {
            if (!u.Alive) continue; alive.Add(u);
            if (u.MechRef == null || u.MechRefVersion != f.MechIndexVersion) u.PowerKind = MechOf(u, f, mechs).PowerSource == "reactor" ? (byte)1 : (byte)2;
            if (u.PowerKind == 1) { if (u.Energy < u.EnergyCap) u.Energy = Math.Min(u.EnergyCap, u.Energy + MechStats.ReactorOutputKw(u.MechRef!, data)); } else hasBattery = true;
        }
        return hasBattery;
    }
    /// <summary>屋外の給電: 撃っていないリアクター機がバッテリー機に給電(alive の [from, to) の中で)。</summary>
    static void FieldCharge(List<MechUnit> alive, int from, int to, Factory f, Dictionary<int, Mech> mechs, GameData data, CombatData c)
    {
        for (int gi = from; gi < to; gi++)
        {
            var giver = alive[gi]; var gm = MechOf(giver, f, mechs);
            if (gm.PowerSource != "reactor" || giver.TargetMech >= 0) continue;   // 交戦中は給電しない
            long give = (long)(MechStats.ReactorOutputKw(gm, data) * c.FieldChargeMul);
            foreach (var taker in alive.Skip(from).Take(to - from).Where(t => MechOf(t, f, mechs).PowerSource == "battery" && t.Energy < t.EnergyCap).OrderBy(t => t.Energy))
            {
                long room = taker.EnergyCap - taker.Energy; long x = Math.Min(room, Math.Min(give, giver.Energy));
                if (x <= 0) break;
                taker.Energy += x; giver.Energy -= x; give -= x;
            }
        }
    }

    /// <summary>隊どうし・軍どうしの式(G21 #2・#4・#5・#9)。注視の外の隊は「単位」(軍に属していれば軍、そうでなければ隊)ごとに、中心・生存機数・平均で索敵・移動・射撃を解く。
    /// ダメージは相手の単位の「下がっていない先頭の機体」へ入れ、機体の記録は共通(個機と撃ち合っても成立する)。軍の単位なら隊は軍の陣形の位置、機体は隊の陣形の位置に置く。</summary>
    static void StepAggregated(Factory f, GameData data, CombatData c, Rng rng, List<Squad> agg, List<Squad> field, Dictionary<int, Mech> mechs, Simulation? sim)
    {
        if (agg.Count == 0) return;
        // 単位を作る: 軍(全隊が集約)はまとめて 1 単位、それ以外は隊 1 つが 1 単位
        // 隊ごとの事前走査(機体に触るのはここだけ): 生存機・戦う機体・生存数、リアクターの回復と給電
        foreach (var q in agg)
        {
            var qa = q.AliveBuf; qa.Clear(); var qf = q.FightBuf; qf.Clear(); var qr = q.RunBuf; qr.Clear();
            bool hasBattery = Recharge(q.Members, qa, f, mechs, data);
            var qd = q.BoardDue; qd.Clear(); int shoot = 0;
            foreach (var u in qa)
            {
                if (u.Retreat == 0) { qf.Add(u); if (u.Engage != 0) shoot++; } else qr.Add(u);
                var prog = u.MechRef!.Program; if (prog != null && ++u.McuTimer >= Math.Max(1, prog.Interval)) { u.McuTimer = 0; qd.Add(u); }   // 制御基板は個機と同じ間隔で(G21 第 8 節)
            }
            q.AliveCache = qa.Count; q.FightCache = qf.Count; q.ShootCache = shoot; if (hasBattery) FieldCharge(qa, 0, qa.Count, f, mechs, data, c);
        }
        int nf = field.Count; for (int i = 0; i < nf; i++) field[i].FieldIdx = i;
        var units = f.AggUnits; units.Clear(); var unitOf = new List<Squad>?[nf]; var armyOf = new Army?[nf];
        var byId = new Dictionary<int, Squad>(nf); foreach (var q in field) byId[q.Id] = q;   // 線形探索をしない(10 万規模)
        foreach (var a in f.Armies)
        {
            var sqs = a.UnitBuf; sqs.Clear(); foreach (var id in a.SquadIds) { if (byId.TryGetValue(id, out var q) && q.State == SquadState.Field && q.Aggregated && q.AliveCache > 0) sqs.Add(q); }
            if (sqs.Count >= 2) { units.Add(sqs); foreach (var q in sqs) { unitOf[q.FieldIdx] = sqs; armyOf[q.FieldIdx] = a; } }
        }
        foreach (var s in agg) if (unitOf[s.FieldIdx] == null && s.AliveCache > 0) { var u1 = s.SelfUnit ??= new List<Squad>(1); u1.Clear(); u1.Add(s); units.Add(u1); unitOf[s.FieldIdx] = u1; }
        // 単位の中心と、全ての隊の空間格子(64 m)・粗格子(256 m、陣営の有無)
        var center = new (double x, double y)[nf]; var hasCenter = new bool[nf];
        foreach (var u in units) { double sx = 0, sy = 0; foreach (var q in u) { sx += q.CX; sy += q.CY; } var cc = (sx / u.Count, sy / u.Count); foreach (var q in u) { center[q.FieldIdx] = cc; hasCenter[q.FieldIdx] = true; } }
        var sgrid = f.AggGrid; foreach (var l0 in sgrid.Values) l0.Clear();
        const int Coarse = 256; var coarse = f.AggCoarse; coarse.Clear();
        foreach (var e in field)
        {
            if (AliveOf(e) == 0) continue; long k = CellKey(e.CX, e.CY); if (!sgrid.TryGetValue(k, out var l)) sgrid[k] = l = new List<Squad>(); l.Add(e);
            long ck = ((long)Math.Floor(e.CX / Coarse) << 32) ^ ((long)Math.Floor(e.CY / Coarse) & 0xFFFFFFFFL); coarse[ck] = coarse.GetValueOrDefault(ck) | FactionBit(e.Faction);
        }
        bool EnemyNear(double x, double y, int faction)
        {
            int mine = FactionBit(faction); int ccx = (int)Math.Floor(x / Coarse), ccy = (int)Math.Floor(y / Coarse);
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) { int m = coarse.GetValueOrDefault(((long)(ccx + dx) << 32) ^ ((long)(ccy + dy) & 0xFFFFFFFFL)); if ((m & ~mine) != 0) return true; }
            return false;
        }
        foreach (var unit in units)
        {
            var s = unit[0]; var (cx, cy) = center[s.FieldIdx];
            List<MechUnit> alive, fighters;
            if (unit.Count == 1) { alive = s.AliveBuf; fighters = s.FightBuf; }
            else { alive = f.AggAlive; alive.Clear(); fighters = f.AggFight; fighters.Clear(); foreach (var q in unit) { alive.AddRange(q.AliveBuf); fighters.AddRange(q.FightBuf); } }
            if (alive.Count == 0) continue;
            int fightCount = 0, shootCount = 0; foreach (var q in unit) { fightCount += q.FightCache; shootCount += q.ShootCache; }
            var lead = MechOf(alive[0], f, mechs);
            bool retreat = s.Order == SquadOrder.Retreat || fightCount == 0;
            // 索敵: 単位の中心どうし(索敵範囲は先頭機、ステルスは相手の世代、遮蔽と地形は中心どうしの射線 1 本)。3 ティックに 1 回、目標は保つ。周りに敵がいなければ飛ばす
            Squad? target = null; double td = 1e9; double search = MechStats.Search(lead, c);
            bool senseNow = (f.TickCount + s.Id) % c.SenseEvery == 0;
            if (!senseNow && s.AutoTarget >= 0)
            {
                byId.TryGetValue(s.AutoTarget, out var keep);
                if (keep != null && keep.State == SquadState.Field && AliveOf(keep) > 0) { var (kx, ky) = hasCenter[keep.FieldIdx] ? center[keep.FieldIdx] : (keep.CX, keep.CY); double dd = Math.Sqrt((kx - cx) * (kx - cx) + (ky - cy) * (ky - cy)); if (dd <= Math.Max(search, c.VisualRange)) { target = keep; td = dd; } }
            }
            if (target == null && !retreat && s.Order != SquadOrder.Collect && s.Order != SquadOrder.Move && (senseNow || s.AutoTarget < 0) && EnemyNear(cx, cy, s.Faction))
            {
                int r = (int)Math.Ceiling(Math.Max(search, c.VisualRange) / GridCell), gcx = (int)Math.Floor(cx / GridCell), gcy = (int)Math.Floor(cy / GridCell);
                for (int qx = gcx - r; qx <= gcx + r; qx++) for (int qy = gcy - r; qy <= gcy + r; qy++)
                if (sgrid.TryGetValue(((long)qx << 32) ^ ((long)qy & 0xFFFFFFFFL), out var cell)) foreach (var e in cell)
                {
                    if (e.Faction == s.Faction || AliveOf(e) == 0) continue;
                    var (ex, ey) = hasCenter[e.FieldIdx] ? center[e.FieldIdx] : (e.CX, e.CY); double d = Math.Sqrt((ex - cx) * (ex - cx) + (ey - cy) * (ey - cy));
                    double range = Math.Max(search * c.StealthSearchMul.GetValueOrDefault(e.Generation, 1.0), c.VisualRange);
                    if (d > range || d >= td) continue;
                    if (d > c.CoverCloseRange && (LineBlocked(f, cx, cy, ex, ey, c) || (sim != null && TerrainBlocked(sim, cx, cy, false, ex, ey, false)))) continue;
                    target = e; td = d;
                }
                if (s.Order == SquadOrder.Attack && s.TargetSquad >= 0 && target == null) { byId.TryGetValue(s.TargetSquad, out var ts); if (ts != null && ts.State == SquadState.Field && AliveOf(ts) > 0) { double dd = Math.Sqrt((ts.CX - cx) * (ts.CX - cx) + (ts.CY - cy) * (ts.CY - cy)); if (dd <= Math.Max(search, c.VisualRange)) { target = ts; td = dd; } } }
            }
            foreach (var q in unit) q.AutoTarget = target?.Id ?? -1;
            // 制御基板(集約): 機体の値はそのまま、敵の距離・数・世代は単位の索敵の値。退却・交戦・自爆の出力を使う(目標の優先・武器・索敵範囲は集約では効かない)
            foreach (var q in unit)
            {
                if (q.BoardDue.Count == 0) continue;
                int tAlive = 0; if (target != null) { var tu0 = unitOf[target.FieldIdx]; if (tu0 != null) foreach (var tq in tu0) tAlive += AliveOf(tq); else tAlive = AliveOf(target); }
                foreach (var u in q.BoardDue)
                {
                    u.LastEnemyDist = target == null ? 1e9 : td; u.LastEnemyCount = tAlive; u.LastEnemyGen = target?.Generation ?? 0;
                    int r0 = u.Retreat; RunBoard(q, u, u.MechRef!, f, data);
                    if (u.Retreat != r0) { if (u.Retreat != 0) q.FightCache--; else q.FightCache++; }
                    if (u.SelfDestruct != 0) q.AnySelfDestruct = true;
                }
            }
            // 移動: 単位の中心 1 点で動き、隊は軍の陣形、機体は隊の陣形の位置へ
            var w = c.Weapons[MechStats.Primary(lead, c)];
            (double gx, double gy)? goal = null;
            if (retreat) { var hb = f.Get(s.HomeBuilding); if (hb != null) goal = (hb.X, hb.Y); }
            else if (s.Order == SquadOrder.Move) goal = (s.OrderX, s.OrderY);
            else if (s.Order == SquadOrder.Collect) { var lt = NearestLoot(f, s); if (lt != null) goal = (lt.X, lt.Y); }
            else if (target != null) { if (td > w.Range * 0.8) goal = hasCenter[target.FieldIdx] ? center[target.FieldIdx] : (target.CX, target.CY); }
            else if (s.Order == SquadOrder.Attack && s.TargetSquad >= 0) { byId.TryGetValue(s.TargetSquad, out var ts); if (ts != null && ts.State == SquadState.Field && AliveOf(ts) > 0) goal = (ts.CX, ts.CY); }
            else if (s.Order == SquadOrder.Attack && s.TargetBuilding >= 0) { var tb = f.Get(s.TargetBuilding); if (tb != null && !tb.Destroyed) { var (bx, by) = Center(tb); double dd = Math.Sqrt((bx - cx) * (bx - cx) + (by - cy) * (by - cy)); if (dd > w.Range * 0.7) goal = (bx, by - w.Range * 0.6); } }
            if (goal is var (ggx, ggy)) goal = Pathing.Waypoint(f, retreat ? s.HomeRoute : s.Route, s.Id, cx, cy, ggx, ggy, f.TickCount, retreat ? s.HomeBuilding : 0);   // 建物を避けて中継点をたどる(G21 第 7 節)
            double spd = MechStats.Speed(lead, c) / Clock.TicksPerSecond; bool fly = false;
            double nx = cx, ny = cy;
            if (goal is var (gx, gy)) { double dx = gx - cx, dy = gy - cy, d = Math.Sqrt(dx * dx + dy * dy); if (lead.Archetype == "fast" && d > c.FighterMinDist) { fly = true; spd *= c.FighterSpeedMul; } if (d > 0.5) { double k = Math.Min(1, spd / d); nx += dx * k; ny += dy * k; } }
            var army = armyOf[s.FieldIdx]; bool orderRetreat = s.Order == SquadOrder.Retreat;
            var hbU = f.Get(s.HomeBuilding); double spd0 = MechStats.Speed(lead, c) / Clock.TicksPerSecond;
            double ax = 0, ay = 0;   // 軍の陣形の重心(陣形の並びは原点対称でないので、重心を単位の中心に合わせないと置くたびにずれる)
            if (army != null) { for (int qi = 0; qi < unit.Count; qi++) { var (ox, oy) = Armies.Slot(army, qi, 0, 0); ax += ox; ay += oy; } ax /= unit.Count; ay /= unit.Count; }
            for (int qi = 0; qi < unit.Count; qi++)
            {
                var q = unit[qi]; var (qx, qy) = army != null ? Armies.Slot(army, qi, nx - ax, ny - ay) : (nx, ny); int i = 0, qalive = q.FightCache; double sqx = qx, sqy = qy;   // 下がった機体は陣形に数えない(個々に帰る)
                if (qalive > 0) { double mx = 0, my = 0; for (int j = 0; j < qalive; j++) { var (ox, oy) = Slot(q, j, 0, 0, c, qalive); mx += ox; my += oy; } qx -= mx / qalive; qy -= my / qalive; }   // 隊の陣形の重心
                bool place = (f.TickCount + q.Id) % c.SenseEvery == 0 || goal != null && (Math.Abs(nx - cx) + Math.Abs(ny - cy)) < 1e-9;   // 位置の置き直しは 3 ティックに 1 回、他は動いた分だけずらす
                double ddx = nx - cx, ddy = ny - cy;
                void RunHome(MechUnit u)
                {   // 下がった機体は個々に帰る(個機と同じ)。塊の置き直しで引き戻さない
                    var (hx, hy) = Pathing.Waypoint(f, q.HomeRoute, q.Id, u.X, u.Y, hbU!.X, hbU.Y, f.TickCount, q.HomeBuilding);
                    double rx = hx - u.X, ry = hy - u.Y, rd = Math.Sqrt(rx * rx + ry * ry); if (rd > 0.5) { double rk = Math.Min(1, spd0 / rd); u.X += rx * rk; u.Y += ry * rk; }
                    u.Flying = false; u.TargetMech = -1;
                }
                if (place || goal != null || orderRetreat || q.LastFly != fly)   // 止まっていて印も変わらない隊は機体に触らない(置き直しの 3 ティックに 1 回で揃う)
                    foreach (var u in q.Members)
                    {
                        if (!u.Alive) continue;
                        if (u.Retreat != 0 && hbU != null) { RunHome(u); continue; }
                        if (place) { var (px, py) = Slot(q, i++, qx, qy, c, qalive); if (!fly) (px, py) = Pathing.Nudge(f, px, py, qx, qy, retreat ? q.HomeBuilding : 0); u.X = px; u.Y = py; } else if (goal != null) { u.X += ddx; u.Y += ddy; }
                        u.Flying = fly; u.TargetMech = -1; if (orderRetreat) u.Retreat = 1;
                    }
                else if (hbU != null) foreach (var u in q.RunBuf) if (u.Alive) RunHome(u);   // 帰る機体だけ
                q.LastFly = fly;
                if (place) { q.CX = sqx; q.CY = sqy; } else if (goal != null) { q.CX += ddx; q.CY += ddy; }   // 中心は置いた位置から分かる(次のティックの分類で走査しない)
                q.CenterKept = true;
            }
            // 射撃: 発射数 = 戦う機体 × 発射率(端数は持ち越し)。資源は機体から順に。命中は平均、ダメージは相手の下がっていない先頭の機体へ
            if (retreat || target == null || td > w.Range * c.FarMul) continue;
            s.FireAccum += (double)shootCount / w.Cooldown;
            int shots = (int)s.FireAccum; s.FireAccum -= shots; if (shots > 0) foreach (var q in unit) q.FoughtHour = true;
            var tunit = unitOf[target.FieldIdx]; if (tunit == null) { tunit = target.SelfUnit ??= new List<Squad>(1); tunit.Clear(); tunit.Add(target); }   // 個機の隊が相手なら隊 1 つ
            MechUnit? nearRun = null; Squad nearRunSq = target; double nearD2 = 0, far2 = w.Range * c.FarMul * (w.Range * c.FarMul);
            void FindRunner()
            {   // 帰っている機体のうち最寄り(位置はこの単位の射撃の間は変わらない)
                nearRun = null; nearD2 = far2;
                foreach (var q in tunit) foreach (var u in q.RunBuf) { if (!u.Alive) continue; double rx = u.X - nx, ry = u.Y - ny, d2 = rx * rx + ry * ry; if (d2 < nearD2) { nearD2 = d2; nearRun = u; nearRunSq = q; } }
            }
            FindRunner(); int tqi = 0, tui = 0;
            bool beam = w.Kind == "beam"; int si = 0;   // 資源の尽きた機体は戻らないので先頭から順に進める
            double cmdMul = Armies.CommandMul(f, s, army) * UnitMoraleMul(unit, c), dmg = w.Damage * MechStats.WeaponQuality(lead) / 70 * MechStats.GenMul(lead, c);
            for (int k = 0; k < shots; k++)
            {
                while (si < fighters.Count && (fighters[si].Retreat != 0 || !fighters[si].Alive || fighters[si].Engage == 0 || !(beam ? fighters[si].Energy >= w.EnergyPerShot : w.AmmoPerShot == 0 || fighters[si].Ammo >= w.AmmoPerShot))) si++;
                if (si >= fighters.Count) break; var shooter = fighters[si];
                if (beam) shooter.Energy -= w.EnergyPerShot; else shooter.Ammo -= w.AmmoPerShot;
                // 目標: 個機の索敵(最も近い敵機)と同じ。塊の戦う機体は単位の距離で先頭から順(#2)、下がって帰る機体はその実距離。射程(farMul)の外は撃たない
                MechUnit? t = null; Squad tq = target;
                while (tqi < tunit.Count) { var fb = tunit[tqi].FightBuf; while (tui < fb.Count && (!fb[tui].Alive || fb[tui].Retreat != 0)) tui++; if (tui < fb.Count) { t = fb[tui]; tq = tunit[tqi]; break; } tqi++; tui = 0; }   // 戦う機体は塊の位置(先頭から順 #2)
                if (nearRun != null && !nearRun.Alive) FindRunner();
                if (nearRun != null && (t == null || nearD2 < td * td)) { t = nearRun; tq = nearRunSq; }   // 帰る機体の方が近ければそちら(個機の最寄り索敵と同じ)
                if (t == null) break;
                var tm = MechOf(t, f, mechs);
                if (w.Missile && tm.Generation >= 5 && fightCount < c.MissileBarrage && rng.Next(10_000) >= (int)(c.MissileLuck * 10_000)) { f.Events.Add(new FactoryEvent { Kind = "miss", Building = s.Id, Floor = t.MechId, X = (int)nx, Y = (int)ny, Power = t.MechId }); continue; }
                double hit = w.Accuracy * (td <= w.Range ? 1 : 1 - (td - w.Range) / (w.Range * (c.FarMul - 1)) * (1 - c.FarHit)) * Math.Max(c.MinHit, 1 - MechStats.Speed(tm, c) / c.SpeedRef) * f.HitMulPct / 100.0 * (t.Flying ? c.FlyingHitMul : 1) * cmdMul;
                bool landed = rng.Next(10_000) < (int)(hit * 10_000);
                f.Events.Add(new FactoryEvent { Kind = landed ? "hit" : "miss", Building = s.Id, Floor = t.MechId, X = (int)shooter.X, Y = (int)shooter.Y, Power = (int)dmg, Radius = td, Destroyed = landed ? 1 : 0 });
                if (!landed) continue;
                t.Hp -= dmg;
                if (t.Hp <= 0) { if (t.Retreat == 0) tq.FightCache--; tq.AliveCache--; Kill(f, t, tm, c, field, tq, s); foreach (var q in unit) if (q != s) { MoraleChange(f, q, c.MoraleKillGain, c); q.Exp = Math.Min(100, q.Exp + c.ExpPerKill); } }   // 軍は 1 つの単位で戦うので全隊に
                else if (t.Hp < t.MaxHp * RetreatFrac(f, tq, c) && tm.Program == null && t.Retreat == 0) { t.Retreat = 1; tq.FightCache--; }   // 個機の既定と同じく 25% で下がる
            }
        }
    }
    /// <summary>士気と練度の命中の倍率(G21 第 6 節): 士気 0 → hitAt0、100 → hitAt100、練度 100 で +hitBonus。</summary>
    public static double MoraleMul(Squad s, CombatData c) => (c.MoraleHitAt0 + (c.MoraleHitAt100 - c.MoraleHitAt0) * Math.Clamp(s.Morale, 0, 100) / 100) * (1 + c.ExpHitBonus * Math.Clamp(s.Exp, 0, 100) / 100);
    static double UnitMoraleMul(List<Squad> unit, CombatData c) { double m = 0; foreach (var q in unit) m += MoraleMul(q, c); return m / unit.Count; }
    /// <summary>下がる耐久の閾値: 士気 50 未満で上がる(士気 0 で +retreatExtra。50 以上は既定のまま)。</summary>
    public static double RetreatFrac(Factory f, Squad s, CombatData c) => f.RetreatHp + c.MoraleRetreatExtra * Math.Clamp((50 - s.Morale) / 50, 0, 1);
    /// <summary>士気の増減。breakBelow を割った出撃中の隊は崩れて帰還する(命令は帰還に置き換わる)。</summary>
    public static void MoraleChange(Factory f, Squad s, double delta, CombatData c)
    {
        s.Morale = Math.Clamp(s.Morale + delta, 0, 100);
        if (s.Morale < c.MoraleBreakBelow && s.State == SquadState.Field && s.Order != SquadOrder.Retreat) { s.Order = SquadOrder.Retreat; f.Events.Add(new FactoryEvent { Kind = "broken", Building = s.Id, Power = (int)s.Morale }); }
    }
    /// <summary>1 時間ごと: 格納庫で回復、出撃中は補給切れと隊長不在で下がり、指揮官の指揮で増減。撃った隊は練度が上がる。</summary>
    public static void MoraleHour(Factory f, CombatData c)
    {
        foreach (var s in f.Squads)
        {
            double d = 0;
            if (s.State == SquadState.InHangar) d += c.MoraleHangarRecover;
            else
            {
                if (f.City != null && s.Faction == 1 && Armies.Commander(f, s.CommanderId) == null) d += c.MoraleNoCommander;
                int alive = 0, low = 0; foreach (var u in s.Members) if (u.Alive) { alive++; if (u.Energy < u.EnergyCap * 0.3 || u.Ammo < c.AmmoCapacity * 0.3) low++; }
                if (alive > 0 && low * 2 > alive) d += c.MoraleFieldNoSupply;
            }
            var cmdr = Armies.Commander(f, s.CommanderId); if (cmdr != null) d += (cmdr.Command - 5) * c.MoraleCommanderPerPoint;
            if (d != 0) MoraleChange(f, s, d, c);
            if (s.FoughtHour) { s.Exp = Math.Min(100, s.Exp + c.ExpPerCombatHour); s.FoughtHour = false; }
        }
    }
    static int AliveOf(Squad q) => q.Aggregated ? q.AliveCache : q.Alive;   // 集約の隊は事前走査の値(機体を数え直さない)
    static int FactionBit(int faction) => faction == 1 ? 1 : 2 | (faction > 100 ? 4 << (faction % 8) : 0);

    /// <summary>格納庫にいる待機中のリアクター機の出力(kW)。建物の供給に加える。</summary>
    public static int HangarSupply(Factory f, Building b, GameData data)
    {
        int sum = 0;
        foreach (var s in f.Squads.Where(s => s.State == SquadState.InHangar && s.HomeBuilding == b.Id))
            foreach (var u in s.Members.Where(u => u.Alive))
            { var m = f.MechById().GetValueOrDefault(u.MechId); if (m != null) sum += MechStats.ReactorOutputKw(m, data); }
        return sum;
    }

    static (double, double) Slot(Squad s, int idx, double cx, double cy, CombatData c, int alive = -1)
    {
        double sp = c.FormationSpacing; if (alive < 0) alive = s.Alive;
        return s.Formation switch
        {
            Formation.Line => (cx + (idx - alive / 2.0) * sp, cy),
            Formation.Column => (cx, cy - idx * sp),
            _ => (cx + (idx % 4 - 1.5) * sp * 1.5, cy - (idx / 4) * sp * 1.5),
        };
    }

    static double Dist(MechUnit a, MechUnit b) { double dx = a.X - b.X, dy = a.Y - b.Y; return Math.Sqrt(dx * dx + dy * dy); }
    const int GridCell = 64;   // 索引は工場(Factory)ごとに持つ(静的にすると複数の計算が混ざる)
    static long CellKey(double x, double y) => ((long)Math.Floor(x / GridCell) << 32) ^ ((long)Math.Floor(y / GridCell) & 0xFFFFFFFFL);
    static MechUnit? FindUnit(Factory f, List<Squad> field, int mechId) => f.CombatUnits.TryGetValue(mechId, out var u) ? u : field.SelectMany(s => s.Members).FirstOrDefault(x => x.MechId == mechId);
    static string WeaponId(MechUnit u, Mech me, CombatData c) => u.WeaponSel == 2 ? MechStats.Melee(me, c) : MechStats.Primary(me, c);

    static void Sense(Factory f, Squad s, MechUnit u, Mech me, List<Squad> field, Dictionary<int, Mech> mechs, GameData data, CombatData c)
    {
        double search = u.SearchOverride > 0 ? u.SearchOverride : MechStats.Search(me, c);
        var seen = new List<(MechUnit t, Mech tm, double d)>();
        double reach = Math.Max(search, c.VisualRange); int r = (int)Math.Ceiling(reach / GridCell), cx = (int)Math.Floor(u.X / GridCell), cy = (int)Math.Floor(u.Y / GridCell);
        for (int gx = cx - r; gx <= cx + r; gx++) for (int gy = cy - r; gy <= cy + r; gy++)
        {
            if (!f.CombatGrid.TryGetValue(((long)gx << 32) ^ ((long)gy & 0xFFFFFFFFL), out var cell)) continue;
            foreach (var (t, es) in cell)
            {
                if (es.Faction == s.Faction || !t.Alive) continue;
                var tm = mechs[t.MechId];
                double d = Dist(u, t);
                double range = search * c.StealthSearchMul.GetValueOrDefault(tm.Generation, 1.0);
                if (d <= Math.Max(range, c.VisualRange) && (d <= c.CoverCloseRange || !LineBlocked(f, u.X, u.Y, t.X, t.Y, c) && !(f.Owner != null && TerrainBlocked(f.Owner, u.X, u.Y, u.Flying, t.X, t.Y, t.Flying)))) seen.Add((t, tm, d));   // 遮蔽: 近距離以外は射線が通るときだけ
            }
        }
        u.LastEnemyCount = seen.Count; u.LastEnemyDist = seen.Count == 0 ? 1e9 : seen.Min(x => x.d);
        u.LastEnemyGen = seen.Count == 0 ? 0 : seen.OrderBy(x => x.d).First().tm.Generation;
        if (seen.Count == 0) { u.TargetMech = -1; return; }
        var pick = u.TargetPrio switch
        {
            1 => seen.OrderBy(x => x.t.Hp).ThenBy(x => x.d).First(),
            2 => seen.OrderByDescending(x => x.t.MaxHp).ThenBy(x => x.d).First(),
            3 => seen.OrderByDescending(x => c.Weapons[MechStats.Primary(x.tm, c)].Range).ThenBy(x => x.d).First(),
            _ => seen.OrderBy(x => x.d).First(),
        };
        u.TargetMech = pick.t.MechId;
    }

    static void RunBoard(Squad s, MechUnit u, Mech me, Factory f, GameData data)
    {
        McuRunner.Evaluate(me.Program!, f, data, f.Get(s.HomeBuilding) ?? new Building(), (type, n) => type switch
        {
            "in.mech.hp" => u.MaxHp <= 0 ? 0 : u.Hp * 100 / u.MaxHp,
            "in.mech.power" => u.EnergyCap <= 0 ? 0 : u.Energy * 100.0 / u.EnergyCap,
            "in.mech.ammo" => u.Ammo * 100.0 / Math.Max(1, 60),
            "in.enemy.distance" => u.LastEnemyDist >= 1e9 ? 99999 : u.LastEnemyDist,
            "in.enemy.count" => u.LastEnemyCount,
            "in.enemy.generation" => u.LastEnemyGen,
            "in.squad.order" => (int)s.Order,
            "in.tick" => f.TickCount,
            _ => 0,
        }, (type, n, v) =>
        {
            switch (type)
            {
                case "out.mech.engage": u.Engage = v != 0 ? 1 : 0; break;
                case "out.mech.retreat": u.Retreat = v != 0 ? 1 : 0; break;
                case "out.mech.target": u.TargetPrio = Math.Clamp((int)v, 0, 3); break;
                case "out.mech.weapon": u.WeaponSel = Math.Clamp((int)v, 0, 2); break;
                case "out.mech.search": u.SearchOverride = Math.Max(0, v); break;
                case "out.mech.selfdestruct": u.SelfDestruct = v != 0 ? 1 : 0; break;
            }
        });
    }

    /// <summary>砲塔: 動力のある建物の砲塔が、射程内の最寄りの敵機を撃つ(建物の持ち主: AI 勢力の拠点ならその勢力、それ以外はプレイヤー)。実弾は荷下ろし場の弾薬を 1 発 1 個。</summary>
    static void StepTurrets(Factory f, GameData data, CombatData c, Rng rng, List<Squad> field)
    {
        var mechs = f.MechById();
        foreach (var b in f.Buildings)
        {
            if (b.Destroyed || b.LastSupply <= 0) continue;
            int owner = b.OwnerPower > 0 ? 100 + b.OwnerPower : 1;
            foreach (var fl in b.Floors) foreach (var s in fl.Structures)
            {
                if (s is not Turret t || !t.Enabled || t.PowerPct <= 0) continue;
                var def = data.Structures[t.DefId];
                if (t.Cooldown > 0) { t.Cooldown--; continue; }
                double tx = b.X + s.X + 0.5, ty = b.Y + s.Y + 0.5;
                MechUnit? best = null; double bestD = def.Range;
                foreach (var sq in field) { if (sq.Faction == owner) continue; foreach (var u in sq.Members) { if (!u.Alive) continue; double d = Math.Sqrt((u.X - tx) * (u.X - tx) + (u.Y - ty) * (u.Y - ty)); if (d <= bestD && (d <= c.CoverCloseRange || !LineBlocked(f, tx, ty, u.X, u.Y, c) && !(f.Owner != null && TerrainBlocked(f.Owner, tx, ty, false, u.X, u.Y, u.Flying)))) { bestD = d; best = u; } } }
                if (best == null)
                {   // 機体がいなければ、戦争中の敵の建物(勢力の拠点・プレイヤーの拠点)を砲撃する(戦艦の砲撃戦。地上の砲塔も同じ)
                    Building? tb = null; double td = def.Range;
                    foreach (var ob in f.Buildings)
                    {
                        if (ob == b || ob.Destroyed || ob.Floors.Count == 0) continue;
                        bool enemy;
                        if (owner == 1) { var pw = ob.OwnerPower > 0 ? f.Diplomacy?.Powers.FirstOrDefault(p => p.Id == ob.OwnerPower) : null; enemy = pw != null && pw.AtWar && !pw.Vassal; }   // 戦争中の勢力の拠点と戦艦
                        else { var me = f.Diplomacy?.Powers.FirstOrDefault(p => p.Faction == owner); enemy = me != null && me.AtWar && ob.OwnerPower == 0 && (ob.Id == f.City?.Capital || ob.IsShip); }   // プレイヤーの拠点と戦艦
                        if (!enemy) continue;
                        double cx = ob.X + ob.Floors[0].W / 2.0, cy = ob.Y + ob.Floors[0].D / 2.0, d = Math.Sqrt((cx - tx) * (cx - tx) + (cy - ty) * (cy - ty));
                        if (d <= td) { td = d; tb = ob; }
                    }
                    if (tb == null) continue;
                    if (def.Ammo != null) { int ai = b.Cargo.FindIndex(x => x.Item == def.Ammo && x.Count > 0); if (ai < 0) continue; var ast = b.Cargo[ai]; if (ast.Count <= 1) b.Cargo.RemoveAt(ai); else b.Cargo[ai] = ast with { Count = ast.Count - 1 }; }
                    t.Cooldown = def.Cooldown; t.Shots++;
                    double bdmg = def.Damage * def.Accuracy / 100.0; tb.Hp = Math.Max(0, tb.Hp - bdmg);
                    f.Events.Add(new FactoryEvent { Kind = "hit_building", Building = -b.Id, Floor = tb.Id, X = (int)tx, Y = (int)ty, Power = (int)bdmg, Radius = td, Destroyed = 1 });
                    if (tb.Hp <= 0) { tb.Destroyed = true; f.Events.Add(new FactoryEvent { Kind = "building_destroyed", Building = tb.Id, Power = -b.Id }); if (tb.IsShip) Ships.Sunk(f, tb); else if (f.Owner != null) DiplomacySim.BuildingFell(f.Owner, tb, null); }
                    continue;
                }
                if (def.Ammo != null)
                {
                    int idx = b.Cargo.FindIndex(x => x.Item == def.Ammo && x.Count > 0); if (idx < 0) continue;
                    var st = b.Cargo[idx]; if (st.Count <= 1) b.Cargo.RemoveAt(idx); else b.Cargo[idx] = st with { Count = st.Count - 1 };
                }
                t.Cooldown = def.Cooldown; t.Shots++;
                var tm = mechs[best.MechId];
                double hit = def.Accuracy / 100.0 * Math.Max(c.MinHit, 1 - MechStats.Speed(tm, c) / c.SpeedRef) * (owner == 1 ? f.HitMulPct / 100.0 : 1) * (best.Flying ? c.FlyingHitMul : 1);
                bool landed = rng.Next(10_000) < (int)(hit * 10_000);
                f.Events.Add(new FactoryEvent { Kind = landed ? "hit" : "miss", Building = -b.Id, Floor = best.MechId, X = (int)tx, Y = (int)ty, Power = def.Damage, Radius = bestD, Destroyed = landed ? 1 : 0 });   // Building が負 = 砲塔
                if (!landed) continue;
                best.Hp -= def.Damage;
                if (best.Hp <= 0) Kill(f, best, tm, c, field);
            }
        }
    }

    // ---------- 遮蔽(街区単位の粗格子) ----------
    // 格子は工場(Factory)ごとに持つ(静的にすると複数の計算が混ざる)
    static void RefreshCover(Factory f, CombatData c)
    {
        int key = f.Buildings.Count * 1000003 + f.Buildings.Sum(b => b.Id * (b.Destroyed ? 7 : 1) + b.Floors.Count * 31);
        if (f.CoverGrid != null && key == f.CoverKey && f.CoverCell == c.CoverCell) return;
        f.CoverKey = key; f.CoverCell = c.CoverCell;
        int n = 0; foreach (var b in f.Buildings) if (!b.IsShip && b.Floors.Count > 0) n = Math.Max(n, (int)Math.Max(b.X + b.Floors[0].W, b.Y + b.Floors[0].D));
        int cells = n / c.CoverCell + 2; var grid = new bool[cells, cells];
        foreach (var b in f.Buildings)
        {
            if (b.IsShip || b.Destroyed || b.Floors.Count == 0 || b.Floors.Count * 3 < c.CoverMinHeight) continue;
            for (int x = (int)b.X / c.CoverCell; x <= (int)(b.X + b.Floors[0].W - 1) / c.CoverCell; x++) for (int y = (int)b.Y / c.CoverCell; y <= (int)(b.Y + b.Floors[0].D - 1) / c.CoverCell; y++)
                if (x >= 0 && y >= 0 && x < cells && y < cells) grid[x, y] = true;
        }
        f.CoverGrid = grid;
    }
    /// <summary>射線が建物の粗格子に遮られるか(両端のセルは除く)。</summary>
    /// <summary>射線が建物の粗格子に遮られるか。レイトレーシングの格子走査(Amanatides–Woo の DDA)で、射線が通るセルだけを順に見る(点を等間隔に打つ方式のように取りこぼさず、長さに比例した整数演算)。
    /// 両端のセルは除く。</summary>
    public static bool LineBlocked(Factory f, double x0, double y0, double x1, double y1, CombatData c)
    {
        var grid = f.CoverGrid; if (grid == null) return false;
        int cs = c.CoverCell;
        int sx = (int)Math.Floor(x0 / cs), sy = (int)Math.Floor(y0 / cs), ex = (int)Math.Floor(x1 / cs), ey = (int)Math.Floor(y1 / cs);
        if (sx == ex && sy == ey) return false;
        return Dda(grid, cs, x0, y0, x1, y1, sx, sy, ex, ey);   // 記憶(セル対のキャッシュ)は計測で DDA 本体より高くついたので持たない
    }
    /// <summary>地面の最上段の高さ(y)。地形が無ければ 0。</summary>
    const int HeightCell = 4;
    /// <summary>地面の最上段の高さ(y)。ブロックを直接引く(格子を作るとき用)。地形が無ければ 0。</summary>
    public static int GroundHeight(Simulation sim, double x, double y) { int ix = (int)Math.Floor(x), iz = (int)Math.Floor(y); for (int h = 15; h >= 0; h--) if (sim.World.Get(ix, h, iz) != BlockRegistry.Air) return h; return 0; }
    /// <summary>地面の高さを 4 m の格子に写す(地図の大きさは建物と街区から)。地形が変わる(World.Version)と作り直す。起伏が無ければ空にして判定を飛ばす。</summary>
    static void RefreshHeights(Simulation sim)
    {
        var f = sim.Factory; if (f.HeightVersion == sim.World.Version && f.HeightGridBuilt) return;
        f.HeightVersion = sim.World.Version; f.HeightGridBuilt = true;
        int n = Math.Max(sim.CityData.MapSize, 1); foreach (var b in f.Buildings) if (b.Floors.Count > 0) n = Math.Max(n, (int)Math.Max(b.X + b.Floors[0].W, b.Y + b.Floors[0].D));
        int cells = n / HeightCell + 2; var grid = new byte[cells, cells]; byte min = 255, max = 0;
        for (int gx = 0; gx < cells; gx++) for (int gy = 0; gy < cells; gy++) { byte h = (byte)GroundHeight(sim, gx * HeightCell + HeightCell / 2, gy * HeightCell + HeightCell / 2); grid[gx, gy] = h; if (h < min) min = h; if (h > max) max = h; }
        f.HeightGrid = max > min ? grid : null;   // 平らなら判定しない
    }
    static int H(byte[,] g, double x, double y) { int gx = (int)(x / HeightCell), gy = (int)(y / HeightCell); return gx < 0 || gy < 0 || gx >= g.GetLength(0) || gy >= g.GetLength(1) ? 0 : g[gx, gy]; }
    /// <summary>地形の起伏による遮蔽: 射線(目の高さ 2 m、飛行中は 18 m)より高い地面が 4 m ごとの標本にあれば遮られる。高さは格子から読む。</summary>
    public static bool TerrainBlocked(Simulation sim, double x0, double y0, bool fly0, double x1, double y1, bool fly1)
    {
        var g = sim.Factory.HeightGrid; if (g == null) return false;
        double h0 = H(g, x0, y0) + (fly0 ? 18 : 2), h1 = H(g, x1, y1) + (fly1 ? 18 : 2);
        double len = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)); int n = (int)(len / HeightCell); if (n < 2) return false;
        for (int i = 1; i < n; i++) { double t = (double)i / n; if (H(g, x0 + (x1 - x0) * t, y0 + (y1 - y0) * t) > h0 + (h1 - h0) * t) return true; }
        return false;
    }

    static bool Dda(bool[,] grid, int cs, double x0, double y0, double x1, double y1, int sx, int sy, int ex, int ey)
    {
        double dx = x1 - x0, dy = y1 - y0;
        int stepX = dx > 0 ? 1 : dx < 0 ? -1 : 0, stepY = dy > 0 ? 1 : dy < 0 ? -1 : 0;
        double tDeltaX = stepX == 0 ? double.PositiveInfinity : cs / Math.Abs(dx), tDeltaY = stepY == 0 ? double.PositiveInfinity : cs / Math.Abs(dy);
        double nextX = stepX > 0 ? (sx + 1) * (double)cs : sx * (double)cs, nextY = stepY > 0 ? (sy + 1) * (double)cs : sy * (double)cs;
        double tMaxX = stepX == 0 ? double.PositiveInfinity : (nextX - x0) / dx, tMaxY = stepY == 0 ? double.PositiveInfinity : (nextY - y0) / dy;
        int gx = sx, gy = sy, w = grid.GetLength(0), h = grid.GetLength(1);
        for (int guard = 0; guard < 4096; guard++)
        {
            if (tMaxX < tMaxY) { gx += stepX; tMaxX += tDeltaX; } else { gy += stepY; tMaxY += tDeltaY; }
            if (gx == ex && gy == ey) return false;                      // 相手のセルに着いた
            if (gx >= 0 && gy >= 0 && gx < w && gy < h && grid[gx, gy]) return true;
            if ((stepX > 0 ? gx > ex : stepX < 0 ? gx < ex : false) || (stepY > 0 ? gy > ey : stepY < 0 ? gy < ey : false)) return false;
        }
        return false;
    }

    static Loot? NearestLoot(Factory f, Squad s) { double cx = s.X, cy = s.Y; return f.Loot.OrderBy(l => (l.X - cx) * (l.X - cx) + (l.Y - cy) * (l.Y - cy)).FirstOrDefault(); }

    /// <summary>修理(1 時間ごと): 格納庫で待機中の機体は、建物の荷下ろし場の部品(回収した機体の部品か装甲板)を 1 時間に 1 個使って耐久を 25% 戻す(G16 #9)。</summary>
    public static void RepairHour(Factory f, CombatData c)
    {
        foreach (var s in f.Squads.Where(s => s.State == SquadState.InHangar))
        {
            var home = f.Get(s.HomeBuilding); if (home == null) continue;
            foreach (var u in s.Members.Where(u => u.Alive && u.Hp < u.MaxHp))
                for (int k = 0; k < c.RepairPartsPerHour && u.Hp < u.MaxHp; k++)
                {
                    int idx = -1; foreach (var it in c.RepairItems) { idx = home.Cargo.FindIndex(x => x.Item == it && x.Count > 0); if (idx >= 0) break; }
                    if (idx < 0) break;
                    var st = home.Cargo[idx]; if (st.Count <= 1) home.Cargo.RemoveAt(idx); else home.Cargo[idx] = st with { Count = st.Count - 1 };
                    u.Hp = Math.Min(u.MaxHp, u.Hp + u.MaxHp * c.RepairPerPart);
                    f.Events.Add(new FactoryEvent { Kind = "repaired", Building = s.Id, Power = u.MechId, Destroyed = (int)Math.Round(u.Hp * 100 / u.MaxHp) });
                }
        }
    }

    static (double, double) Center(Building b) => b.Floors.Count == 0 ? (b.X, b.Y) : (b.X + b.Floors[0].W / 2.0, b.Y + b.Floors[0].D / 2.0);

    static void Kill(Factory f, MechUnit t, Mech tm, CombatData c, List<Squad> field, Squad? victim = null, Squad? shooter = null)
    {
        t.Alive = false; t.Hp = 0;
        var sq = victim ?? field.FirstOrDefault(s => s.Members.Contains(t)); if (sq != null) { DiplomacySim.MechLost(f, sq.Faction); MoraleChange(f, sq, -c.MoraleLossPerMech, c); }
        if (shooter != null) { MoraleChange(f, shooter, c.MoraleKillGain, c); shooter.Exp = Math.Min(100, shooter.Exp + c.ExpPerKill); }
        int parts = (int)Math.Round(tm.Parts.Count * c.LootRate);
        f.Events.Add(new FactoryEvent { Kind = "destroyed", X = (int)t.X, Y = (int)t.Y, Power = tm.Id, Destroyed = parts });
        f.Loot.Add(new Loot { X = (int)t.X, Y = (int)t.Y, Parts = tm.Parts.OrderByDescending(p => p.Quality).Take(parts).Select(p => new MechPart { Item = p.Item, Quality = p.Quality }).ToList() });
    }

    /// <summary>自爆: リアクターを 150% の過負荷にして爆発。工場と同じ式(半径 = 出力 × 0.02 セル)。周囲の敵味方に被害。</summary>
    static void Explode(Factory f, Squad s, MechUnit u, Mech me, List<Squad> field, Dictionary<int, Mech> mechs, GameData data, CombatData c)
    {
        int output = me.PowerSource == "reactor" ? MechStats.ReactorOutputKw(me, data) * data.OverloadMax / 100 : 0;
        double radius = output * c.SelfDestructRadiusPerKw, dmg = output * c.SelfDestructDamagePerKw;
        int hitCount = 0;
        foreach (var t in field.SelectMany(x => x.Members).Where(t => t.Alive && t != u).ToList())
            if (Dist(u, t) <= radius) { t.Hp -= dmg; hitCount++; if (t.Hp <= 0) Kill(f, t, mechs[t.MechId], c, field); }
        u.Alive = false; u.Hp = 0; u.SelfDestruct = 0;
        f.Events.Add(new FactoryEvent { Kind = "selfdestruct", Building = s.Id, X = (int)u.X, Y = (int)u.Y, Power = output, Radius = radius, Destroyed = hitCount });
    }
}

/// <summary>防御壁を築く(段階 1〜3)。費用 = 外周 × 5 × 上げる段階(国庫、創造モードは無料)。耐久の上限が増え、増えた分はすぐ加わる。</summary>
public sealed record BuildWall(int Building, int Level) : ICommand
{
    public void Apply(Simulation s)
    {
        var f = s.Factory; var b = f.Get(Building); if (b == null || b.Floors.Count == 0) return;
        int level = Math.Clamp(Level, 0, s.Combat.WallMaxLevel); if (level <= b.WallLevel) return;
        long cost = f.Creative ? 0 : (long)Math.Round(b.Perimeter * s.Combat.WallCostPerCell * (level - b.WallLevel));
        if (f.Treasury < cost) { f.Events.Add(new FactoryEvent { Kind = "build_failed", Building = Building, Power = (int)cost }); return; }
        f.Treasury -= cost;
        double add = b.Perimeter * s.Combat.WallHpPerCell * (level - b.WallLevel);
        b.WallLevel = level; b.MaxHp += add; b.Hp += add;
        f.Events.Add(new FactoryEvent { Kind = "wall_built", Building = Building, Power = level, Destroyed = (int)cost });
    }
}

/// <summary>撃破された機体の部品(戦場に残る。回収は工場連携の実装時)。</summary>
public sealed class Loot { public int X { get; set; } public int Y { get; set; } public List<MechPart> Parts { get; set; } = new(); }

// ---------- 命令 ----------
/// <summary>建物の地図上の位置(戦場の座標。機体の帰還先)。</summary>
public sealed record SetBuildingPosition(int Building, double X, double Y) : ICommand
{
    public void Apply(Simulation s) { var b = s.Factory.Get(Building); if (b != null) { b.X = X; b.Y = Y; } }
}

/// <summary>機体を直接作る(AI 国家・試験用。工場を通さない)。</summary>
public sealed record CreateMech(int Id, int Generation, string Archetype, int Quality, string PowerSource = "reactor") : ICommand
{
    public void Apply(Simulation s)
    {
        if (s.Factory.Mechs.Any(m => m.Id == Id)) return;
        var m = new Mech { Id = Id, Generation = Generation, Archetype = Archetype, PowerSource = PowerSource, Quality = Quality };
        foreach (var (it, n) in new[] { ("mech_head", 1), ("mech_torso", 1), ("mech_arm", 2), ("mech_leg", 2), ("mech_back", 1), ("mech_weapon", 1), (PowerSource == "reactor" ? "reactor_core" : "battery_pack", 1) })
            for (int i = 0; i < n; i++) m.Parts.Add(new MechPart { Item = it, Quality = Quality });
        s.Factory.Mechs.Add(m); s.Factory.NextMechId = Math.Max(s.Factory.NextMechId, Id + 1);
    }
}

/// <summary>隊を作る(格納庫で待機)。同じ系統・同じ世代の機体だけ。最大 12 機。</summary>
public sealed record FormSquad(int Id, int Faction, string Name, IReadOnlyList<int> MechIds, int HomeBuilding) : ICommand
{
    public void Apply(Simulation s)
    {
        if (s.Factory.Squads.Any(x => x.Id == Id) || MechIds.Count == 0) return;
        var mechs = MechIds.Select(id => s.Factory.Mechs.FirstOrDefault(m => m.Id == id)).ToList();
        if (mechs.Any(m => m == null) || s.Factory.Squads.Any(q => q.Members.Any(u => MechIds.Contains(u.MechId)))) return;

        var first = mechs[0]!;
        if (mechs.Any(m => m!.Archetype != first.Archetype || m.Generation != first.Generation) || mechs.Count > s.Combat.SquadMax) return;
        var sq = new Squad { Id = Id, Faction = Faction, Name = Name, Archetype = first.Archetype, Generation = first.Generation, HomeBuilding = HomeBuilding, State = SquadState.InHangar, Morale = s.Combat.MoraleStart };
        var hb = s.Factory.Get(HomeBuilding);
        foreach (var m in mechs)
        {
            double hp = MechStats.MaxHp(m!, s.Combat); long cap = MechStats.EnergyCap(m!, s.Data, s.Combat);
            sq.Members.Add(new MechUnit { MechId = m!.Id, Hp = hp, MaxHp = hp, Energy = cap, EnergyCap = cap, Ammo = s.Combat.AmmoCapacity, X = hb?.X ?? 0, Y = hb?.Y ?? 0 });
        }
        s.Factory.Squads.Add(sq); s.Factory.NextSquadId = Math.Max(s.Factory.NextSquadId, Id + 1);
    }
}

/// <summary>隊を屋外に出す(格納庫から出撃。位置は格納庫の建物の前)。</summary>
public sealed record Deploy(int SquadId, double X, double Y) : ICommand
{
    public void Apply(Simulation s)
    {
        var sq = s.Factory.Squads.FirstOrDefault(x => x.Id == SquadId); if (sq == null) return;
        if (sq.Faction == 1 && !CitySim.CanDeploy(s.Factory, sq, s.CityData)) { s.Factory.Events.Add(new FactoryEvent { Kind = "no_crew", Building = SquadId }); return; }   // G17 #8・#18
        if (!Armies.HasCommander(s.Factory, sq)) { s.Factory.Events.Add(new FactoryEvent { Kind = "no_commander", Building = SquadId }); return; }   // G21 #1: 隊長がいないと出撃できない
        sq.State = SquadState.Field; int i = 0;
        foreach (var u in sq.Members.Where(u => u.Alive)) { u.X = X + (i % 4) * 3; u.Y = Y + (i / 4) * 3; i++; }
        sq.Order = SquadOrder.Hold; sq.OrderX = X; sq.OrderY = Y;
    }
}

/// <summary>隊への命令: 待機 / 移動(x, y) / 攻撃(隊) / 退却 / 自爆。陣形も指定できる。</summary>
public sealed record OrderSquad(int SquadId, SquadOrder Order, double X = 0, double Y = 0, int TargetSquad = -1, Formation? Formation = null, int TargetBuilding = -1) : ICommand
{
    public void Apply(Simulation s)
    {
        var sq = s.Factory.Squads.FirstOrDefault(x => x.Id == SquadId); if (sq == null) return;
        sq.Order = Order; sq.OrderX = X; sq.OrderY = Y; sq.TargetSquad = TargetSquad; sq.TargetBuilding = TargetBuilding;
        if (Formation != null) sq.Formation = Formation.Value;
    }
}
