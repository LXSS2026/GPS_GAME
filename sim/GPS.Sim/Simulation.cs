namespace GPS.Sim;

/// <summary>入力は全てコマンドとして投入され、固定ティックで順に適用される(将来のロックステップ同期の前提)。</summary>
public interface ICommand { void Apply(VoxelWorld world); }

public sealed record PlaceBlock(BlockPos Pos, byte Id) : ICommand
{
    public void Apply(VoxelWorld w) { if (!w.IsSolid(Pos)) w.Set(Pos, Id); }
}

public sealed record RemoveBlock(BlockPos Pos) : ICommand
{
    public void Apply(VoxelWorld w) => w.Set(Pos, BlockRegistry.Air);
}

public sealed class Simulation
{
    public const int TicksPerSecond = 60;
    public readonly VoxelWorld World = new();
    public readonly BlockRegistry Blocks;
    public long Tick { get; private set; }
    readonly List<ICommand> _pending = new();

    public Simulation(BlockRegistry blocks) => Blocks = blocks;

    public void Enqueue(ICommand c) => _pending.Add(c);

    public void Step()
    {
        foreach (var c in _pending) c.Apply(World);
        _pending.Clear();
        Tick++;
    }
}
