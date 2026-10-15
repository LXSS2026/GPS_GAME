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
    public int CoverCell { get; set; } = 16; public double CoverMinHeight { get; set; } = 6; public double CoverCloseRange { get; set; } = 20;
    public double FighterMinDist { get; set; } = 60; public double FighterSpeedMul { get; set; } = 2; public double FlyingHitMul { get; set; } = 0.7;
    public double WallHpPerCell { get; set; } = 400;
    public double WallCostPerCell { get; set; } = 5;
    public int WallMaxLevel { get; set; } = 3;
    public double RepairPerPart { get; set; } = 0.25;
    public int RepairPartsPerHour { get; set; } = 1;
    public List<string> RepairItems { get; set; } = new();
    public double DefaultRetreatHp { get; set; } = 0.25;
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
        var cv = r.GetProperty("cover"); c.CoverCell = cv.GetProperty("cellSize").GetInt32(); c.CoverMinHeight = cv.GetProperty("minHeight").GetDouble(); c.CoverCloseRange = cv.GetProperty("closeRange").GetDouble();
        var fy = r.GetProperty("flight"); c.FighterMinDist = fy.GetProperty("fighterMinDist").GetDouble(); c.FighterSpeedMul = fy.GetProperty("speedMul").GetDouble(); c.FlyingHitMul = fy.GetProperty("hitMul").GetDouble();
        var wl = r.GetProperty("wall"); c.WallHpPerCell = wl.GetProperty("hpPerCell").GetDouble(); c.WallCostPerCell = wl.GetProperty("costPerCell").GetDouble(); c.WallMaxLevel = wl.GetProperty("maxLevel").GetInt32();
        var rp = r.GetProperty("repair"); c.RepairPerPart = rp.GetProperty("perPart").GetDouble(); c.RepairPartsPerHour = rp.GetProperty("partsPerHour").GetInt32(); c.RepairItems = rp.GetProperty("items").EnumerateArray().Select(e => e.GetString()!).ToList(); c.DefaultRetreatHp = r.GetProperty("defaultRetreatHp").GetDouble(); c.SquadMax = r.GetProperty("squadMax").GetInt32(); c.FormationSpacing = r.GetProperty("formationSpacing").GetDouble();
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
    [JsonIgnore] public bool Flying { get; set; }
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
    public Formation Formation { get; set; }
    public double X => Members.Where(m => m.Alive).Select(m => m.X).DefaultIfEmpty(OrderX).Average();
    public double Y => Members.Where(m => m.Alive).Select(m => m.Y).DefaultIfEmpty(OrderY).Average();
    public int Alive => Members.Count(m => m.Alive);
}

/// <summary>機体の性能値(部品の品質と系統、世代から)。</summary>
public static class MechStats
{
    static double Q(Mech m, string item) { var ps = m.Parts.Where(p => p.Item == item).Select(p => (double)p.Quality).ToList(); return ps.Count == 0 ? 70 : ps.Average(); }
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
    public static int ReactorOutputKw(Mech m, GameData data) => m.PowerSource != "reactor" ? 0 : Building.ReactorOutput(data.Structures[$"reactor_g{m.Generation}"], (int)Q(m, "reactor_core"), data);
    public static long EnergyCap(Mech m, GameData data, CombatData c) => m.PowerSource == "reactor" ? (long)ReactorOutputKw(m, data) * Clock.TicksPerSecond * c.ReactorEnergySeconds : data.MechBatteryCapacity;
    public static string Primary(Mech m, CombatData c) => c.Archetype[m.Archetype].Primary;
    public static string Melee(Mech m, CombatData c) => c.Archetype[m.Archetype].Melee;
}

