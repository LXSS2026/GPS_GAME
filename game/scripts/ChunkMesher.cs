using Godot;
using GPS.Sim;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>ブロックテクスチャのアトラス。atlas.json の名前 → 番地と、UV の矩形。</summary>
public sealed class Atlas
{
    public readonly ImageTexture Texture;
    readonly Dictionary<string, int> _index;
    readonly int _cols, _rows;

    public Atlas(string jsonPath, string pngPath)
    {
        var doc = JsonDocument.Parse(FileAccess.GetFileAsString(jsonPath));
        _cols = doc.RootElement.GetProperty("cols").GetInt32();
        _index = new();
        foreach (var p in doc.RootElement.GetProperty("index").EnumerateObject()) _index[p.Name] = p.Value.GetInt32();
        var img = Image.LoadFromFile(pngPath);
        _rows = img.GetHeight() / (img.GetWidth() / _cols);
        img.GenerateMipmaps();
        Texture = ImageTexture.CreateFromImage(img);
    }

    /// <summary>名前のタイルの UV 原点と大きさ。未知の名前は 0 番。</summary>
    public (Vector2 origin, Vector2 size) Uv(string? name)
    {
        int i = name != null && _index.TryGetValue(name, out var v) ? v : 0;
        return (new Vector2((i % _cols) / (float)_cols, (i / _cols) / (float)_rows), new Vector2(1f / _cols, 1f / _rows));
    }
}

/// <summary>隣が空気の面だけを出す素朴なメッシュ化 + 頂点 AO。ponytail: greedy meshing は頂点数が問題になってから。</summary>
public static class ChunkMesher
{
    // 各面: 隣接オフセット(法線)、外から見て反時計回りの 4 頂点
    static readonly (Vector3I d, Vector3I[] v)[] Faces =
    {
        (new(1, 0, 0),  new Vector3I[] { new(1, 0, 0), new(1, 1, 0), new(1, 1, 1), new(1, 0, 1) }),
        (new(-1, 0, 0), new Vector3I[] { new(0, 0, 1), new(0, 1, 1), new(0, 1, 0), new(0, 0, 0) }),
        (new(0, 1, 0),  new Vector3I[] { new(0, 1, 0), new(0, 1, 1), new(1, 1, 1), new(1, 1, 0) }),
        (new(0, -1, 0), new Vector3I[] { new(0, 0, 0), new(1, 0, 0), new(1, 0, 1), new(0, 0, 1) }),
        (new(0, 0, 1),  new Vector3I[] { new(0, 0, 1), new(1, 0, 1), new(1, 1, 1), new(0, 1, 1) }),
        (new(0, 0, -1), new Vector3I[] { new(1, 0, 0), new(0, 0, 0), new(0, 1, 0), new(1, 1, 0) }),
    };
    // 面内の UV: 頂点 i の (u, v)。上面は xz、側面は横軸と高さ
    static readonly Vector2[] FaceUv = { new(0, 1), new(0, 0), new(1, 0), new(1, 1) };

    public static ArrayMesh? Build(VoxelWorld w, BlockRegistry blocks, Atlas atlas, BlockPos c)
    {
        var chunk = w.GetChunk(c);
        if (chunk == null) return null;
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var colors = new List<Color>();
        var uvs = new List<Vector2>();
        var indices = new List<int>();
        int ox = c.X * Chunk.Size, oy = c.Y * Chunk.Size, oz = c.Z * Chunk.Size;

        for (int ly = 0; ly < Chunk.Size; ly++)
            for (int lz = 0; lz < Chunk.Size; lz++)
                for (int lx = 0; lx < Chunk.Size; lx++)
                {
                    byte id = chunk.Blocks[Chunk.Index(lx, ly, lz)];
                    if (id == BlockRegistry.Air) continue;
                    var def = blocks[id];
                    var p = new Vector3I(ox + lx, oy + ly, oz + lz);
                    foreach (var (d, v) in Faces)
                    {
                        if (w.Get(p.X + d.X, p.Y + d.Y, p.Z + d.Z) != BlockRegistry.Air) continue;
                        string? tex = d.Y > 0 ? def.Top : d.Y < 0 ? def.Bottom : def.Side;
                        var (uo, us) = atlas.Uv(tex);
                        int b = verts.Count;
                        for (int i = 0; i < 4; i++)
                        {
                            verts.Add(new Vector3(lx, ly, lz) + (Vector3)v[i]);
                            normals.Add((Vector3)d);
                            float ao = VertexAo(w, p, d, v[i]);
                            colors.Add(new Color(ao, ao, ao, 1));
                            uvs.Add(uo + FaceUv[i] * us);
                        }
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
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>頂点 AO: 面の外側の層で、頂点に接する 2 辺と角のブロックの塞がり具合(0〜3)を明るさにする。</summary>
    static float VertexAo(VoxelWorld w, Vector3I p, Vector3I d, Vector3I corner)
    {
        // 法線以外の 2 軸について、頂点座標 0 → -1 側、1 → +1 側
        Vector3I t1 = Vector3I.Zero, t2 = Vector3I.Zero;
        if (d.X != 0) { t1 = new(0, corner.Y == 1 ? 1 : -1, 0); t2 = new(0, 0, corner.Z == 1 ? 1 : -1); }
        else if (d.Y != 0) { t1 = new(corner.X == 1 ? 1 : -1, 0, 0); t2 = new(0, 0, corner.Z == 1 ? 1 : -1); }
        else { t1 = new(corner.X == 1 ? 1 : -1, 0, 0); t2 = new(0, corner.Y == 1 ? 1 : -1, 0); }
        var o = p + d;
        bool s1 = w.IsSolid(new BlockPos(o.X + t1.X, o.Y + t1.Y, o.Z + t1.Z));
        bool s2 = w.IsSolid(new BlockPos(o.X + t2.X, o.Y + t2.Y, o.Z + t2.Z));
        bool cn = w.IsSolid(new BlockPos(o.X + t1.X + t2.X, o.Y + t1.Y + t2.Y, o.Z + t1.Z + t2.Z));
        int level = (s1 && s2) ? 0 : 3 - ((s1 ? 1 : 0) + (s2 ? 1 : 0) + (cn ? 1 : 0));
        return 0.55f + 0.15f * level;
    }
}
