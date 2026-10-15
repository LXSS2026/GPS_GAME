using Godot;
using GPS.Sim;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 戦場の描画試作(手順 4 第 1 段階)。計算は GPS.Sim の Combat がそのまま動き、ここは機体の位置・耐久・射線・爆発を描くだけ。
/// 起動: godot --path game res://scenes/battle.tscn ++ [--ticks N] [--screenshot path] [--pivot x,y,z] [--dist d] [--yaw r] [--elev r]
/// 操作: 左ドラッグで視点回転、ホイールで距離、Space で一時停止、1〜4 で味方の隊の命令(攻撃/待機/退却/自爆)。
/// </summary>
public partial class Battle : Node3D
{
    const float Y = 3f;   // 地面の高さ(ボクセル 3 段の上)
    static readonly Color Navy = new(8 / 255f, 20 / 255f, 48 / 255f, 0.86f), Cyan = new(159 / 255f, 216 / 255f, 1f), Accent = new(56 / 255f, 198 / 255f, 1f), Text = new(244 / 255f, 248 / 255f, 1f);
    static readonly Color Line40 = new(159 / 255f, 216 / 255f, 1f, 0.4f), Warm = new(1f, 154 / 255f, 60 / 255f), Green = new(95 / 255f, 224 / 255f, 138 / 255f), Red = new(1f, 0.35f, 0.3f);
    static readonly List<string> MechHangar = new() { "base_12x12_office", "hangar_mech_12x12", "roof_12x12_heli_office" };

    VoxelWorld _world = new();
    BlockRegistry _blocks = null!;
    KitBash _kb = null!;
    FloorModules _fm = null!;
    MechBuilder _mb = null!;
    Simulation _sim = null!;
    GameData _data = null!;
    Camera3D _cam = null!;
    readonly Dictionary<int, Node3D> _mechNodes = new();
    readonly Dictionary<int, Label3D> _tags = new();
    readonly Dictionary<int, double> _facing = new();
    readonly List<(Node3D n, int life)> _fx = new();
    int _eventCursor;
    Vector3 _pivot = new(120, 3, 80);
    float _dist = 150, _yaw = 0.9f, _elev = 0.45f;
    bool _paused;
    int _frame, _shotTicks;
    bool _autoPivot = true;
    double _acc;
    Label _hud = null!;
    Label _log = null!;
    readonly List<string> _logLines = new();
    readonly Random _scatter = new(3);   // 描画専用(計算の乱数は使わない = 再生の一致を壊さない)

    byte B(string name) => _blocks.IdOf(name);
    static string? Arg(string name) { var a = OS.GetCmdlineUserArgs(); int i = Array.IndexOf(a, name); return i < 0 ? null : (i + 1 < a.Length ? a[i + 1] : ""); }

    public override void _Ready()
    {
        _blocks = BlockRegistry.FromJson(FileAccess.GetFileAsString("res://data/blocks.json"));
        _kb = new KitBash(11); _fm = new FloorModules(_kb, 9); _mb = new MechBuilder(_fm);
        _data = GameData.Load(ProjectSettings.GlobalizePath("res://data"));
        _sim = new Simulation(_blocks, _data, 5, CombatData.Load(ProjectSettings.GlobalizePath("res://data")));
        _sim.Factory.Creative = true;
        BuildGround();
        BuildForces();
        _kb.Flush(this);
        var tr = new TerrainRenderer(); AddChild(tr); tr.Init(_world, _blocks); tr.BuildAll();
        AddChild(SceneSetup.Lighting());
        if (Arg("--pivot") is string pv) { var p = pv.Split(','); _pivot = new Vector3(float.Parse(p[0]), float.Parse(p[1]), float.Parse(p[2])); _autoPivot = false; }
        if (Arg("--dist") is string ds) _dist = float.Parse(ds);
        if (Arg("--yaw") is string ys) _yaw = float.Parse(ys);
        if (Arg("--elev") is string es) _elev = float.Parse(es);
        _cam = new Camera3D { Fov = 38, Far = 3000 }; AddChild(_cam); PlaceCamera();
        BuildHud();
        if (Arg("--ticks") is string tk) { _shotTicks = int.Parse(tk); for (int i = 0; i < _shotTicks - 60; i++) StepOne(); }
        SyncMechs();
    }