/// <summary>戦闘の計算(手順 4 第 1 段階)。毎ティック、屋外の隊について 索敵 → 制御基板 → 移動 → 射撃と近接 → 給電 の順。</summary>
public static class Combat
{
    public static void Tick(Factory f, GameData data, CombatData c, Rng rng, Simulation? sim = null)
    {
        Ships.Step(f); RefreshCover(f, c);
        var field = f.Squads.Where(s => s.State == SquadState.Field).ToList();
        StepTurrets(f, data, c, rng, field);
        if (field.Count == 0) return;
        var mechs = f.Mechs.ToDictionary(m => m.Id);
        // 索敵と制御基板
        foreach (var s in field)
            foreach (var u in s.Members.Where(u => u.Alive))
            {
                var me = mechs[u.MechId];
                Sense(f, s, u, me, field, mechs, data, c);
                if (me.Program != null && ++u.McuTimer >= Math.Max(1, me.Program.Interval)) { u.McuTimer = 0; RunBoard(s, u, me, f, data); }
                else if (me.Program == null) { u.Retreat = u.Hp < u.MaxHp * f.RetreatHp ? 1 : 0; u.Engage = 1; }
            }
        // 移動
        foreach (var s in field)
        {
            if (s.Order == SquadOrder.SelfDestruct) { foreach (var u in s.Members.Where(u => u.Alive)) u.SelfDestruct = 1; }
            int idx = 0;
            foreach (var u in s.Members.Where(u => u.Alive))
            {
                var me = mechs[u.MechId];
                double spd = MechStats.Speed(me, c) / Clock.TicksPerSecond;
                (double tx, double ty)? goal = null;
                if (u.Retreat != 0 || s.Order == SquadOrder.Retreat) { var hb = f.Get(s.HomeBuilding); if (hb != null) goal = (hb.X, hb.Y); }
                else if (s.Order == SquadOrder.Move) goal = Slot(s, idx, s.OrderX, s.OrderY, c);
                else if (s.Order == SquadOrder.Collect) { var lt = NearestLoot(f, s); if (lt != null) goal = Slot(s, idx, lt.X, lt.Y, c); }
                else if (s.Order == SquadOrder.Attack && u.Engage != 0 && u.TargetMech >= 0)
                {
                    var t = FindUnit(field, u.TargetMech); var w = c.Weapons[WeaponId(u, me, c)];
                    if (t != null && Dist(u, t) > w.Range * 0.8) goal = (t.X, t.Y);
                }
                else if (s.Order == SquadOrder.Attack && s.TargetSquad >= 0) { var ts = f.Squads.FirstOrDefault(x => x.Id == s.TargetSquad); if (ts != null && ts.Alive > 0) goal = Slot(s, idx, ts.X, ts.Y, c); }
                else if (s.Order == SquadOrder.Attack && s.TargetBuilding >= 0) { var tb = f.Get(s.TargetBuilding); if (tb != null && !tb.Destroyed) { var (bx, by) = Center(tb); var w = c.Weapons[WeaponId(u, me, c)]; double dd = Math.Sqrt((bx - u.X) * (bx - u.X) + (by - u.Y) * (by - u.Y)); if (dd > w.Range * 0.7) goal = Slot(s, idx, bx, by - w.Range * 0.6, c); } }
                u.Flying = false;
                if (goal is var (gx, gy)) { double dx = gx - u.X, dy = gy - u.Y, d = Math.Sqrt(dx * dx + dy * dy); if (me.Archetype == "fast" && d > c.FighterMinDist) { u.Flying = true; spd *= c.FighterSpeedMul; } if (d > 0.5) { double k = Math.Min(1, spd / d); u.X += dx * k; u.Y += dy * k; } }
                idx++;
            }
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
            if (home != null && s.Alive > 0 && (s.Order == SquadOrder.Retreat || s.Members.Where(u => u.Alive).All(u => u.Retreat != 0)) && s.Members.Where(u => u.Alive).All(u => Math.Abs(u.X - home.X) + Math.Abs(u.Y - home.Y) < 3))
            {
                s.State = SquadState.InHangar; s.Order = SquadOrder.Hold; s.TargetBuilding = -1; foreach (var u in s.Members) { u.Retreat = 0; if (f.AmmoResupply) u.Ammo = c.AmmoCapacity; }   // 軍事費が低いと補給されない
                foreach (var p in s.Cargo) home.Cargo.Add(new ItemStack(p.Item, p.Quality)); s.Cargo.Clear();   // 回収した部品は建物の荷下ろし場へ
            }
        }
        // 射撃・近接・自爆
        foreach (var s in field)
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
                var t = FindUnit(field, u.TargetMech); if (t == null || !t.Alive) { u.TargetMech = -1; continue; }
                string wid = WeaponId(u, me, c); var w = c.Weapons[wid];
                double dist = Dist(u, t);
                if (dist > w.Range * c.FarMul) continue;
                if (w.Kind == "beam") { if (u.Energy < w.EnergyPerShot) continue; } else if (w.AmmoPerShot > 0) { if (u.Ammo < w.AmmoPerShot) continue; }
                u.Cooldown = w.Cooldown;
                if (w.Kind == "beam") u.Energy -= w.EnergyPerShot; else u.Ammo -= w.AmmoPerShot;
                var tm = mechs[t.MechId];
                if (t.Flying && w.Melee != null) continue;   // 飛んでいる相手に近接は届かない
                double hit = w.Accuracy * (dist <= w.Range ? 1 : 1 - (dist - w.Range) / (w.Range * (c.FarMul - 1)) * (1 - c.FarHit)) * Math.Max(c.MinHit, 1 - MechStats.Speed(tm, c) / c.SpeedRef) * f.HitMulPct / 100.0 * (t.Flying ? c.FlyingHitMul : 1);   // 軍の長の効果(G19)、飛行中は当たりにくい
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
                if (t.Hp <= 0) Kill(f, t, tm, c, field);
            }
        // 給電: 屋外では、撃っていないリアクター機がバッテリー機に給電。リアクター機は毎ティック回復
        foreach (var s in field)
        {
            var alive = s.Members.Where(u => u.Alive).ToList();
            foreach (var u in alive) { var me = mechs[u.MechId]; if (me.PowerSource == "reactor") u.Energy = Math.Min(u.EnergyCap, u.Energy + MechStats.ReactorOutputKw(me, data)); }
            foreach (var giver in alive)
            {
                var gm = mechs[giver.MechId];
                if (gm.PowerSource != "reactor" || giver.TargetMech >= 0) continue;   // 交戦中は給電しない
                long give = (long)(MechStats.ReactorOutputKw(gm, data) * c.FieldChargeMul);
                foreach (var taker in alive.Where(t => mechs[t.MechId].PowerSource == "battery" && t.Energy < t.EnergyCap).OrderBy(t => t.Energy))
                {
                    long room = taker.EnergyCap - taker.Energy; long x = Math.Min(room, Math.Min(give, giver.Energy));
                    if (x <= 0) break;
                    taker.Energy += x; giver.Energy -= x; give -= x;
                }
            }
        }
    }

    /// <summary>格納庫にいる待機中のリアクター機の出力(kW)。建物の供給に加える。</summary>
    public static int HangarSupply(Factory f, Building b, GameData data)
    {
        int sum = 0;
        foreach (var s in f.Squads.Where(s => s.State == SquadState.InHangar && s.HomeBuilding == b.Id))
            foreach (var u in s.Members.Where(u => u.Alive))
            { var m = f.Mechs.FirstOrDefault(x => x.Id == u.MechId); if (m != null) sum += MechStats.ReactorOutputKw(m, data); }
        return sum;
    }

    static (double, double) Slot(Squad s, int idx, double cx, double cy, CombatData c)
    {
        double sp = c.FormationSpacing;
        return s.Formation switch
        {
            Formation.Line => (cx + (idx - s.Alive / 2.0) * sp, cy),
            Formation.Column => (cx, cy - idx * sp),
            _ => (cx + (idx % 4 - 1.5) * sp * 1.5, cy - (idx / 4) * sp * 1.5),
        };
    }

    static double Dist(MechUnit a, MechUnit b) { double dx = a.X - b.X, dy = a.Y - b.Y; return Math.Sqrt(dx * dx + dy * dy); }
    static MechUnit? FindUnit(List<Squad> field, int mechId) => field.SelectMany(s => s.Members).FirstOrDefault(u => u.MechId == mechId);
    static string WeaponId(MechUnit u, Mech me, CombatData c) => u.WeaponSel == 2 ? MechStats.Melee(me, c) : MechStats.Primary(me, c);

    static void Sense(Factory f, Squad s, MechUnit u, Mech me, List<Squad> field, Dictionary<int, Mech> mechs, GameData data, CombatData c)
    {
        double search = u.SearchOverride > 0 ? u.SearchOverride : MechStats.Search(me, c);
        var seen = new List<(MechUnit t, Mech tm, double d)>();
        foreach (var es in field.Where(x => x.Faction != s.Faction))
            foreach (var t in es.Members.Where(t => t.Alive))
            {
                var tm = mechs[t.MechId];
                double d = Dist(u, t);
                double range = search * c.StealthSearchMul.GetValueOrDefault(tm.Generation, 1.0);
                if (d <= Math.Max(range, c.VisualRange) && (d <= c.CoverCloseRange || !LineBlocked(f, u.X, u.Y, t.X, t.Y, c))) seen.Add((t, tm, d));   // 遮蔽: 近距離以外は射線が通るときだけ
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
        var mechs = f.Mechs.ToDictionary(m => m.Id);
        foreach (var b in f.Buildings)
        {
            if (b.Destroyed || b.LastSupply <= 0) continue;
            int owner = f.Diplomacy?.Powers.FirstOrDefault(p => p.HomeBuilding == b.Id)?.Faction ?? 1;
            foreach (var fl in b.Floors) foreach (var s in fl.Structures)
            {
                if (s is not Turret t || !t.Enabled || t.PowerPct <= 0) continue;
                var def = data.Structures[t.DefId];
                if (t.Cooldown > 0) { t.Cooldown--; continue; }
                double tx = b.X + s.X + 0.5, ty = b.Y + s.Y + 0.5;
                MechUnit? best = null; double bestD = def.Range;
                foreach (var sq in field) { if (sq.Faction == owner) continue; foreach (var u in sq.Members) { if (!u.Alive) continue; double d = Math.Sqrt((u.X - tx) * (u.X - tx) + (u.Y - ty) * (u.Y - ty)); if (d <= bestD && (d <= c.CoverCloseRange || !LineBlocked(f, tx, ty, u.X, u.Y, c))) { bestD = d; best = u; } } }
                if (best == null)
                {   // 機体がいなければ、戦争中の敵の建物(勢力の拠点・プレイヤーの拠点)を砲撃する(戦艦の砲撃戦。地上の砲塔も同じ)
                    Building? tb = null; double td = def.Range;
                    foreach (var ob in f.Buildings)
                    {
                        if (ob == b || ob.Destroyed || ob.Floors.Count == 0) continue;
                        var pw = f.Diplomacy?.Powers.FirstOrDefault(p => p.HomeBuilding == ob.Id);
                        bool enemy = owner == 1 ? pw != null && pw.AtWar && !pw.Vassal : (ob.Id == f.City?.Capital && (f.Diplomacy?.Powers.FirstOrDefault(p => p.Faction == owner)?.AtWar ?? false));
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
    public static bool LineBlocked(Factory f, double x0, double y0, double x1, double y1, CombatData c)
    {
        var grid = f.CoverGrid; if (grid == null) return false;
        int cs = c.CoverCell, n = (int)Math.Ceiling(Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)) / (cs / 2.0)); if (n < 2) return false;
        int sx = (int)x0 / cs, sy = (int)y0 / cs, ex = (int)x1 / cs, ey = (int)y1 / cs;
        for (int i = 1; i < n; i++)
        {
            double t = (double)i / n; int gx = (int)(x0 + (x1 - x0) * t) / cs, gy = (int)(y0 + (y1 - y0) * t) / cs;
            if ((gx == sx && gy == sy) || (gx == ex && gy == ey)) continue;
            if (gx < 0 || gy < 0 || gx >= grid.GetLength(0) || gy >= grid.GetLength(1)) continue;
            if (grid[gx, gy]) return true;
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

    static void Kill(Factory f, MechUnit t, Mech tm, CombatData c, List<Squad> field)
    {
        t.Alive = false; t.Hp = 0;
        var sq = field.FirstOrDefault(s => s.Members.Contains(t)); if (sq != null) DiplomacySim.MechLost(f, sq.Faction);
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
        var sq = new Squad { Id = Id, Faction = Faction, Name = Name, Archetype = first.Archetype, Generation = first.Generation, HomeBuilding = HomeBuilding, State = SquadState.InHangar };
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
