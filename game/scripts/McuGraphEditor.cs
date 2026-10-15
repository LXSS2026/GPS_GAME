using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GPS.Sim;

/// <summary>マイコンの図式エディタ(Stormworks 式)。Godot の GraphEdit でノードを置き、線でつなぎ、保存で SetProgram 命令にする。
/// 入力・出力ノードは参照する設備を建物内から選ぶ。動作中の値はノードの下に出す(10 回/秒)。</summary>
public partial class McuGraphEditor : Control
{
    static readonly Color Navy = new(8 / 255f, 20 / 255f, 48 / 255f, 0.96f), Cyan = new(159 / 255f, 216 / 255f, 1f), Accent = new(56 / 255f, 198 / 255f, 1f), Text = new(244 / 255f, 248 / 255f, 1f), Warm = new(1f, 154 / 255f, 60 / 255f);
    static readonly Dictionary<string, Color> GroupColor = new() { ["入力"] = new Color(0.35f, 0.85f, 0.55f), ["演算"] = Cyan, ["比較"] = new Color(0.95f, 0.85f, 0.35f), ["論理"] = new Color(0.95f, 0.85f, 0.35f), ["出力"] = Warm, ["機体"] = new Color(0.8f, 0.5f, 1f) };

    Simulation _sim = null!; GameData _data = null!; int _building, _floor, _mcuId;
    Func<string, Texture2D> _icon = null!;
    Action _onClose = null!;
    GraphEdit _graph = null!;
    LineEdit _name = null!; SpinBox _interval = null!;
    readonly Dictionary<int, (McuNode node, Label value)> _nodes = new();
    int _nextId = 1;
    double _acc;

    public static McuGraphEditor Open(Node parent, Simulation sim, GameData data, int building, int floor, int mcuId, Func<string, Texture2D> icon, Theme theme, Action onClose)
    {
        var ed = new McuGraphEditor { _sim = sim, _data = data, _building = building, _floor = floor, _mcuId = mcuId, _icon = icon, _onClose = onClose, Theme = theme };
        ed.SetAnchorsPreset(LayoutPreset.FullRect); ed.OffsetLeft = 16; ed.OffsetTop = 16; ed.OffsetRight = -16; ed.OffsetBottom = -16;
        parent.AddChild(ed);
        ed.Build();
        return ed;
    }

    Mcu? Mcu => _sim.Factory.Get(_building)?.Find(_floor, _mcuId) as Mcu;