    // ---------- 戦場: 街区 1 つ分の平地と街路、両端に格納庫 ----------
    void BuildGround()
    {
        byte stone = B("Stone"), plaza = B("Plaza"), conc = B("Concrete"), rx = B("RoadX"), asphalt = B("Asphalt"), grass = B("Grass");
        for (int x = 0; x < 240; x++)
            for (int z = 0; z < 160; z++)
            {
                _world.Set(x, 0, z, stone); _world.Set(x, 1, z, stone);
                bool road = z >= 72 && z < 88;
                byte top = road ? (z == 80 ? asphalt : rx) : x < 40 ? conc : x >= 200 ? conc : (x / 20 + z / 20) % 3 == 0 ? grass : plaza;
                _world.Set(x, 2, z, top);
            }
        _world.Dirty.Clear();
        _fm.Build(this, 14, Y, 100, MechHangar, 90, cutawayBelow: 2);   // 味方の格納庫(大扉が +X 側を向く)
        _fm.Build(this, 214, Y, 100, MechHangar, -90);                  // 敵の格納庫
        for (int i = 0; i < 4; i++) _fm.Build(this, 60 + i * 32, Y, 16, _fm.Plan("office", 20, 20, 2 + i % 3, false), 0);   // 街並み(遮蔽は第 1 段階では計算に効かない)
        for (int i = 0; i < 4; i++) _fm.Build(this, 76 + i * 32, Y, 124, _fm.Plan("office", 20, 20, 1 + i % 2, false), 0);
        _kb.Light(this, new Vector3(120, 30, 80), new Color(0.9f, 0.92f, 1.0f), 160f, 1.2f);
    }

    // ---------- 両軍: 味方 = 第 3 世代 汎用 4 + 重装 2、敵 = 第 2 世代 高速 3 + 重装 3 ----------
    void BuildForces()
    {
        var cmds = new List<ICommand>
        {
            new CreateBuilding(1, "味方の格納庫", 12, 12, 1, false), new SetBuildingPosition(1, 26, 100),
            new CreateBuilding(2, "敵の格納庫", 12, 12, 1, false), new SetBuildingPosition(2, 214, 100),
        };
        for (int i = 1; i <= 4; i++) cmds.Add(new CreateMech(i, 3, "general", 80));
        for (int i = 5; i <= 6; i++) cmds.Add(new CreateMech(i, 3, "heavy", 80));
        for (int i = 11; i <= 13; i++) cmds.Add(new CreateMech(i, 2, "fast", 75));
        for (int i = 14; i <= 16; i++) cmds.Add(new CreateMech(i, 2, "heavy", 75));
        foreach (var c in cmds) _sim.Enqueue(c); _sim.Step();
        // 制御基板の例: 耐久 30% 未満で退却(味方の汎用機)
        var prog = new GPS.Sim.Program { Name = "耐久 30% で退却", Interval = 6, Nodes = { new McuNode { Id = 1, Type = "in.mech.hp" }, new McuNode { Id = 2, Type = "in.const", Params = { ["value"] = 30 } }, new McuNode { Id = 3, Type = "op.lt" }, new McuNode { Id = 4, Type = "out.mech.retreat" } }, Links = { new(1, 0, 3, 0), new(2, 0, 3, 1), new(3, 0, 4, 0) } };
        for (int i = 1; i <= 4; i++) _sim.Enqueue(new SetMechProgram(i, prog));
        _sim.Enqueue(new FormSquad(1, 1, "第 1 小隊", new[] { 1, 2, 3, 4 }, 1)); _sim.Enqueue(new FormSquad(2, 1, "第 2 小隊(重装)", new[] { 5, 6 }, 1));
        _sim.Enqueue(new FormSquad(3, 2, "高速隊", new[] { 11, 12, 13 }, 2)); _sim.Enqueue(new FormSquad(4, 2, "重装隊", new[] { 14, 15, 16 }, 2));
        _sim.Step();
        _sim.Enqueue(new Deploy(1, 40, 96)); _sim.Enqueue(new Deploy(2, 36, 110)); _sim.Enqueue(new Deploy(3, 196, 60)); _sim.Enqueue(new Deploy(4, 200, 104));
        _sim.Step();
        _sim.Enqueue(new OrderSquad(1, SquadOrder.Attack, TargetSquad: 3, Formation: Formation.Line)); _sim.Enqueue(new OrderSquad(2, SquadOrder.Attack, TargetSquad: 4, Formation: Formation.Line));
        _sim.Enqueue(new OrderSquad(3, SquadOrder.Attack, TargetSquad: 1, Formation: Formation.Spread)); _sim.Enqueue(new OrderSquad(4, SquadOrder.Attack, TargetSquad: 2, Formation: Formation.Column));
        _sim.Step();
        foreach (var m in _sim.Factory.Mechs)
        {
            var node = _mb.Build(this, m.Archetype, $"g{m.Generation}", 0, Y, 0, 0);
            _mechNodes[m.Id] = node;
            var tag = new Label3D { Text = "", FontSize = 40, PixelSize = 0.025f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, OutlineSize = 10, Visible = false };
            AddChild(tag); _tags[m.Id] = tag;
        }
    }

