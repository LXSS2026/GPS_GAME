using Godot;
using GPS.Sim;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 都市の画面(マクロ。G5 のモック、G17 #14)。上の帯に人口・食料・水・支持率・日時、左に都市の板(雇用・インフラ・政策・人物・通知)、
/// 建物を選ぶと右に概要と階ごとの状態、「編集」でその建物のミクロの地図(MicroEditor)に切り替わる。計算は GPS.Sim(CitySim)。
/// 起動: godot --path game res://scenes/city.tscn ++ [--hours N] [--select id] [--open id] [--screenshot path]
/// </summary>
public partial class CityScreen : Node3D
{
    static readonly Color Navy = new(8 / 255f, 20 / 255f, 48 / 255f, 0.86f), NavyDeep = new(6 / 255f, 14 / 255f, 34 / 255f, 0.94f), Cyan = new(159 / 255f, 216 / 255f, 1f), Accent = new(56 / 255f, 198 / 255f, 1f), Text = new(244 / 255f, 248 / 255f, 1f);
    static readonly Color Line40 = new(159 / 255f, 216 / 255f, 1f, 0.4f), Warm = new(1f, 154 / 255f, 60 / 255f), Green = new(95 / 255f, 224 / 255f, 138 / 255f), Red = new(1f, 0.35f, 0.3f);
    static readonly List<string> MechHangar = new() { "base_12x12_office", "hangar_mech_12x12", "roof_12x12_heli_office" };
    static readonly List<string> BigShip = new() { "ship_stern_30x16", "ship_turret_30x16", "ship_lift_30x16", "ship_mid_30x16", "ship_hangar_30x24", "ship_hangar_30x24", "ship_mid_30x16", "ship_bridge_30x16", "ship_lift_30x16", "ship_turret_30x16", "ship_bow_30x28" };
    static readonly List<string> SmallShip = new() { "ship_stern_18x12", "ship_lift_18x12", "ship_mid_18x12", "ship_hangar_18x18", "ship_bridge_18x12", "ship_turret_18x12", "ship_bow_18x24" };
    const float Y = SampleCity.GroundTop + 1;

    Simulation _sim = null!;
    GameData _data = null!;
    KitBash _kb = null!;
    FloorModules _fm = null!;
    MechBuilder _mb = null!;
    Camera3D _cam = null!;
    Node3D _cityRoot = null!;
    CanvasLayer _ui = null!;
    Vector3 _pivot = new(SampleCity.N / 2f, Y, SampleCity.N / 2f);
    float _dist = 260, _yaw = 0.6f, _elev = 0.8f;
    int _speed = 1, _frame, _selected = -1, _seenEvents;
    readonly List<(Aabb box, int id)> _hit = new();
    readonly Dictionary<int, Node3D> _marks = new();
    readonly Dictionary<int, Node3D> _idleMechs = new();
    Label _top = null!, _city = null!, _notice = null!;
    VBoxContainer _side = null!;
    PanelContainer _sidePanel = null!;
    HSlider _tax = null!, _draft = null!;
    MicroEditor? _micro;
    readonly List<string> _notices = new();
    long _sideTick = -1;

    static string? Arg(string name) { var a = OS.GetCmdlineUserArgs(); int i = Array.IndexOf(a, name); return i < 0 ? null : (i + 1 < a.Length ? a[i + 1] : ""); }

    public override void _Ready()
    {
        Engine.PhysicsTicksPerSecond = Clock.TicksPerSecond;
        _data = GameData.Load(ProjectSettings.GlobalizePath("res://data"));
        _sim = new Simulation(BlockRegistry.FromJson(FileAccess.GetFileAsString("res://data/blocks.json")), _data, 1);
        SampleCity.Build(_sim);
        if (Arg("--war") is string wr) { _sim.Enqueue(new DeclareWar(int.Parse(wr))); _sim.Step(); }
        if (Arg("--hours") is string hs) _sim.Run(Clock.TicksPerHour * int.Parse(hs));
        if (Arg("--move") is string mvs)
        {   // 撮影用: --move 隊,x,z で出撃させて移動(要員は人口を増やして満たす)
            var a3 = mvs.Split(','); int sq = int.Parse(a3[0]); _sim.Factory.City!.Population = Math.Max(_sim.Factory.City.Population, 300); _sim.Factory.City.ConscriptionRate = 0.5; CitySim.Hour(_sim, _sim.Factory.City);
            var hb = _sim.Factory.Get(_sim.Factory.Squads.First(q => q.Id == sq).HomeBuilding)!;
            _sim.Enqueue(new Deploy(sq, hb.X + 14, hb.Y + 6)); _sim.Enqueue(new OrderSquad(sq, SquadOrder.Move, double.Parse(a3[1]), double.Parse(a3[2]))); _sim.Step();
        }
        if (Arg("--defend") != null)
        {   // 撮影用: 自軍の隊を出撃させ、最寄りの敵の隊を攻撃
            foreach (var sq in _sim.Factory.Squads.Where(q => q.Faction == 1 && q.State == SquadState.InHangar).ToList())
            {
                var hb = _sim.Factory.Get(sq.HomeBuilding); var enemy = _sim.Factory.Squads.Where(q => q.Faction != 1 && q.State == SquadState.Field).OrderBy(q => Math.Abs(q.X - (hb?.X ?? 0)) + Math.Abs(q.Y - (hb?.Y ?? 0))).FirstOrDefault();
                _sim.Factory.City!.ConscriptionRate = 0.5; CitySim.Hour(_sim, _sim.Factory.City);
                _sim.Enqueue(new Deploy(sq.Id, (hb?.X ?? 0) + 14, (hb?.Y ?? 0) + 6)); if (enemy != null) _sim.Enqueue(new OrderSquad(sq.Id, SquadOrder.Attack, TargetSquad: enemy.Id));
            }
            _sim.Step();
        }
        if (Arg("--ticks") is string tk) _sim.Run(long.Parse(tk));
        if (Arg("--road") is string rd) { var a = rd.Split(','); _sim.Enqueue(new BuildRoad(int.Parse(a[0]), int.Parse(a[1]), int.Parse(a[2]), int.Parse(a[3]))); _sim.Step(); }
        if (Arg("--build") is string bl) { var a = bl.Split(','); _sim.Enqueue(new BuildBuilding(0, $"{a[0]} 新", a[0], int.Parse(a[1]), int.Parse(a[2]), int.Parse(a[3]), int.Parse(a[4]))); _sim.Step(); }
        _kb = new KitBash(11); _fm = new FloorModules(_kb, 9); _mb = new MechBuilder(_fm);
        _cityRoot = new Node3D { Name = "city" }; AddChild(_cityRoot);
        var tr = new TerrainRenderer(); _cityRoot.AddChild(tr); tr.Init(_sim.World, _sim.Blocks); tr.BuildAll(); _terrain = tr;
        _cityRoot.AddChild(SceneSetup.Lighting());
        BuildBuildings();
        _kb.Flush(_cityRoot);
        if (Arg("--pivot") is string pv) { var p = pv.Split(','); _pivot = new Vector3(float.Parse(p[0]), float.Parse(p[1]), float.Parse(p[2])); }
        if (Arg("--dist") is string ds) _dist = float.Parse(ds);
        if (Arg("--yaw") is string ys) _yaw = float.Parse(ys);
        if (Arg("--elev") is string es) _elev = float.Parse(es);
        _cam = new Camera3D { Fov = 40, Far = 3000 }; _cityRoot.AddChild(_cam); PlaceCamera();
        BuildHud();
        if (Arg("--select") is string sel) Select(int.Parse(sel));
        if (Arg("--all") != null) _drawAll = true;
        if (Arg("--mode") is string md) { SetBuildMode(md == "place" ? BuildMode.Place : md == "road" ? BuildMode.Road : BuildMode.Demolish); if (md == "place") UpdateGhost(Arg("--ghost") is string gh ? _cam.UnprojectPosition(new Vector3(float.Parse(gh.Split(',')[0]), Y, float.Parse(gh.Split(',')[1]))) : GetViewport().GetVisibleRect().Size / 2); }
        if (Arg("--office") is string of) _pickOffice = of;
        if (Arg("--pick") is string pk) _pickPerson = int.Parse(pk);
        if (Arg("--screen") is string sc) OpenOverlay(sc);
        if (Arg("--open") is string op) { Select(int.Parse(op)); CallDeferred(nameof(OpenMicroDeferred), int.Parse(op)); }
    }

    // ---------- 建物の見た目(用途ごと) ----------
    void BuildBuildings()
    {
        foreach (var b in _sim.Factory.Buildings) AddBuildingVisual(b);
        // 見本の隊の機体を格納庫の前に(待機中の表示。出撃中は RefreshField が描く)
        var hangar = _sim.Factory.Get(22);
        if (hangar != null) foreach (var (m, i) in _sim.Factory.Mechs.Where(m => _sim.Factory.Squads.Any(s => s.Faction == 1 && s.Members.Any(u => u.MechId == m.Id))).Select((m, i) => (m, i))) { var n = _mb.Build(_cityRoot, m.Archetype, $"g{m.Generation}", (float)hangar.X + 3 + i * 6, Y, (float)hangar.Y - 8, 180); _idleMechs[m.Id] = n; }
        // AI 勢力の拠点の名札
        foreach (var p in _sim.Factory.Diplomacy?.Powers ?? new())
        {
            var hb = _sim.Factory.Get(p.HomeBuilding); if (hb == null) continue;
            var tag = new Label3D { Text = p.Name, FontSize = 48, PixelSize = 0.05f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, OutlineSize = 12, Modulate = Red, Position = new Vector3((float)hb.X + 6, Y + 14, (float)hb.Y + 6) };
            _cityRoot.AddChild(tag);
        }
    }

    /// <summary>建物 1 棟の見た目(用途ごと)。撤去できるよう建物ごとの子ノードにまとめる。KitBash の物は呼び出し側で Flush する。</summary>
    void AddBuildingVisual(Building b)
    {
        if (b.Floors.Count == 0) return;
        int w = b.Floors[0].W, d = b.Floors[0].D, floors = b.Floors.Count; float x = (float)b.X, z = (float)b.Y;
        float h = floors * 3f;
        var node = new Node3D { Name = $"b{b.Id}" }; _cityRoot.AddChild(node); _buildingNodes[b.Id] = node;
        if (b.IsShip)
        {   // 航空戦艦: 区画の積み上げを高度に浮かべる。位置は毎フレーム更新(SyncShips)
            var k = Ships.Kinds[b.ShipKind];
            var hull = new Node3D { Name = "hull" }; node.AddChild(hull);
            _fm.Ship(hull, 0, 0, 0, b.ShipKind == "big" ? BigShip : SmallShip, 0);
            node.Position = new Vector3(x + w / 2f, Y + k.Altitude, z + d / 2f);
            _hit.Add((new Aabb(new Vector3(x, Y, z), new Vector3(w, k.Altitude + 12, d)), b.Id));
            var tag = new Label3D { Text = b.Name, FontSize = 48, PixelSize = 0.05f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, OutlineSize = 12, Modulate = Cyan, Position = new Vector3(0, 16, 0) }; node.AddChild(tag);
            return;
        }
        switch (b.Use)
        {
            case "farm":
                for (int i = 0; i < w; i += 4) _kb.Strip(node, new Vector3(x + i + 1.5f, Y + 0.15f, z + d / 2f), new Vector3(1.2f, 0.3f, d - 2), _kb.PaintDark);   // 畝
                h = 1; break;
            case "park":
                for (int i = 0; i < 6; i++) _kb.Cyl(node, new Vector3(x + 3 + (i % 3) * (w - 6) / 2f, Y + 2.2f, z + 3 + (i / 3) * (d - 6)), 1.6f, 3.4f, _kb.PaintDark, topR: 0.2f);   // 木
                h = 4; break;
            case "well":
                _kb.Cyl(node, new Vector3(x + w / 2f, Y + 0.8f, z + d / 2f), 2.5f, 1.6f, _kb.PaintDark); _kb.Box(node, new Vector3(x + w / 2f, Y + 3f, z + d / 2f), new Vector3(1.2f, 4f, 1.2f), _kb.PaintDark);
                h = 5; break;
            default:
                if (b.Name.Contains("格納庫")) { _fm.Build(node, x, Y, z, MechHangar, 0); h = 9; }
                else
                {
                    string fam = b.Use == "residence" ? "res" : b.Use == "factory" ? "concrete" : "office";
                    _fm.Build(node, x, Y, z, _fm.Plan(fam, w, d, floors, false), 0);
                }
                break;
        }
        if (b.WallLevel > 0)
        {   // 防御壁: 外周の 1 m 外側に、段階 × 1.5 m + 2 m の高さの壁
            float wh = 2f + 1.5f * b.WallLevel, o = 1f;
            _kb.Box(node, new Vector3(x + w / 2f, Y + wh / 2, z - o), new Vector3(w + 2 * o + 1, wh, 1), _kb.Concrete);
            _kb.Box(node, new Vector3(x + w / 2f, Y + wh / 2, z + d + o), new Vector3(w + 2 * o + 1, wh, 1), _kb.Concrete);
            _kb.Box(node, new Vector3(x - o, Y + wh / 2, z + d / 2f), new Vector3(1, wh, d + 2 * o + 1), _kb.Concrete);
            _kb.Box(node, new Vector3(x + w + o, Y + wh / 2, z + d / 2f), new Vector3(1, wh, d + 2 * o + 1), _kb.Concrete);
        }
        _hit.Add((new Aabb(new Vector3(x, Y, z), new Vector3(w, h, d)), b.Id));
    }

    // ---------- 計算と描画 ----------
    public override void _PhysicsProcess(double delta)
    {
        if (_micro != null) return;   // ミクロを開いている間はそちらが進める
        for (int i = 0; i < _speed; i++) _sim.Step();
    }

    public override void _Process(double delta)
    {
        if (_micro == null) { RefreshTop(); RefreshCity(); RefreshSide(); RefreshMarks(); SyncShips(); RefreshField(); RefreshOverlay(); }
        var ev = _sim.Factory.Events;
        for (; _seenEvents < ev.Count; _seenEvents++)
        {
            var e = ev[_seenEvents];
            var pw = _sim.Factory.Diplomacy?.Powers.FirstOrDefault(p => p.Id == e.Building);
            string? s = e.Kind switch { "war_declared" => $"{pw?.Name} に宣戦した", "war_received" => $"{pw?.Name} が宣戦してきた", "raid" => $"{pw?.Name} の襲撃隊が来た", "offer" => $"{pw?.Name} から申し出(政治画面で回答)", "warning" => $"{pw?.Name} の兵力が増えている(侵攻の恐れ)", "treaty" => $"{pw?.Name} と条約を結んだ", "proposal_failed" => $"{pw?.Name} は提案を断った", "threat_ok" => $"{pw?.Name} が貢納を飲んだ(月 {e.Power})", "threat_failed" => $"{pw?.Name} は威圧に応じない", "peace" => $"{pw?.Name} と和平した", "peace_failed" => $"{pw?.Name} は和平を断った", "vassal" => $"{pw?.Name} の拠点が落ち属国になった(スクラップ {e.Power})", "raided" => $"建物 #{e.Building} が略奪された", "conquered" => "拠点が落ちた(征服)", "building_destroyed" => $"建物 #{e.Building} が落ちた", "victory" => e.Power == 1 ? "勝利: 大陸の制覇" : "勝利: 同盟網による支配", "faction" => $"派閥ができた(首領 #{e.Power})", "faction_end" => $"派閥が解消した(首領 #{e.Power})", "defect" => $"派閥が離反した(首領 #{e.Power}、{e.Destroyed} 人が去った)", "rebellion" => "反乱が起きた", "rebellion_end" => "反乱が鎮圧された", "built" => $"建物 #{e.Building} を建てた(費用 {e.Power:N0})", "demolished" => $"建物 #{e.Building} を撤去した(戻り {e.Power:N0})", "road_built" => $"街路を {e.Destroyed} セル敷いた(費用 {e.Power:N0})", "build_failed" => "建てられない(場所か国庫)", "ship_built" => $"戦艦 #{e.Building} を建造した(費用 {e.Power:N0})", "ship_arrived" => $"戦艦 #{e.Building} が ({e.X}, {e.Y}) に着いた", "ship_sunk" => $"戦艦 #{e.Building} が撃沈された(搭載 {e.Destroyed} 隊を失った)", "building_repaired" => $"建物 #{e.Building} を修復した(費用 {e.Power:N0})", "wall_built" => $"建物 #{e.Building} の防御壁を段階 {e.Power} に(費用 {e.Destroyed:N0})", "collected" => $"隊 #{e.Building} が部品 {e.Destroyed} 個を回収した", "repaired" => $"機体 #{e.Power} を修理(耐久 {e.Destroyed}%)", "no_ammo" => $"隊 #{e.Building} は弾薬が尽きて補給に戻る", "robot_cap" => "製造ロボットの導入率の上限を超えるため配置できない", "stage" => $"段階が {StageName(e.Power)} に進んだ", "defeat" => "支持率の低下により失脚(敗北)", "no_crew" => $"隊 #{e.Building} は要員不足で出撃できない", "mech" => $"機体が完成(第 {e.Power} 世代)", _ => null };
            if (s != null) Notice(s);
        }
        var path = Arg("--screenshot"); if (path == null || _micro != null) return;
        if (++_frame == 40) { GetViewport().GetTexture().GetImage().SavePng(path); GetTree().Quit(); }
    }

    void Notice(string s) { var (yr, day, hr, mn) = Clock.Of(_sim.Tick); _notices.Add($"{yr} 年 {day} 日 {hr:00}:{mn:00}  {s}"); if (_notices.Count > 6) _notices.RemoveAt(0); }
    static string StageName(int st) => st switch { 1 => "村", 2 => "町", 3 => "都市", _ => "国家" };
    static string BandName(string b) => b switch { "high" => "高い", "normal" => "普通", "low" => "低い", "danger" => "危険", _ => "失脚の危機" };
    static string UseName(CityData cd, string use) => cd.Uses.TryGetValue(use, out var u) ? u.Name : use;
    static string InfraName(string k) => k switch { "food" => "食料", "water" => "水", "power" => "電力", "sewage" => "下水処理", "waste" => "廃棄物処理", "road" => "道路の接続", "security" => "治安", "medical" => "医療", "education" => "教育", "park" => "公園・環境", "housing" => "住居", _ => k };

    void RefreshTop()
    {
        var c = _sim.Factory.City!; var (yr, day, hr, mn) = Clock.Of(_sim.Tick);
        int season = (day - 1) / 90; string sn = new[] { "春", "夏", "秋", "冬" }[Math.Clamp(season, 0, 3)];
        _top.Text = $"{StageName(c.Stage)} {c.Name}    人口 {c.Population:0}    食料 {c.Ratio("food") * 100:0}%    水 {c.Ratio("water") * 100:0}%    支持 {c.Support:0}%({BandName(c.Band)})    国庫 {_sim.Factory.Treasury:N0}    {yr} 年目 {sn} {(day - 1) % 90 + 1} 日 {hr:00}:{mn:00}    速度 {(_speed == 0 ? "停止" : _speed + "x")}";
    }

    void RefreshCity()
    {
        var c = _sim.Factory.City!; var cd = _sim.CityData;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"労働人口 {c.Labor:0}  職場 {c.Jobs:0}  就業 {c.Employed:0}  失業率 {c.Unemployment * 100:0}%");
        sb.AppendLine($"設備の要員 {c.MachineStaff:0}  軍の要員 {c.Conscripts:0} / {c.CrewNeed:0}  住居の上限 {c.PopulationCap:0}" + (c.Riot ? "  暴動(生産効率 70%)" : ""));
        sb.AppendLine($"支持率 {c.Support:0.0}% → 目標 {c.SupportTarget:0.0}%   公害 −{c.Pollution:0.0}");
        sb.AppendLine($"税収 {c.LastTax:0} / 日  維持費 {c.LastUpkeep:0}  給与 {c.LastPay:0}   出生 {c.LastBirths:0.0}  流入 {c.LastInflow:0.0}  流出 {c.LastOutflow:0.0}");
        sb.AppendLine("インフラの充足");
        foreach (var k in new[] { "food", "water", "power", "road", "sewage", "security", "medical", "education", "park", "waste" })
        {
            if (!c.Supplies.TryGetValue(k, out var s)) continue;
            bool locked = _data.Infrastructure.TryGetValue(k, out var lv) && c.Stage < lv && k != "water";
            sb.AppendLine($"  {InfraName(k),-7} {Bar(s.Ratio)} {s.Have:0} / {s.Need:0}" + (locked ? "  (未解放)" : ""));
        }
        sb.AppendLine($"段階の昇格: 人口 {(c.Stage - 1 < cd.StagePopulationMin.Count ? cd.StagePopulationMin[c.Stage - 1].ToString() : "-")} 人以上 かつ 支持率 {cd.StageSupportMin}% を {cd.StageDays} 日  (現在 {c.StageDays} 日)");
        if (c.Persons.Count > 0) { sb.AppendLine("人物(登用候補)"); foreach (var p in c.Persons.TakeLast(2)) sb.AppendLine($"  {p.Name}  統治 {p.Govern} 生産 {p.Produce} 指揮 {p.Command} 交渉 {p.Negotiate}  忠誠 {p.Loyalty} 野心 {p.Ambition}"); }
        _city.Text = sb.ToString().TrimEnd();
        _notice.Text = _notices.Count == 0 ? "(通知なし)" : string.Join("\n", _notices);
    }
    static string Bar(double r) { int n = (int)Math.Round(Math.Clamp(r, 0, 1) * 10); return new string('|', n) + new string('.', 10 - n); }

    void RefreshSide()
    {
        if (_selected < 0 || _sim.Tick == _sideTick) return;
        if (_sim.Tick - _sideTick < 60 && _sideTick >= 0) return;
        _sideTick = _sim.Tick;
        foreach (var ch in _side.GetChildren()) ch.QueueFree();
        var b = _sim.Factory.Get(_selected); if (b == null) { _sidePanel.Visible = false; return; }
        _sidePanel.Visible = true; var cd = _sim.CityData; var c = _sim.Factory.City!;
        _side.AddChild(Lbl(b.Name, 17, Accent));
        var use = new OptionButton(); int idx = 0, cur = 0;
        foreach (var (k, u) in cd.Uses) { use.AddItem(u.Name, idx); use.SetItemMetadata(idx, k); if (k == b.Use) cur = idx; idx++; }
        use.Selected = cur; int bid = b.Id;
        use.ItemSelected += i => { _sim.Enqueue(new SetBuildingUse(bid, (string)use.GetItemMetadata((int)i))); _sim.Step(); CitySim.Hour(_sim, _sim.Factory.City!); _sideTick = -1; };
        var ur = new HBoxContainer(); ur.AddThemeConstantOverride("separation", 8); ur.AddChild(Lbl("用途")); ur.AddChild(use); _side.AddChild(ur);
        int w = b.Floors[0].W, d = b.Floors[0].D;
        if (b.IsShip)
        {
            var k = Ships.Kinds[b.ShipKind];
            _side.AddChild(Lbl($"{k.Name}  全長 {d} m 幅 {w} m {b.Floors.Count} 層  速度 {k.Speed} m/s  高度 {k.Altitude} m  位置 ({b.X:0}, {b.Y:0})" + (b.MoveTargetX is double mx ? $"  → ({mx:0}, {b.MoveTargetZ:0})" : "  停泊中"), 13));
            var mvb = Btn(_buildMode == BuildMode.ShipMove && _shipMoving == bid ? "移動先をクリック(右クリックでやめる)" : "移動", () => { _shipMoving = bid; SetBuildMode(BuildMode.ShipMove); _sideTick = -1; }, true); mvb.CustomMinimumSize = new Vector2(0, 26); _side.AddChild(mvb);
        }
        else _side.AddChild(Lbl($"床面積 {w}x{d} m  {b.Floors.Count} 階  {(b.RoadConnected ? "街路に接続" : "街路に接していない")}", 13, b.RoadConnected ? Text : Warm));
        _side.AddChild(Lbl(b.Destroyed ? "陥落(動かない)" : $"耐久 {b.Hp:0} / {b.MaxHp:0}   防御壁 段階 {b.WallLevel}" + (b.Floors.Sum(f => f.Structures.OfType<Turret>().Count()) is int nt && nt > 0 ? $"   砲塔 {nt} 基" : ""), 13, b.Destroyed ? Red : b.Hp < b.MaxHp ? Warm : Text));
        if (b.Hp < b.MaxHp)
        {
            long bc = b.IsShip ? (long)Math.Round(b.Floors[0].W * b.Floors[0].D * b.Floors.Count * Ships.Kinds[b.ShipKind].CostPerCell) : Construction.Cost(_sim, b.Use, b.Floors[0].W, b.Floors.Count);
            long rc = (long)Math.Round((b.MaxHp - b.Hp) / b.MaxHp * bc * 0.5);
            var rb = Btn($"{(b.Destroyed ? "復旧" : "修復")}(費用 {rc:N0})", () => { _sim.Enqueue(new RepairBuilding(bid)); _sim.Step(); _sideTick = -1; }, true); rb.CustomMinimumSize = new Vector2(0, 26); rb.Disabled = _sim.Factory.Treasury < rc; _side.AddChild(rb);
        }
        if (b.WallLevel < _sim.Combat.WallMaxLevel)
        {
            long wc = (long)Math.Round(b.Perimeter * _sim.Combat.WallCostPerCell); int next = b.WallLevel + 1;
            var wb = Btn($"防御壁を段階 {next} に(費用 {wc:N0}、耐久 +{b.Perimeter * _sim.Combat.WallHpPerCell:N0})", () => { _sim.Enqueue(new BuildWall(bid, next)); _sim.Step(); if (_sim.Factory.Get(bid)!.WallLevel == next) { RemoveBuildingVisual(bid); AddBuildingVisual(_sim.Factory.Get(bid)!); _kb.Flush(_cityRoot); _selected = bid; } _sideTick = -1; });
            wb.CustomMinimumSize = new Vector2(0, 26); wb.Disabled = _sim.Factory.Treasury < wc; _side.AddChild(wb);
        }
        if (cd.Uses.TryGetValue(b.Use, out var ud) && ud.Supplies.Count > 0)
        {
            int fl = ud.MaxFloors > 0 ? Math.Min(ud.MaxFloors, b.Floors.Count) : b.Floors.Count;
            foreach (var (k, per) in ud.Supplies) _side.AddChild(Lbl($"{InfraName(k)}の供給 {w * d * fl * per:0} 人分" + (ud.StaffPerCell > 0 ? $"  要員 {w * d * fl * ud.StaffPerCell:0} 人" : "")));
        }
        if (b.Use == "factory")
        {
            int supply = b.LastSupply, demand = b.LastDemand;
            _side.AddChild(Lbl($"電力 {supply} kW 供給 / {demand} kW 需要   設備 {b.Floors.Sum(f => f.Structures.Count)} 台"));
            _side.AddChild(Lbl("階ごとの状態", 13, Line40));
            for (int i = b.Floors.Count - 1; i >= 0; i--)
            {
                var f = b.Floors[i]; var machines = f.Structures.OfType<Machine>().ToList();
                int active = machines.Count(m => m.Progress >= 0 && m.PowerPct > 0), waiting = machines.Count(m => m.Progress < 0);
                string recipes = string.Join("・", machines.Where(m => m.Recipe != null).Select(m => _data.Recipes.TryGetValue(m.Recipe!, out var r) ? r.Name : m.Recipe!).Distinct().Take(2));
                int staff = machines.Sum(m => m.Staff.Count), need = machines.Sum(m => _data.Structures[m.DefId].Workers);
                string st = machines.Count == 0 ? (f.Structures.Count == 0 ? "空き" : "搬入・倉庫") : active > 0 ? "稼働" : waiting > 0 ? "入力待ち" : "停止";
                var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
                row.AddChild(Lbl($"{i + 1} 階", 13, Accent)); row.AddChild(Lbl($"{(recipes == "" ? "設備 " + f.Structures.Count + " 台" : recipes)}  {st}  要員 {staff}/{need}"));
                int fi = i; var eb = Btn("編集", () => OpenMicro(bid, fi)); eb.CustomMinimumSize = new Vector2(0, 26); row.AddChild(eb);
                _side.AddChild(row);
            }
            var sq = _sim.Factory.Squads.Where(s => s.HomeBuilding == bid).ToList();
            foreach (var s in sq)
            {
                double hp = s.Members.Where(u => u.Alive).Sum(u => u.Hp), max = s.Members.Where(u => u.Alive).Sum(u => u.MaxHp);
                _side.AddChild(Lbl($"隊 {s.Name}  {s.Alive}/{s.Members.Count} 機  耐久 {(max > 0 ? hp / max * 100 : 0):0}%  {(s.State == SquadState.InHangar ? "格納庫で待機(建物へ給電・修理)" : s.Order == SquadOrder.Collect ? "部品を回収中" : "出撃中")}  要員 {(CitySim.CanDeploy(_sim.Factory, s, cd) ? "充足" : "不足")}", 13, Green));
                var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); int sid = s.Id;
                if (s.State == SquadState.InHangar && _sim.Factory.Loot.Count > 0) { var cb = Btn($"部品を回収に出す(戦場に {_sim.Factory.Loot.Count} か所)", () => { _sim.Enqueue(new Deploy(sid, b.X + 14, b.Y + 6)); _sim.Enqueue(new OrderSquad(sid, SquadOrder.Collect)); _sim.Step(); _sideTick = -1; }); cb.CustomMinimumSize = new Vector2(0, 26); cb.Disabled = !CitySim.CanDeploy(_sim.Factory, s, cd); row.AddChild(cb); }
                if (s.State == SquadState.Field) { var rb = Btn("帰還", () => { _sim.Enqueue(new OrderSquad(sid, SquadOrder.Retreat)); _sim.Step(); _sideTick = -1; }); rb.CustomMinimumSize = new Vector2(0, 26); row.AddChild(rb); }
                if (row.GetChildCount() > 0) _side.AddChild(row);
            }
        }
        if (b.Cargo.Count > 0)
        {
            var groups = b.Cargo.GroupBy(x => x.Item).Select(g => $"{(_data.Items.TryGetValue(g.Key, out var idf) ? idf.Name : g.Key)} {g.Sum(x => x.Count)}");
            _side.AddChild(Lbl($"荷下ろし場: {string.Join("・", groups)}", 13, Warm));
            var chest = b.Floors.Select((f, i) => (f, i)).SelectMany(x => x.f.Structures.OfType<Chest>().Select(ch => (x.i, ch))).FirstOrDefault();
            if (chest.ch != null) { var ub = Btn($"{chest.i + 1} 階の箱へ移す", () => { _sim.Enqueue(new UnloadCargo(bid, chest.i, chest.ch.Id)); _sim.Step(); _sideTick = -1; }); ub.CustomMinimumSize = new Vector2(0, 26); _side.AddChild(ub); }
            else _side.AddChild(Lbl("箱が無いので工場で使えない(ミクロで箱を置く)", 11, Line40));
        }
        else _side.AddChild(Btn("編集(1 階を開く)", () => OpenMicro(bid, 0), true));
    }

    /// <summary>戦艦の位置を毎フレーム合わせる(当たり判定の箱も)。</summary>
    void SyncShips()
    {
        foreach (var b in _sim.Factory.Buildings.Where(b => b.IsShip))
        {
            if (!_buildingNodes.TryGetValue(b.Id, out var n)) continue;
            var k = Ships.Kinds[b.ShipKind]; int w = b.Floors[0].W, d = b.Floors[0].D;
            n.Position = new Vector3((float)b.X + w / 2f, Y + k.Altitude, (float)b.Y + d / 2f); n.Visible = !b.Destroyed;
            if (b.MoveTargetX is double tx) { double dx = tx - b.X, dz = b.MoveTargetZ!.Value - b.Y; if (dx * dx + dz * dz > 1) n.RotationDegrees = new Vector3(0, Mathf.RadToDeg((float)Math.Atan2(dx, dz)), 0); }
            int i = _hit.FindIndex(h => h.id == b.Id); if (i >= 0) _hit[i] = (new Aabb(new Vector3((float)b.X, Y, (float)b.Y), new Vector3(w, k.Altitude + 12, d)), b.Id);
        }
    }

    void RefreshMarks()
    {
        foreach (var (mid, n) in _idleMechs) { var sq = _sim.Factory.Squads.FirstOrDefault(s => s.Members.Any(u => u.MechId == mid)); n.Visible = sq == null || sq.State == SquadState.InHangar; }
        foreach (var (box, id) in _hit)
        {
            bool sel = id == _selected;
            if (!_marks.TryGetValue(id, out var n))
            {
                n = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(box.Size.X + 0.6f, 0.25f, box.Size.Z + 0.6f) }, Position = box.Position + new Vector3(box.Size.X / 2, 0.15f, box.Size.Z / 2), MaterialOverride = new StandardMaterial3D { AlbedoColor = Accent, EmissionEnabled = true, Emission = Accent, EmissionEnergyMultiplier = 2f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } };
                _cityRoot.AddChild(n); _marks[id] = n;
            }
            n.Visible = sel;
        }
    }

    void Select(int id) { _selected = id; _sideTick = -1; }

    // ---------- ミクロへ ----------
    void OpenMicro(int building, int floor = 0)
    {
        if (_micro != null) return;
        MicroEditor.ExternalSim = _sim; MicroEditor.ExternalBuilding = building; MicroEditor.ExternalFloor = floor;
        MicroEditor.OnBack = CloseMicro;
        _cityRoot.Visible = false; _ui.Visible = false;
        _micro = new MicroEditor(); AddChild(_micro);
    }
    void OpenMicroDeferred(int building) => OpenMicro(building, 0);
    void CloseMicro()
    {
        if (_micro == null) return;
        _micro.QueueFree(); _micro = null;
        MicroEditor.ExternalSim = null; MicroEditor.OnBack = null;
        _cityRoot.Visible = true; _ui.Visible = true; _cam.MakeCurrent(); _sideTick = -1;
        foreach (var n in _marks.Values) n.QueueFree(); _marks.Clear();
    }

    // ---------- 画面の板 ----------
    void BuildHud()
    {
        _ui = new CanvasLayer(); AddChild(_ui);
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = MakeTheme() }; root.SetAnchorsPreset(Control.LayoutPreset.FullRect); _ui.AddChild(root);
        // 上の帯
        var top = new HBoxContainer(); top.AddThemeConstantOverride("separation", 16);
        _top = Lbl("", 14); top.AddChild(_top);
        foreach (var (t, sp) in new[] { ("停止", 0), ("1x", 1), ("2x", 2), ("4x", 4) }) { int s = sp; var bt = Btn(t, () => _speed = s); bt.CustomMinimumSize = new Vector2(0, 28); top.AddChild(bt); }
        top.AddChild(new VSeparator());
        foreach (var (t, k) in new[] { ("登用", "recruit"), ("政治", "politics") }) { string kk = k; var bt = Btn(t, () => { if (_overlayKind == kk) CloseOverlay(); else OpenOverlay(kk); }); bt.CustomMinimumSize = new Vector2(0, 28); top.AddChild(bt); }
        var topPanel = Panel(top); topPanel.Position = new Vector2(16, 16); root.AddChild(topPanel);
        // 左: 都市の板
        var left = new VBoxContainer(); left.AddThemeConstantOverride("separation", 6);
        left.AddChild(Lbl("都市", 15, Accent));
        _city = Lbl("", 12); left.AddChild(_city);
        left.AddChild(Lbl("政策", 13, Line40));
        var c = _sim.Factory.City!;
        _tax = Slider(left, "税率", c.TaxRate, _sim.CityData.MaxTaxRate, v => { _sim.Enqueue(new SetTaxRate(v)); _sim.Step(); });
        _draft = Slider(left, "徴兵率", c.ConscriptionRate, _sim.CityData.MaxConscriptionRate, v => { _sim.Enqueue(new SetConscriptionRate(v)); _sim.Step(); });
        left.AddChild(Lbl("通知", 13, Line40));
        _notice = Lbl("", 12); left.AddChild(_notice);
        var leftPanel = Panel(left); leftPanel.Position = new Vector2(16, 72); leftPanel.CustomMinimumSize = new Vector2(540, 0); root.AddChild(leftPanel);
        // 右: 選択した建物
        _side = new VBoxContainer(); _side.AddThemeConstantOverride("separation", 6);
        _sidePanel = Panel(_side); _sidePanel.AnchorLeft = 1; _sidePanel.AnchorRight = 1; _sidePanel.OffsetLeft = -420; _sidePanel.OffsetRight = -16; _sidePanel.OffsetTop = 72; _sidePanel.Visible = false; root.AddChild(_sidePanel);
        BuildPalette(root);
        var keys = Lbl("左クリック 建物を選ぶ   中ドラッグ 回転   ホイール 距離", 12, Line40); var kp = Panel(keys); kp.AnchorTop = 1; kp.AnchorBottom = 1; kp.OffsetTop = -52; kp.OffsetBottom = -16; kp.OffsetLeft = 16; root.AddChild(kp);
    }

    HSlider Slider(VBoxContainer parent, string name, double value, double max, Action<double> onChange)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
        var lab = Lbl($"{name} {value * 100:0}%", 13); lab.CustomMinimumSize = new Vector2(110, 0);
        var sl = new HSlider { MinValue = 0, MaxValue = max, Step = 0.01, Value = value, CustomMinimumSize = new Vector2(300, 20), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        sl.ValueChanged += v => { lab.Text = $"{name} {v * 100:0}%"; onChange(v); };
        row.AddChild(lab); row.AddChild(sl); parent.AddChild(row); return sl;
    }

    static StyleBoxFlat Box(Color bg, Color border, int bw = 1, int pad = 8)
    {
        var s = new StyleBoxFlat { BgColor = bg, BorderColor = border, ContentMarginLeft = pad, ContentMarginRight = pad, ContentMarginTop = pad, ContentMarginBottom = pad }; s.SetBorderWidthAll(bw); return s;
    }
    static Theme MakeTheme()
    {
        var th = new Theme { DefaultFont = new SystemFont { FontNames = new[] { "Yu Gothic UI", "Meiryo", "sans-serif" } }, DefaultFontSize = 13 };
        th.SetColor("font_color", "Label", Text);
        foreach (var t in new[] { "Button", "OptionButton" })
        {
            th.SetStylebox("normal", t, Box(Navy, Cyan, 1, 8)); th.SetStylebox("hover", t, Box(new Color(18 / 255f, 40 / 255f, 84 / 255f, 0.9f), Accent, 1, 8)); th.SetStylebox("pressed", t, Box(new Color(24 / 255f, 56 / 255f, 110 / 255f, 0.95f), Accent, 2, 8)); th.SetStylebox("focus", t, new StyleBoxEmpty());
            th.SetColor("font_color", t, Text); th.SetColor("font_hover_color", t, Text); th.SetColor("font_pressed_color", t, Text);
        }
        th.SetStylebox("panel", "PopupMenu", Box(NavyDeep, Cyan, 1, 4)); th.SetStylebox("hover", "PopupMenu", Box(new Color(24 / 255f, 56 / 255f, 110 / 255f, 1f), Accent, 1, 4)); th.SetColor("font_color", "PopupMenu", Text); th.SetColor("font_hover_color", "PopupMenu", Text);
        th.SetStylebox("panel", "PanelContainer", Box(Navy, Cyan, 1, 12));
        th.SetStylebox("slider", "HSlider", Box(NavyDeep, Line40, 1, 0)); th.SetStylebox("grabber_area", "HSlider", Box(Accent, Accent, 0, 0)); th.SetStylebox("grabber_area_highlight", "HSlider", Box(Accent, Accent, 0, 0));
        return th;
    }
    static Label Lbl(string text, int size = 13, Color? color = null) { var l = new Label { Text = text }; l.AddThemeFontSizeOverride("font_size", size); l.AddThemeColorOverride("font_color", color ?? Text); return l; }
    static Button Btn(string text, Action onPress, bool accent = false) { var b = new Button { Text = text, CustomMinimumSize = new Vector2(0, 32) }; if (accent) b.AddThemeStyleboxOverride("normal", Box(new Color(18 / 255f, 40 / 255f, 84 / 255f, 0.9f), Accent, 2, 8)); b.Pressed += onPress; return b; }
    static PanelContainer Panel(Control inner) { var p = new PanelContainer(); p.AddChild(inner); return p; }

    // ---------- 視点と入力 ----------
    void PlaceCamera()
    {
        _cam.Position = _pivot + new Vector3(Mathf.Cos(_elev) * Mathf.Sin(_yaw), Mathf.Sin(_elev), Mathf.Cos(_elev) * Mathf.Cos(_yaw)) * _dist;
        _cam.LookAt(_pivot);
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (_micro != null) return;
        if (ev is InputEventMouseMotion mv) UpdateGhost(mv.Position);
        if (ev is InputEventMouseButton mb0 && mb0.Pressed && HandleBuildClick(mb0)) return;
        if (ev is InputEventMouseMotion mm && (mm.ButtonMask & MouseButtonMask.Middle) != 0) { _yaw -= mm.Relative.X * 0.005f; _elev = Mathf.Clamp(_elev + mm.Relative.Y * 0.005f, 0.2f, 1.5f); PlaceCamera(); }
        if (ev is InputEventMouseButton mb && mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp) { _dist = Mathf.Max(30, _dist * 0.9f); PlaceCamera(); }
            if (mb.ButtonIndex == MouseButton.WheelDown) { _dist = Mathf.Min(900, _dist / 0.9f); PlaceCamera(); }
            if (mb.ButtonIndex == MouseButton.Left) Pick(mb.Position);
        }
    }

    void Pick(Vector2 screen)
    {
        var from = _cam.ProjectRayOrigin(screen); var dir = _cam.ProjectRayNormal(screen);
        int best = -1; float bestT = float.MaxValue;
        foreach (var (box, id) in _hit)
        {
            if (RayHit(box, from, dir, out float t) && t < bestT) { bestT = t; best = id; }
        }
        Select(best);
    }
    static bool RayHit(Aabb b, Vector3 o, Vector3 d, out float t)
    {
        float tmin = 0, tmax = float.MaxValue; var min = b.Position; var max = b.End;
        for (int i = 0; i < 3; i++)
        {
            float di = d[i]; if (Mathf.Abs(di) < 1e-6f) { if (o[i] < min[i] || o[i] > max[i]) { t = 0; return false; } continue; }
            float t1 = (min[i] - o[i]) / di, t2 = (max[i] - o[i]) / di; if (t1 > t2) (t1, t2) = (t2, t1);
            tmin = Mathf.Max(tmin, t1); tmax = Mathf.Min(tmax, t2); if (tmin > tmax) { t = 0; return false; }
        }
        t = tmin; return true;
    }
}
