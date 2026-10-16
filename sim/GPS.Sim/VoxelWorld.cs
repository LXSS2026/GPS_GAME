namespace GPS.Sim;

public sealed class Chunk
{
    public const int Size = 16;
    public const int Shift = 4;
    public const int Mask = Size - 1;
    public readonly byte[] Blocks = new byte[Size * Size * Size];
    public static int Index(int lx, int ly, int lz) => lx + Size * (lz + Size * ly);
}

/// <summary>チャンク分割されたブロック格納。座標は負も可(算術シフトで床割り)。</summary>
public sealed class VoxelWorld
{
    readonly Dictionary<BlockPos, Chunk> _chunks = new();
    public readonly HashSet<BlockPos> Dirty = new();

    public static BlockPos ChunkOf(BlockPos p) => new(p.X >> Chunk.Shift, p.Y >> Chunk.Shift, p.Z >> Chunk.Shift);
    public IEnumerable<BlockPos> ChunkCoords => _chunks.Keys;
    public Chunk? GetChunk(BlockPos c) => _chunks.GetValueOrDefault(c);

    public byte Get(int x, int y, int z)
    {
        var c = _chunks.GetValueOrDefault(ChunkOf(new(x, y, z)));
        return c == null ? BlockRegistry.Air : c.Blocks[Chunk.Index(x & Chunk.Mask, y & Chunk.Mask, z & Chunk.Mask)];
    }
    public byte Get(BlockPos p) => Get(p.X, p.Y, p.Z);
    public bool IsSolid(BlockPos p) => Get(p) != BlockRegistry.Air;

    /// <summary>ブロックを置くたびに進む版。高さの格子など、地形から作る物の作り直しの合図。</summary>
    public long Version { get; private set; }

    public void Set(int x, int y, int z, byte id)
    {
        Version++;
        var cc = ChunkOf(new(x, y, z));
        if (!_chunks.TryGetValue(cc, out var c))
        {
            if (id == BlockRegistry.Air) return;
            _chunks[cc] = c = new Chunk();
        }
        int lx = x & Chunk.Mask, ly = y & Chunk.Mask, lz = z & Chunk.Mask;
        int i = Chunk.Index(lx, ly, lz);
        if (c.Blocks[i] == id) return;
        c.Blocks[i] = id;
        Dirty.Add(cc);
        // 面の表示はチャンク境界の隣接ブロックに依存するので、境界なら隣も再構築対象にする
        if (lx == 0) Dirty.Add(cc with { X = cc.X - 1 }); else if (lx == Chunk.Mask) Dirty.Add(cc with { X = cc.X + 1 });
        if (ly == 0) Dirty.Add(cc with { Y = cc.Y - 1 }); else if (ly == Chunk.Mask) Dirty.Add(cc with { Y = cc.Y + 1 });
        if (lz == 0) Dirty.Add(cc with { Z = cc.Z - 1 }); else if (lz == Chunk.Mask) Dirty.Add(cc with { Z = cc.Z + 1 });
    }
    public void Set(BlockPos p, byte id) => Set(p.X, p.Y, p.Z, id);
}
