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
    const float FloorY = 3f;
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
    Vector3 _pivot = new(50, FloorY, 50);
    float _yaw = 0.6f, _elev = 0.9f, _dist = 60;
    int _speed = 1;
    string? _palette;
    Direction _dir = Direction.East;
    int _selected = -1;
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
        for (int i = 0; i <= W; i += 10) { _kb.Strip(this, new Vector3(i, FloorY + 0.02f, D / 2f), new Vector3(0.04f, 0.02f, D), _kb.GlowCyan); _kb.Strip(this, new Vector3(W / 2f, FloorY + 0.02f, i), new Vector3(W, 0.02f, 0.04f), _kb.GlowCyan); }
        _kb.Flush(this);
        _eqRoot = new Node3D { Name = "equipment" }; AddChild(_eqRoot);
        _itemsRoot = new Node3D { Name = "items" }; AddChild(_itemsRoot);
        _cursor = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.02f, 0.06f, 1.02f) }, MaterialOverride = _kb.GlowCyan, Visible = false }; AddChild(_cursor);
        _cam = new Camera3D { Far = 1000, Fov = 45 }; AddChild(_cam); UpdateCamera();

        // 工場: 平地ブロック 100x100、鉱脈は北西の 20x20、設備の在庫を支給
        _sim.Enqueue(new CreateBuilding(1, "平地ブロック A-1", W, D, 1, true));
        for (int x = 0; x < 20; x++) for (int y = D - 20; y < D; y++) _sim.Enqueue(new SetOre(1, x, y, (x + y) % 7 == 0 ? "copper_ore" : "iron_ore", 20_000_000, 30 + (x * 7 + y * 3) % 41));
        foreach (var def in _data.Structures.Values) _sim.Enqueue(new AddInventory(1, def.Id, 500));
        _sim.Step();
        BuildUi();
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
        var (y, d, h, mi) = Clock.Of(_sim.Tick);
        _clock.Text = $"{y} 年目  {d} 日  {h:00}:{mi:00}";
        var b = Building;
        _power.Text = $"動力  供給 {b.LastSupply} kW  需要 {b.LastDemand} kW  リアクター {Floor.Structures.Count(s => s is Reactor)} 基";
        RefreshSide();
        Screenshot();
    }

    Building Building => _sim.Factory.Get(1)!;
    Floor Floor => Building.Floors[0];

    void Apply(ICommand cmd, ICommand? undo)
    {
        _sim.Enqueue(cmd); _sim.Step();
        if (undo != null) { _undo.Push((undo, cmd)); _redo.Clear(); }
        _structureVersion++;
    }

    // ---------- 入力 ----------
    public override void _UnhandledInput(InputEvent e)
    {
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
                if (k.Keycode == Key.R) { _dir = (Direction)(((int)_dir + 1) % 6); _dirLabel.Text = DirName(_dir); }
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
        if (!place)
        {
            if (hit != null) Apply(new RemoveStructure(1, 0, hit.Id), new PlaceStructure(1, 0, hit.DefId, hit.X, hit.Y, hit.Z, hit.Dir));
            if (_selected == hit?.Id) _selected = -1;
            return;
        }
        if (_palette != null)
        {
            var def = _data.Structures[_palette];
            if (!Floor.Fits(def, x, z, 0) || Building.Inventory.GetValueOrDefault(_palette) <= 0) return;
            int id = Building.NextStructureId;
            Apply(new PlaceStructure(1, 0, _palette, x, z, 0, _dir), new RemoveStructure(1, 0, id));
            _selected = id;
            return;
        }
        _selected = hit?.Id ?? -1;
    }

    void RemoveSelected()
    {
        var s = Building.Find(0, _selected); if (s == null) return;
        Apply(new RemoveStructure(1, 0, s.Id), new PlaceStructure(1, 0, s.DefId, s.X, s.Y, s.Z, s.Dir));
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
    static string Model(StructureDef def) => def.Kind switch
    {
        "furnace" => "furnace", "assembler" => "assembler", "miner" => "miner", "belt" => "belt", "inserter" => "inserter", "chest" => "chest",
        "tank" => "tank", "reactor" => "tank", "port" => "chest", "pump" => "miner", _ => "pole",
    };

    void RebuildEquipment()
    {
        foreach (var c in _eqRoot.GetChildren()) c.QueueFree();
        foreach (var s in Floor.Structures)
        {
            var def = _data.Structures[s.DefId];
            string model = Model(def);
            if (def.Kind == "pipe") { _kb.Pipe(_eqRoot, new Vector3(s.X + 0.5f, FloorY + 0.3f, s.Y + 0.5f) - new Vector3(0.5f, 0, 0), new Vector3(s.X + 0.5f, FloorY + 0.3f, s.Y + 0.5f) + new Vector3(0.5f, 0, 0), 0.12f, _kb.Rust); continue; }
            if (def.W == 1 && def.D == 1) _eq.Place(model, s.X, FloorY, s.Y, Rot(s.Dir));
            else for (int i = 0; i < def.W; i++) for (int j = 0; j < def.D; j++) _eq.Place(model, s.X + i, FloorY, s.Y + j, Rot(s.Dir));
            if (s is Reactor r)
            {
                var col = ReactorColor(def.Generation);
                _eqRoot.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.35f, Height = 0.7f }, MaterialOverride = new StandardMaterial3D { AlbedoColor = col, EmissionEnabled = true, Emission = col, EmissionEnergyMultiplier = 2.5f }, Position = new Vector3(s.X + 0.5f, FloorY + 2.3f, s.Y + 0.5f) });
            }
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

    static Color ReactorColor(int gen) => gen switch { 0 => new Color(1f, 0.2f, 0.15f), 1 => new Color(1f, 0.5f, 0.1f), 2 => new Color(1f, 0.9f, 0.2f), 3 => new Color(0.6f, 1f, 0.2f), 4 => new Color(0.2f, 1f, 0.4f), 5 => new Color(0.25f, 0.5f, 1f), _ => new Color(0.75f, 0.35f, 1f) };
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

    // ---------- UI(G8 の様式) ----------
    static StyleBoxFlat PanelStyle(bool accent = false) => new()
    {
        BgColor = Navy, BorderColor = accent ? Accent : Cyan, BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 8, ContentMarginBottom = 8,   // 斜めの要素は使わない(2026-10-15 指示)
    };
    static Font JpFont() => new SystemFont { FontNames = new[] { "Yu Gothic UI", "Meiryo", "sans-serif" } };
    Label Lbl(string text, int size = 14, Color? color = null) { var l = new Label { Text = text }; l.AddThemeFontOverride("font", JpFont()); l.AddThemeFontSizeOverride("font_size", size); l.AddThemeColorOverride("font_color", color ?? Text); return l; }
    Button Btn(string text, Action onPress, bool accent = false)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(0, 32), Flat = false };
        b.AddThemeFontOverride("font", JpFont()); b.AddThemeFontSizeOverride("font_size", 13); b.AddThemeColorOverride("font_color", Text);
        var st = PanelStyle(accent); st.ContentMarginTop = 4; st.ContentMarginBottom = 4;
        b.AddThemeStyleboxOverride("normal", st); b.AddThemeStyleboxOverride("hover", PanelStyle(true)); b.AddThemeStyleboxOverride("pressed", PanelStyle(true)); b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        b.Pressed += onPress;
        return b;
    }
    PanelContainer Panel(Control inner, bool accent = false) { var p = new PanelContainer(); p.AddThemeStyleboxOverride("panel", PanelStyle(accent)); p.AddChild(inner); return p; }

    void BuildUi()
    {
        var ui = new CanvasLayer(); AddChild(ui);
        // 上の帯
        var top = new HBoxContainer { Position = new Vector2(16, 16) }; top.AddThemeConstantOverride("separation", 18);
        _title = Lbl(Building.Name, 15); _clock = Lbl("", 15, Cyan); _power = Lbl("", 13);
        top.AddChild(Lbl("平地ブロック", 12, Cyan)); top.AddChild(_title); top.AddChild(_clock); top.AddChild(_power);
        _policy = new OptionButton(); foreach (var (n, _) in new[] { ("不足時: 比例", 0), ("不足時: 優先順位", 1), ("不足時: 全停止", 2) }) _policy.AddItem(n);
        _policy.AddThemeFontOverride("font", JpFont()); _policy.AddThemeFontSizeOverride("font_size", 13); _policy.AddThemeStyleboxOverride("normal", PanelStyle());
        _policy.ItemSelected += i => { _sim.Enqueue(new SetPolicy(1, (PowerPolicy)(int)i)); _sim.Step(); };
        top.AddChild(_policy);
        ui.AddChild(Panel(top));
        // 速度
        var spd = new HBoxContainer { Position = new Vector2(16, 72) };
        foreach (var (n, v) in new[] { ("停止", 0), ("1x", 1), ("2x", 2), ("4x", 4) }) { var b = Btn(n, () => SetSpeed(v), v == 1); _speedButtons.Add(b); spd.AddChild(b); }
        spd.AddChild(Btn("取り消し", Undo)); spd.AddChild(Btn("やり直し", Redo));
        ui.AddChild(spd);
        // 左: 設備のパレット
        var pal = new VBoxContainer(); pal.AddChild(Lbl("設備", 12, Cyan));
        foreach (var def in _data.Structures.Values.OrderBy(d => d.TechLevel).ThenBy(d => d.Id))
        {
            string id = def.Id;
            var b = Btn($"{def.Name}  {def.W}x{def.D}", () => SetPalette(id)); b.TooltipText = $"消費 {def.Power} kW / 要員 {def.Workers} / 技術 L{def.TechLevel}";
            b.Alignment = HorizontalAlignment.Left; _paletteButtons[id] = b; pal.AddChild(b);
        }
        _dirLabel = Lbl(DirName(_dir), 13, Cyan); var dirRow = new HBoxContainer(); dirRow.AddChild(Lbl("向き", 12, Cyan)); dirRow.AddChild(_dirLabel); dirRow.AddChild(Btn("回す (R)", () => { _dir = (Direction)(((int)_dir + 1) % 6); _dirLabel.Text = DirName(_dir); }));
        pal.AddChild(dirRow);
        var palPanel = Panel(pal); palPanel.Position = new Vector2(16, 120); ui.AddChild(palPanel);
        // 右: 選択中の設備
        _side = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) };
        var sidePanel = Panel(_side); sidePanel.AnchorLeft = 1; sidePanel.AnchorRight = 1; sidePanel.OffsetLeft = -340; sidePanel.OffsetRight = -16; sidePanel.OffsetTop = 72; ui.AddChild(sidePanel);
    }

    static string DirName(Direction d) => d switch { Direction.East => "東 (+x)", Direction.North => "北 (+y)", Direction.West => "西 (-x)", Direction.South => "南 (-y)", Direction.Up => "上", _ => "下" };

    void SetPalette(string? id)
    {
        _palette = id;
        foreach (var (k, b) in _paletteButtons) b.AddThemeStyleboxOverride("normal", k == id ? PanelStyle(true) : PanelStyle());
    }

    void SetSpeed(int v)
    {
        _speed = v;
        int[] vals = { 0, 1, 2, 4 };
        for (int i = 0; i < _speedButtons.Count; i++) _speedButtons[i].AddThemeStyleboxOverride("normal", vals[i] == v ? PanelStyle(true) : PanelStyle());
    }

    int _sideFor = -2; long _sideTick = -1;
    void RefreshSide()
    {
        if (_sim.Tick - _sideTick < 10 && _sideFor == _selected) return;   // 6 回/秒で更新
        _sideTick = _sim.Tick; _sideFor = _selected;
        foreach (var c in _side.GetChildren()) c.QueueFree();
        var s = Building.Find(0, _selected);
        if (s == null)
        {
            _side.AddChild(Lbl("選択なし", 12, Cyan));
            _side.AddChild(Lbl($"設備 {Floor.Structures.Count} 台  在庫 {Building.Inventory.Values.Sum()} 台", 13));
            var made = Floor.Structures.OfType<Machine>().Sum(m => m.Made); var failed = Floor.Structures.OfType<Machine>().Sum(m => m.Failed);
            _side.AddChild(Lbl($"製造 {made}  不良 {failed}", 13));
            return;
        }
        var def = _data.Structures[s.DefId];
        _side.AddChild(Lbl($"{def.Name}  ({s.X}, {s.Y})  {DirName(s.Dir)}", 15));
        _side.AddChild(Lbl($"消費 {def.Power} kW  動力 {s.PowerPct}%  要員 {def.Workers}", 13, Cyan));
        switch (s)
        {
            case Machine m:
            {
                var opt = new OptionButton(); opt.AddThemeFontOverride("font", JpFont()); opt.AddThemeFontSizeOverride("font_size", 13); opt.AddThemeStyleboxOverride("normal", PanelStyle());
                var recipes = _data.Recipes.Values.Where(r => r.Machine == def.Kind).OrderBy(r => r.TechLevel).ThenBy(r => r.Id).ToList();
                opt.AddItem("レシピなし"); for (int i = 0; i < recipes.Count; i++) { opt.AddItem($"L{recipes[i].TechLevel} {recipes[i].Name}"); if (recipes[i].Id == m.Recipe) opt.Selected = i + 1; }
                int sid = s.Id; opt.ItemSelected += i => { _sim.Enqueue(new SetRecipe(1, 0, sid, i == 0 ? null : recipes[(int)i - 1].Id)); _sim.Step(); _sideTick = -1; };
                _side.AddChild(opt);
                if (m.Recipe != null)
                {
                    var r = _data.Recipes[m.Recipe];
                    _side.AddChild(Lbl("入力  " + string.Join("  ", r.Inputs.Select(i => $"{_data.Items[i.Item].Name}×{i.Count} ({TierName(i.MinTier)}以上)")), 12));
                    _side.AddChild(Lbl($"所要 {r.Ticks} ティック  失敗時の回収 {r.ScrapRate / 100}%", 12));
                }
                _side.AddChild(Lbl($"要員 {m.Staff.Count}/{def.Workers}  " + string.Join(" ", m.Staff.Select(w => $"{w.Name}(技能 {w.Skill})")), 13));
                var row = new HBoxContainer(); var skill = new SpinBox { MinValue = 0, MaxValue = 10, Value = 3 }; skill.AddThemeStyleboxOverride("normal", PanelStyle());
                row.AddChild(Lbl("技能", 12, Cyan)); row.AddChild(skill);
                row.AddChild(Btn("要員を割り当て", () => { _sim.Enqueue(new AssignStaff(1, 0, sid, new Worker(m.Staff.Count + 1, $"作業員 {m.Staff.Count + 1}", (int)skill.Value))); _sim.Step(); _sideTick = -1; }, true));
                _side.AddChild(row);
                _side.AddChild(Lbl($"在庫  入力 {Count(m.Inputs)}  出力 {Count(m.Outputs)}", 13));
                _side.AddChild(Lbl($"進捗 {(m.Progress < 0 ? "待機" : (m.Progress / Math.Max(1, _data.Recipes[m.Recipe ?? ""]?.Ticks ?? 1)) + "%")}  製造 {m.Made}  不良 {m.Failed}", 13));
                break;
            }
            case Reactor r:
                _side.AddChild(Lbl($"第 {def.Generation} 世代  品質 {r.Quality}  出力 {Building.ReactorOutput(def, r.Quality, _data)} kW", 13));
                break;
            case Chest c:
                _side.AddChild(Lbl($"{c.Items.Count}/{def.Capacity}  " + Count(c.Items), 13));
                break;
            case Belt b:
                _side.AddChild(Lbl("コマ  " + string.Join(" ", b.Slots.Select(x => x is { } it ? _data.Items[it.Item].Name : "-")), 13));
                break;
            case Miner mi:
                var ore = Floor.OreAt(mi.X, mi.Y);
                _side.AddChild(Lbl(ore == null ? "鉱脈なし" : $"{_data.Items[ore.Item].Name}  残り {ore.Amount:N0}  品質 {ore.Quality}", 13));
                break;
            case Tank t:
                _side.AddChild(Lbl(t.Fluid == null ? "空" : $"{_data.Items[t.Fluid].Name} {t.Amount}/{def.Capacity}", 13));
                break;
            case Port p:
                _side.AddChild(Lbl(p.LinkBuilding < 0 ? "未接続" : $"接続先 建物 {p.LinkBuilding} 階 {p.LinkFloor}  待ち {p.Buffer.Count}", 13));
                break;
        }
        _side.AddChild(Btn("撤去 (Del)", RemoveSelected));
    }

    string Count(IEnumerable<ItemStack> items) => string.Join("  ", items.GroupBy(i => i.Item).Select(g => $"{_data.Items[g.Key].Name}×{g.Sum(i => i.Count)}"));
    static string TierName(QualityTier t) => t switch { QualityTier.Defect => "不良", QualityTier.Normal => "通常", QualityTier.High => "高", _ => "最高" };

    // ---------- 動作確認(無人): --demo で見本の生産線を置き、--screenshot で保存 ----------
    static string? Arg(string name) { var a = OS.GetCmdlineUserArgs(); int i = Array.IndexOf(a, name); return i < 0 ? null : (i + 1 < a.Length ? a[i + 1] : ""); }

    void Demo()
    {
        var cmds = new List<ICommand>();
        int y0 = D - 21;   // 鉱脈のすぐ南
        for (int x = 0; x < 12; x++) { cmds.Add(new PlaceStructure(1, 0, "miner", x, D - 20, 0, Direction.South)); cmds.Add(new PlaceStructure(1, 0, "inserter", x, y0, 0, Direction.South)); cmds.Add(new PlaceStructure(1, 0, "belt", x, y0 - 1, 0, Direction.East)); }
        for (int x = 12; x < 20; x++) cmds.Add(new PlaceStructure(1, 0, "belt", x, y0 - 1, 0, Direction.East));
        for (int x = 2; x < 20; x += 3)
        {
            cmds.Add(new PlaceStructure(1, 0, "inserter", x, y0 - 2, 0, Direction.South));
            cmds.Add(new PlaceStructure(1, 0, "furnace", x, y0 - 3, 0, Direction.South));
            cmds.Add(new PlaceStructure(1, 0, "inserter", x, y0 - 4, 0, Direction.South));
            cmds.Add(new PlaceStructure(1, 0, "chest", x, y0 - 5, 0, Direction.South));
        }
        cmds.Add(new PlaceStructure(1, 0, "reactor_g2", 22, y0 - 3, 0, Direction.East)); cmds.Add(new PlaceStructure(1, 0, "reactor_g2", 24, y0 - 3, 0, Direction.East));
        foreach (var c in cmds) _sim.Enqueue(c);
        _sim.Step();
        int wid = 1;
        foreach (var m in Floor.Structures.OfType<Machine>()) { _sim.Enqueue(new SetRecipe(1, 0, m.Id, "iron_plate")); _sim.Enqueue(new AssignStaff(1, 0, m.Id, new Worker(wid, $"作業員 {wid}", 4))); wid++; }
        _sim.Step();
        _sim.Run(3600);
        _structureVersion++;
        _selected = Floor.Structures.First(s => s is Machine).Id;
        _pivot = new Vector3(10, FloorY, y0 - 2); _dist = 28; _yaw = 0.5f; _elev = 0.7f; UpdateCamera();
    }

    void Screenshot()
    {
        var path = Arg("--screenshot"); if (path == null) return;
        if (++_frame == 40) { GetViewport().GetTexture().GetImage().SavePng(path); GetTree().Quit(); }
    }
}
