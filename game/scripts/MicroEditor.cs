using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GPS.Sim;

/// <summary>ミクロの地図の編集画面(手順2 第 2 段階・2026-10-15)。GPS.Sim の工場を 60 ティック/秒で回し、平地 1 ブロック(100x100)の 1 階に設備を置く。
/// 入力は全て命令(ICommand)にして投入し、描画は状態を写すだけ。見た目は G8 のデザイン言語。</summary>
public partial class MicroEditor : Node3D
{
    const int W = 100, D = 100;
    const float GroundY = 3f;
    int _floor;                       // 編集中の階(0 始まり)
    float FloorY => GroundY + _floor * 3f;   // 編集中の階の床の高さ(階高 3 m)
    MeshInstance3D? _slab;
    static readonly Color Navy = new(8 / 255f, 20 / 255f, 48 / 255f, 0.86f), Cyan = new(159 / 255f, 216 / 255f, 1f), Accent = new(56 / 255f, 198 / 255f, 1f), Text = new(244 / 255f, 248 / 255f, 1f);

    Simulation _sim = null!;
    GameData _data = null!;
    VoxelWorld _world = new();
    KitBash _kb = null!;
    FloorModules _fm = null!;
    Equipment _eq = null!;
    Camera3D _cam = null!;
    Node3D _eqRoot = null!, _itemsRoot = null!;
    MultiMeshInstance3D? _items;
    Node3D _cursor = null!;
    Vector3 _pivot = new(50, GroundY, 50);
    float _yaw = 0.6f, _elev = 0.9f, _dist = 60;
    int _speed = 1;
    string? _palette;
    Direction _dir = Direction.East;
    int _selected = -1;
    enum Mode { Normal, Region, Paste, Move, Link }
    Mode _mode = Mode.Normal;
    (int x, int z)? _regionA; (int x0, int z0, int x1, int z1)? _region;
    int _blueprint = -1;
    (int floor, int id)? _linkFrom;
    MeshInstance3D? _regionBox;
    readonly Stack<(ICommand undo, ICommand redo)> _undo = new(), _redo = new();
    int _structureVersion = -1, _renderedVersion = -2;
    int _frame;

    // UI
    Label _clock = null!, _power = null!, _title = null!;
    OptionButton _policy = null!;
    VBoxContainer _side = null!;
    readonly Dictionary<string, Button> _paletteButtons = new();
    Label _dirLabel = null!;
    readonly List<Button> _speedButtons = new();

