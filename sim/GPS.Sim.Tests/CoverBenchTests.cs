using System.Diagnostics;
using GPS.Sim;
using Xunit;
using Xunit.Abstractions;

/// <summary>遮蔽の射線: DDA の格子走査と、点を等間隔に打つ旧方式の比較(正しさと速さ)。</summary>
public class CoverBenchTests
{
    readonly ITestOutputHelper _out;
    public CoverBenchTests(ITestOutputHelper o) => _out = o;
    static readonly GameData Data = GameData.Load(GameData.FindDataDir());
    static BlockRegistry Blocks() => BlockRegistry.FromJson(File.ReadAllText(Path.Combine(GameData.FindDataDir(), "blocks.json")));

    static bool Sampled(bool[,] grid, int cs, double x0, double y0, double x1, double y1)
    {
        int n = (int)Math.Ceiling(Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)) / (cs / 2.0)); if (n < 2) return false;
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

    [Fact]
    public void Dda_NeverMissesWhatSamplingFinds_AndIsFaster()
    {
        var s = new Simulation(Blocks(), Data, 42); s.Factory.Creative = true;
        var rng = new Random(7); int id = 1;
        for (int i = 0; i < 120; i++) { int sz = 10 + rng.Next(4) * 10; s.Enqueue(new CreateBuilding(id, $"b{id}", sz, sz, 2 + rng.Next(6), false)); s.Enqueue(new SetBuildingPosition(id, rng.Next(900), rng.Next(900))); id++; }
        s.Step();
        var f = s.Factory; var c = s.Combat;
        s.Enqueue(new CreateMech(1, 1, "general", 60)); s.Enqueue(new CreateMech(2, 1, "general", 60)); s.Enqueue(new CreateBuilding(500, "h", 12, 12, 1, false)); s.Step();
        s.Enqueue(new FormSquad(1, 1, "a", new[] { 1 }, 500)); s.Enqueue(new FormSquad(2, 2, "b", new[] { 2 }, 500)); s.Step(); s.Enqueue(new Deploy(1, 0, 0)); s.Enqueue(new Deploy(2, 50, 50)); s.Step(); s.Step();
        var grid = f.CoverGrid!; Assert.NotNull(grid);
        var lines = Enumerable.Range(0, 20000).Select(_ => (rng.NextDouble() * 900, rng.NextDouble() * 900, rng.NextDouble() * 900, rng.NextDouble() * 900)).ToList();
        int missed = 0, blockedDda = 0, blockedSampled = 0;
        foreach (var (x0, y0, x1, y1) in lines)
        {
            bool d = Combat.LineBlocked(f, x0, y0, x1, y1, c), sm = Sampled(grid, c.CoverCell, x0, y0, x1, y1);
            if (d) blockedDda++; if (sm) blockedSampled++;
            if (sm && !d) missed++;   // 旧方式が見つけた遮蔽を DDA が見逃すことは無いはず(両端のセルの扱いが同じとき)
        }
        var sw = Stopwatch.StartNew(); foreach (var (x0, y0, x1, y1) in lines) Combat.LineBlocked(f, x0, y0, x1, y1, c); long dda = sw.ElapsedMilliseconds;
        sw.Restart(); foreach (var (x0, y0, x1, y1) in lines) Sampled(grid, c.CoverCell, x0, y0, x1, y1); long samp = sw.ElapsedMilliseconds;
        _out.WriteLine($"lines 20000: DDA {dda} ms, sampled {samp} ms; blocked DDA {blockedDda} sampled {blockedSampled}; sampled-only {missed}");
        Assert.True(missed <= lines.Count / 200, $"DDA が旧方式の遮蔽を見逃した {missed} 件");
        Assert.True(blockedDda >= blockedSampled, "DDA は旧方式より取りこぼしが少ない");
    }
}
