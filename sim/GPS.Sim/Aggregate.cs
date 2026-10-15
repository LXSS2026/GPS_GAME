using System.Text.Json.Serialization;

namespace GPS.Sim;

/// <summary>見ていない建物の集計近似(2026-10-15 確定: 精度は高く)。
/// 全計算で ProfileTicks だけ動かして速度の記録を取り、その後は Interval ごとに記録の速度を積算する。
/// 箱の満杯・鉱脈の枯渇に当たったとき、および ReprofileEvery ごとに記録を取り直す(その間は全計算)。</summary>
public sealed class AggregateState
{
    public const int ProfileTicks = 1_800;        // 記録の窓(30 秒)
    public const int Interval = 60;               // 積算の間隔(1 秒)
    public const long ReprofileEvery = Clock.TicksPerHour;

    public bool Profiling { get; set; } = true;
    public int ProfileLeft { get; set; } = ProfileTicks;
    public long SinceProfile { get; set; }
    public int IntervalLeft { get; set; } = Interval;
    public Dictionary<int, MachineRate> Machines { get; set; } = new();
    public Dictionary<int, long> OreRate { get; set; } = new();          // 採掘機 id → 窓内に掘った数
    public Dictionary<int, List<ChestDelta>> Chests { get; set; } = new(); // 箱 id → 窓内の増減(品目・階級ごと)
    public Dictionary<int, int> TankRate { get; set; } = new();          // タンク id → 窓内の増減
    // 記録の開始時の値
    public Dictionary<int, MachineRate> StartMachines { get; set; } = new();
    public Dictionary<int, long> StartOre { get; set; } = new();
    public Dictionary<int, Dictionary<string, int>> StartChests { get; set; } = new();
    public Dictionary<int, int> StartTanks { get; set; } = new();
    // 積算の端数(1/10,000 単位)
    public Dictionary<int, long> Frac { get; set; } = new();
    public int Supply { get; set; }
    public int Demand { get; set; }
}

public sealed class MachineRate { public long Made { get; set; } public long Failed { get; set; } }
public sealed class ChestDelta { public string Item { get; set; } = ""; public QualityTier Tier { get; set; } public int Count { get; set; } public int MeanQuality { get; set; } }   // MeanQuality: 窓内に増えた品の平均品質(精度のため)

/// <summary>建物を見る / 見ないの切り替え。見ない建物は集計近似に入る。</summary>
public sealed record SetViewed(int Building, bool Viewed) : ICommand
{
    public void Apply(Simulation s)
    {
        var b = s.Factory.Get(Building); if (b == null) return;
        if (Viewed) b.Aggregate = null;
        else b.Aggregate ??= new AggregateState();
    }
}

public static class Aggregation
{
    static string Key(ItemStack it) => $"{it.Item}|{(int)it.Tier}";

    /// <summary>全計算の前に呼ぶ。true なら全計算が必要(記録中)。false なら積算だけで済ませた。</summary>
    public static bool BeforeTick(Building b, Factory f, GameData data, Rng rng)
    {
        var a = b.Aggregate!;
        if (a.Profiling)
        {
            if (a.ProfileLeft == AggregateState.ProfileTicks) Begin(b, a);
            return true;
        }
        if (--a.IntervalLeft > 0) return false;
        a.IntervalLeft = AggregateState.Interval;
        Advance(b, a, AggregateState.Interval, rng, data);
        a.SinceProfile += AggregateState.Interval;
        if (a.SinceProfile >= AggregateState.ReprofileEvery) Reprofile(a);
        return false;
    }

    /// <summary>全計算の後に呼ぶ(記録中のみ)。窓が終わったら速度を確定する。</summary>
    public static void AfterTick(Building b)
    {
        var a = b.Aggregate!;
        if (!a.Profiling) return;
        if (--a.ProfileLeft > 0) return;
        End(b, a);
    }

    static void Reprofile(AggregateState a) { a.Profiling = true; a.ProfileLeft = AggregateState.ProfileTicks; a.SinceProfile = 0; }

    static void Begin(Building b, AggregateState a)
    {
        a.StartMachines.Clear(); a.StartOre.Clear(); a.StartChests.Clear(); a.StartTanks.Clear();
        foreach (var fl in b.Floors)
            foreach (var s in fl.Structures)
                switch (s)
                {
                    case Machine m: a.StartMachines[m.Id] = new MachineRate { Made = m.Made, Failed = m.Failed }; break;
                    case Miner mi: a.StartOre[mi.Id] = fl.OreAt(mi.X, mi.Y)?.Amount ?? 0; break;
                    case Chest c: a.StartChests[c.Id] = c.Items.GroupBy(Key).ToDictionary(g => g.Key, g => g.Sum(i => i.Count)); break;
                    case Tank t: a.StartTanks[t.Id] = t.Amount; break;
                }
    }