    public override void _Ready()
    {
        Engine.PhysicsTicksPerSecond = Clock.TicksPerSecond;
        _data = GameData.Load(ProjectSettings.GlobalizePath("res://data"));
        _sim = new Simulation(BlockRegistry.FromJson(FileAccess.GetFileAsString("res://data/blocks.json")), _data, 1);
        _kb = new KitBash(11); _fm = new FloorModules(_kb, 9); _eq = new Equipment(_fm);

        // 地面: 平地ブロックはコンクリート、周囲は広場、街路
        byte stone = _sim.Blocks.IdOf("Stone"), plaza = _sim.Blocks.IdOf("Plaza"), conc = _sim.Blocks.IdOf("Concrete"), road = _sim.Blocks.IdOf("RoadX");
        for (int x = -16; x < W + 16; x++) for (int z = -16; z < D + 16; z++)
        {
            _world.Set(x, 0, z, stone); _world.Set(x, 1, z, stone);
            bool inBlock = x >= 0 && x < W && z >= 0 && z < D;
            _world.Set(x, 2, z, inBlock ? conc : (x < -2 || x >= W + 2 || z < -2 || z >= D + 2) ? road : plaza);
        }
        var tr = new TerrainRenderer(); AddChild(tr); tr.Init(_world, _sim.Blocks); tr.BuildAll();
        AddChild(SceneSetup.Lighting());
        _kb.Light(this, new Vector3(50, 40, 50), new Color(0.9f, 0.92f, 1f), 160f, 1.2f);
        for (int i = 0; i <= W; i += 10) { _kb.Strip(this, new Vector3(i, GroundY + 0.02f, D / 2f), new Vector3(0.04f, 0.02f, D), _kb.GlowCyan); _kb.Strip(this, new Vector3(W / 2f, GroundY + 0.02f, i), new Vector3(W, 0.02f, 0.04f), _kb.GlowCyan); }
        _kb.Flush(this);
        _eqRoot = new Node3D { Name = "equipment" }; AddChild(_eqRoot);
        _itemsRoot = new Node3D { Name = "items" }; AddChild(_itemsRoot);
        _cursor = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.02f, 0.06f, 1.02f) }, MaterialOverride = _kb.GlowCyan, Visible = false }; AddChild(_cursor);
        _cam = new Camera3D { Far = 1000, Fov = 45 }; AddChild(_cam); UpdateCamera();

        // 工場: 平地ブロック 100x100、鉱脈は北西の 20x20、設備の在庫を支給
        _sim.Enqueue(new CreateBuilding(1, "平地ブロック A-1", W, D, 3, true));   // 3 階建て(昇降機の確認用。編集は 1 階)
        for (int x = 0; x < 20; x++) for (int y = D - 20; y < D; y++) _sim.Enqueue(new SetOre(1, x, y, (x + y) % 7 == 0 ? "copper_ore" : "iron_ore", 20_000_000, 30 + (x * 7 + y * 3) % 41));
        foreach (var def in _data.Structures.Values) _sim.Enqueue(new AddInventory(1, def.Id, 500));
        _sim.Step();
        BuildUi();
        SetFloor(0);
        if (Arg("--demo") != null) Demo();
    }

    // ---------- 計算 ----------
    public override void _PhysicsProcess(double delta)
    {
        for (int i = 0; i < _speed; i++) _sim.Step();
    }

    public override void _Process(double delta)
    {
        Pan(delta);
        if (_structureVersion != _renderedVersion) { RebuildEquipment(); _renderedVersion = _structureVersion; }
        RebuildItems();
        RefreshTop();
        RefreshSide();
        if (_sim.Factory.Events.Count > _seenEvents) { _seenEvents = _sim.Factory.Events.Count; _structureVersion++; var ev = _sim.Factory.Events[^1]; _lastEvent = ev.Kind == "mech" ? $"機体 完成  第 {ev.Power} 世代  品質 {ev.Destroyed}  ({ev.X}, {ev.Y})" : $"爆発  ({ev.X}, {ev.Y})  {ev.Power} kW  半径 {ev.Radius:F1}  消失 {ev.Destroyed} 台"; _selected = -1; }
        Screenshot();
    }

    Building Building => _sim.Factory.Get(1)!;
    Floor Floor => Building.Floors[_floor];

    void Apply(ICommand cmd, ICommand? undo)
    {
        _sim.Enqueue(cmd); _sim.Step();
        if (undo != null) { _undo.Push((undo, cmd)); _redo.Clear(); }
        _structureVersion++;
    }

    // ---------- 入力 ----------
    public override void _UnhandledInput(InputEvent e)
    {
        if (_mcuEditor != null && e is InputEventKey) return;   // 図式エディタが開いている間、キーは図式へ
        switch (e)
        {
            case InputEventMouseButton { Pressed: true } mb:
                switch (mb.ButtonIndex)
                {
                    case MouseButton.WheelUp: _dist = Mathf.Max(6, _dist * 0.9f); UpdateCamera(); break;
                    case MouseButton.WheelDown: _dist = Mathf.Min(260, _dist / 0.9f); UpdateCamera(); break;
                    case MouseButton.Left: ClickCell(mb.Position, place: true); break;
                    case MouseButton.Right: ClickCell(mb.Position, place: false); break;
                }
                break;
            case InputEventMouseMotion mm:
                if (Input.IsMouseButtonPressed(MouseButton.Middle)) { _yaw -= mm.Relative.X * 0.005f; _elev = Mathf.Clamp(_elev + mm.Relative.Y * 0.005f, 0.2f, 1.5f); UpdateCamera(); }
                else if (Cell(mm.Position) is var (cx, cz, ok) && ok) { _cursor.Visible = true; _cursor.Position = new Vector3(cx + 0.5f, FloorY + 0.03f, cz + 0.5f); }
                else _cursor.Visible = false;
                break;
            case InputEventKey { Pressed: true, Echo: false } k:
                if (k.Keycode == Key.R) SetDir((Direction)(((int)_dir + 1) % 6));
                else if (k.Keycode == Key.Escape) { SetPalette(null); _selected = -1; }
                else if (k.Keycode == Key.Delete && _selected >= 0) RemoveSelected();
                else if (k.Keycode == Key.Z && k.CtrlPressed) Undo();
                else if (k.Keycode == Key.Y && k.CtrlPressed) Redo();
                else if (k.Keycode == Key.Space) SetSpeed(_speed == 0 ? 1 : 0);
                break;
        }
    }

    (int x, int z, bool ok) Cell(Vector2 screen)
    {
        var o = _cam.ProjectRayOrigin(screen); var d = _cam.ProjectRayNormal(screen);
        if (Mathf.Abs(d.Y) < 1e-4f) return (0, 0, false);
        float t = (FloorY - o.Y) / d.Y; if (t < 0) return (0, 0, false);
        var p = o + d * t;
        int x = Mathf.FloorToInt(p.X), z = Mathf.FloorToInt(p.Z);
        return (x, z, x >= 0 && x < W && z >= 0 && z < D);
    }

    void ClickCell(Vector2 screen, bool place)
    {
        var (x, z, ok) = Cell(screen); if (!ok) return;
        var hit = Floor.At(x, z, 0);
        if (place && _mcuEditor != null && _mcuEditor.Picking) { if (hit != null) _mcuEditor.Picked(_floor, hit); return; }
        if (!place && _mode != Mode.Normal) { SetMode(Mode.Normal); return; }   // 右クリックでモードを抜ける
        if (place && _mode == Mode.Region)
        {
            if (_regionA == null) { _regionA = (x, z); return; }
            var a = _regionA.Value; _region = (Math.Min(a.x, x), Math.Min(a.z, z), Math.Max(a.x, x), Math.Max(a.z, z)); _regionA = null; ShowRegion(); _mode = Mode.Normal; _sideTick = -1; return;
        }
        if (place && _mode == Mode.Paste && _blueprint >= 0)
        {
            int before = Building.NextStructureId;
            _sim.Enqueue(new PasteBlueprint(1, _floor, x, z, _blueprint)); _sim.Step(); _structureVersion++;
            _undo.Clear(); _redo.Clear();   // 複数設置の取り消しは未対応
            _status.Text = $"貼り付け  {Building.NextStructureId - before} 台";
            return;
        }
        if (place && _mode == Mode.Move && _region is var (x0, z0, x1, z1))
        {
            _sim.Enqueue(new MoveRegion(1, _moveFromFloor, x0, z0, x1, z1, 1, _floor, x, z)); _sim.Step(); _structureVersion++; _undo.Clear(); _redo.Clear();
            _region = null; ShowRegion(); SetMode(Mode.Normal); return;
        }
        if (place && _mode == Mode.Link && _linkFrom is var (lf, lid))
        {
            if (hit is Port) { _sim.Enqueue(new LinkPorts(1, lf, lid, 1, _floor, hit.Id)); _sim.Step(); _structureVersion++; _selected = hit.Id; }
            SetMode(Mode.Normal); return;
        }
        if (!place)
        {
            if (hit != null) Apply(new RemoveStructure(1, _floor, hit.Id), new PlaceStructure(1, _floor, hit.DefId, hit.X, hit.Y, hit.Z, hit.Dir));
            if (_selected == hit?.Id) _selected = -1;
            return;
        }
        if (_palette != null)
        {
            var def = _data.Structures[_palette];
            if (!Floor.Fits(def, x, z, 0) || Building.Inventory.GetValueOrDefault(_palette) <= 0) return;
            int id = Building.NextStructureId;
            Apply(new PlaceStructure(1, _floor, _palette, x, z, 0, _dir), new RemoveStructure(1, _floor, id));
            _selected = id;
            return;
        }
        _selected = hit?.Id ?? -1;
    }

    McuGraphEditor? _mcuEditor; CanvasLayer? _uiLayer; PanelContainer? _sidePanel;
    void OpenMcu(int mcuId)
    {
        if (_mcuEditor != null) return;
        _mcuEditor = McuGraphEditor.Open(_uiLayer!, _sim, _data, 1, _floor, mcuId, Icon, _uiLayer!.GetChild<Control>(0).Theme!, () => { _mcuEditor = null; _sideTick = -1; _structureVersion++; if (_sidePanel != null) _sidePanel.Visible = true; }, () => _structureVersion++);
        if (_sidePanel != null) _sidePanel.Visible = false;   // 右の板を隠して 3D の帯を広げる
        _structureVersion++;
    }
    int _moveFromFloor;
    void SetMode(Mode mode)
    {
        _mode = mode; _regionA = null; if (mode != Mode.Link) _linkFrom = null;
        if (mode == Mode.Move) _moveFromFloor = _floor;
        if (mode != Mode.Paste) _palette = _mode == Mode.Normal ? _palette : null;
        _sideTick = -1;
    }

    void ShowRegion()
    {
        _regionBox?.QueueFree(); _regionBox = null;
        if (_region is not var (x0, z0, x1, z1)) return;
        var size = new Vector3(x1 - x0 + 1, 0.08f, z1 - z0 + 1);
        _regionBox = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.22f, 0.78f, 1f, 0.35f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, EmissionEnabled = true, Emission = Accent, EmissionEnergyMultiplier = 0.4f }, Position = new Vector3(x0 + size.X / 2, FloorY + 0.05f, z0 + size.Z / 2) };
        AddChild(_regionBox);
    }

    void SetFloor(int f)
    {
        if (f < 0 || f >= Building.Floors.Count) return;
        _floor = f; _selected = -1; _region = null; ShowRegion(); _structureVersion++; _sideTick = -1;
        _slab?.QueueFree();
        _slab = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(W, 0.12f, D) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.28f, 0.3f, 0.34f) }, Position = new Vector3(W / 2f, FloorY - 0.06f, D / 2f), Visible = f > 0 };
        AddChild(_slab);
        _pivot.Y = FloorY; UpdateCamera();
        foreach (var (k, b) in _floorButtons) b.AddThemeStyleboxOverride("normal", k == f ? Box(new Color(18 / 255f, 40 / 255f, 84 / 255f, 0.9f), Accent, 2, 6) : Box(Navy, Cyan, 1, 6));
    }
    readonly Dictionary<int, Button> _floorButtons = new();

    void RemoveSelected()
    {
        var s = Building.Find(_floor, _selected); if (s == null) return;
        Apply(new RemoveStructure(1, _floor, s.Id), new PlaceStructure(1, _floor, s.DefId, s.X, s.Y, s.Z, s.Dir));
        _selected = -1;
    }

    void Undo() { if (_undo.Count == 0) return; var (u, r) = _undo.Pop(); _sim.Enqueue(u); _sim.Step(); _redo.Push((u, r)); _structureVersion++; _selected = -1; }
    void Redo() { if (_redo.Count == 0) return; var (u, r) = _redo.Pop(); _sim.Enqueue(r); _sim.Step(); _undo.Push((u, r)); _structureVersion++; _selected = -1; }

    void Pan(double delta)
    {
        var move = Vector2.Zero;
        if (Input.IsPhysicalKeyPressed(Key.W)) move.Y += 1; if (Input.IsPhysicalKeyPressed(Key.S)) move.Y -= 1;
        if (Input.IsPhysicalKeyPressed(Key.D)) move.X += 1; if (Input.IsPhysicalKeyPressed(Key.A)) move.X -= 1;
        if (move == Vector2.Zero) return;
        var fwd = -new Vector3(Mathf.Sin(_yaw), 0, Mathf.Cos(_yaw)); var right = new Vector3(-fwd.Z, 0, fwd.X);
        _pivot += (fwd * move.Y + right * move.X) * (float)delta * _dist * 0.6f;
        _pivot.X = Mathf.Clamp(_pivot.X, 0, W); _pivot.Z = Mathf.Clamp(_pivot.Z, 0, D);
        UpdateCamera();
    }

    void UpdateCamera()
    {
        _cam.Position = _pivot + new Vector3(Mathf.Cos(_elev) * Mathf.Sin(_yaw), Mathf.Sin(_elev), Mathf.Cos(_elev) * Mathf.Cos(_yaw)) * _dist;
        _cam.LookAt(_pivot);
    }

    // ---------- 描画 ----------
    static int Rot(Direction d) => d switch { Direction.East => 90, Direction.North => 180, Direction.West => 270, _ => 0 };
    void RebuildEquipment()
    {
        foreach (var c in _eqRoot.GetChildren()) c.QueueFree();
        foreach (var s in Floor.Structures) Equipment.PlaceVisual(_eq, _kb, _eqRoot, _data.Structures[s.DefId], s.X, FloorY, s.Y, Rot(s.Dir), s is Port p && p.LinkBuilding >= 0 ? Math.Sign(p.LinkFloor - 0) : 0);
        foreach (var s in Floor.Structures)
        {
            var def = _data.Structures[s.DefId];
            string text = s.Label;
            if (_mcuEditor != null) { var refs = _mcuEditor.NodesReferencing(_floor, s.Id); if (refs.Count > 0) text = (text == "" ? "" : text + "  ") + "ノード " + string.Join(",", refs); }
            if (text == "") continue;
            _eqRoot.AddChild(new Label3D { Text = text, FontSize = 48, PixelSize = 0.01f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Modulate = s.Label != "" && !text.StartsWith("ノード") ? Cyan : Accent, OutlineSize = 12, OutlineModulate = new Color(0, 0, 0, 0.8f), NoDepthTest = true, Position = new Vector3(s.X + def.W / 2f, FloorY + def.H + 0.8f, s.Y + def.D / 2f) });
        }
        _kb.Flush(_eqRoot);
        _eq.Flush(_eqRoot);
        // 鉱脈: 残量のある床を暗い茶で示す
        var ore = new List<Transform3D>();
        for (int x = 0; x < W; x++) for (int y = 0; y < D; y++) if (Floor.OreAt(x, y) != null) ore.Add(new Transform3D(Basis.Identity, new Vector3(x + 0.5f, FloorY + 0.01f, y + 0.5f)));
        if (ore.Count > 0)
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new BoxMesh { Size = new Vector3(0.92f, 0.02f, 0.92f) }, InstanceCount = ore.Count };
            for (int i = 0; i < ore.Count; i++) mm.SetInstanceTransform(i, ore[i]);
            _eqRoot.AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = _kb.Rust });
        }
    }

    static Color ReactorColor(int gen) => Equipment.ReactorColor(gen);
    static Color TierColor(QualityTier t) => t switch { QualityTier.Defect => new Color(0.5f, 0.5f, 0.5f), QualityTier.Normal => new Color(0.95f, 0.95f, 0.95f), QualityTier.High => new Color(0.35f, 0.95f, 0.45f), _ => new Color(1f, 0.85f, 0.25f) };

    /// <summary>ベルトの上・インサータの手・機械の出口のアイテムを小さな箱で描く(品質の階級の色)。毎フレーム作り直す。</summary>
    void RebuildItems()
    {
        var xf = new List<(Transform3D, Color)>();
        foreach (var s in Floor.Structures)
        {
            switch (s)
            {
                case Belt b:
                    var (dx, dy, _) = b.Dir.Delta();
                    for (int i = 0; i < 4; i++)
                    {
                        if (b.Slots[i] is not { } it) continue;
                        float t = (i + 0.5f) / 4f - 0.5f;
                        xf.Add((new Transform3D(Basis.Identity, new Vector3(b.X + 0.5f + dx * t, FloorY + 0.45f, b.Y + 0.5f + dy * t)), TierColor(it.Tier)));
                    }
                    break;
                case Inserter ins when ins.Hand is { } h:
                    xf.Add((new Transform3D(Basis.Identity, new Vector3(ins.X + 0.5f, FloorY + 1.1f, ins.Y + 0.5f)), TierColor(h.Tier)));
                    break;
                case Machine m when m.Outputs.Count > 0:
                    xf.Add((new Transform3D(Basis.Identity.Scaled(Vector3.One * Mathf.Min(2f, 0.8f + m.Outputs.Count * 0.1f)), new Vector3(m.X + 0.5f, FloorY + 2.3f, m.Y + 0.5f)), TierColor(m.Outputs[0].Tier)));
                    break;
            }
        }
        _items?.QueueFree(); _items = null;
        if (xf.Count == 0) return;
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = new BoxMesh { Size = Vector3.One * 0.3f }, InstanceCount = xf.Count };
        for (int i = 0; i < xf.Count; i++) { mm.SetInstanceTransform(i, xf[i].Item1); mm.SetInstanceColor(i, xf[i].Item2); }
        _items = new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, EmissionEnabled = true, Emission = new Color(0.3f, 0.3f, 0.3f), EmissionEnergyMultiplier = 0.4f } };
        _itemsRoot.AddChild(_items);
    }

    // ---------- UI(G8 の様式。斜めの要素は使わない) ----------
    static readonly Color NavyDeep = new(6 / 255f, 14 / 255f, 34 / 255f, 0.94f), Line40 = new(159 / 255f, 216 / 255f, 1f, 0.4f), Warm = new(1f, 154 / 255f, 60 / 255f), Green = new(95 / 255f, 224 / 255f, 138 / 255f);
    readonly Dictionary<string, Texture2D> _icons = new();
    Texture2D Icon(string id)
    {
        if (!_icons.TryGetValue(id, out var t))
        {   // 設備は実物の部品を描いた png(IconRenderer)、部品の無いアイテムは線画 svg
            string png = $"res://assets/icons/{id}.png", svg = $"res://assets/icons/{id}.svg";
            t = ResourceLoader.Exists(png) ? GD.Load<Texture2D>(png) : ResourceLoader.Exists(svg) ? GD.Load<Texture2D>(svg) : GD.Load<Texture2D>("res://assets/icons/chest.svg");
            _icons[id] = t;
        }
        return t;
    }

    static StyleBoxFlat Box(Color bg, Color border, int bw = 1, int pad = 8, int inner = 0)
    {
        var s = new StyleBoxFlat { BgColor = bg, BorderColor = border, ContentMarginLeft = pad, ContentMarginRight = pad, ContentMarginTop = pad, ContentMarginBottom = pad };
        s.SetBorderWidthAll(bw);
        if (inner > 0) { s.ShadowColor = Line40; s.ShadowSize = inner; s.ShadowOffset = Vector2.Zero; }
        return s;
    }
    static Font JpFont() => new SystemFont { FontNames = new[] { "Yu Gothic UI", "Meiryo", "sans-serif" } };

    /// <summary>全部品に当てる Theme(板・ボタン・プルダウン・数値入力・進捗の棒・ツールチップ)。</summary>
    Theme MakeTheme()
    {
        var th = new Theme();
        var font = JpFont();
        th.DefaultFont = font; th.DefaultFontSize = 13;
        th.SetColor("font_color", "Label", Text);
        foreach (var t in new[] { "Button", "OptionButton", "CheckButton" })
        {
            th.SetStylebox("normal", t, Box(Navy, Cyan, 1, 8)); th.SetStylebox("hover", t, Box(new Color(18 / 255f, 40 / 255f, 84 / 255f, 0.9f), Accent, 1, 8));
            th.SetStylebox("pressed", t, Box(new Color(24 / 255f, 56 / 255f, 110 / 255f, 0.95f), Accent, 2, 8)); th.SetStylebox("focus", t, new StyleBoxEmpty());
            th.SetStylebox("disabled", t, Box(Navy, Line40, 1, 8));
            th.SetColor("font_color", t, Text); th.SetColor("font_hover_color", t, Text); th.SetColor("font_pressed_color", t, Text); th.SetColor("font_disabled_color", t, Line40);
        }
        th.SetStylebox("panel", "PopupMenu", Box(NavyDeep, Cyan, 1, 4)); th.SetStylebox("hover", "PopupMenu", Box(new Color(24 / 255f, 56 / 255f, 110 / 255f, 1f), Accent, 1, 4));
        th.SetColor("font_color", "PopupMenu", Text); th.SetColor("font_hover_color", "PopupMenu", Text); th.SetColor("font_separator_color", "PopupMenu", Cyan);
        th.SetStylebox("normal", "LineEdit", Box(NavyDeep, Cyan, 1, 6)); th.SetStylebox("focus", "LineEdit", Box(NavyDeep, Accent, 1, 6)); th.SetStylebox("read_only", "LineEdit", Box(NavyDeep, Line40, 1, 6));
        th.SetColor("font_color", "LineEdit", Text); th.SetColor("caret_color", "LineEdit", Accent); th.SetColor("selection_color", "LineEdit", new Color(Accent, 0.4f));
        th.SetStylebox("up_background", "SpinBox", Box(Navy, Cyan, 1, 0)); th.SetStylebox("down_background", "SpinBox", Box(Navy, Cyan, 1, 0));
        th.SetStylebox("up_background_hovered", "SpinBox", Box(Navy, Accent, 1, 0)); th.SetStylebox("down_background_hovered", "SpinBox", Box(Navy, Accent, 1, 0));
        th.SetStylebox("background", "ProgressBar", Box(NavyDeep, Line40, 1, 0)); th.SetStylebox("fill", "ProgressBar", Box(Accent, Accent, 0, 0));
        th.SetColor("font_color", "ProgressBar", Text);
        th.SetStylebox("panel", "PanelContainer", Box(Navy, Cyan, 1, 12, 2));
        th.SetStylebox("panel", "TooltipPanel", Box(NavyDeep, Cyan, 1, 8)); th.SetColor("font_color", "TooltipLabel", Text);
        th.SetStylebox("panel", "ScrollContainer", new StyleBoxEmpty());
        return th;
    }

    Label Lbl(string text, int size = 13, Color? color = null, bool bold = false)
    {
        var l = new Label { Text = text }; l.AddThemeFontSizeOverride("font_size", size); l.AddThemeColorOverride("font_color", color ?? Text);
        if (bold) l.AddThemeColorOverride("font_outline_color", color ?? Text);
        return l;
    }
    Button Btn(string text, Action onPress, bool accent = false, string? icon = null)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(0, 34) };
        if (icon != null) { b.Icon = Icon(icon); b.AddThemeConstantOverride("icon_max_width", 20); }
        if (accent) { b.AddThemeStyleboxOverride("normal", Box(new Color(18 / 255f, 40 / 255f, 84 / 255f, 0.9f), Accent, 2, 8)); }
        b.Pressed += onPress; return b;
    }
    static PanelContainer Panel(Control inner, StyleBox? style = null) { var p = new PanelContainer(); if (style != null) p.AddThemeStyleboxOverride("panel", style); p.AddChild(inner); return p; }
    static Control Sep() { var h = new HSeparator(); h.AddThemeStyleboxOverride("separator", new StyleBoxLine { Color = Line40, Thickness = 1 }); return h; }
    HBoxContainer Chip(string icon, string text, Color border, string? tip = null)
    {
        var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 6);
        var i = new TextureRect { Texture = Icon(icon), CustomMinimumSize = new Vector2(22, 22), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered };
        h.AddChild(i); h.AddChild(Lbl(text, 12));
        var p = Panel(h, Box(NavyDeep, border, 1, 4)); p.TooltipText = tip ?? ""; var wrap = new HBoxContainer(); wrap.AddChild(p); return wrap;
    }

    Label _clockBig = null!, _powerText = null!, _status = null!;
    ProgressBar _powerBar = null!;
    readonly Dictionary<string, Label> _stockLabels = new();
    readonly Dictionary<Direction, Button> _dirButtons = new();
    GridContainer? _sideGrid;

    void BuildUi()
    {
        var layer = new CanvasLayer(); AddChild(layer); _uiLayer = layer;
        var root = new Control { Theme = MakeTheme(), MouseFilter = Control.MouseFilterEnum.Ignore }; root.SetAnchorsPreset(Control.LayoutPreset.FullRect); layer.AddChild(root);

        // ----- 上の帯 -----
        var top = new HBoxContainer(); top.AddThemeConstantOverride("separation", 20);
        var tb = new VBoxContainer(); tb.AddThemeConstantOverride("separation", 0);
        tb.AddChild(Lbl("平地ブロック", 11, Cyan)); _title = Lbl(Building.Name, 17); tb.AddChild(_title); top.AddChild(tb);
        top.AddChild(new VSeparator());
        var cb = new VBoxContainer(); cb.AddThemeConstantOverride("separation", 0);
        _clock = Lbl("", 11, Cyan); _clockBig = Lbl("", 17); cb.AddChild(_clock); cb.AddChild(_clockBig); top.AddChild(cb);
        top.AddChild(new VSeparator());
        var pb = new VBoxContainer { CustomMinimumSize = new Vector2(260, 0) }; pb.AddThemeConstantOverride("separation", 2);
        var ph = new HBoxContainer(); ph.AddChild(new TextureRect { Texture = Icon("reactor_g2"), CustomMinimumSize = new Vector2(20, 20), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered });
        ph.AddChild(Lbl("動力", 11, Cyan)); _powerText = Lbl("", 12); ph.AddChild(_powerText); pb.AddChild(ph);
        _powerBar = new ProgressBar { MinValue = 0, MaxValue = 100, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 8) }; pb.AddChild(_powerBar);
        _power = Lbl("", 11, Cyan); pb.AddChild(_power); top.AddChild(pb);
        _policy = new OptionButton(); foreach (var n in new[] { "不足時: 比例", "不足時: 優先順位", "不足時: 全停止" }) _policy.AddItem(n);
        _policy.ItemSelected += i => { _sim.Enqueue(new SetPolicy(1, (PowerPolicy)(int)i)); _sim.Step(); };
        top.AddChild(_policy);
        top.AddChild(new VSeparator());
        foreach (var (n, v) in new[] { ("停止", 0), ("1x", 1), ("2x", 2), ("4x", 4) }) { var b = Btn(n, () => SetSpeed(v)); b.CustomMinimumSize = new Vector2(48, 34); _speedButtons.Add(b); top.AddChild(b); }
        top.AddChild(new VSeparator());
        top.AddChild(Btn("取り消し", Undo)); top.AddChild(Btn("やり直し", Redo));
        top.AddChild(new VSeparator()); top.AddChild(Lbl("階", 11, Cyan));
        for (int f = 0; f < Building.Floors.Count; f++) { int ff = f; var b = Btn($"{f + 1}", () => SetFloor(ff)); b.CustomMinimumSize = new Vector2(36, 34); _floorButtons[f] = b; top.AddChild(b); }
        var topPanel = Panel(top); topPanel.Position = new Vector2(16, 16); root.AddChild(topPanel);
        SetSpeed(1);

        // ----- 左: 設備のパレット(種別ごとの格子) -----
        var pal = new VBoxContainer(); pal.AddThemeConstantOverride("separation", 8);
        pal.AddChild(Lbl("設備", 11, Cyan));
        var groups = new (string name, string[] kinds)[] { ("生産", new[] { "miner", "furnace", "assembler", "mech_assembler" }), ("物流", new[] { "belt", "inserter", "chest", "port", "trade" }), ("流体", new[] { "pump", "pipe", "tank" }), ("動力", new[] { "reactor", "battery" }), ("制御", new[] { "mcu" }) };
        foreach (var (gname, kinds) in groups)
        {
            pal.AddChild(Lbl(gname, 11, Line40));
            var grid = new GridContainer { Columns = 3 }; grid.AddThemeConstantOverride("h_separation", 6); grid.AddThemeConstantOverride("v_separation", 6);
            foreach (var def in _data.Structures.Values.Where(d => kinds.Contains(d.Kind)).OrderBy(d => d.TechLevel).ThenBy(d => d.Generation).ThenBy(d => d.Id))
            {
                string id = def.Id;
                var b = new Button { Icon = Icon(id), Text = def.Kind == "reactor" ? $"リアクター\n第 {def.Generation} 世代" : def.Name.Replace("(", "\n("), IconAlignment = HorizontalAlignment.Center, VerticalIconAlignment = VerticalAlignment.Top, ExpandIcon = true, CustomMinimumSize = new Vector2(92, 96), ClipText = true };
                b.AddThemeFontSizeOverride("font_size", 11); b.AddThemeConstantOverride("icon_max_width", 40);
                b.TooltipText = $"{def.Name}  {def.W}x{def.D}\n消費 {def.Power} kW  要員 {def.Workers}  技術 L{def.TechLevel}" + (def.Kind == "reactor" ? $"\n第 {def.Generation} 世代  上限 {def.Output} kW" : "");
                b.Pressed += () => SetPalette(id);
                var stock = Lbl("", 10, Cyan); stock.SetAnchorsPreset(Control.LayoutPreset.TopRight); stock.OffsetLeft = -40; stock.OffsetRight = -6; stock.OffsetTop = 4; stock.HorizontalAlignment = HorizontalAlignment.Right; stock.MouseFilter = Control.MouseFilterEnum.Ignore;
                b.AddChild(stock); _stockLabels[id] = stock; _paletteButtons[id] = b; grid.AddChild(b);
            }
            pal.AddChild(grid);
        }
        pal.AddChild(Sep());
        var dirRow = new HBoxContainer(); dirRow.AddThemeConstantOverride("separation", 4); dirRow.AddChild(Lbl("向き", 11, Cyan));
        foreach (var (d, n) in new[] { (Direction.East, "東"), (Direction.North, "北"), (Direction.West, "西"), (Direction.South, "南"), (Direction.Up, "上"), (Direction.Down, "下") })
        { var dd = d; var b = Btn(n, () => SetDir(dd)); b.CustomMinimumSize = new Vector2(36, 30); _dirButtons[d] = b; dirRow.AddChild(b); }
        pal.AddChild(dirRow); SetDir(_dir);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(330, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; scroll.AddChild(pal);
        var palPanel = Panel(scroll); palPanel.Position = new Vector2(16, 92); palPanel.AnchorBottom = 1; palPanel.OffsetBottom = -104; palPanel.OffsetTop = 92; palPanel.CustomMinimumSize = new Vector2(0, 0);
        root.AddChild(palPanel);

        // ----- 右: 選択した設備 -----
        _side = new VBoxContainer { CustomMinimumSize = new Vector2(340, 0) }; _side.AddThemeConstantOverride("separation", 8);
        var sidePanel = Panel(_side); sidePanel.AnchorLeft = 1; sidePanel.AnchorRight = 1; sidePanel.OffsetLeft = -392; sidePanel.OffsetRight = -16; sidePanel.OffsetTop = 92; root.AddChild(sidePanel);
        _sidePanel = sidePanel;

        // ----- 範囲の操作(左の板の下) -----
        var ops = new HBoxContainer(); ops.AddThemeConstantOverride("separation", 6);
        ops.AddChild(Btn("範囲選択", () => SetMode(Mode.Region)));
        ops.AddChild(Btn("コピー", () => { if (_region is var (x0, z0, x1, z1)) { _blueprint = _sim.Factory.NextBlueprintId; _sim.Enqueue(new CopyRegion(1, _floor, x0, z0, x1, z1, _blueprint, $"範囲 {x1 - x0 + 1}x{z1 - z0 + 1}")); _sim.Step(); _status.Text = $"コピー  設計図 #{_blueprint}"; } }));
        ops.AddChild(Btn("貼り付け", () => { if (_blueprint >= 0) SetMode(Mode.Paste); }));
        ops.AddChild(Btn("引っ越し", () => { if (_region != null) SetMode(Mode.Move); }));
        var opsPanel = Panel(ops); opsPanel.AnchorTop = 1; opsPanel.AnchorBottom = 1; opsPanel.OffsetTop = -96; opsPanel.OffsetBottom = -60; opsPanel.OffsetLeft = 16; root.AddChild(opsPanel);
        // ----- 下: 状態 -----
        _status = Lbl("", 12);
        var st = new HBoxContainer(); st.AddThemeConstantOverride("separation", 10); st.AddChild(new TextureRect { Texture = Icon("assembler"), CustomMinimumSize = new Vector2(20, 20), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered }); st.AddChild(_status);
        var stPanel = Panel(st); stPanel.AnchorTop = 1; stPanel.AnchorBottom = 1; stPanel.OffsetTop = -44; stPanel.OffsetBottom = -16; stPanel.OffsetLeft = 16; root.AddChild(stPanel);
        _dirLabel = Lbl("", 1);   // 旧 UI との互換(未使用)
    }

    void SetDir(Direction d)
    {
        _dir = d;
        foreach (var (k, b) in _dirButtons) b.AddThemeStyleboxOverride("normal", k == d ? Box(new Color(18 / 255f, 40 / 255f, 84 / 255f, 0.9f), Accent, 2, 6) : Box(Navy, Cyan, 1, 6));
    }

    static string DirName(Direction d) => d switch { Direction.East => "東 (+x)", Direction.North => "北 (+y)", Direction.West => "西 (-x)", Direction.South => "南 (-y)", Direction.Up => "上", _ => "下" };

    void SetPalette(string? id)
    {
        _palette = id;
        foreach (var (k, b) in _paletteButtons) b.AddThemeStyleboxOverride("normal", k == id ? Box(new Color(18 / 255f, 40 / 255f, 84 / 255f, 0.9f), Accent, 2, 6) : Box(Navy, Cyan, 1, 6));
    }

    void SetSpeed(int v)
    {
        _speed = v;
        int[] vals = { 0, 1, 2, 4 };
        for (int i = 0; i < _speedButtons.Count; i++) _speedButtons[i].AddThemeStyleboxOverride("normal", vals[i] == v ? Box(new Color(18 / 255f, 40 / 255f, 84 / 255f, 0.9f), Accent, 2, 8) : Box(Navy, Cyan, 1, 8));
    }

    void RefreshTop()
    {
        var (y, d, h, mi) = Clock.Of(_sim.Tick);
        _clock.Text = $"{y} 年目  {d} 日"; _clockBig.Text = $"{h:00}:{mi:00}";
        var b = Building;
        _powerText.Text = $"{b.LastSupply} / {b.LastDemand} kW";
        _powerBar.MaxValue = Math.Max(1, Math.Max(b.LastSupply, b.LastDemand)); _powerBar.Value = Math.Min(b.LastSupply, b.LastDemand);
        _powerBar.AddThemeStyleboxOverride("fill", Box(b.LastSupply >= b.LastDemand ? Accent : Warm, Color.Color8(0, 0, 0, 0), 0, 0));
        _power.Text = $"供給 {b.LastSupply} kW  需要 {b.LastDemand} kW  リアクター {b.Floors.Sum(f => f.Structures.Count(s => s is Reactor))} 基";
        foreach (var (id, l) in _stockLabels) l.Text = b.Inventory.GetValueOrDefault(id).ToString();
        var made = Floor.Structures.OfType<Machine>().Sum(m => m.Made); var failed = Floor.Structures.OfType<Machine>().Sum(m => m.Failed);
        long stored = Floor.Structures.OfType<Battery>().Sum(x => x.Stored);
        _status.Text = $"国庫 {_sim.Factory.Treasury:N0}   機体 {_sim.Factory.Mechs.Count} 機   設備 {Floor.Structures.Count} 台   在庫 {b.Inventory.Values.Sum()} 台   製造 {made}   不良 {failed}   蓄電 {stored / 216000.0:F2} kWh   {(_palette == null ? "" : _data.Structures[_palette].Name + " を設置中  " + DirName(_dir))}   {_mode switch { Mode.Region => _regionA == null ? "範囲選択: 1 つ目の角をクリック" : "範囲選択: 2 つ目の角をクリック", Mode.Paste => $"貼り付け: 左上の位置をクリック(設計図 #{_blueprint})", Mode.Move => "引っ越し: 移動先の左上をクリック(階を変えてもよい)", Mode.Link => "接続: 相手の口をクリック(階を変えてもよい)", _ => _region is var (rx0, rz0, rx1, rz1) ? $"範囲 ({rx0},{rz0})-({rx1},{rz1})" : "" }}   {_lastEvent}";
    }

    int _seenEvents; string _lastEvent = "";
    int _sideFor = -2; long _sideTick = -1;
    void RefreshSide()
    {
        if (_sim.Tick - _sideTick < 10 && _sideFor == _selected) return;   // 6 回/秒で更新
        _sideTick = _sim.Tick; _sideFor = _selected;
        foreach (var c in _side.GetChildren()) c.QueueFree();
        var s = Building.Find(_floor, _selected);
        if (s == null)
        {
            _side.AddChild(Lbl("選択なし", 11, Cyan));
            if (_sim.Factory.Mechs.Count > 0)
            {
                _side.AddChild(Sep()); _side.AddChild(Lbl($"製造した機体  {_sim.Factory.Mechs.Count} 機", 10, Cyan));
                foreach (var mech in _sim.Factory.Mechs.OrderByDescending(x => x.Id).Take(8))
                {
                    var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
                    var arch = mech.Archetype switch { "heavy" => "重装", "fast" => "高速", _ => "汎用" };
                    row.AddChild(Chip("mech_torso", $"#{mech.Id}  第 {mech.Generation} 世代  {arch}", ReactorColor(mech.Generation)));
                    row.AddChild(Lbl($"品質 {mech.Quality}  {(mech.PowerSource == "reactor" ? "リアクター" : "バッテリー")}", 12, TierColor(Quality.TierOf(mech.Quality))));
                    _side.AddChild(row);
                }
            }
            return;
        }
        var def = _data.Structures[s.DefId];
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 10);
        head.AddChild(new TextureRect { Texture = Icon(def.Id), CustomMinimumSize = new Vector2(48, 48), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered });
        var hv = new VBoxContainer(); hv.AddThemeConstantOverride("separation", 0); hv.AddChild(Lbl(s.Label == "" ? def.Name : $"{s.Label}  ({def.Name})", 17)); hv.AddChild(Lbl($"#{s.Id}  ({s.X}, {s.Y})  {DirName(s.Dir)}  {def.W}x{def.D}", 11, Cyan)); head.AddChild(hv);
        _side.AddChild(head);
        var lr = new HBoxContainer(); lr.AddThemeConstantOverride("separation", 8); lr.AddChild(Lbl("名札", 11, Cyan));
        var le = new LineEdit { Text = s.Label, PlaceholderText = "例: 炉心 A", CustomMinimumSize = new Vector2(160, 30), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        int lid = s.Id; le.TextSubmitted += t => { _sim.Enqueue(new SetLabel(1, _floor, lid, t)); _sim.Step(); _structureVersion++; _sideTick = -1; };
        lr.AddChild(le); lr.AddChild(Btn("付ける", () => { _sim.Enqueue(new SetLabel(1, _floor, lid, le.Text)); _sim.Step(); _structureVersion++; _sideTick = -1; })); _side.AddChild(lr);
        _side.AddChild(Sep());
        var g = new GridContainer { Columns = 3 }; g.AddThemeConstantOverride("h_separation", 14);
        void Stat(string k, string v, Color? c = null) { var vb = new VBoxContainer(); vb.AddThemeConstantOverride("separation", 0); vb.AddChild(Lbl(k, 10, Cyan)); vb.AddChild(Lbl(v, 15, c)); g.AddChild(vb); }
        Stat("消費", $"{def.Power} kW"); Stat("動力", $"{s.PowerPct}%", s.PowerPct >= 100 ? Green : Warm); Stat("要員", def.Workers.ToString());
        _side.AddChild(g);
        switch (s)
        {
            case Machine m:
            {
                _side.AddChild(Sep()); _side.AddChild(Lbl("レシピ", 10, Cyan));
                var opt = new OptionButton(); opt.AddThemeConstantOverride("icon_max_width", 24);
                var recipes = _data.Recipes.Values.Where(r => r.Machine == def.Kind).OrderBy(r => r.TechLevel).ThenBy(r => r.Id).ToList();
                opt.AddItem("レシピなし"); for (int i = 0; i < recipes.Count; i++) { opt.AddIconItem(Icon(recipes[i].Outputs.Count > 0 ? recipes[i].Outputs[0].Item : "mech_torso"), $"L{recipes[i].TechLevel}  {recipes[i].Name}"); if (recipes[i].Id == m.Recipe) opt.Selected = i + 1; }
                int sid = s.Id; opt.ItemSelected += i => { _sim.Enqueue(new SetRecipe(1, _floor, sid, i == 0 ? null : recipes[(int)i - 1].Id)); _sim.Step(); _sideTick = -1; };
                _side.AddChild(opt);
                if (def.Kind == "mech_assembler")
                {
                    var ar2 = new HBoxContainer(); ar2.AddThemeConstantOverride("separation", 8); ar2.AddChild(Lbl("系統", 11, Cyan));
                    var ao = new OptionButton(); var archs = new[] { ("heavy", "重装"), ("fast", "高速"), ("general", "汎用") };
                    for (int i = 0; i < archs.Length; i++) { ao.AddItem(archs[i].Item2); if (archs[i].Item1 == m.Archetype) ao.Selected = i; }
                    ao.ItemSelected += i => { _sim.Enqueue(new SetArchetype(1, _floor, sid, archs[(int)i].Item1)); _sim.Step(); _sideTick = -1; };
                    ar2.AddChild(ao); _side.AddChild(ar2);
                }
                if (m.Recipe != null)
                {
                    var r = _data.Recipes[m.Recipe];
                    var row = new HFlowContainer(); row.AddThemeConstantOverride("h_separation", 6); row.AddThemeConstantOverride("v_separation", 6);
                    foreach (var i in r.Inputs) row.AddChild(Chip(i.Item, $"{_data.Items[i.Item].Name} ×{i.Count}", TierColor(i.MinTier), $"品質 {TierName(i.MinTier)} 以上"));
                    row.AddChild(Lbl("→", 14, Cyan));
                    foreach (var o in r.Outputs) row.AddChild(Chip(o.Item, $"{_data.Items[o.Item].Name} ×{o.Count}", Accent));
                    if (r.MechGeneration > 0) row.AddChild(Chip("mech_torso", $"機体 第 {r.MechGeneration} 世代", ReactorColor(r.MechGeneration)));
                    _side.AddChild(row);
                    _side.AddChild(Lbl($"所要 {r.Ticks} ティック ({r.Ticks / 60.0:F1} 秒)   失敗時の回収 {r.ScrapRate / 100}%", 11, Cyan));
                    var prog = new ProgressBar { MinValue = 0, MaxValue = r.Ticks * 100, Value = Math.Max(0, m.Progress), ShowPercentage = false, CustomMinimumSize = new Vector2(0, 10) };
                    _side.AddChild(prog);
                }
                var g2 = new GridContainer { Columns = 3 }; g2.AddThemeConstantOverride("h_separation", 14);
                void Stat2(string k, string v, Color? c = null) { var vb = new VBoxContainer(); vb.AddThemeConstantOverride("separation", 0); vb.AddChild(Lbl(k, 10, Cyan)); vb.AddChild(Lbl(v, 15, c)); g2.AddChild(vb); }
                Stat2("状態", m.Recipe == null ? "レシピなし" : m.Staff.Count < def.Workers ? "要員不足" : m.Progress < 0 ? "入力待ち" : "稼働", m.Recipe != null && m.Staff.Count >= def.Workers && m.Progress >= 0 ? Green : Warm);
                Stat2("製造", m.Made.ToString(), Green); Stat2("不良", m.Failed.ToString(), m.Failed > 0 ? Warm : null);
                _side.AddChild(g2);
                if (m.Inputs.Count > 0 || m.Outputs.Count > 0)
                {
                    var inv = new HBoxContainer(); inv.AddThemeConstantOverride("separation", 6);
                    foreach (var grp in m.Inputs.GroupBy(i => i.Item)) inv.AddChild(Chip(grp.Key, $"×{grp.Sum(i => i.Count)}", Line40, "入力の在庫"));
                    if (m.Outputs.Count > 0) { inv.AddChild(Lbl("|", 12, Line40)); foreach (var grp in m.Outputs.GroupBy(i => (i.Item, i.Tier))) inv.AddChild(Chip(grp.Key.Item, $"×{grp.Sum(i => i.Count)}", TierColor(grp.Key.Tier), $"出力  品質 {TierName(grp.Key.Tier)}")); }
                    _side.AddChild(inv);
                }
                _side.AddChild(Sep()); _side.AddChild(Lbl($"要員  {m.Staff.Count} / {def.Workers}", 10, Cyan));
                if (m.Staff.Count > 0)
                {
                    var sr = new HBoxContainer(); sr.AddThemeConstantOverride("separation", 6);
                    foreach (var w in m.Staff)
                    {
                        var chip = Chip(w.IsRobot ? "factory_robot" : "mech_head", $"{w.Name}  技能 {w.Skill}", w.IsRobot ? Accent : Line40);
                        int wid = w.Id; var x = Btn("外す", () => { _sim.Enqueue(new RemoveStaff(1, _floor, sid, wid, NearestChest(m)?.Id ?? -1)); _sim.Step(); _sideTick = -1; }); x.CustomMinimumSize = new Vector2(0, 26);
                        chip.AddChild(x); sr.AddChild(chip);
                    }
                    _side.AddChild(sr);
                }
                var ar = new HBoxContainer(); ar.AddThemeConstantOverride("separation", 8);
                var skill = new SpinBox { MinValue = 0, MaxValue = 10, Value = 3, CustomMinimumSize = new Vector2(90, 34) };
                ar.AddChild(Lbl("技能", 11, Cyan)); ar.AddChild(skill);
                ar.AddChild(Btn("要員を割り当て", () => { _sim.Enqueue(new AssignStaff(1, _floor, sid, new Worker(m.Staff.Count + 1, $"作業員 {m.Staff.Count + 1}", (int)skill.Value))); _sim.Step(); _sideTick = -1; }, true));
                _side.AddChild(ar);
                var robotChest = Floor.Structures.OfType<Chest>().Where(c => c.Items.Any(i => i.Item == "factory_robot")).OrderBy(c => Math.Abs(c.X - m.X) + Math.Abs(c.Y - m.Y)).FirstOrDefault();
                if (robotChest != null) _side.AddChild(Btn($"製造ロボットを割り当て(箱 ({robotChest.X}, {robotChest.Y}) から)", () => { _sim.Enqueue(new AssignRobot(1, _floor, sid, robotChest.Id)); _sim.Step(); _sideTick = -1; }, true, "factory_robot"));
                break;
            }
            case Reactor r:
            {
                _side.AddChild(Sep());
                var g3 = new GridContainer { Columns = 3 }; g3.AddThemeConstantOverride("h_separation", 14);
                void S3(string k, string v, Color? c = null) { var vb = new VBoxContainer(); vb.AddThemeConstantOverride("separation", 0); vb.AddChild(Lbl(k, 10, Cyan)); vb.AddChild(Lbl(v, 15, c)); g3.AddChild(vb); }
                S3("世代", $"第 {def.Generation}", ReactorColor(def.Generation)); S3("品質", r.Quality.ToString(), TierColor(Quality.TierOf(r.Quality))); S3("出力", $"{Building.ReactorOutput(def, r.Quality, _data)} / {def.Output} kW", Green);
                _side.AddChild(g3);
                var pr = new ProgressBar { MinValue = 0, MaxValue = def.Output * _data.OverloadMax / 100, Value = Building.ReactorOutput(def, r.Quality, _data) * r.OutputPct / 100, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 10) };
                pr.AddThemeStyleboxOverride("fill", Box(r.OutputPct > 100 ? Warm : ReactorColor(def.Generation), Color.Color8(0, 0, 0, 0), 0, 0)); _side.AddChild(pr);
                // 出力設定(0〜150%)と過負荷
                var orow = new HBoxContainer(); orow.AddThemeConstantOverride("separation", 8);
                orow.AddChild(Lbl("出力設定", 11, Cyan));
                var slider = new HSlider { MinValue = 0, MaxValue = _data.OverloadMax, Step = 5, Value = r.OutputPct, CustomMinimumSize = new Vector2(150, 24), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                var pctLbl = Lbl($"{r.OutputPct}%", 15, r.OutputPct > 100 ? Warm : null);
                int rid = s.Id; slider.ValueChanged += v => { pctLbl.Text = $"{(int)v}%"; _sim.Enqueue(new SetReactorOutput(1, _floor, rid, (int)v)); _sim.Step(); _sideTick = -1; };
                orow.AddChild(slider); orow.AddChild(pctLbl); _side.AddChild(orow);
                var lp = new ProgressBar { MinValue = 0, MaxValue = _data.OverloadLimit, Value = r.Load, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 8) };
                lp.AddThemeStyleboxOverride("fill", Box(new Color(1f, 0.3f, 0.2f), Color.Color8(0, 0, 0, 0), 0, 0)); _side.AddChild(lp);
                string loadText = r.Load == 0 ? "なし" : r.OutputPct > 100 ? $"{r.Load * 100.0 / _data.OverloadLimit:F1}%  爆発まで {(_data.OverloadLimit - r.Load) / (r.OutputPct - 100) / 60.0:F0} 秒" : r.OutputPct < 100 ? $"{r.Load * 100.0 / _data.OverloadLimit:F1}%  回復中" : $"{r.Load * 100.0 / _data.OverloadLimit:F1}%  100% では回復しない";
                _side.AddChild(Lbl("過負荷  " + loadText, 11, r.Load > 0 ? Warm : Cyan));
                _side.AddChild(Lbl($"爆発の半径 {Building.ReactorOutput(def, r.Quality, _data) * r.OutputPct / 100 * _data.BlastRadiusPerKw:F1} セル(面積は発電量の 2 乗に比例)", 11, Cyan));
                break;
            }
            case Chest c:
            {
                _side.AddChild(Sep()); _side.AddChild(Lbl($"在庫  {c.Items.Count} / {def.Capacity}", 10, Cyan));
                var pr = new ProgressBar { MinValue = 0, MaxValue = def.Capacity, Value = c.Items.Count, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 8) }; _side.AddChild(pr);
                var inv = new GridContainer { Columns = 2 }; inv.AddThemeConstantOverride("h_separation", 6); inv.AddThemeConstantOverride("v_separation", 6);
                foreach (var grp in c.Items.GroupBy(i => (i.Item, i.Tier)).OrderBy(g => g.Key.Item).ThenBy(g => g.Key.Tier)) inv.AddChild(Chip(grp.Key.Item, $"{_data.Items[grp.Key.Item].Name} ×{grp.Count()}", TierColor(grp.Key.Tier), $"品質 {TierName(grp.Key.Tier)}"));
                _side.AddChild(inv);
                break;
            }
            case Belt b:
            {
                _side.AddChild(Sep()); var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 6);
                for (int i = 0; i < 4; i++) row.AddChild(b.Slots[i] is { } it ? Chip(it.Item, _data.Items[it.Item].Name, TierColor(it.Tier)) : Chip("belt", "-", Line40));
                _side.AddChild(row); break;
            }
            case Miner mi:
            {
                _side.AddChild(Sep()); var ore = Floor.OreAt(mi.X, mi.Y);
                if (ore == null) _side.AddChild(Lbl("鉱脈なし", 13, Warm));
                else { var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); row.AddChild(Chip(ore.Item, _data.Items[ore.Item].Name, TierColor(Quality.TierOf(ore.Quality)))); row.AddChild(Lbl($"残り {ore.Amount:N0}   品質 {ore.Quality}", 13)); _side.AddChild(row); }
                if (mi.Output is { } o) _side.AddChild(Chip(o.Item, "出口に 1 個", TierColor(o.Tier)));
                break;
            }
            case Tank t:
            {
                _side.AddChild(Sep());
                _side.AddChild(t.Fluid == null ? Lbl("空", 13, Line40) : Chip(t.Fluid, $"{_data.Items[t.Fluid].Name}  {t.Amount} / {def.Capacity}", Accent));
                var pr = new ProgressBar { MinValue = 0, MaxValue = def.Capacity, Value = t.Amount, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 8) }; _side.AddChild(pr);
                break;
            }
            case Battery bt:
            {
                _side.AddChild(Sep());
                var bd = def;
                var g4 = new GridContainer { Columns = 3 }; g4.AddThemeConstantOverride("h_separation", 14);
                void S4(string k, string v, Color? c = null) { var vb = new VBoxContainer(); vb.AddThemeConstantOverride("separation", 0); vb.AddChild(Lbl(k, 10, Cyan)); vb.AddChild(Lbl(v, 15, c)); g4.AddChild(vb); }
                S4("蓄電", $"{bt.Stored / 216000.0:F2} / {bd.Capacity / 216000.0:F1} kWh", Accent); S4("充電", $"{bd.ChargeRate} kW"); S4("放電", $"{bd.DischargeRate} kW");
                _side.AddChild(g4);
                var pr = new ProgressBar { MinValue = 0, MaxValue = bd.Capacity, Value = bt.Stored, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 10) }; _side.AddChild(pr);
                _side.AddChild(Lbl("方式", 10, Cyan));
                var mode = new OptionButton(); foreach (var n in new[] { "ピーク用(不足時に放電)", "非常用(リアクター停止時だけ)", "停止" }) mode.AddItem(n); mode.Selected = (int)bt.Mode;
                int bid = s.Id; mode.ItemSelected += i => { _sim.Enqueue(new SetBatteryMode(1, _floor, bid, (BatteryMode)(int)i)); _sim.Step(); _sideTick = -1; };
                _side.AddChild(mode);
                break;
            }
            case Trade td:
            {
                _side.AddChild(Sep());
                var g5 = new GridContainer { Columns = 3 }; g5.AddThemeConstantOverride("h_separation", 14);
                void S5(string k, string v, Color? c = null) { var vb = new VBoxContainer(); vb.AddThemeConstantOverride("separation", 0); vb.AddChild(Lbl(k, 10, Cyan)); vb.AddChild(Lbl(v, 15, c)); g5.AddChild(vb); }
                S5("輸出", td.Sold.ToString(), Green); S5("輸入", td.Bought.ToString(), Accent); S5("国庫", _sim.Factory.Treasury.ToString("N0"));
                _side.AddChild(g5);
                _side.AddChild(Lbl("輸入する品(入った品は売る)", 10, Cyan));
                var opt = new OptionButton(); opt.AddThemeConstantOverride("icon_max_width", 24);
                var its = _data.Items.Values.Where(i => !i.Fluid).OrderBy(i => i.TechLevel).ThenBy(i => i.Id).ToList();
                opt.AddItem("輸出のみ"); for (int i = 0; i < its.Count; i++) { opt.AddIconItem(Icon(its[i].Id), $"{its[i].Name}  {its[i].Price * _data.ImportMul / 100}"); if (its[i].Id == td.Import) opt.Selected = i + 1; }
                int tid = s.Id; opt.ItemSelected += i => { _sim.Enqueue(new SetImport(1, _floor, tid, i == 0 ? null : its[(int)i - 1].Id)); _sim.Step(); _sideTick = -1; };
                _side.AddChild(opt);
                if (td.Outbox.Count > 0) _side.AddChild(Chip(td.Outbox[0].Item, $"出口に {td.Outbox.Count} 個", Accent));
                break;
            }
            case Mcu mc:
            {
                _side.AddChild(Sep());
                _side.AddChild(Lbl($"プログラム  {(mc.Program.Name == "" ? "(無題)" : mc.Program.Name)}   ノード {mc.Program.Nodes.Count}  線 {mc.Program.Links.Count}  間隔 {mc.Program.Interval}", 12));
                _side.AddChild(Lbl($"評価 {mc.Evaluations:N0} 回   {(Building.LastSupply > 0 ? "動作中" : "電気なし")}", 12, Building.LastSupply > 0 ? Green : Warm));
                var en = new CheckButton { Text = "稼働", ButtonPressed = mc.Enabled }; int mid = s.Id; en.Toggled += v => { _sim.Enqueue(new SetEnabled(1, _floor, mid, v)); _sim.Step(); }; _side.AddChild(en);
                _side.AddChild(Btn("プログラムを編集(図式)", () => OpenMcu(mid), true));
                break;
            }
            case Port p:
                _side.AddChild(Sep());
                _side.AddChild(Btn(def.Id.Contains("lift") ? "接続先の口を選ぶ(別の階へ)" : "接続先の口を選ぶ", () => { _linkFrom = (_floor, p.Id); SetMode(Mode.Link); }, true));
                _side.AddChild(Lbl(p.LinkBuilding < 0 ? "未接続" : $"接続先  建物 {p.LinkBuilding}  階 {p.LinkFloor + 1}  {(def.Id.Contains("lift") ? (p.LinkFloor > 0 ? "上り" : "下り") : "")}   待ち {p.Buffer.Count}  到着 {p.Inbox.Count}", 13, p.LinkBuilding < 0 ? Warm : null));
                break;
        }
        _side.AddChild(Sep());
        var rm = Btn("撤去", RemoveSelected); rm.AddThemeStyleboxOverride("normal", Box(Navy, Warm, 1, 8)); _side.AddChild(rm);
    }

    Chest? NearestChest(Structure s) => Floor.Structures.OfType<Chest>().OrderBy(c => Math.Abs(c.X - s.X) + Math.Abs(c.Y - s.Y)).FirstOrDefault();
    static string TierName(QualityTier t) => t switch { QualityTier.Defect => "不良", QualityTier.Normal => "通常", QualityTier.High => "高", _ => "最高" };

    // ---------- 動作確認(無人): --demo で見本の生産線を置き、--screenshot で保存 ----------
    static string? Arg(string name) { var a = OS.GetCmdlineUserArgs(); int i = Array.IndexOf(a, name); return i < 0 ? null : (i + 1 < a.Length ? a[i + 1] : ""); }

    void Demo()
    {
        var cmds = new List<ICommand>();
        int y0 = D - 21;   // 鉱脈のすぐ南
        for (int x = 0; x < 12; x++) { cmds.Add(new PlaceStructure(1, _floor, "miner", x, D - 20, 0, Direction.South)); cmds.Add(new PlaceStructure(1, _floor, "inserter", x, y0, 0, Direction.South)); cmds.Add(new PlaceStructure(1, _floor, "belt", x, y0 - 1, 0, Direction.East)); }
        for (int x = 12; x < 20; x++) cmds.Add(new PlaceStructure(1, _floor, "belt", x, y0 - 1, 0, Direction.East));
        for (int x = 2; x < 20; x += 3)
        {
            cmds.Add(new PlaceStructure(1, _floor, "inserter", x, y0 - 2, 0, Direction.South));
            cmds.Add(new PlaceStructure(1, _floor, "furnace", x, y0 - 3, 0, Direction.South));
            cmds.Add(new PlaceStructure(1, _floor, "inserter", x, y0 - 4, 0, Direction.South));
            cmds.Add(new PlaceStructure(1, _floor, "chest", x, y0 - 5, 0, Direction.South));
        }
        cmds.Add(new PlaceStructure(1, _floor, "reactor_g2", 22, y0 - 3, 0, Direction.East)); cmds.Add(new PlaceStructure(1, _floor, "reactor_g2", 24, y0 - 3, 0, Direction.East)); cmds.Add(new PlaceStructure(1, _floor, "battery_g2", 22, y0 - 7, 0, Direction.East)); cmds.Add(new PlaceStructure(1, _floor, "trade", 26, y0 - 5, 0, Direction.East)); cmds.Add(new AddTreasury(5000));
        // 昇降機の口: 上り(2 階へ)と下り(地下は無いので 2 階側から見た下り = 1 階の口を 2 階から結ぶ)。1 階に上り 1 つ、未接続 1 つ、2 階と 3 階にも置いて 3 階→1 階を下りとして示す
        cmds.Add(new PlaceStructure(1, _floor, "port_lift", 30, y0 - 3, 0, Direction.South)); cmds.Add(new PlaceStructure(1, 1, "port_lift", 30, y0 - 3, 0, Direction.South));
        cmds.Add(new PlaceStructure(1, _floor, "port_lift", 32, y0 - 3, 0, Direction.South));
        cmds.Add(new PlaceStructure(1, _floor, "port_lift", 34, y0 - 3, 0, Direction.South));
        foreach (var c in cmds) _sim.Enqueue(c);
        _sim.Step();
        var lifts = Floor.Structures.OfType<Port>().OrderBy(p => p.X).ToList(); var up1 = Building.Floors[1].Structures.OfType<Port>().First();
        _sim.Enqueue(new LinkPorts(1, 0, lifts[0].Id, 1, 1, up1.Id));   // (30): 2 階へ = 上り
        _sim.Enqueue(new SetLiftDown(lifts[2].Id));                      // (34): 下り(見本用に接続先を -1 階相当として印だけ)
        int wid = 1;
        foreach (var m in Floor.Structures.OfType<Machine>()) { _sim.Enqueue(new SetRecipe(1, _floor, m.Id, "iron_plate")); _sim.Enqueue(new AssignStaff(1, _floor, m.Id, new Worker(wid, $"作業員 {wid}", 4))); wid++; }
        _sim.Step();
        var rc = Floor.Structures.OfType<Chest>().First(); _sim.Enqueue(new AddItems(1, _floor, rc.Id, "factory_robot", 85, 2)); _sim.Step();
        // 機体組立機(4x4)と部品の箱
        int mx = 40, my = y0 - 8;
        _sim.Enqueue(new PlaceStructure(1, _floor, "mech_assembler", mx, my, 0, Direction.South)); _sim.Enqueue(new PlaceStructure(1, _floor, "inserter", mx + 4, my + 1, 0, Direction.West)); _sim.Enqueue(new PlaceStructure(1, _floor, "chest", mx + 5, my + 1, 0, Direction.West)); _sim.Step();
        var ma = Floor.Structures.OfType<Machine>().First(x => _data.Structures[x.DefId].Kind == "mech_assembler"); var pc = (Chest)Floor.At(mx + 5, my + 1, 0)!;
        _sim.Enqueue(new SetRecipe(1, _floor, ma.Id, "mech_g2_reactor")); _sim.Enqueue(new SetArchetype(1, _floor, ma.Id, "heavy"));
        for (int i = 1; i <= 4; i++) _sim.Enqueue(new AssignStaff(1, _floor, ma.Id, new Worker(i, $"技師 {i}", 6)));
        foreach (var (it, n) in new[] { ("mech_head", 2), ("mech_torso", 2), ("mech_arm", 4), ("mech_leg", 4), ("mech_back", 2), ("mech_weapon", 2), ("reactor_core", 2) }) _sim.Enqueue(new AddItems(1, _floor, pc.Id, it, 72, n));
        _sim.Step();
        // 制御室: リアクターの過負荷を見張り、50% 超で出力設定 80%、未満で 150%(見本)
        _sim.Enqueue(new PlaceStructure(1, 0, "control_room", 28, y0 - 8, 0, Direction.South)); _sim.Step();
        var mcu = Floor.Structures.OfType<Mcu>().First(); var react = Floor.Structures.OfType<Reactor>().First();
        _sim.Enqueue(new SetReactorOutput(1, 0, react.Id, 150));
        _sim.Enqueue(new SetLabel(1, 0, react.Id, "炉心 A")); _sim.Enqueue(new SetLabel(1, 0, Floor.Structures.OfType<Reactor>().Skip(1).First().Id, "炉心 B")); _sim.Enqueue(new SetLabel(1, 0, mcu.Id, "中央制御室"));
        _sim.Enqueue(new SetProgram(1, 0, mcu.Id, new Program { Name = "過負荷の見張り", Interval = 6, Nodes = {
            new McuNode { Id = 1, Type = "in.reactor.load", TargetBuilding = 1, TargetFloor = 0, TargetId = react.Id, X = 40, Y = 60 },
            new McuNode { Id = 2, Type = "in.const", Params = { ["value"] = 50 }, X = 40, Y = 240 },
            new McuNode { Id = 3, Type = "op.gt", X = 800, Y = 120 },
            new McuNode { Id = 4, Type = "in.const", Params = { ["value"] = 80 }, X = 800, Y = 320 },
            new McuNode { Id = 5, Type = "in.const", Params = { ["value"] = 150 }, X = 800, Y = 470 },
            new McuNode { Id = 6, Type = "op.select", X = 1080, Y = 220 },
            new McuNode { Id = 7, Type = "out.reactor.setting", TargetBuilding = 1, TargetFloor = 0, TargetId = react.Id, X = 1360, Y = 220 } },
            Links = { new(1, 0, 3, 0), new(2, 0, 3, 1), new(3, 0, 6, 0), new(4, 0, 6, 1), new(5, 0, 6, 2), new(6, 0, 7, 0) } }));
        _sim.Enqueue(new CopyRegion(1, 0, 2, y0 - 5, 8, y0 - 2, 99, "見本"));
        _sim.Enqueue(new PasteBlueprint(1, 1, 10, 10, 99)); _sim.Enqueue(new PasteBlueprint(1, 1, 20, 10, 99));   // 2 階に生産線の一部を貼り付け
        _sim.Run(Arg("--select") == "mech" ? 60 * 9 + 4800 + 600 : 3600);   // 機体: 部品の搬入 + 組立 4,800 ティック
        _structureVersion++;
        _selected = Arg("--select") switch { "reactor" => Floor.Structures.First(s => s is Reactor).Id, "mech" => ma.Id, "mcu" => mcu.Id, "none" => -1, _ => Floor.Structures.First(s => s is Machine).Id };
        if (Arg("--select") == "mcu" && Arg("--open") != null) CallDeferred(nameof(OpenMcuDeferred), mcu.Id);
        _pivot = new Vector3(10, GroundY, y0 - 2); _dist = 28; _yaw = 0.5f; _elev = 0.7f;
        if (Arg("--floor") is string fs) SetFloor(int.Parse(fs) - 1);
        if (Arg("--pivot") is string pv) { var p = pv.Split(','); _pivot = new Vector3(float.Parse(p[0]), float.Parse(p[1]), float.Parse(p[2])); }
        if (Arg("--dist") is string ds) _dist = float.Parse(ds);
        if (Arg("--yaw") is string ys) _yaw = float.Parse(ys);
        if (Arg("--elev") is string es) _elev = float.Parse(es);
        UpdateCamera();
    }

    /// <summary>見本専用: 下りの口を示すために接続先を仮に下の階にする(計算上は未接続のまま)。</summary>
    sealed record SetLiftDown(int Id) : ICommand { public void Apply(Simulation s) { if (s.Factory.Get(1)?.Find(0, Id) is Port p) { p.LinkBuilding = 1; p.LinkFloor = -1; p.LinkPort = -1; } } }

    void OpenMcuDeferred(int id) => OpenMcu(id);

    void Screenshot()
    {
        var path = Arg("--screenshot"); if (path == null) return;
        if (++_frame == 40) { GetViewport().GetTexture().GetImage().SavePng(path); GetTree().Quit(); }
    }
}