    MechUnit? Unit(int mechId) => _sim.Factory.Squads.SelectMany(s => s.Members).FirstOrDefault(u => u.MechId == mechId);
    Squad? SquadOf(int mechId) => _sim.Factory.Squads.FirstOrDefault(s => s.Members.Any(u => u.MechId == mechId));

    void StepOne()
    {
        _sim.Step();
        var ev = _sim.Factory.Events;
        for (; _eventCursor < ev.Count; _eventCursor++) OnEvent(ev[_eventCursor]);
    }

    void OnEvent(FactoryEvent e)
    {
        switch (e.Kind)
        {
            case "hit":
            case "miss":
            {
                var to = Unit(e.Floor);
                if (to == null) return;
                var a = new Vector3(e.X, Y + 2.6f, e.Y); var b = new Vector3((float)to.X, Y + 2.2f, (float)to.Y);
                if (e.Kind == "miss") b += new Vector3(_scatter.Next(7) - 3, 0, _scatter.Next(7) - 3) * 0.7f;
                var sq = _sim.Factory.Squads.FirstOrDefault(s => s.Id == e.Building);
                bool beam = sq != null && _sim.Combat.Archetype[sq.Archetype].Primary.EndsWith("beam");
                Beam(a, b, beam ? new Color(0.4f, 0.9f, 1f) : new Color(1f, 0.85f, 0.5f), beam ? 0.3f : 0.15f, beam ? 14 : 10);
                if (e.Kind == "hit") Flash(b, 1.8f, new Color(1f, 0.6f, 0.2f), 12);
                break;
            }
            case "destroyed":
                Flash(new Vector3(e.X, Y + 2f, e.Y), 4f, new Color(1f, 0.45f, 0.1f), 30);
                Log($"機体 #{e.Power} 撃破(部品 {e.Destroyed} 個が残る)");
                break;
            case "selfdestruct":
                Flash(new Vector3(e.X, Y + 1f, e.Y), (float)e.Radius, new Color(1f, 0.3f, 0.1f, 0.7f), 45);
                Log($"自爆 出力 {e.Power} kW 半径 {e.Radius:0} m 巻き込み {e.Destroyed} 機");
                break;
        }
    }

