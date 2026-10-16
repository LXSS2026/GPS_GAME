using Godot;
using GPS.Sim;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>都市の画面に重ねる 登用画面(G6)と政治画面(G7 の左半分)。計算は GPS.Sim の PoliticsSim。</summary>
public partial class CityScreen
{
    PanelContainer? _overlay;
    string _overlayKind = "";
    string _pickOffice = "head";
    int _pickPerson = -1;
    long _overlayTick = -1;
    readonly Dictionary<string, HSlider> _policy = new();

    void OpenOverlay(string kind)
    {
        CloseOverlay(); _overlayKind = kind; _overlayTick = -1;
        _overlay = new PanelContainer(); _overlay.AnchorRight = 1; _overlay.OffsetLeft = 16; _overlay.OffsetTop = 72; _overlay.OffsetRight = -16;   // 高さは中身に合わせる
        _ui.GetChild<Control>(0).AddChild(_overlay);
        foreach (var n in _ui.GetChild<Control>(0).GetChildren().OfType<PanelContainer>()) if (n != _overlay && n.Position.Y > 40) n.Visible = false;
        RefreshOverlay(true);
    }
    void CloseOverlay()
    {
        if (_overlay == null) return;
        _overlay.QueueFree(); _overlay = null; _overlayKind = "";
        foreach (var n in _ui.GetChild<Control>(0).GetChildren().OfType<PanelContainer>()) n.Visible = true;
        _sidePanel.Visible = _selected >= 0 || _selectedSquad >= 0;
    }

    void RefreshOverlay(bool force = false)
    {
        if (_overlay == null) return;
        if (!force && _sim.Tick - _overlayTick < Clock.TicksPerSecond) return;
        _overlayTick = _sim.Tick;
        foreach (var ch in _overlay.GetChildren()) ch.QueueFree();
        if (_overlayKind == "recruit") BuildRecruit(_overlay); else if (_overlayKind == "army") BuildArmy(_overlay); else BuildPolitics(_overlay);
    }

    static string Risk(Person p) => p.Ambition - p.Loyalty >= 5 ? "高" : p.Ambition - p.Loyalty >= 1 ? "中" : "低";
    static Color RiskColor(string r) => r == "高" ? Red : r == "中" ? Warm : Green;
    string JobName(string job) => _sim.PoliticsData.Jobs.TryGetValue(job, out var n) ? n : job;
    static int Ability(Person p, string ability) => ability switch { "govern" => p.Govern, "produce" => p.Produce, "command" => p.Command, _ => p.Negotiate };
    static string AbilityName(string a) => a switch { "govern" => "統治", "produce" => "生産", "command" => "指揮", _ => "交渉" };

