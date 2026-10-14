namespace GPS.Sim;

public static class WorldGen
{
    /// <summary>値ノイズによる起伏地形。seed が同じなら同じ地形(決定論)。</summary>
    public static void Terrain(VoxelWorld w, BlockRegistry blocks, int sizeX, int sizeZ, int seed)
    {
        byte stone = blocks.IdOf("Stone"), dirt = blocks.IdOf("Dirt"), grass = blocks.IdOf("Grass");
        for (int x = 0; x < sizeX; x++)
            for (int z = 0; z < sizeZ; z++)
            {
                int h = 4 + (int)(Noise(x, z, seed, 8) * 6);
                for (int y = 0; y < h; y++)
                    w.Set(x, y, z, y == h - 1 ? grass : y >= h - 3 ? dirt : stone);
            }
        w.Dirty.Clear(); // 生成直後は全チャンクを一括構築するので差分不要
    }

    static float Hash(int x, int z, int seed)
    {
        uint h = (uint)(x * 374761393 + z * 668265263 + seed * 1442695041);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xFFFF) / 65535f;
    }

    static float Noise(int x, int z, int seed, int cell)
    {
        int cx = x / cell, cz = z / cell;
        float fx = (x % cell) / (float)cell, fz = (z % cell) / (float)cell;
        fx = fx * fx * (3 - 2 * fx); fz = fz * fz * (3 - 2 * fz);
        float a = Hash(cx, cz, seed), b = Hash(cx + 1, cz, seed), c = Hash(cx, cz + 1, seed), d = Hash(cx + 1, cz + 1, seed);
        return (a * (1 - fx) + b * fx) * (1 - fz) + (c * (1 - fx) + d * fx) * fz;
    }
}