    void Beam(Vector3 a, Vector3 b, Color c, float r, int life)
    {
        var mesh = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = r, BottomRadius = r, Height = a.DistanceTo(b), RadialSegments = 6 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = 4f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha } };
        mesh.Position = (a + b) / 2;
        var dir = (b - a).Normalized();
        var up = Mathf.Abs(dir.Dot(Vector3.Up)) > 0.99f ? Vector3.Right : Vector3.Up;
        mesh.Basis = Basis.LookingAt(dir, up) * new Basis(Vector3.Right, Mathf.Pi / 2);
        AddChild(mesh); _fx.Add((mesh, life));
    }
    void Flash(Vector3 at, float radius, Color c, int life)
    {
        var mesh = new MeshInstance3D { Mesh = new SphereMesh { Radius = radius, Height = radius * 2 }, Position = at,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(c, 0.55f), EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = 3f, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } };
        AddChild(mesh); _fx.Add((mesh, life));
    }
    void Log(string s) { _logLines.Add($"[{_sim.Tick / 60}s] {s}"); if (_logLines.Count > 8) _logLines.RemoveAt(0); }

    void SyncMechs()
    {
        var f = _sim.Factory;
        foreach (var m in f.Mechs)
        {
            var node = _mechNodes[m.Id]; var u = Unit(m.Id); var sq = SquadOf(m.Id);
            var tag = _tags[m.Id];
            if (u == null || sq == null || sq.State == SquadState.InHangar) { node.Visible = false; tag.Visible = false; continue; }
            node.Visible = true;
            var pos = new Vector3((float)u.X, Y, (float)u.Y);
            if (!u.Alive) { node.Position = pos + new Vector3(0, 0.6f, 0); node.RotationDegrees = new Vector3(0, (float)_facing.GetValueOrDefault(m.Id), 82); tag.Visible = false; continue; }
            tag.Visible = true; tag.Position = pos + new Vector3(0, 9.5f, 0);
            double face = _facing.GetValueOrDefault(m.Id, sq.Faction == 1 ? 90 : -90);
            var tgt = u.TargetMech >= 0 ? Unit(u.TargetMech) : null;
            double dx = 0, dy = 0;
            if (tgt != null) { dx = tgt.X - u.X; dy = tgt.Y - u.Y; }
            else { dx = u.X - node.Position.X; dy = u.Y - node.Position.Z; }
            if (dx * dx + dy * dy > 0.01) face = Mathf.RadToDeg(Math.Atan2(dx, dy));
            _facing[m.Id] = face;
            node.Position = pos; node.RotationDegrees = new Vector3(0, (float)face, 0);
            int pct = (int)Math.Round(u.Hp * 100 / Math.Max(1, u.MaxHp));
            tag.Text = $"{sq.Name} #{m.Id}\n{Bar(pct)} {pct}%" + (u.Retreat != 0 ? "\n退却" : "");
            tag.Modulate = sq.Faction == 1 ? (pct < 30 ? Warm : Cyan) : Red;
        }
    }
    static string Bar(int pct) { int n = Math.Clamp((pct + 9) / 10, 0, 10); return new string('|', n) + new string('.', 10 - n); }

    // ---------- 画面の板: 隊の一覧と命令、ログ ----------
    void BuildHud()
    {
        var layer = new CanvasLayer(); AddChild(layer);
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore }; root.SetAnchorsPreset(Control.LayoutPreset.FullRect); layer.AddChild(root);
        var font = new SystemFont { FontNames = new[] { "Yu Gothic UI", "Meiryo", "sans-serif" } };
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 6);
        var title = new Label { Text = "戦場  手順 4 第 1 段階(計算は GPS.Sim.Combat・同じティック)" }; title.AddThemeFontOverride("font", font); title.AddThemeFontSizeOverride("font_size", 15); title.AddThemeColorOverride("font_color", Text); box.AddChild(title);
        _hud = new Label(); _hud.AddThemeFontOverride("font", font); _hud.AddThemeFontSizeOverride("font_size", 13); _hud.AddThemeColorOverride("font_color", Text); box.AddChild(_hud);
        var keys = new Label { Text = "1 攻撃  2 待機  3 退却  4 自爆(第 1 小隊)   Space 一時停止   ドラッグ 回転  ホイール 距離" }; keys.AddThemeFontOverride("font", font); keys.AddThemeFontSizeOverride("font_size", 12); keys.AddThemeColorOverride("font_color", Line40); box.AddChild(keys);
        var panel = new PanelContainer { Position = new Vector2(16, 16) }; panel.AddThemeStyleboxOverride("panel", Box(Navy, Cyan, 1, 12)); panel.AddChild(box); root.AddChild(panel);
        _log = new Label(); _log.AddThemeFontOverride("font", font); _log.AddThemeFontSizeOverride("font_size", 13); _log.AddThemeColorOverride("font_color", Text);
        var lp = new PanelContainer(); lp.AddThemeStyleboxOverride("panel", Box(Navy, Cyan, 1, 12)); lp.AddChild(_log); lp.AnchorTop = 1; lp.AnchorBottom = 1; lp.OffsetLeft = 16; lp.OffsetTop = -150; lp.OffsetBottom = -16; lp.CustomMinimumSize = new Vector2(520, 0); root.AddChild(lp);
    }
    static StyleBoxFlat Box(Color bg, Color border, int bw, int pad)
    {
        var s = new StyleBoxFlat { BgColor = bg, BorderColor = border, ContentMarginLeft = pad, ContentMarginRight = pad, ContentMarginTop = pad, ContentMarginBottom = pad }; s.SetBorderWidthAll(bw); return s;
    }
    void RefreshHud()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"時刻 {_sim.Tick / 60}.{_sim.Tick % 60:00} 秒" + (_paused ? "  一時停止" : ""));
        foreach (var s in _sim.Factory.Squads)
        {
            string arch = s.Archetype switch { "heavy" => "重装", "fast" => "高速", _ => "汎用" };
            string order = s.Order switch { SquadOrder.Attack => "攻撃", SquadOrder.Move => "移動", SquadOrder.Retreat => "退却", SquadOrder.SelfDestruct => "自爆", _ => "待機" };
            string st = s.State == SquadState.InHangar ? "格納庫" : "出撃中";
            double hp = s.Members.Sum(u => u.Hp), max = s.Members.Sum(u => u.MaxHp);
            sb.AppendLine($"{(s.Faction == 1 ? "味方" : "敵  ")} {s.Name,-12} {arch} 第 {s.Generation} 世代  {s.Alive}/{s.Members.Count} 機  耐久 {hp / Math.Max(1, max) * 100:0}%  {order}  {st}");
        }
        _hud.Text = sb.ToString().TrimEnd();
        _log.Text = _logLines.Count == 0 ? "(記録なし)" : string.Join("\n", _logLines);
    }

    void PlaceCamera()
    {
        _cam.Position = _pivot + new Vector3(Mathf.Cos(_elev) * Mathf.Sin(_yaw), Mathf.Sin(_elev), Mathf.Cos(_elev) * Mathf.Cos(_yaw)) * _dist;
        _cam.LookAt(_pivot);
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventMouseMotion mm && (mm.ButtonMask & MouseButtonMask.Left) != 0) { _yaw -= mm.Relative.X * 0.005f; _elev = Mathf.Clamp(_elev + mm.Relative.Y * 0.005f, 0.1f, 1.4f); PlaceCamera(); }
        if (ev is InputEventMouseButton mb && mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp) { _dist = Mathf.Max(20, _dist * 0.9f); PlaceCamera(); }
            if (mb.ButtonIndex == MouseButton.WheelDown) { _dist = Mathf.Min(600, _dist / 0.9f); PlaceCamera(); }
        }
        if (ev is InputEventKey k && k.Pressed && !k.Echo)
        {
            if (k.Keycode == Key.Space) _paused = !_paused;
            if (k.Keycode == Key.Key1) _sim.Enqueue(new OrderSquad(1, SquadOrder.Attack, TargetSquad: 3));
            if (k.Keycode == Key.Key2) _sim.Enqueue(new OrderSquad(1, SquadOrder.Hold));
            if (k.Keycode == Key.Key3) _sim.Enqueue(new OrderSquad(1, SquadOrder.Retreat));
            if (k.Keycode == Key.Key4) _sim.Enqueue(new OrderSquad(1, SquadOrder.SelfDestruct));
        }
    }

    public override void _Process(double delta)
    {
        if (!_paused && Arg("--screenshot") == null)
        {
            _acc += delta * Clock.TicksPerSecond;
            int n = Math.Min(10, (int)_acc); _acc -= n;
            for (int i = 0; i < n; i++) StepOne();
        }
        for (int i = _fx.Count - 1; i >= 0; i--)
        {
            var (node, life) = _fx[i];
            if (--life <= 0) { node.QueueFree(); _fx.RemoveAt(i); continue; }
            _fx[i] = (node, life);
        }
        SyncMechs(); RefreshHud();
        var path = Arg("--screenshot"); if (path == null) return;
        _frame++;
        if (_frame >= 30 && _frame < 40 && _shotTicks > 0) { for (int i = 0; i < 6; i++) StepOne(); SyncMechs(); }   // 最後の 60 ティックは描画しながら(射線・爆発が写る)
        if (_frame == 30 && _autoPivot) { var all = _sim.Factory.Squads.Where(s => s.State == SquadState.Field).SelectMany(s => s.Members).Where(u => u.Alive).ToList(); var pts = all.Where(u => u.TargetMech >= 0).ToList(); if (pts.Count == 0) pts = all; if (pts.Count > 0) { _pivot = new Vector3((float)pts.Average(u => u.X), Y + 2, (float)pts.Average(u => u.Y)); _dist = Mathf.Clamp((float)(pts.Max(u => u.X) - pts.Min(u => u.X) + pts.Max(u => u.Y) - pts.Min(u => u.Y)) * 0.7f + 40f, 60f, 170f); PlaceCamera(); } }
        if (_frame == 40) { GetViewport().GetTexture().GetImage().SavePng(path); GetTree().Quit(); }
    }
}
