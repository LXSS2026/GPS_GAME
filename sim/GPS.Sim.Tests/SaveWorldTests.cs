using GPS.Sim;
using Xunit;

/// <summary>写しに地形(チャンクの連長圧縮)が含まれ、街路を敷いた地形ごと復元できること(G22 保存)。</summary>
public class SaveWorldTests
{
    static readonly GameData Data = GameData.Load(GameData.FindDataDir());
    static BlockRegistry Blocks() => BlockRegistry.FromJson(File.ReadAllText(Path.Combine(GameData.FindDataDir(), "blocks.json")));

    [Fact]
    public void World_RoundTrips_AndContinuesIdentically()
    {
        var s = new Simulation(Blocks(), Data, 1); SampleCity.Build(s);
        s.Enqueue(new BuildRoad(10, 150, 10, 190)); s.Step(); s.Run(600);   // 街路を敷いて地形を変えてから写す
        var json = Save.Snapshot(s); var loaded = Save.Load(json, Blocks(), Data);
        foreach (var c in s.World.ChunkCoords) Assert.Equal(s.World.GetChunk(c)!.Blocks, loaded.World.GetChunk(c)?.Blocks ?? new byte[0]);
        Assert.Equal(Construction.GroundY(s, 10, 160), Construction.GroundY(loaded, 10, 160));
        s.Run(600); loaded.Run(600);
        Assert.Equal(Save.StateHash(s), Save.StateHash(loaded));
    }
}
