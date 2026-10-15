using System.Text.Json.Serialization;

namespace GPS.Sim;

/// <summary>入力は全てコマンドとして投入され、固定ティックで順に適用される(将来のロックステップ同期の前提)。保存の命令列にもなる。</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$c")]
[JsonDerivedType(typeof(PlaceBlock), "placeBlock")]
[JsonDerivedType(typeof(RemoveBlock), "removeBlock")]
[JsonDerivedType(typeof(CreateBuilding), "createBuilding")]
[JsonDerivedType(typeof(PlaceStructure), "placeStructure")]
[JsonDerivedType(typeof(RemoveStructure), "removeStructure")]
[JsonDerivedType(typeof(SetRecipe), "setRecipe")]
[JsonDerivedType(typeof(AssignStaff), "assignStaff")]
[JsonDerivedType(typeof(SetPolicy), "setPolicy")]
[JsonDerivedType(typeof(LinkPorts), "linkPorts")]
[JsonDerivedType(typeof(AddInventory), "addInventory")]
[JsonDerivedType(typeof(SetOre), "setOre")]
[JsonDerivedType(typeof(AddItems), "addItems")]
[JsonDerivedType(typeof(SetRecipeByKind), "setRecipeByKind")]
public interface ICommand { void Apply(Simulation sim); }

public sealed record PlaceBlock(BlockPos Pos, byte Id) : ICommand
{
    public void Apply(Simulation s) { if (!s.World.IsSolid(Pos)) s.World.Set(Pos, Id); }
}

public sealed record RemoveBlock(BlockPos Pos) : ICommand
{
    public void Apply(Simulation s) => s.World.Set(Pos, BlockRegistry.Air);
}

public sealed record CommandEntry(long Tick, ICommand Command);

public sealed class Simulation
{
    public const int TicksPerSecond = Clock.TicksPerSecond;
    public readonly VoxelWorld World = new();
    public readonly BlockRegistry Blocks;
    public readonly GameData Data;
    public Factory Factory { get; private set; } = new();
    public Rng Rng { get; private set; }
    public ulong Seed { get; }
    public long Tick { get; private set; }
    /// <summary>適用した命令の記録(保存形式 (c): 状態の写し + 命令列)。</summary>
    public readonly List<CommandEntry> Log = new();
    readonly List<ICommand> _pending = new();

    public Simulation(BlockRegistry blocks, GameData data, ulong seed = 1)
    {
        Blocks = blocks; Data = data; Seed = seed; Rng = new Rng(seed);
    }

    public void Enqueue(ICommand c) => _pending.Add(c);

    public void Step()
    {
        foreach (var c in _pending) { c.Apply(this); Log.Add(new CommandEntry(Tick, c)); }
        _pending.Clear();
        Factory.Tick(Data, Rng);
        Tick++;
    }

    public void Run(long ticks) { for (long i = 0; i < ticks; i++) Step(); }

    /// <summary>状態の写しを読み込んだあとに呼ぶ。</summary>
    internal void Restore(Factory f, Rng rng, long tick, IEnumerable<CommandEntry> log)
    {
        Factory = f; Rng = rng; Tick = tick; Log.Clear(); Log.AddRange(log);
        Factory.Reindex(Data);
    }
}
