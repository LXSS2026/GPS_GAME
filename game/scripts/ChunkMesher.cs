using Godot;
using GPS.Sim;
using System.Collections.Generic;

/// <summary>隣が空気の面だけを出す素朴なメッシュ化。ponytail: greedy meshing は頂点数が問題になってから。</summary>
public static class ChunkMesher
{
    // 各面: 隣接オフセット、法線、外から見て反時計回りの 4 頂点、陰影係数
    static readonly (Vector3I d, Vector3 n, Vector3[] v, float shade)[] Faces =
    {
        (new(1, 0, 0),  Vector3.Right,   new Vector3[] { new(1, 0, 0), new(1, 1, 0), new(1, 1, 1), new(1, 0, 1) }, 0.8f),
        (new(-1, 0, 0), Vector3.Left,    new Vector3[] { new(0, 0, 1), new(0, 1, 1), new(0, 1, 0), new(0, 0, 0) }, 0.8f),
        (new(0, 1, 0),  Vector3.Up,      new Vector3[] { new(0, 1, 0), new(0, 1, 1), new(1, 1, 1), new(1, 1, 0) }, 1.0f),
        (new(0, -1, 0), Vector3.Down,    new Vector3[] { new(0, 0, 0), new(1, 0, 0), new(1, 0, 1), new(0, 0, 1) }, 0.5f),
        (new(0, 0, 1),  Vector3.Back,    new Vector3[] { new(0, 0, 1), new(1, 0, 1), new(1, 1, 1), new(0, 1, 1) }, 0.7f),
        (new(0, 0, -1), Vector3.Forward, new Vector3[] { new(1, 0, 0), new(0, 0, 0), new(0, 1, 0), new(1, 1, 0) }, 0.7f),
    };

    public static ArrayMesh? Build(VoxelWorld w, Color[] palette, BlockPos c)
    {
        var chunk = w.GetChunk(c);
        if (chunk == null) return null;
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var colors = new List<Color>();
        var indices = new List<int>();
        int ox = c.X * Chunk.Size, oy = c.Y * Chunk.Size, oz = c.Z * Chunk.Size;

        for (int ly = 0; ly < Chunk.Size; ly++)
            for (int lz = 0; lz < Chunk.Size; lz++)
                for (int lx = 0; lx < Chunk.Size; lx++)
                {
                    byte id = chunk.Blocks[Chunk.Index(lx, ly, lz)];
                    if (id == BlockRegistry.Air) continue;
                    var local = new Vector3(lx, ly, lz);
                    foreach (var (d, n, v, shade) in Faces)
                    {
                        if (w.Get(ox + lx + d.X, oy + ly + d.Y, oz + lz + d.Z) != BlockRegistry.Air) continue;
                        int b = verts.Count;
                        var col = palette[id] * shade;
                        col.A = 1;
                        for (int i = 0; i < 4; i++) { verts.Add(local + v[i]); normals.Add(n); colors.Add(col); }
                        // Godot の表面は時計回りなので反時計回り定義を反転して積む
                        indices.Add(b); indices.Add(b + 2); indices.Add(b + 1);
                        indices.Add(b); indices.Add(b + 3); indices.Add(b + 2);
                    }
                }
        if (verts.Count == 0) return null;

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = colors.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
