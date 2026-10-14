using System.Numerics;
using GPS.Sim;
using Xunit;

public class WorldTests
{
    static BlockRegistry Blocks() => BlockRegistry.FromJson(File.ReadAllText("blocks.json"));

    [Fact]
    public void SetGet_AcrossChunkBordersAndNegatives()
    {
        var w = new VoxelWorld();
        foreach (var p in new BlockPos[] { new(0, 0, 0), new(15, 15, 15), new(16, 0, 0), new(-1, -1, -1), new(-17, 3, 40) })
        {
            w.Set(p, 1);
            Assert.Equal(1, w.Get(p));
        }
        Assert.Equal(0, w.Get(1, 0, 0));
        w.Set(16, 0, 0, 0);
        Assert.Equal(0, w.Get(16, 0, 0));
    }

    [Fact]
    public void Set_OnChunkBorder_MarksNeighbourDirty()
    {
        var w = new VoxelWorld();
        w.Set(16, 5, 5, 1);
        Assert.Contains(new BlockPos(1, 0, 0), w.Dirty);
        Assert.Contains(new BlockPos(0, 0, 0), w.Dirty);
    }

    [Fact]
    public void Raycast_HitsFirstSolid_AndReportsCellBefore()
    {
        var w = new VoxelWorld();
        w.Set(5, 0, 0, 1);
        Assert.True(Raycast.Cast(w, new Vector3(0.5f, 0.5f, 0.5f), new Vector3(1, 0, 0), 20, out var hit, out var before));
        Assert.Equal(new BlockPos(5, 0, 0), hit);
        Assert.Equal(new BlockPos(4, 0, 0), before);
        Assert.False(Raycast.Cast(w, new Vector3(0.5f, 0.5f, 0.5f), new Vector3(0, 1, 0), 20, out _, out _));
    }

    [Fact]
    public void Raycast_DiagonalFromAbove_HitsTerrainTop()
    {
        var w = new VoxelWorld();
        for (int x = 0; x < 8; x++) for (int z = 0; z < 8; z++) w.Set(x, 0, z, 1);
        var o = new Vector3(0.5f, 10, 0.5f);
        var d = Vector3.Normalize(new Vector3(3, -10, 3));
        Assert.True(Raycast.Cast(w, o, d, 50, out var hit, out var before));
        Assert.Equal(0, hit.Y);
        Assert.Equal(1, before.Y);
    }

    [Fact]
    public void Simulation_AppliesCommandsInOrder_PlaceDoesNotOverwrite()
    {
        var sim = new Simulation(Blocks());
        var p = new BlockPos(1, 2, 3);
        sim.Enqueue(new PlaceBlock(p, 1));
        sim.Enqueue(new PlaceBlock(p, 2));
        sim.Step();
        Assert.Equal(1, sim.World.Get(p));
        sim.Enqueue(new RemoveBlock(p));
        sim.Enqueue(new PlaceBlock(p, 2));
        sim.Step();
        Assert.Equal(2, sim.World.Get(p));
        Assert.Equal(2, sim.Tick);
    }

    [Fact]
    public void WorldGen_IsDeterministic()
    {
        var b = Blocks();
        var a = new VoxelWorld(); WorldGen.Terrain(a, b, 32, 32, 7);
        var c = new VoxelWorld(); WorldGen.Terrain(c, b, 32, 32, 7);
        for (int x = 0; x < 32; x++) for (int z = 0; z < 32; z++) for (int y = 0; y < 12; y++)
            Assert.Equal(a.Get(x, y, z), c.Get(x, y, z));
        Assert.Equal(b.IdOf("Grass"), a.Get(3, Top(a, 3, 3), 3));
    }

    static int Top(VoxelWorld w, int x, int z) { int y = 0; while (w.IsSolid(new(x, y + 1, z))) y++; return y; }
}
