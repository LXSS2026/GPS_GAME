using System.Diagnostics;
using GPS.Sim;
using Xunit;
using Xunit.Abstractions;

/// <summary>規模の実測(G2「10 万の軍」の粒度): 交戦中の隊の数とティックの所要。</summary>
public class ScalePerfTests
{
    readonly ITestOutputHelper _out;
    public ScalePerfTests(ITestOutputHelper o) => _out = o;
    static readonly GameData Data = GameData.Load(GameData.FindDataDir());
    static BlockRegistry Blocks() => BlockRegistry.FromJson(File.ReadAllText(Path.Combine(GameData.FindDataDir(), "blocks.json")));

    static Simulation Field(int squadsPerSide, int mechsPerSquad)
    {
        var s = new Simulation(Blocks(), Data, 99); s.Factory.Creative = true;
        s.Enqueue(new CreateBuilding(1, "a", 12, 12, 1, false)); s.Enqueue(new SetBuildingPosition(1, 0, 0)); s.Enqueue(new CreateBuilding(2, "b", 12, 12, 1, false)); s.Enqueue(new SetBuildingPosition(2, 2000, 0)); s.Step();
        int mid = 1, sid = 1;
        for (int side = 0; side < 2; side++)
            for (int q = 0; q < squadsPerSide; q++)
            {
                var ids = new List<int>();
                for (int m = 0; m < mechsPerSquad; m++) { s.Enqueue(new CreateMech(mid, 2, q % 3 == 0 ? "heavy" : "general", 70)); ids.Add(mid++); }   // 隊は同じ系統で揃える
                s.Enqueue(new FormSquad(sid, side + 1, $"s{sid}", ids, side == 0 ? 1 : 2)); sid++;
            }
        s.Step();
        sid = 1;
        for (int side = 0; side < 2; side++)
            for (int q = 0; q < squadsPerSide; q++)
            {   // 2 列に並べ、向かい合う隊を攻撃
                double x = side == 0 ? 100 : 220, y = (q % 50) * 40 + (q / 50) * 10;   // 120 m = 有効射程の内側で、すぐ撃ち合う
                s.Enqueue(new Deploy(sid, x, y)); s.Enqueue(new OrderSquad(sid, SquadOrder.Attack, TargetSquad: side == 0 ? sid + squadsPerSide : sid - squadsPerSide)); sid++;
            }
        s.Step();
        return s;
    }

    [Theory]
    [InlineData(10, 12)]
    [InlineData(100, 12)]
    public void TickCost_GrowsWithEngagedMechs(int squadsPerSide, int mechsPerSquad)
    {
        var s = Field(squadsPerSide, mechsPerSquad);
        s.Run(60);   // 慣らし
        var sw = Stopwatch.StartNew(); s.Run(600); sw.Stop();
        int mechs = squadsPerSide * mechsPerSquad * 2; double perTick = sw.Elapsed.TotalMilliseconds / 600;
        _out.WriteLine($"mechs {mechs} (squads {squadsPerSide * 2}): {perTick:F2} ms/tick, realtime x{1000.0 / 60 / perTick:F1}; events {s.Factory.Events.Count}");
        Assert.True(perTick < 1000.0 / 60 * 4 || mechs > 5000, $"4x でも実時間に追いつかない: {perTick:F2} ms/tick");
    }
}
