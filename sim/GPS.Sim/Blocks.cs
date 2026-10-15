using System.Text.Json;

namespace GPS.Sim;

public readonly record struct BlockPos(int X, int Y, int Z);

/// <summary>Top/Side/Bottom は描画用のテクスチャ名(atlas.json の名前)。計算側は参照しない。</summary>
public sealed record BlockDef(byte Id, string Name, string Color, string? Top = null, string? Side = null, string? Bottom = null);

/// <summary>ブロック定義。id 0 は常に空気で、データファイルには書かない。</summary>
public sealed class BlockRegistry
{
    public const byte Air = 0;
    readonly BlockDef?[] _defs;

    BlockRegistry(BlockDef?[] defs) => _defs = defs;

    public static BlockRegistry FromJson(string json)
    {
        var list = JsonSerializer.Deserialize<List<BlockDef>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? throw new InvalidDataException("blocks.json is empty");
        var defs = new BlockDef?[256];
        defs[Air] = new BlockDef(Air, "Air", "#000000");
        foreach (var d in list)
        {
            if (d.Id == Air) throw new InvalidDataException("block id 0 is reserved for air");
            if (defs[d.Id] != null) throw new InvalidDataException($"duplicate block id {d.Id}");
            defs[d.Id] = d;
        }
        return new BlockRegistry(defs);
    }

    public BlockDef this[byte id] => _defs[id] ?? throw new KeyNotFoundException($"unknown block id {id}");
    public IEnumerable<BlockDef> Solid => _defs.Where(d => d != null && d.Id != Air)!;
    public byte IdOf(string name) => Solid.First(d => d.Name == name).Id;
    public bool Has(string name) => Solid.Any(d => d.Name == name);
}