    // ---------- 登用画面(G6) ----------
    void BuildRecruit(Control root)
    {
        var f = _sim.Factory; var c = f.City!; var pd = _sim.PoliticsData; var pol = PoliticsSim.Of(f);
        var slots = PoliticsSim.Slots(f, pd); var persons = c.Persons.ToDictionary(p => p.Id);
        var col = new VBoxContainer(); col.AddThemeConstantOverride("separation", 8); root.AddChild(col);
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 16);
        head.AddChild(Lbl($"登用    {StageName(c.Stage)} {c.Name}    人口 {c.Population:0}    支持 {c.Support:0}%", 16, Accent));
        var back = Btn("地図へ戻る", CloseOverlay); back.CustomMinimumSize = new Vector2(0, 28); head.AddChild(back); col.AddChild(head);
        var body = new HBoxContainer(); body.AddThemeConstantOverride("separation", 24); col.AddChild(body);
        // 左: 役職と空席
        var left = new VBoxContainer(); left.AddThemeConstantOverride("separation", 4); left.CustomMinimumSize = new Vector2(360, 0); body.AddChild(left);
        left.AddChild(Lbl("役職(空席は橙)", 13, Line40));
        if (!slots.Any(s => s.Id == _pickOffice)) _pickOffice = slots.Count > 0 ? slots[0].Id : "";
        foreach (var s in slots)
        {
            string holder = pol.Offices.TryGetValue(s.Id, out var pid) && persons.TryGetValue(pid, out var hp) ? $"{hp.Name}  ({AbilityName(s.Ability)} {Ability(hp, s.Ability)})" : "空席";
            var b = Btn($"{s.Name}    {holder}", () => { _pickOffice = s.Id; _pickPerson = -1; RefreshOverlay(true); }, s.Id == _pickOffice);
            b.Alignment = HorizontalAlignment.Left; if (holder == "空席") b.AddThemeColorOverride("font_color", Warm); left.AddChild(b);
        }
        string next = c.Stage switch { 1 => "町で 隊長(隊 3 つごと)・区長(住居 10 棟ごと)、都市で 市長・将軍・外交官、国家で 閣僚 4・元帥", 2 => "都市で 市長・将軍・外交官、国家で 閣僚 4・元帥", 3 => "国家で 閣僚 4・元帥", _ => "全て解放済み" };
        left.AddChild(Lbl($"段階が進むと増える役職: {next}", 11, Line40));
        left.AddChild(Lbl("役職の効果(G19 #2): (能力 − 5) × 係数。空席は能力 0 扱い", 11, Line40));
        var slot = slots.FirstOrDefault(s => s.Id == _pickOffice);
        // 右: 候補
        var right = new VBoxContainer(); right.AddThemeConstantOverride("separation", 6); right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; body.AddChild(right);
        if (slot == null) { right.AddChild(Lbl("役職がありません")); return; }
        right.AddChild(Lbl($"{slot.Name} の候補({AbilityName(slot.Ability)} の高い順)", 14, Accent));
        var grid = new GridContainer { Columns = 11 }; grid.AddThemeConstantOverride("h_separation", 14); grid.AddThemeConstantOverride("v_separation", 4); right.AddChild(grid);
        foreach (var h in new[] { "", "名前", "統治", "生産", "指揮", "交渉", "忠誠", "野心", "不満", "現在", "" }) grid.AddChild(Lbl(h, 12, Line40));
        var cands = c.Persons.Where(p => p.Office == null || p.Office == slot.Id).OrderByDescending(p => Ability(p, slot.Ability)).ThenByDescending(p => p.Loyalty).ToList();
        foreach (var p in cands)
        {
            bool sel = p.Id == _pickPerson, holder = p.Office == slot.Id;
            grid.AddChild(new Portrait(p, 36));
            var nameBtn = Btn(p.Name + (holder ? "(現職)" : ""), () => { _pickPerson = p.Id; RefreshOverlay(true); }, sel); nameBtn.CustomMinimumSize = new Vector2(150, 26); nameBtn.Alignment = HorizontalAlignment.Left; grid.AddChild(nameBtn);
            foreach (var v in new[] { p.Govern, p.Produce, p.Command, p.Negotiate }) grid.AddChild(Lbl(v.ToString(), 13));
            grid.AddChild(Lbl(p.Loyalty.ToString(), 13, p.Loyalty <= 3 ? Warm : Text)); grid.AddChild(Lbl(p.Ambition.ToString(), 13, p.Ambition >= 7 ? Warm : Text)); grid.AddChild(Lbl($"{p.Discontent:0.0}", 13, p.Discontent >= 6 ? Red : Text));
            grid.AddChild(Lbl(holder ? slot.Name : pol.Factions.Any(x => x.LeaderId == p.Id) ? "派閥の首領" : JobName(p.Job), 12));
            grid.AddChild(Lbl(p.Office != null && !holder ? p.Office : "", 11, Line40));
        }
        if (cands.Count == 0) right.AddChild(Lbl("候補がいません(人物は 30 日ごとに現れます)", 12, Warm));
        // 見込み
        var pick = cands.FirstOrDefault(p => p.Id == _pickPerson);
        if (pick != null)
        {
            int cur = pol.Offices.TryGetValue(slot.Id, out var hid) && persons.TryGetValue(hid, out var hp2) ? Ability(hp2, slot.Ability) : 0;
            int a = Ability(pick, slot.Ability);
            right.AddChild(Lbl($"{pick.Name} を {slot.Name} にした場合", 14, Accent));
            right.AddChild(Lbl(EffectText(slot, cur, a), 13));
            string risk = Risk(pick); right.AddChild(Lbl($"離反リスク(野心 {pick.Ambition} / 忠誠 {pick.Loyalty})  {risk}", 13, RiskColor(risk)));
            right.AddChild(Lbl($"元の職の穴  {JobName(pick.Job)} 1 名減", 13));
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10);
            if (pick.Office == slot.Id) row.AddChild(Btn("解任", () => { _sim.Enqueue(new Dismiss(slot.Id)); _sim.Step(); _pickPerson = -1; RefreshOverlay(true); }));
            else row.AddChild(Btn($"{pick.Name} を登用", () => { _sim.Enqueue(new Appoint(slot.Id, pick.Id)); _sim.Step(); RefreshOverlay(true); }, true));
            right.AddChild(row);
        }
        right.AddChild(Lbl("人物は市民の中から能力と性格を持って現れ、登用されないまま 12 か月で去る。忠誠が低く野心の高い人物は、不満が溜まると派閥を作る(政治画面)", 11, Line40));
    }

    string EffectText(OfficeSlot slot, int cur, int a)
    {
        var e = _sim.PoliticsData.Effects.GetValueOrDefault(slot.Kind) ?? new();
        double d = a - cur;
        return slot.Kind switch
        {
            "head" => $"支持率の目標 {d * e.GetValueOrDefault("supportPerPoint"):+0.0;-0.0}   税収 {d * e.GetValueOrDefault("taxPerPoint") * 100:+0;-0}%",
            "factory" => $"全工場の失敗率 {-d * e.GetValueOrDefault("failurePerPoint") * 100:+0;-0}%   加工速度 {d * e.GetValueOrDefault("speedPerPoint") * 100:+0;-0}%",
            "military" => $"隊の命中率 {d * e.GetValueOrDefault("hitPerPoint") * 100:+0;-0}%   退却の閾値 {-d * e.GetValueOrDefault("retreatPerPoint") * 100:+0;-0}%   徴兵の充足 {d * e.GetValueOrDefault("conscriptionPerPoint") * 100:+0;-0}%",
            "negotiate" => $"交易の売値 {d * e.GetValueOrDefault("pricePerPoint") * 100:+0;-0}%(外交の成功率は手順 5)",
            "engineer" => $"機体の高品質率 ×{1 + (a - 5) * e.GetValueOrDefault("highQualityPerPoint"):0.0}(今 ×{1 + (cur - 5) * e.GetValueOrDefault("highQualityPerPoint"):0.0})",
            "marshal" => $"隊の命中率 {d * e.GetValueOrDefault("hitPerPoint") * 100:+0;-0}%",
            "district" => $"支持率の目標 {(cur == 0 ? 2 * e.GetValueOrDefault("supportPerSlot") : 0):+0.0;-0.0}(空席の減点が消える)",
            _ => $"隊の命中率 {(cur == 0 ? 2 * e.GetValueOrDefault("hitPerSlot") * 100 : 0):+0;-0}%(空席の減点が消える)",
        };
    }

    // ---------- 政治画面(G7 の左半分) ----------
    void BuildPolitics(Control root)
    {
        var f = _sim.Factory; var c = f.City!; var pd = _sim.PoliticsData; var cd = _sim.CityData; var pol = PoliticsSim.Of(f);
        var col = new VBoxContainer(); col.AddThemeConstantOverride("separation", 8); root.AddChild(col);
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 16);
        head.AddChild(Lbl($"政治    {StageName(c.Stage)} {c.Name}    支持 {c.Support:0}%({BandName(c.Band)})    国庫 {f.Treasury:N0}    収支 {c.LastTax - c.LastUpkeep - c.LastPay:+0;-0} / 日" + (pol.Rebellion ? "    反乱中" : ""), 16, pol.Rebellion ? Red : Accent));
        var back = Btn("地図へ戻る", CloseOverlay); back.CustomMinimumSize = new Vector2(0, 28); head.AddChild(back); col.AddChild(head);
        var body = new HBoxContainer(); body.AddThemeConstantOverride("separation", 28); col.AddChild(body);
        // 左: 層ごとの支持率・不満の原因・派閥
        var left = new VBoxContainer(); left.AddThemeConstantOverride("separation", 6); left.CustomMinimumSize = new Vector2(520, 0); body.AddChild(left);
        left.AddChild(Lbl("支持率の内訳(層ごと。目標値)", 14, Accent));
        var g = new GridContainer { Columns = 3 }; g.AddThemeConstantOverride("h_separation", 14); left.AddChild(g);
        foreach (var (k, st) in pol.Strata)
        {
            var def = pd.Strata[k];
            g.AddChild(Lbl($"{def.Name} ({st.Count:0} 人)", 13)); g.AddChild(Lbl(Bar(st.Target / 100), 13, st.Target < 30 ? Red : st.Target < 50 ? Warm : Green)); g.AddChild(Lbl($"{st.Target:0}%", 13));
        }
        left.AddChild(Lbl("不満の原因(目標を下げている項)", 14, Accent));
        var w = cd.Support; var causes = new List<(string, double)>
        {
            ("失業", w["unemployment"] * c.Unemployment), ("税率", w["tax"] * (pol.HasDecree("taxcut") ? 0 : c.TaxRate)), ("徴兵率", w["conscription"] * c.ConscriptionRate),
            ("食料の不足", w["food"] * Math.Max(0, 1 - c.Ratio("food"))), ("水の不足", w["water"] * Math.Max(0, 1 - c.Ratio("water"))), ("治安の不足", w["security"] * (1 - c.Ratio("security"))),
            ("廃棄物", w["waste"] * (1 - c.Ratio("waste"))), ("公害", c.Pollution), ("役職の空席と能力", Math.Max(0, -pol.SupportBonus)), ("派閥", pol.FactionPenalty), ("製造ロボットの導入(工場労働者)", pd.Strata["worker"].RobotShare * pol.RobotShare), ("国庫の赤字", f.Treasury < 0 ? w["treasuryNegative"] : 0),
        };
        foreach (var (n, v) in causes.Where(x => x.Item2 > 0.05).OrderByDescending(x => x.Item2).Take(6)) left.AddChild(Lbl($"  {n}  −{v:0.0}", 13, v >= 10 ? Red : Text));
        if (!causes.Any(x => x.Item2 > 0.05)) left.AddChild(Lbl("  (なし)", 13));
        left.AddChild(Lbl("派閥", 14, Accent));
        if (pol.Factions.Count == 0) left.AddChild(Lbl("  (なし)", 13));
        foreach (var x in pol.Factions)
        {
            var leader = c.Persons.FirstOrDefault(p => p.Id == x.LeaderId);
            bool met = PoliticsSim.DemandMet(x.Demand, _sim, c, pol);
            left.AddChild(Lbl($"  {leader?.Name ?? "#" + x.LeaderId} 派  {x.Supporters:0} 人  要求: {pd.Demands[x.Demand]}  {x.Days} 日目  {(met ? $"要求を満たして {x.MetDays} 日(7 日で解消)" : "未対応")}  放置 60 日で離反", 12, met ? Green : Warm));
        }
        if (pol.Exiles.Count > 0) { left.AddChild(Lbl("亡命者(外交の種。手順 5)", 13, Line40)); foreach (var e in pol.Exiles.TakeLast(3)) left.AddChild(Lbl($"  {e.Name}  {e.Count:0} 人  {e.Day} 日目  要求: {pd.Demands.GetValueOrDefault(e.Demand, e.Demand)}", 12)); }
        // 右: 予算と政策・布告・見込み
        var right = new VBoxContainer(); right.AddThemeConstantOverride("separation", 6); right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; body.AddChild(right);
        right.AddChild(Lbl("予算と政策", 14, Accent));
        _policy.Clear();
        _policy["tax"] = Slider(right, "税率", c.TaxRate, cd.MaxTaxRate, v => { _sim.Enqueue(new SetTaxRate(v)); _sim.Step(); _overlayTick = -1; });
        _policy["draft"] = Slider(right, "徴兵率", c.ConscriptionRate, cd.MaxConscriptionRate, v => { _sim.Enqueue(new SetConscriptionRate(v)); _sim.Step(); _overlayTick = -1; });
        _policy["infra"] = Slider(right, "インフラ投資", pol.InfraInvest / 100.0, 1, v => { _sim.Enqueue(new SetBudget((int)Math.Round(v * 100), pol.Military)); _sim.Step(); _overlayTick = -1; });
        _policy["military"] = Slider(right, "軍事費", pol.Military / 100.0, 1, v => { _sim.Enqueue(new SetBudget(pol.InfraInvest, (int)Math.Round(v * 100))); _sim.Step(); _overlayTick = -1; });
        _policy["robot"] = Slider(right, "製造ロボットの導入率", pol.RobotCap / 100.0, 1, v => { _sim.Enqueue(new SetRobotCap((int)Math.Round(v * 100))); _sim.Step(); _overlayTick = -1; });
        right.AddChild(Lbl($"インフラ投資は供給能力の係数(0 で × 0.5)と維持費。軍事費は徴兵の充足(50 未満で下がる)・給与・弾薬補給(50 未満で止まる)。導入率は設備の要員のうち製造ロボット(人間大)にしてよい上限で、今 {pol.RobotShare * 100:0}%", 11, Line40));
        right.AddChild(Lbl("布告", 14, Accent));
        var drow = new HBoxContainer(); drow.AddThemeConstantOverride("separation", 10); right.AddChild(drow);
        foreach (var (k, d) in pd.Decrees)
        {
            var active = pol.Decrees.FirstOrDefault(x => x.Kind == k);
            string cost = d.CostPerPersonPerDay > 0 ? $"人口 × {d.CostPerPersonPerDay:0} / 日" : d.CostPerPerson > 0 ? $"人口 × {d.CostPerPerson:0}" : "費用なし";
            var b = Btn(active != null ? $"{d.Name}(あと {active.DaysLeft} 日)" : $"{d.Name}  {cost}", () => { _sim.Enqueue(new Decree(k)); _sim.Step(); RefreshOverlay(true); }, active == null); b.Disabled = active != null; drow.AddChild(b);
        }
        // 見込み(次の月)
        var (t0, m0, c0, need) = PoliticsSim.Forecast(_sim, c.TaxRate, c.ConscriptionRate, pol.InfraInvest, pol.Military);
        right.AddChild(Lbl("見込み(次の月。今の政策で)", 14, Accent));
        right.AddChild(Lbl($"支持率 {c.Support:0}% → 目標 {t0:0}%    国庫 {m0:+#,0;-#,0}    軍の要員の充足 {c0:0} / {need:0}", 13));
        var (t1, m1, c1, _) = PoliticsSim.Forecast(_sim, Math.Max(0, c.TaxRate - 0.05), c.ConscriptionRate, pol.InfraInvest, pol.Military);
        var (t2, m2, c2, _) = PoliticsSim.Forecast(_sim, c.TaxRate, Math.Min(cd.MaxConscriptionRate, c.ConscriptionRate + 0.1), pol.InfraInvest, pol.Military);
        right.AddChild(Lbl($"税率 −5% なら: 支持 {t1:0}%  国庫 {m1:+#,0;-#,0}      徴兵率 +10% なら: 支持 {t2:0}%  要員 {c2:0} / {need:0}", 12, Line40));
        BuildDiplomacy(right);
    }
}
