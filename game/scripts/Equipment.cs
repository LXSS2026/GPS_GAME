using System.Collections.Generic;
using System.Text.Json;
using Godot;

/// <summary>設備(工場の機械)の配置。部品は tools/blender/make_equipment.py が assets/equipment/*.glb と data/equipment.json に書く。
/// 1 セル角、原点はセルの床の中心、正面は Blender の +Y(GLB の -Z)。rot は正面の向き(度)。</summary>
public sealed class Equipment
{
    public sealed record Def(string Id, int H);
    public readonly Dictionary<string, Def> Defs = new();
    readonly Dictionary<string, PackedScene> _scenes = new();
    readonly FloorModules _fm;

    public Equipment(FloorModules fm)
    {
        _fm = fm;
        using var doc = JsonDocument.Parse(FileAccess.GetFileAsString("res://data/equipment.json"));
        foreach (var e in doc.RootElement.GetProperty("equipment").EnumerateArray())
            Defs[e.GetProperty("id").GetString()!] = new Def(e.GetProperty("id").GetString()!, e.GetProperty("h").GetInt32());
    }

    /// <summary>セル (cx, cz) の床 y に設備を置く。rot は 0 = -Z 向き(手前)、90 = +X、180 = +Z、270 = -X。</summary>
    public Node3D Place(Node3D parent, string id, int cx, float y, int cz, int rot = 0)
    {
        if (!_scenes.TryGetValue(id, out var ps)) _scenes[id] = ps = GD.Load<PackedScene>($"res://assets/equipment/{id}.glb");
        var n = (Node3D)ps.Instantiate();
        n.Name = $"{id}_{cx}_{cz}";
        Swap(n);
        n.Position = new Vector3(cx + 0.5f, y, cz + 0.5f);
        n.RotationDegrees = new Vector3(0, rot, 0);
        parent.AddChild(n);
        return n;
    }

    void Swap(Node node)
    {
        foreach (var c in node.GetChildren()) Swap(c);
        if (node is not MeshInstance3D mi || mi.Mesh == null) return;
        for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
        {
            var m = mi.GetActiveMaterial(i);
            if (m != null && _fm.Materials.TryGetValue(m.ResourceName, out var rep)) mi.SetSurfaceOverrideMaterial(i, rep);
        }
    }

    /// <summary>平地 1 ブロック(5x5)の工場の見本: 採掘機 → ベルト → 炉 → ベルト → 組立機 → 箱。ox, oz はブロックの左手前のセル。</summary>
    public void SampleGround(Node3D parent, int ox, float y, int oz)
    {
        Place(parent, "miner", ox + 0, y, oz + 4, 0);
        Place(parent, "belt", ox + 0, y, oz + 3, 0);
        Place(parent, "belt", ox + 0, y, oz + 2, 0);
        Place(parent, "belt_turn", ox + 0, y, oz + 1, 0);
        Place(parent, "belt", ox + 1, y, oz + 1, 90);
        Place(parent, "inserter", ox + 2, y, oz + 1, 90);
        Place(parent, "furnace", ox + 3, y, oz + 1, 180);
        Place(parent, "inserter", ox + 3, y, oz + 2, 180);
        Place(parent, "belt", ox + 3, y, oz + 3, 180);
        Place(parent, "inserter", ox + 3, y, oz + 4, 180);
        Place(parent, "assembler", ox + 4, y, oz + 4, 270);
        Place(parent, "chest", ox + 4, y, oz + 2, 0);
        Place(parent, "inserter", ox + 4, y, oz + 3, 0);
        Place(parent, "tank", ox + 2, y, oz + 4, 0);
        Place(parent, "pole", ox + 1, y, oz + 3, 0);
    }

    /// <summary>建物の 1 階(W×D)の工場の見本: 外周に沿ったベルトと中央の機械列。</summary>
    public void SampleFloor(Node3D parent, int ox, float y, int oz, int w, int d)
    {
        for (int i = 0; i < w - 1; i++) Place(parent, i == w - 2 ? "belt_turn" : "belt", ox + i, y, oz, 90);
        for (int j = 1; j < d - 1; j++) Place(parent, "belt", ox + w - 1, y, oz + j, 180);
        Place(parent, "chest", ox + w - 1, y, oz + d - 1, 0);
        Place(parent, "inserter", ox + 0, y, oz + 1, 180);
        Place(parent, "furnace", ox + 0, y, oz + 2, 180);
        Place(parent, "inserter", ox + 1, y, oz + 2, 90);
        Place(parent, "assembler", ox + 2, y, oz + 2, 180);
        Place(parent, "inserter", ox + 3, y, oz + 2, 90);
        if (w > 4) Place(parent, "inserter", ox + w - 2, y, oz + 1, 90);
        Place(parent, "tank", ox + 1, y, oz + d - 1, 0);
        Place(parent, "chest", ox + 2, y, oz + d - 1, 0);
        Place(parent, "pole", ox + 0, y, oz + d - 1, 0);
    }
}
