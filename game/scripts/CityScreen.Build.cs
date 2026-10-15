using Godot;
using GPS.Sim;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>都市の画面の建設: 用途・床面積・階数を選んで地面をクリックで建てる、街路を 2 点で敷く、撤去。計算は GPS.Sim の Construction。</summary>
public partial class CityScreen
{
    enum BuildMode { None, Place, Road, RemoveRoad, Demolish, ShipMove, ShipPlace }
    int _shipMoving = -1; string _shipKind = "small";
    BuildMode _buildMode;
    string _buildUse = "residence";
    int _buildSize = 10, _buildFloors = 1;
    Vector3? _roadStart;
    MeshInstance3D? _ghost;
    Label _buildInfo = null!;
    OptionButton _sizeSel = null!, _floorSel = null!;
    readonly Dictionary<int, Node3D> _buildingNodes = new();
    TerrainRenderer _terrain = null!;

    void BuildPalette(Control root)
    {
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 6);
        box.AddChild(Lbl("建設", 14, Accent));
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
        var use = new OptionButton(); int i = 0; foreach (var (k, u) in _sim.CityData.Uses) { use.AddItem(u.Name, i); use.SetItemMetadata(i, k); if (k == _buildUse) use.Selected = i; i++; }
        use.ItemSelected += idx => { _buildUse = (string)use.GetItemMetadata((int)idx); RefreshBuildInfo(); }; row.AddChild(Lbl("用途")); row.AddChild(use);
        _sizeSel = new OptionButton(); _floorSel = new OptionButton(); RefreshBuildChoices();
        _sizeSel.ItemSelected += idx => { _buildSize = (int)_sizeSel.GetItemId((int)idx); RefreshBuildInfo(); }; _floorSel.ItemSelected += idx => { _buildFloors = (int)_floorSel.GetItemId((int)idx); RefreshBuildInfo(); };
        row.AddChild(Lbl("床面積")); row.AddChild(_sizeSel); row.AddChild(Lbl("階数")); row.AddChild(_floorSel); box.AddChild(row);
        var ops = new HBoxContainer(); ops.AddThemeConstantOverride("separation", 8);
        foreach (var (t, m) in new[] { ("建てる", BuildMode.Place), ("街路を敷く", BuildMode.Road), ("街路を外す", BuildMode.RemoveRoad), ("撤去", BuildMode.Demolish), ("戦艦を建造(小)", BuildMode.ShipPlace) })
        { var mm = m; var b = Btn(t, () => SetBuildMode(_buildMode == mm ? BuildMode.None : mm)); b.CustomMinimumSize = new Vector2(0, 28); ops.AddChild(b); }
        box.AddChild(ops);
        _buildInfo = Lbl("", 12, Line40); box.AddChild(_buildInfo); RefreshBuildInfo();
        var panel = Panel(box); panel.AnchorLeft = 1; panel.AnchorRight = 1; panel.AnchorTop = 1; panel.AnchorBottom = 1; panel.OffsetLeft = -760; panel.OffsetRight = -16; panel.OffsetTop = -150; panel.OffsetBottom = -16; root.AddChild(panel);   // 右下(左の都市の板と重ねない)
    }

    void RefreshBuildChoices()
    {
        _sizeSel.Clear(); for (int sz = 10; sz <= Construction.MaxSize(_sim); sz += 10) _sizeSel.AddItem($"{sz} m 角", sz);
        _buildSize = Math.Min(_buildSize, Construction.MaxSize(_sim)); _sizeSel.Selected = _buildSize / 10 - 1;
        _floorSel.Clear(); for (int fl = 1; fl <= Construction.MaxFloors(_sim); fl++) _floorSel.AddItem($"{fl} 階", fl);
        _buildFloors = Math.Min(_buildFloors, Construction.MaxFloors(_sim)); _floorSel.Selected = _buildFloors - 1;
    }
    void RefreshBuildInfo()
    {
        long cost = Construction.Cost(_sim, _buildUse, _buildSize, _buildFloors);
        string mode = _buildMode switch { BuildMode.ShipPlace => $"戦艦を置く位置をクリック(費用 {18 * 102 * 2 * 8:N0}、国家の段階から)", BuildMode.ShipMove => "戦艦の移動先をクリック", BuildMode.Place => "地面をクリックで建てる(右クリックでやめる)", BuildMode.Road => _roadStart == null ? "街路の始点をクリック" : "街路の終点をクリック(幅 4 m)", BuildMode.RemoveRoad => "外す街路の始点と終点をクリック", BuildMode.Demolish => "撤去する建物をクリック", _ => "" };
        _buildInfo.Text = $"{UseName(_sim.CityData, _buildUse)} {_buildSize}x{_buildSize} m {_buildFloors} 階  費用 {cost:N0}(国庫 {_sim.Factory.Treasury:N0})   上限 {Construction.MaxSize(_sim)} m 角 {Construction.MaxFloors(_sim)} 階(段階 {StageName(_sim.Factory.City!.Stage)})   {mode}";
    }
    void SetBuildMode(BuildMode m) { _buildMode = m; _roadStart = null; if (_ghost != null) { _ghost.QueueFree(); _ghost = null; } RefreshBuildInfo(); }

    /// <summary>画面の点から地面(y = Y)の座標へ。</summary>
    Vector3? GroundPoint(Vector2 screen)
    {
        var from = _cam.ProjectRayOrigin(screen); var dir = _cam.ProjectRayNormal(screen);
        if (Mathf.Abs(dir.Y) < 1e-5f) return null; float t = (Y - from.Y) / dir.Y; if (t < 0) return null;
        return from + dir * t;
    }

    bool HandleBuildClick(InputEventMouseButton mb)
    {
        if (_buildMode == BuildMode.None) return false;
        if (mb.ButtonIndex == MouseButton.Right) { SetBuildMode(BuildMode.None); return true; }
        if (mb.ButtonIndex != MouseButton.Left) return false;
        var g = GroundPoint(mb.Position); if (g == null) return true;
        int gx = (int)Math.Floor(g.Value.X), gz = (int)Math.Floor(g.Value.Z);
        switch (_buildMode)
        {
            case BuildMode.Place:
            {
                int x = gx - _buildSize / 2, z = gz - _buildSize / 2; x = x / 2 * 2; z = z / 2 * 2;
                var err = Construction.CheckSite(_sim, x, z, _buildSize, _buildFloors, _buildUse);
                if (err != null) { Notice($"建てられない: {err}"); return true; }
                int before = _sim.Factory.Buildings.Count;
                _sim.Enqueue(new BuildBuilding(0, $"{UseName(_sim.CityData, _buildUse)} {_sim.Factory.Buildings.Count + 1}", _buildUse, x, z, _buildSize, _buildFloors)); _sim.Step();
                if (_sim.Factory.Buildings.Count > before) { var nb = _sim.Factory.Buildings[^1]; AddBuildingVisual(nb); _kb.Flush(_cityRoot); Notice($"{nb.Name} を建てた(費用 {Construction.Cost(_sim, _buildUse, _buildSize, _buildFloors):N0})"); Select(nb.Id); }
                else Notice("建てられない: 国庫が足りない");
                RefreshBuildInfo(); return true;
            }
            case BuildMode.ShipMove:
                _sim.Enqueue(new MoveShip(_shipMoving, gx, gz)); _sim.Step(); SetBuildMode(BuildMode.None); _sideTick = -1; return true;
            case BuildMode.ShipPlace:
            {
                int before = _sim.Factory.Buildings.Count;
                _sim.Enqueue(new BuildShip(0, $"航空戦艦 {_sim.Factory.Buildings.Count(b => b.IsShip) + 1}", _shipKind, gx - 9, gz - 51)); _sim.Step();
                if (_sim.Factory.Buildings.Count > before) { var nb = _sim.Factory.Buildings[^1]; AddBuildingVisual(nb); _kb.Flush(_cityRoot); Notice($"{nb.Name} を建造した"); Select(nb.Id); SetBuildMode(BuildMode.None); }
                else Notice("建造できない(国家の段階と国庫が要る)");
                return true;
            }
            case BuildMode.Road:
            case BuildMode.RemoveRoad:
                if (_roadStart == null) { _roadStart = new Vector3(gx, 0, gz); RefreshBuildInfo(); return true; }
                _sim.Enqueue(new BuildRoad((int)_roadStart.Value.X, (int)_roadStart.Value.Z, gx, gz, 4, _buildMode == BuildMode.RemoveRoad)); _sim.Step();
                _terrain.BuildDirty(); _roadStart = null; RefreshBuildInfo(); _sideTick = -1; return true;
            case BuildMode.Demolish:
            {
                int id = PickBuilding(mb.Position); if (id < 0) return true;
                _sim.Enqueue(new Demolish(id)); _sim.Step();
                if (_sim.Factory.Get(id) == null) { RemoveBuildingVisual(id); Notice($"建物 #{id} を撤去した"); if (_selected == id) Select(-1); } else Notice("撤去できない(拠点・格納庫・勢力の拠点)");
                return true;
            }
        }
        return true;
    }

    void UpdateGhost(Vector2 screen)
    {
        if (_buildMode != BuildMode.Place) return;
        var g = GroundPoint(screen); if (g == null) return;
        int x = (int)Math.Floor(g.Value.X) - _buildSize / 2, z = (int)Math.Floor(g.Value.Z) - _buildSize / 2; x = x / 2 * 2; z = z / 2 * 2;
        bool ok = Construction.CheckSite(_sim, x, z, _buildSize, _buildFloors, _buildUse) == null;
        if (_ghost == null) { _ghost = new MeshInstance3D { Mesh = new BoxMesh() }; _cityRoot.AddChild(_ghost); }
        ((BoxMesh)_ghost.Mesh).Size = new Vector3(_buildSize, _buildFloors * 3f, _buildSize);
        _ghost.Position = new Vector3(x + _buildSize / 2f, Y + _buildFloors * 1.5f, z + _buildSize / 2f);
        var c = ok ? Accent : Red; _ghost.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(c, 0.35f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
    }

    int PickBuilding(Vector2 screen)
    {
        var from = _cam.ProjectRayOrigin(screen); var dir = _cam.ProjectRayNormal(screen); int best = -1; float bestT = float.MaxValue;
        foreach (var (box, id) in _hit) if (RayHit(box, from, dir, out float t) && t < bestT) { bestT = t; best = id; }
        return best;
    }

    void RemoveBuildingVisual(int id)
    {
        if (_buildingNodes.TryGetValue(id, out var n)) { n.QueueFree(); _buildingNodes.Remove(id); }
        _hit.RemoveAll(h => h.id == id);
        if (_marks.TryGetValue(id, out var m)) { m.QueueFree(); _marks.Remove(id); }
    }
}