    static void End(Building b, AggregateState a)
    {
        a.Machines.Clear(); a.OreRate.Clear(); a.Chests.Clear(); a.TankRate.Clear();
        foreach (var fl in b.Floors)
            foreach (var s in fl.Structures)
                switch (s)
                {
                    case Machine m when a.StartMachines.TryGetValue(m.Id, out var st):
                        a.Machines[m.Id] = new MachineRate { Made = m.Made - st.Made, Failed = m.Failed - st.Failed }; break;
                    case Miner mi when a.StartOre.TryGetValue(mi.Id, out var so):
                        a.OreRate[mi.Id] = so - (fl.OreAt(mi.X, mi.Y)?.Amount ?? 0); break;
                    case Chest c when a.StartChests.TryGetValue(c.Id, out var sc):
                    {
                        var now = c.Items.GroupBy(Key).ToDictionary(g => g.Key, g => g.Sum(i => i.Count));
                        var deltas = new List<ChestDelta>();
                        foreach (var k in now.Keys.Union(sc.Keys).OrderBy(k => k, StringComparer.Ordinal))
                        {
                            int d = now.GetValueOrDefault(k) - sc.GetValueOrDefault(k);
                            if (d == 0) continue;
                            var p = k.Split('|'); var tier = (QualityTier)int.Parse(p[1]);
                            var same = c.Items.Where(it => it.Item == p[0] && it.Tier == tier).ToList();
                            int mean = same.Count == 0 ? Quality.NormalMin : (int)Math.Round(same.Average(it => it.Quality));
                            deltas.Add(new ChestDelta { Item = p[0], Tier = tier, Count = d, MeanQuality = mean });
                        }
                        if (deltas.Count > 0) a.Chests[c.Id] = deltas;
                        break;
                    }
                    case Tank t when a.StartTanks.TryGetValue(t.Id, out var stt): a.TankRate[t.Id] = t.Amount - stt; break;
                }
        a.Supply = b.LastSupply; a.Demand = b.LastDemand;
        a.Profiling = false; a.ProfileLeft = AggregateState.ProfileTicks; a.IntervalLeft = AggregateState.Interval; a.SinceProfile = 0;
    }

    /// <summary>記録の速度 × ticks を状態に加える。端数は 1/10,000 で持ち越す。境界に当たったら記録を取り直す。</summary>
    static void Advance(Building b, AggregateState a, int ticks, Rng rng, GameData data)   // rng は将来の確率的な近似用に受ける
    {
        long Take(int key, long perWindow)   // 窓あたりの量 → この区間の整数量(端数を持ち越し)
        {
            long acc = a.Frac.GetValueOrDefault(key) + perWindow * ticks * 10_000L / AggregateState.ProfileTicks;
            long whole = acc / 10_000; a.Frac[key] = acc - whole * 10_000; return whole;
        }
        bool boundary = false;
        foreach (var fl in b.Floors)
            foreach (var s in fl.Structures)
                switch (s)
                {
                    case Machine m when a.Machines.TryGetValue(m.Id, out var r):
                        m.Made += Take(m.Id * 4 + 0, r.Made); m.Failed += Take(m.Id * 4 + 1, r.Failed); break;
                    case Miner mi when a.OreRate.TryGetValue(mi.Id, out var or):
                    {
                        var ore = fl.OreAt(mi.X, mi.Y); if (ore == null) break;
                        long n = Take(mi.Id * 4 + 2, or);
                        ore.Amount -= n;
                        if (ore.Amount <= 0) { fl.Ore![mi.X + fl.W * mi.Y] = null; boundary = true; }
                        break;
                    }
                    case Chest c when a.Chests.TryGetValue(c.Id, out var deltas):
                    {
                        int cap = data.Structures[c.DefId].Capacity;
                        for (int k = 0; k < deltas.Count; k++)
                        {
                            var d = deltas[k];
                            long n = Take(c.Id * 64 + 4 + k, d.Count);
                            if (n > 0)
                            {
                                for (long i = 0; i < n; i++)
                                {
                                    if (c.Items.Count >= cap) { boundary = true; break; }
                                    c.Items.Add(new ItemStack(d.Item, d.MeanQuality));   // 窓内の平均品質(階級内)で置く
                                }
                            }
                            else if (n < 0)
                            {
                                for (long i = 0; i < -n; i++)
                                {
                                    int idx = c.Items.FindLastIndex(it => it.Item == d.Item && it.Tier == d.Tier);
                                    if (idx < 0) { boundary = true; break; }
                                    c.Items.RemoveAt(idx);
                                }
                            }
                        }
                        break;
                    }
                    case Tank t when a.TankRate.TryGetValue(t.Id, out var tr):
                    {
                        int cap = data.Structures[t.DefId].Capacity;
                        long n = Take(t.Id * 4 + 3, tr);
                        t.Amount = (int)Math.Clamp(t.Amount + n, 0, cap);
                        if (t.Amount == 0 || t.Amount == cap) boundary = true;
                        break;
                    }
                }
        b.SetPowerSummary(a.Supply, a.Demand);
        if (boundary) Reprofile(a);
    }
}
