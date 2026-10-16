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

    /// <summary>保存用: チャンクごとに [個数, 種類, 個数, 種類, ...] の連長圧縮(地形は同じブロックが並ぶので小さい)。</summary>
    public List<ChunkData> Export()
    {
        var list = new List<ChunkData>();
        foreach (var (c, ch) in _chunks)
        {
            var rle = new List<int>(); int run = 0; byte cur = ch.Blocks[0];
            foreach (var b in ch.Blocks) { if (b == cur) { run++; continue; } rle.Add(run); rle.Add(cur); cur = b; run = 1; }
            rle.Add(run); rle.Add(cur);
            if (rle.Count == 2 && cur == BlockRegistry.Air) continue;   // 空のチャンクは書かない
            list.Add(new ChunkData { X = c.X, Y = c.Y, Z = c.Z, Rle = rle });
        }
        return list;
    }
    public void Import(List<ChunkData> data)
    {
        _chunks.Clear(); Dirty.Clear(); Version++;
        foreach (var d in data)
        {
            var ch = new Chunk(); int i = 0;
            for (int k = 0; k + 1 < d.Rle.Count; k += 2) { byte id = (byte)d.Rle[k + 1]; for (int n = 0; n < d.Rle[k]; n++) ch.Blocks[i++] = id; }
            var c = new BlockPos(d.X, d.Y, d.Z); _chunks[c] = ch; Dirty.Add(c);
        }
    }
}

public sealed class ChunkData
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public List<int> Rle { get; set; } = new();
}
