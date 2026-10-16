using Godot;
using GPS.Sim;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>軍の画面(G5 の上の帯「軍」)。隊の一覧と命令(出撃・攻撃・回収・帰還・自爆)。Going Medieval 方式: 隊の単位で指揮し、機体は制御基板で戦う。</summary>
public partial class CityScreen
{
    int _armyPickSquad = -1, _armyPick = -1;
    readonly HashSet<int> _armySel = new();

    void BuildArmy(Control root)
    {
        var f = _sim.Factory; var c = f.City!; var cd = _sim.CityData;
        var col = new VBoxContainer(); col.AddThemeConstantOverride("separation", 8); root.AddChild(col);
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 16);
        head.AddChild(Lbl($"軍    {StageName(c.Stage)} {c.Name}    軍の要員 {c.Conscripts:0} / {c.CrewNeed:0}(徴兵率 {c.ConscriptionRate * 100:0}%)    戦場に残る部品 {f.Loot.Count} か所    戦力 {DiplomacySim.PlayerStrength(f):0.0}", 16, Accent));
        var back = Btn("地図へ戻る", CloseOverlay); back.CustomMinimumSize = new Vector2(0, 28); head.AddChild(back); col.AddChild(head);
        var mine = f.Squads.Where(s => s.Faction == 1).ToList();
        if (mine.Count == 0) { col.AddChild(Lbl("隊がありません。機体を製造して格納庫で隊を編成します", 13, Warm)); return; }
        var g = new GridContainer { Columns = 12 }; g.AddThemeConstantOverride("h_separation", 14); g.AddThemeConstantOverride("v_separation", 4); col.AddChild(g);
        foreach (var h in new[] { "", "隊", "隊長", "軍", "系統", "世代", "機数", "耐久", "状態", "命令", "要員", "帰還先" }) g.AddChild(Lbl(h, 12, Line40));
        if (!mine.Any(s => s.Id == _armyPickSquad)) _armyPickSquad = mine[0].Id;
        foreach (var s in mine)
        {
            var cb = new CheckBox { ButtonPressed = _armySel.Contains(s.Id) }; int csid = s.Id; cb.Toggled += on => { if (on) _armySel.Add(csid); else _armySel.Remove(csid); }; g.AddChild(cb);
            var nb = Btn(s.Name, () => { _armyPickSquad = s.Id; SetFocus(s.X, s.Y); RefreshOverlay(true); }, s.Id == _armyPickSquad); nb.CustomMinimumSize = new Vector2(150, 26); nb.Alignment = HorizontalAlignment.Left; g.AddChild(nb);
            var cmdr = Armies.Commander(f, s.CommanderId); g.AddChild(Lbl(cmdr != null ? $"{cmdr.Name}(指揮 {cmdr.Command})" : "なし", 12, cmdr != null ? Text : Warm));
            g.AddChild(Lbl(Armies.ArmyOf(f, s)?.Name ?? "-", 12));
            g.AddChild(Lbl(s.Archetype switch { "heavy" => "重装", "fast" => "高速", _ => "汎用" }, 13)); g.AddChild(Lbl($"第 {s.Generation}", 13)); g.AddChild(Lbl($"{s.Alive}/{s.Members.Count}", 13));
            double hp = s.Members.Where(u => u.Alive).Sum(u => u.Hp), max = s.Members.Where(u => u.Alive).Sum(u => u.MaxHp); int pct = max > 0 ? (int)Math.Round(hp / max * 100) : 0;
            g.AddChild(Lbl($"{pct}%", 13, pct < 30 ? Red : pct < 70 ? Warm : Green));
            g.AddChild(Lbl(s.State == SquadState.InHangar ? "格納庫" : "出撃中", 13));
            g.AddChild(Lbl(s.Order switch { SquadOrder.Attack => s.TargetBuilding >= 0 ? $"攻撃 建物 #{s.TargetBuilding}" : s.TargetSquad >= 0 ? $"攻撃 隊 #{s.TargetSquad}" : "攻撃", SquadOrder.Move => $"移動 ({s.OrderX:0}, {s.OrderY:0})", SquadOrder.Retreat => "帰還", SquadOrder.Collect => "回収", SquadOrder.SelfDestruct => "自爆", _ => "待機" }, 13));
            bool crew = CitySim.CanDeploy(f, s, cd); g.AddChild(Lbl(crew ? "充足" : "不足", 13, crew ? Green : Warm));
            g.AddChild(Lbl(f.Get(s.HomeBuilding)?.Name ?? "-", 12));
        }
        var pick = mine.First(s => s.Id == _armyPickSquad); int sid = pick.Id; var hb = f.Get(pick.HomeBuilding);
        // 隊長の任命
        var free = c.Persons.Where(p => p.Office == null && !PoliticsSim.Of(f).Offices.ContainsValue(p.Id)).OrderByDescending(p => p.Command).ToList();
        var crow = new HBoxContainer(); crow.AddThemeConstantOverride("separation", 8);
        var pc = Armies.Commander(f, pick.CommanderId);
        crow.AddChild(Lbl(pc != null ? $"隊長 {pc.Name}(指揮 {pc.Command}、命中 {(pc.Command - 5) * 2:+0;-0}%)" : "隊長なし(出撃できない)", 13, pc != null ? Green : Warm));
        if (pc != null) { var db = Btn("解任", () => { _sim.Enqueue(new DismissCommander("squad", sid)); _sim.Step(); RefreshOverlay(true); }); db.CustomMinimumSize = new Vector2(0, 26); crow.AddChild(db); }
        if (free.Count > 0)
        {
            var opt = new OptionButton(); for (int i = 0; i < free.Count; i++) { opt.AddItem($"{free[i].Name}  指揮 {free[i].Command}", i); }
            var ab = Btn("隊長に任命", () => { if (opt.Selected >= 0) { _sim.Enqueue(new AssignCommander("squad", sid, free[opt.Selected].Id)); _sim.Step(); RefreshOverlay(true); } }, true); ab.CustomMinimumSize = new Vector2(0, 26);
            crow.AddChild(opt); crow.AddChild(ab);
        }
        else crow.AddChild(Lbl("任命できる人物がいない(登用画面の候補)", 12, Line40));
        col.AddChild(crow);
        col.AddChild(Lbl($"{pick.Name} への命令", 14, Accent));
        foreach (var u in pick.Members)
        {
            var m = f.Mechs.FirstOrDefault(x => x.Id == u.MechId);
            col.AddChild(Lbl($"  #{u.MechId}  品質 {m?.Quality}  耐久 {(u.Alive ? u.Hp / Math.Max(1, u.MaxHp) * 100 : 0):0}%  電力 {(u.EnergyCap > 0 ? u.Energy * 100.0 / u.EnergyCap : 0):0}%  弾薬 {u.Ammo}  {(m?.Program != null ? "制御基板 " + m.Program.Name : "制御基板なし(既定: 交戦・耐久 25% で退却)")}" + (u.Alive ? "" : "  撃破"), 12));
        }
        var acts = new HBoxContainer(); acts.AddThemeConstantOverride("separation", 8); col.AddChild(acts);
        void Act(string text, Action a, bool enabled = true) { var b = Btn(text, () => { a(); _sim.Step(); RefreshOverlay(true); }); b.CustomMinimumSize = new Vector2(0, 28); b.Disabled = !enabled; acts.AddChild(b); }
        bool inHangar = pick.State == SquadState.InHangar, crewOk = CitySim.CanDeploy(f, pick, cd);
        if (inHangar)
        {
            Act("出撃(格納庫の前で待機)", () => _sim.Enqueue(new Deploy(sid, (hb?.X ?? 0) + 14, (hb?.Y ?? 0) + 6)), crewOk && pick.Alive > 0);
            if (f.Loot.Count > 0) Act("出撃して部品を回収", () => { _sim.Enqueue(new Deploy(sid, (hb?.X ?? 0) + 14, (hb?.Y ?? 0) + 6)); _sim.Enqueue(new OrderSquad(sid, SquadOrder.Collect)); }, crewOk && pick.Alive > 0);
        }
        else
        {
            var enemies = f.Squads.Where(q => q.Faction != 1 && q.State == SquadState.Field && q.Alive > 0).ToList();
            foreach (var e in enemies.Take(3)) { int eid = e.Id; Act($"攻撃 → {e.Name}", () => _sim.Enqueue(new OrderSquad(sid, SquadOrder.Attack, TargetSquad: eid))); }
            foreach (var p in (f.Diplomacy?.Powers ?? new()).Where(p => p.AtWar && !p.Vassal)) { int tb = p.HomeBuilding; Act($"攻撃 → {p.Name} の拠点", () => _sim.Enqueue(new OrderSquad(sid, SquadOrder.Attack, TargetBuilding: tb))); }
            Act("待機", () => _sim.Enqueue(new OrderSquad(sid, SquadOrder.Hold)));
            if (f.Loot.Count > 0) Act("部品を回収", () => _sim.Enqueue(new OrderSquad(sid, SquadOrder.Collect)));
            Act("帰還", () => _sim.Enqueue(new OrderSquad(sid, SquadOrder.Retreat)));
            Act("自爆(リアクター 150%)", () => _sim.Enqueue(new OrderSquad(sid, SquadOrder.SelfDestruct)));
            Act("移動先を地図でクリック", () => { _squadMoving = sid; SetBuildMode(BuildMode.SquadMove); CloseOverlay(); });
        }
        // 軍: 編成・司令官・命令
        col.AddChild(Lbl("軍(隊の束。司令官が要る。命令は配下の隊へ配る)", 14, Accent));
        var mrow = new HBoxContainer(); mrow.AddThemeConstantOverride("separation", 8);
        var fb = Btn($"印を付けた {_armySel.Count} 隊で軍を編成", () => { if (_armySel.Count > 0) { int aid = f.NextArmyId; _sim.Enqueue(new FormArmy(aid, $"第 {aid} 軍", _armySel.ToList())); _sim.Step(); _armySel.Clear(); _armyPick = aid; RefreshOverlay(true); } }, true); fb.CustomMinimumSize = new Vector2(0, 26); fb.Disabled = _armySel.Count == 0; mrow.AddChild(fb); col.AddChild(mrow);
        if (f.Armies.Count == 0) col.AddChild(Lbl("  (軍はまだ無い)", 12, Line40));
        foreach (var a in f.Armies)
        {
            int aid = a.Id; var ac = Armies.Commander(f, a.CommanderId); int alive = a.SquadIds.Sum(id => f.Squads.FirstOrDefault(q => q.Id == id)?.Alive ?? 0);
            var ar = new HBoxContainer(); ar.AddThemeConstantOverride("separation", 8);
            var abn = Btn($"{a.Name}  隊 {a.SquadIds.Count}  機 {alive}  司令官 {(ac != null ? ac.Name : "なし")}", () => { _armyPick = aid; RefreshOverlay(true); }, aid == _armyPick); abn.CustomMinimumSize = new Vector2(0, 26); abn.Alignment = HorizontalAlignment.Left; ar.AddChild(abn);
            if (aid == _armyPick)
            {
                if (ac != null) { var dbb = Btn("司令官を解任", () => { _sim.Enqueue(new DismissCommander("army", aid)); _sim.Step(); RefreshOverlay(true); }); dbb.CustomMinimumSize = new Vector2(0, 26); ar.AddChild(dbb); }
                else if (free.Count > 0) { var opt2 = new OptionButton(); for (int i = 0; i < free.Count; i++) opt2.AddItem($"{free[i].Name}  指揮 {free[i].Command}", i); var ab2 = Btn("司令官に任命", () => { if (opt2.Selected >= 0) { _sim.Enqueue(new AssignCommander("army", aid, free[opt2.Selected].Id)); _sim.Step(); RefreshOverlay(true); } }, true); ab2.CustomMinimumSize = new Vector2(0, 26); ar.AddChild(opt2); ar.AddChild(ab2); }
                void AAct(string text, Action act, bool enabled = true) { var b = Btn(text, () => { act(); _sim.Step(); RefreshOverlay(true); }); b.CustomMinimumSize = new Vector2(0, 26); b.Disabled = !enabled; ar.AddChild(b); }
                var hb0 = f.Get(f.Squads.FirstOrDefault(q => q.Id == a.SquadIds[0])?.HomeBuilding ?? -1);
                AAct("出撃", () => _sim.Enqueue(new DeployArmy(aid, (hb0?.X ?? 0) + 20, (hb0?.Y ?? 0) + 10)), ac != null);
                foreach (var e in f.Squads.Where(q => q.Faction != 1 && q.State == SquadState.Field && q.Alive > 0).Take(2)) { int eid = e.Id; AAct($"攻撃 → {e.Name}", () => _sim.Enqueue(new OrderArmy(aid, SquadOrder.Attack, TargetSquad: eid)), ac != null); }
                foreach (var p in (f.Diplomacy?.Powers ?? new()).Where(p => p.AtWar && !p.Vassal)) { int tb = p.HomeBuilding; AAct($"攻撃 → {p.Name} の拠点", () => _sim.Enqueue(new OrderArmy(aid, SquadOrder.Attack, TargetBuilding: tb)), ac != null); }
                AAct("移動先を地図でクリック", () => { _armyMoving = aid; SetBuildMode(BuildMode.ArmyMove); CloseOverlay(); }, ac != null);
                AAct("帰還", () => _sim.Enqueue(new OrderArmy(aid, SquadOrder.Retreat)), ac != null);
                AAct("解散", () => _sim.Enqueue(new DisbandArmy(aid)));
            }
            col.AddChild(ar);
        }
        col.AddChild(Lbl("隊と軍の単位で指揮し、個々の機体は装備と制御基板(索敵・交戦・退却・優先目標・武器)で自動で戦う。注視範囲(選んだ建物・隊の周り 300 m)の外の隊は隊どうしの式で解く(G21)", 11, Line40));
        var enemyField = f.Squads.Where(q => q.Faction != 1 && q.State == SquadState.Field && q.Alive > 0).ToList();
        if (enemyField.Count > 0) { col.AddChild(Lbl("出撃中の敵", 13, Red)); foreach (var e in enemyField) col.AddChild(Lbl($"  {e.Name}  {e.Alive} 機  位置 ({e.X:0}, {e.Y:0})  {(e.TargetBuilding >= 0 ? $"建物 #{e.TargetBuilding} を攻撃中" : "")}", 12, Red)); }
    }
}
