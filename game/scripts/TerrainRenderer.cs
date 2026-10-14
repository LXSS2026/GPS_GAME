using Godot;
using GPS.Sim;
using System.Collections.Generic;
using System.Linq;

/// <summary>VoxelWorld をチャンク単位のメッシュとして描く。Main(ゲーム)と Preview(描画試作)で共用。</summary>
public partial class TerrainRenderer : Node3D
{
    readonly Dictionary<BlockPos, MeshInstance3D> _nodes = new();
    VoxelWorld _world = null!;
    BlockRegistry _blocks = null!;
    Atlas _atlas = null!;
    StandardMaterial3D _mat = null!;

    public void Init(VoxelWorld world, BlockRegistry blocks)
    {
        _world = world;
        _blocks = blocks;
        _atlas = new Atlas("res://assets/textures/atlas.json", ProjectSettings.GlobalizePath("res://assets/textures/atlas.png"));
        _mat = new StandardMaterial3D
        {
            AlbedoTexture = _atlas.Texture,
            VertexColorUseAsAlbedo = true,   // 頂点 AO を乗せる
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            Roughness = 1.0f,
        };
    }

    public void BuildAll()
    {
        foreach (var c in _world.ChunkCoords.ToList()) Rebuild(c);
        _world.Dirty.Clear();
    }

    public void BuildDirty()
    {
        if (_world.Dirty.Count == 0) return;
        foreach (var c in _world.Dirty) Rebuild(c);
        _world.Dirty.Clear();
    }

    void Rebuild(BlockPos c)
    {
        var mesh = ChunkMesher.Build(_world, _blocks, _atlas, c);
        if (_nodes.TryGetValue(c, out var mi))
        {
            if (mesh == null) { mi.QueueFree(); _nodes.Remove(c); }
            else mi.Mesh = mesh;
            return;
        }
        if (mesh == null) return;
        mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = _mat, Position = new Vector3(c.X, c.Y, c.Z) * Chunk.Size };
        AddChild(mi);
        _nodes[c] = mi;
    }
}
