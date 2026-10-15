using System.Collections.Generic;
using System.Linq;
using Godot;
using GPS.Sim;

/// <summary>設備のアイコンを実物の 3D 部品から描く(2026-10-15 指示)。透明背景の SubViewport で 1 点ずつ描き、game/assets/icons/{id}.png に保存する。
/// 実行: godot --path game res://scenes/icons.tscn ++ --out <dir>。部品の無いアイテムは線画 SVG のまま。</summary>
public partial class IconRenderer : Node
{
    SubViewport _vp = null!;
    Node3D _root = null!, _stage = null!;
    Camera3D _cam = null!;
    KitBash _kb = null!;
    Equipment _eq = null!;
    List<StructureDef> _defs = null!;
    int _i = -1, _wait;
    string _out = "";

    public override void _Ready()
    {
        var data = GameData.Load(ProjectSettings.GlobalizePath("res://data"));
        _defs = data.Structures.Values.OrderBy(d => d.Id).ToList();
        var args = OS.GetCmdlineUserArgs(); int oi = System.Array.IndexOf(args, "--out");
        _out = oi >= 0 ? args[oi + 1] : ProjectSettings.GlobalizePath("res://assets/icons");
        _kb = new KitBash(11); _eq = new Equipment(new FloorModules(_kb, 9));
        _vp = new SubViewport { Size = new Vector2I(256, 256), TransparentBg = true, Msaa3D = Viewport.Msaa.Msaa4X, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        AddChild(_vp);
        _root = new Node3D(); _vp.AddChild(_root);
        _root.AddChild(new DirectionalLight3D { LightEnergy = 1.6f, LightColor = new Color(1f, 0.92f, 0.82f), RotationDegrees = new Vector3(-40, 40, 0), ShadowEnabled = true });
        _root.AddChild(new DirectionalLight3D { LightEnergy = 0.6f, LightColor = new Color(0.6f, 0.75f, 1f), RotationDegrees = new Vector3(-30, -120, 0) });
        _root.AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0, 0, 0, 0), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.5f, 0.58f, 0.7f), AmbientLightEnergy = 1.2f, TonemapMode = Godot.Environment.ToneMapper.Aces, GlowEnabled = true, GlowIntensity = 0.6f } });
        _cam = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 3f }; _root.AddChild(_cam);
        _stage = new Node3D(); _root.AddChild(_stage);
    }

    public override void _Process(double delta)
    {
        if (_wait > 0) { if (--_wait == 0) Save(); return; }
        _i++;
        if (_i >= _defs.Count) { GD.Print($"[icons] {_defs.Count} written to {_out}"); GetTree().Quit(); return; }
        foreach (var c in _stage.GetChildren()) { _stage.RemoveChild(c); c.QueueFree(); }
        var def = _defs[_i];
        Equipment.PlaceVisual(_eq, _kb, _stage, def, 0, 0, 0, 0);
        _kb.Flush(_stage); _eq.Flush(_stage);
        float w = def.W, d = def.D, h = System.Math.Max(1, def.H);
        var center = new Vector3(w / 2f, h * 0.45f, d / 2f);
        _cam.Size = System.Math.Max(w, System.Math.Max(d, h)) * 1.35f + 0.4f;
        _cam.Position = center + new Vector3(1, 0.9f, 1).Normalized() * 20f;
        _cam.LookAt(center);
        _wait = 4;
    }

    void Save()
    {
        var img = _vp.GetTexture().GetImage();
        img.SavePng(System.IO.Path.Combine(_out, _defs[_i].Id + ".png"));
    }
}