    void Build()
    {
        var bg = new PanelContainer(); bg.SetAnchorsPreset(LayoutPreset.FullRect);
        var st = new StyleBoxFlat { BgColor = Navy, BorderColor = Cyan }; st.SetBorderWidthAll(1); st.SetContentMarginAll(10); bg.AddThemeStyleboxOverride("panel", st);
        AddChild(bg);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 8); bg.AddChild(root);
        // 上: 名前・間隔・保存・閉じる
        var top = new HBoxContainer(); top.AddThemeConstantOverride("separation", 12);
        top.AddChild(Lbl("制御室のプログラム", 15));
        top.AddChild(Lbl("名前", 11, Cyan)); _name = new LineEdit { Text = Mcu?.Program.Name ?? "", CustomMinimumSize = new Vector2(220, 32) }; top.AddChild(_name);
        top.AddChild(Lbl("評価の間隔(ティック)", 11, Cyan)); _interval = new SpinBox { MinValue = 1, MaxValue = 600, Value = Mcu?.Program.Interval ?? 6, CustomMinimumSize = new Vector2(100, 32) }; top.AddChild(_interval);
        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill }; top.AddChild(spacer);
        top.AddChild(Btn("保存して適用", Save, true)); top.AddChild(Btn("閉じる", () => { _onClose(); QueueFree(); }));
        root.AddChild(top);
        // 中: 左にノードの一覧、右に図
        var mid = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; mid.AddThemeConstantOverride("separation", 10); root.AddChild(mid);
        var palScroll = new ScrollContainer { CustomMinimumSize = new Vector2(250, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; mid.AddChild(palScroll);
        var pal = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; pal.AddThemeConstantOverride("separation", 4); palScroll.AddChild(pal);
        foreach (var g in McuCatalog.Types.GroupBy(t => t.Group))
        {
            pal.AddChild(Lbl(g.Key, 11, GroupColor.GetValueOrDefault(g.Key, Cyan)));
            foreach (var t in g)
            {
                var tt = t; var b = Btn(t.Name, () => AddNode(new McuNode { Id = _nextId++, Type = tt.Type, X = 200 + (_nextId % 5) * 40, Y = 100 + (_nextId % 7) * 40 })); b.Alignment = HorizontalAlignment.Left; b.CustomMinimumSize = new Vector2(0, 28);
                b.AddThemeFontSizeOverride("font_size", 12); pal.AddChild(b);
            }
        }
        _graph = new GraphEdit { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, RightDisconnects = true, ShowGrid = true, MinimapEnabled = false };
        _graph.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(4 / 255f, 10 / 255f, 26 / 255f, 1f) });
        _graph.ConnectionRequest += (from, fp, to, tp) => { foreach (var c in Incoming(to, (int)tp)) _graph.DisconnectNode((StringName)(string)c["from_node"], (int)c["from_port"], to, (int)tp); _graph.ConnectNode(from, (int)fp, to, (int)tp); };
        _graph.DisconnectionRequest += (from, fp, to, tp) => _graph.DisconnectNode(from, (int)fp, to, (int)tp);
        _graph.DeleteNodesRequest += names => { foreach (var nm in names) { var gn = _graph.GetNodeOrNull<GraphNode>(nm.ToString()); if (gn == null) continue; int id = int.Parse(gn.Name.ToString()[1..]); _nodes.Remove(id); gn.QueueFree(); } };
        mid.AddChild(_graph);
        // 既存のプログラムを読み込む
        var prog = Mcu?.Program;
        if (prog != null)
        {
            foreach (var n in prog.Nodes) { AddNode(Clone(n)); _nextId = Math.Max(_nextId, n.Id + 1); }
            foreach (var l in prog.Links) _graph.ConnectNode($"n{l.FromNode}", l.FromPort, $"n{l.ToNode}", l.ToPort);
        }
        root.AddChild(Lbl("ノードは一覧から追加、右の口から左の口へ線を引く。Delete で消す。入力・出力の設備は各ノードで選ぶ。保存で制御室に適用(命令として記録)。", 11, Cyan));
    }

    IEnumerable<Godot.Collections.Dictionary> Incoming(StringName to, int port) => _graph.GetConnectionList().Where(c => (string)c["to_node"] == to.ToString() && (int)c["to_port"] == port).ToList();

    static McuNode Clone(McuNode n) => new() { Id = n.Id, Type = n.Type, TargetBuilding = n.TargetBuilding, TargetFloor = n.TargetFloor, TargetId = n.TargetId, Params = new(n.Params), X = n.X, Y = n.Y };

    void AddNode(McuNode n)
    {
        var t = McuCatalog.Of(n.Type);
        var gn = new GraphNode { Name = $"n{n.Id}", Title = $"{n.Id}  {t.Name}", PositionOffset = new Vector2(n.X, n.Y), Resizable = false };
        var col = GroupColor.GetValueOrDefault(t.Group, Cyan);
        var tb = new StyleBoxFlat { BgColor = new Color(col, 0.35f), BorderColor = col }; tb.SetBorderWidthAll(1); tb.SetContentMarginAll(6); gn.AddThemeStyleboxOverride("titlebar", tb);
        var pb = new StyleBoxFlat { BgColor = Navy, BorderColor = col }; pb.SetBorderWidthAll(1); pb.SetContentMarginAll(8); gn.AddThemeStyleboxOverride("panel", pb);
        int rows = Math.Max(t.Inputs, Math.Max(t.Outputs, 1));
        string[] inNames = n.Type switch { "op.select" => new[] { "条件", "真", "偽" }, "op.memory" => new[] { "書込", "値" }, _ => new[] { "a", "b", "c" } };
        for (int i = 0; i < rows; i++)
        {
            var row = new HBoxContainer(); row.CustomMinimumSize = new Vector2(180, 22);
            row.AddChild(Lbl(i < t.Inputs ? (t.Inputs == 1 ? "入" : inNames[i]) : "", 11));
            var sp = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill }; row.AddChild(sp);
            row.AddChild(Lbl(i < t.Outputs ? "出" : "", 11));
            gn.AddChild(row);
            gn.SetSlot(i, i < t.Inputs, 0, Cyan, i < t.Outputs, 0, col);
        }
        // 対象の設備
        if (t.TargetKind != null && t.TargetKind != "building")
        {
            var opt = new OptionButton { CustomMinimumSize = new Vector2(220, 30) }; opt.AddThemeConstantOverride("icon_max_width", 20);   // 設備のアイコンは原寸 256px なので絞る
            var cands = Candidates(t.TargetKind).ToList();
            opt.AddItem("(設備を選ぶ)");
            for (int i = 0; i < cands.Count; i++) { var (fl, s) = cands[i]; opt.AddIconItem(_icon(s.DefId), $"{fl + 1} 階 ({s.X},{s.Y}) {_data.Structures[s.DefId].Name}"); if (n.TargetId == s.Id && (n.TargetFloor < 0 ? 0 : n.TargetFloor) == fl) opt.Selected = i + 1; }
            opt.ItemSelected += i => { if (i == 0) { n.TargetId = -1; } else { var (fl, s) = cands[(int)i - 1]; n.TargetBuilding = _building; n.TargetFloor = fl; n.TargetId = s.Id; } };
            gn.AddChild(opt);
        }
        if (t.ParamNames != null)
            foreach (var pn in t.ParamNames)
            {
                var r = new HBoxContainer(); r.AddChild(Lbl(pn, 11, Cyan));
                var sb = new SpinBox { MinValue = -1e9, MaxValue = 1e9, Step = 0.01, Value = n.Params.GetValueOrDefault(pn, pn == "max" ? 100 : 0), CustomMinimumSize = new Vector2(150, 28) };
                string key = pn; sb.ValueChanged += v => n.Params[key] = v; r.AddChild(sb); gn.AddChild(r);
            }
        var val = Lbl("値  -", 11, Warm); gn.AddChild(val);
        gn.PositionOffsetChanged += () => { n.X = gn.PositionOffset.X; n.Y = gn.PositionOffset.Y; };
        _graph.AddChild(gn);
        _nodes[n.Id] = (n, val);
    }

    IEnumerable<(int floor, Structure s)> Candidates(string kind)
    {
        var b = _sim.Factory.Get(_building)!;
        for (int f = 0; f < b.Floors.Count; f++)
            foreach (var s in b.Floors[f].Structures.OrderBy(x => x.Id))
            {
                var k = _data.Structures[s.DefId].Kind;
                bool ok = kind == "any" || k == kind || (kind == "machine" && k is "furnace" or "assembler" or "mech_assembler");
                if (ok) yield return (f, s);
            }
    }

    void Save()
    {
        var prog = new Program { Name = _name.Text, Interval = (int)_interval.Value };
        foreach (var (id, (n, _)) in _nodes.OrderBy(kv => kv.Key)) prog.Nodes.Add(Clone(n));
        foreach (var c in _graph.GetConnectionList())
            prog.Links.Add(new McuLink(int.Parse(((string)c["from_node"])[1..]), (int)c["from_port"], int.Parse(((string)c["to_node"])[1..]), (int)c["to_port"]));
        _sim.Enqueue(new SetProgram(_building, _floor, _mcuId, prog)); _sim.Step();
    }

    public override void _Process(double delta)
    {
        _acc += delta; if (_acc < 0.1) return; _acc = 0;
        var live = Mcu?.Program; if (live == null) return;
        foreach (var (id, (_, lbl)) in _nodes)
        {
            var ln = live.Nodes.FirstOrDefault(x => x.Id == id);
            lbl.Text = ln == null ? "値  (未適用)" : $"値  {ln.Values[0]:0.##}";
        }
    }

    static Label Lbl(string t, int size, Color? c = null) { var l = new Label { Text = t }; l.AddThemeFontSizeOverride("font_size", size); l.AddThemeColorOverride("font_color", c ?? Text); return l; }
    static Button Btn(string t, Action a, bool accent = false)
    {
        var b = new Button { Text = t, CustomMinimumSize = new Vector2(0, 32) };
        if (accent) { var s = new StyleBoxFlat { BgColor = new Color(18 / 255f, 40 / 255f, 84 / 255f, 0.9f), BorderColor = Accent }; s.SetBorderWidthAll(2); s.SetContentMarginAll(8); b.AddThemeStyleboxOverride("normal", s); }
        b.Pressed += a; return b;
    }
}
