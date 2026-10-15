using System.Text.Json;
using System.Text.Json.Serialization;

namespace GPS.Sim;

/// <summary>保存形式 (c)(2026-10-15 確定): 状態の写し + その後の命令列。読み込みは写しを復元し、命令列は記録として引き継ぐ。</summary>
public static class Save
{
    static readonly JsonSerializerOptions Opt = new() { WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public sealed class File
    {
        public int Version { get; set; } = 1;
        public ulong Seed { get; set; }
        public long Tick { get; set; }
        public ulong RngState { get; set; }
        public Factory Factory { get; set; } = new();
        public List<CommandEntry> Log { get; set; } = new();
    }

    public static string Snapshot(Simulation sim) => JsonSerializer.Serialize(new File { Seed = sim.Seed, Tick = sim.Tick, RngState = sim.Rng.State, Factory = sim.Factory, Log = sim.Log }, Opt);

    public static Simulation Load(string json, BlockRegistry blocks, GameData data)
    {
        var f = JsonSerializer.Deserialize<File>(json, Opt) ?? throw new InvalidDataException("empty save");
        var sim = new Simulation(blocks, data, f.Seed);
        sim.Restore(f.Factory, new Rng(1) { State = f.RngState }, f.Tick, f.Log);
        return sim;
    }

    /// <summary>命令列だけから再計算して復元する(リプレイ)。写しと一致するのが決定論の条件。</summary>
    public static Simulation Replay(ulong seed, IEnumerable<CommandEntry> log, long untilTick, BlockRegistry blocks, GameData data, bool creative = false)
    {
        var sim = new Simulation(blocks, data, seed);
        sim.Factory.Creative = creative;   // 在庫なしで置ける設定は命令ではなく開始時の状態なので、写しと同じ値を渡す
        var queue = new Queue<CommandEntry>(log.OrderBy(e => e.Tick));
        while (sim.Tick < untilTick)
        {
            while (queue.Count > 0 && queue.Peek().Tick == sim.Tick) sim.Enqueue(queue.Dequeue().Command);
            sim.Step();
        }
        return sim;
    }

    /// <summary>状態だけの比較用(命令列を除く)。</summary>
    public static string StateHash(Simulation sim)
    {
        var s = JsonSerializer.Serialize(new { sim.Tick, Rng = sim.Rng.State, sim.Factory }, Opt);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s)));
    }
}
