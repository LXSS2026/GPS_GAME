using Godot;
using GPS.Sim;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>軍の画面(G5 の上の帯「軍」)。隊の一覧と命令(出撃・攻撃・回収・帰還・自爆)。Going Medieval 方式: 隊の単位で指揮し、機体は制御基板で戦う。</summary>
public partial class CityScreen
{
    int _armyPickSquad = -1;

    void BuildArmy(Control root)
    {
        var f = _sim.Factory; var c = f.City!; var cd = _sim.CityData;
        var col = new VBoxContainer(); col.AddThemeConstantOverride("separation", 8); root.AddChild(col);
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 16);
        head.AddChild(Lbl($"軍    {StageName(c.Stage)} {c.Name}    軍の要員 {c.Conscripts:0} / {c.CrewNeed:0}(徴兵率 {c.ConscriptionRate * 100:0}%)    戦場に残る部品 {f.Loot.Count} か所    戦力 {DiplomacySim.PlayerStrength(f):0.0}", 16, Accent));
        var back = Btn("地図へ戻る", CloseOverlay); back.CustomMinimumSize = new Vector2(0, 28); head.AddChild(back); col.AddChild(head);
        var mine = f.Squads.Where(s => s.Faction == 1).ToList();
        if (mine.Count == 0) { col.AddChild(Lbl("隊がありません。機体を製造して格納庫で隊を編成します", 13, Warm)); return; }
        var g = new GridContainer { Columns = 9 }; g.AddThemeConstantOverride("h_separation", 14); g.AddThemeConstantOverride("v_separation", 4); col.AddChild(g);
        foreach (var h in new[] { "隊", "系統", "世代", "機数", "耐久", "状態", "命令", "要員", "帰還先" }) g.AddChild(Lbl(h, 12, Line40));
        if (!mine.Any(s => s.Id == _armyPickSquad)) _armyPickSquad = mine[0].Id;
        foreach (var s in mine)
        {
            var nb = Btn(s.Name, () => { _armyPickSquad = s.Id; RefreshOverlay(true); }, s.Id == _armyPickSquad); nb.CustomMinimumSize = new Vector2(150, 26); nb.Alignment = HorizontalAlignment.Left; g.AddChild(nb);
            g.AddChild(Lbl(s.Archetype switch { "heavy" => "重装", "fast" => "高速", _ => "汎用" }, 13)); g.AddChild(Lbl($"第 {s.Generation}", 13)); g.AddChild(Lbl($"{s.Alive}/{s.Members.Count}", 13));
            double hp = s.Members.Where(u => u.Alive).Sum(u => u.Hp), max = s.Members.Where(u => u.Alive).Sum(u => u.MaxHp); int pct = max > 0 ? (int)Math.Round(hp / max * 100) : 0;
            g.AddChild(Lbl($"{pct}%", 13, pct < 30 ? Red : pct < 70 ? Warm : Green));
            g.AddChild(Lbl(s.State == SquadState.InHangar ? "格納庫" : "出撃中", 13));
            g.AddChild(Lbl(s.Order switch { SquadOrder.Attack => s.TargetBuilding >= 0 ? $"攻撃 建物 #{s.TargetBuilding}" : s.TargetSquad >= 0 ? $"攻撃 隊 #{s.TargetSquad}" : "攻撃", SquadOrder.Move => $"移動 ({s.OrderX:0}, {s.OrderY:0})", SquadOrder.Retreat => "帰還", SquadOrder.Collect => "回収", SquadOrder.SelfDestruct => "自爆", _ => "待機" }, 13));
            bool crew = CitySim.CanDeploy(f, s, cd); g.AddChild(Lbl(crew ? "充足" : "不足", 13, crew ? Green : Warm));
            g.AddChild(Lbl(f.Get(s.HomeBuilding)?.Name ?? "-", 12));
        }
        var pick = mine.First(s => s.Id == _armyPickSquad); int sid = pick.Id; var hb = f.Get(pick.HomeBuilding);
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
        col.AddChild(Lbl("隊の単位で指揮し、個々の機体は装備と制御基板(索敵・交戦・退却・優先目標・武器)で自動で戦う。陣形は横列 / 縦列 / 散開(OrderSquad の Formation)", 11, Line40));
        var enemyField = f.Squads.Where(q => q.Faction != 1 && q.State == SquadState.Field && q.Alive > 0).ToList();
        if (enemyField.Count > 0) { col.AddChild(Lbl("出撃中の敵", 13, Red)); foreach (var e in enemyField) col.AddChild(Lbl($"  {e.Name}  {e.Alive} 機  位置 ({e.X:0}, {e.Y:0})  {(e.TargetBuilding >= 0 ? $"建物 #{e.TargetBuilding} を攻撃中" : "")}", 12, Red)); }
    }
}
