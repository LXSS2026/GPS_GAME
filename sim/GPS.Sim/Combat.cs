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
        c.LootRate = r.GetProperty("lootRate").GetDouble(); c.DefaultRetreatHp = r.GetProperty("defaultRetreatHp").GetDouble(); c.SquadMax = r.GetProperty("squadMax").GetInt32(); c.FormationSpacing = r.GetProperty("formationSpacing").GetDouble();
        return c;
    }
}

public enum SquadOrder { Hold = 0, Move = 1, Attack = 2, Retreat = 3, SelfDestruct = 4 }
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
    public static void Tick(Factory f, GameData data, CombatData c, Rng rng)
    {
        var field = f.Squads.Where(s => s.State == SquadState.Field).ToList();
        if (field.Count == 0) return;
        var mechs = f.Mechs.ToDictionary(m => m.Id);
        // 索敵と制御基板
        foreach (var s in field)
            foreach (var u in s.Members.Where(u => u.Alive))
            {
                var me = mechs[u.MechId];
                Sense(s, u, me, field, mechs, data, c);
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
                else if (s.Order == SquadOrder.Attack && u.Engage != 0 && u.TargetMech >= 0)
                {
                    var t = FindUnit(field, u.TargetMech); var w = c.Weapons[WeaponId(u, me, c)];
                    if (t != null && Dist(u, t) > w.Range * 0.8) goal = (t.X, t.Y);
                }
                else if (s.Order == SquadOrder.Attack && s.TargetSquad >= 0) { var ts = f.Squads.FirstOrDefault(x => x.Id == s.TargetSquad); if (ts != null && ts.Alive > 0) goal = Slot(s, idx, ts.X, ts.Y, c); }
                if (goal is var (gx, gy)) { double dx = gx - u.X, dy = gy - u.Y, d = Math.Sqrt(dx * dx + dy * dy); if (d > 0.5) { double k = Math.Min(1, spd / d); u.X += dx * k; u.Y += dy * k; } }
                idx++;
            }
            // 退却して帰還先に着いたら格納庫へ
            var home = f.Get(s.HomeBuilding);
            if (home != null && s.Alive > 0 && (s.Order == SquadOrder.Retreat || s.Members.Where(u => u.Alive).All(u => u.Retreat != 0)) && s.Members.Where(u => u.Alive).All(u => Math.Abs(u.X - home.X) + Math.Abs(u.Y - home.Y) < 3))
            { s.State = SquadState.InHangar; s.Order = SquadOrder.Hold; foreach (var u in s.Members) { u.Retreat = 0; if (f.AmmoResupply) u.Ammo = c.AmmoCapacity; } }   // 軍事費が低いと補給されない
        }
        // 射撃・近接・自爆
        foreach (var s in field)
            foreach (var u in s.Members.Where(u => u.Alive).ToList())
            {
                var me = mechs[u.MechId];
                if (u.SelfDestruct != 0) { Explode(f, s, u, me, field, mechs, data, c); continue; }
                if (u.Cooldown > 0) { u.Cooldown--; continue; }
                if (u.Engage == 0 || u.Retreat != 0 || u.TargetMech < 0) continue;
                var t = FindUnit(field, u.TargetMech); if (t == null || !t.Alive) { u.TargetMech = -1; continue; }
                string wid = WeaponId(u, me, c); var w = c.Weapons[wid];
                double dist = Dist(u, t);
                if (dist > w.Range * c.FarMul) continue;
                if (w.Kind == "beam") { if (u.Energy < w.EnergyPerShot) continue; } else if (w.AmmoPerShot > 0) { if (u.Ammo < w.AmmoPerShot) continue; }
                u.Cooldown = w.Cooldown;
                if (w.Kind == "beam") u.Energy -= w.EnergyPerShot; else u.Ammo -= w.AmmoPerShot;
                var tm = mechs[t.MechId];
                double hit = w.Accuracy * (dist <= w.Range ? 1 : 1 - (dist - w.Range) / (w.Range * (c.FarMul - 1)) * (1 - c.FarHit)) * Math.Max(c.MinHit, 1 - MechStats.Speed(tm, c) / c.SpeedRef) * f.HitMulPct / 100.0;   // 軍の長の効果(G19)
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
                if (t.Hp <= 0) Kill(f, t, tm, c);
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

    static void Sense(Squad s, MechUnit u, Mech me, List<Squad> field, Dictionary<int, Mech> mechs, GameData data, CombatData c)
    {
        double search = u.SearchOverride > 0 ? u.SearchOverride : MechStats.Search(me, c);
        var seen = new List<(MechUnit t, Mech tm, double d)>();
        foreach (var es in field.Where(x => x.Faction != s.Faction))
            foreach (var t in es.Members.Where(t => t.Alive))
            {
                var tm = mechs[t.MechId];
                double d = Dist(u, t);
                double range = search * c.StealthSearchMul.GetValueOrDefault(tm.Generation, 1.0);
                if (d <= Math.Max(range, c.VisualRange)) seen.Add((t, tm, d));
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

    static void Kill(Factory f, MechUnit t, Mech tm, CombatData c)
    {
        t.Alive = false; t.Hp = 0;
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
            if (Dist(u, t) <= radius) { t.Hp -= dmg; hitCount++; if (t.Hp <= 0) Kill(f, t, mechs[t.MechId], c); }
        u.Alive = false; u.Hp = 0; u.SelfDestruct = 0;
        f.Events.Add(new FactoryEvent { Kind = "selfdestruct", Building = s.Id, X = (int)u.X, Y = (int)u.Y, Power = output, Radius = radius, Destroyed = hitCount });
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
        if (!CitySim.CanDeploy(s.Factory, sq, s.CityData)) { s.Factory.Events.Add(new FactoryEvent { Kind = "no_crew", Building = SquadId }); return; }   // G17 #8・#18
        sq.State = SquadState.Field; int i = 0;
        foreach (var u in sq.Members.Where(u => u.Alive)) { u.X = X + (i % 4) * 3; u.Y = Y + (i / 4) * 3; i++; }
        sq.Order = SquadOrder.Hold; sq.OrderX = X; sq.OrderY = Y;
    }
}

/// <summary>隊への命令: 待機 / 移動(x, y) / 攻撃(隊) / 退却 / 自爆。陣形も指定できる。</summary>
public sealed record OrderSquad(int SquadId, SquadOrder Order, double X = 0, double Y = 0, int TargetSquad = -1, Formation? Formation = null) : ICommand
{
    public void Apply(Simulation s)
    {
        var sq = s.Factory.Squads.FirstOrDefault(x => x.Id == SquadId); if (sq == null) return;
        sq.Order = Order; sq.OrderX = X; sq.OrderY = Y; sq.TargetSquad = TargetSquad;
        if (Formation != null) sq.Formation = Formation.Value;
    }
}
