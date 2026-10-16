using Godot;
using GPS.Sim;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>地図の上での隊の選択と命令(試遊版の操作の見直し)。左クリックで出撃中の機体を選ぶとその隊が選ばれ、右クリックで移動・攻撃を命じる。</summary>
public partial class CityScreen
{
    int _selectedSquad = -1;
    MeshInstance3D? _squadMark;
    Vector2 _rightDown; bool _rightDragged;

    /// <summary>画面の点に最も近い出撃中の機体(28 px 以内)の隊。</summary>
    int PickSquad(Vector2 screen, out bool enemy)
    {
        enemy = false; var f = _sim.Factory; int best = -1; float bestD = 28f;
        foreach (var sq in f.Squads)
        {
            if (sq.State != SquadState.Field) continue;
            foreach (var u in sq.Members)
            {
                if (!u.Alive) continue;
                Node3D? n = u.Flying && _fieldFighters.TryGetValue(u.MechId, out var fn) ? fn : _fieldMechs.GetValueOrDefault(u.MechId);
                if (n == null || !n.Visible) continue;
                var p = n.Position + new Vector3(0, 3, 0); if (_cam.IsPositionBehind(p)) continue;
                float d = _cam.UnprojectPosition(p).DistanceTo(screen);
                if (d < bestD) { bestD = d; best = sq.Id; enemy = sq.Faction != 1; }
            }
        }
        return best;
    }

    void SelectSquad(int id)
    {
        _selectedSquad = id; _selected = -1; _sideTick = -1;
        var sq = _sim.Factory.Squads.FirstOrDefault(q => q.Id == id);
        if (sq != null) SetFocus(sq.X, sq.Y);
    }

    /// <summary>右クリック(ドラッグでない): 選んだ自軍の隊に、敵の機体なら攻撃、敵の建物なら攻撃、地面なら移動。格納庫にいる隊は出撃してその地点へ。</summary>
    void RightClickOrder(Vector2 screen)
    {
        var f = _sim.Factory; var sq = f.Squads.FirstOrDefault(q => q.Id == _selectedSquad);
        if (sq == null || sq.Faction != 1) return;
        var g = GroundPoint(screen); if (g == null) return;
        int gx = (int)Math.Floor(g.Value.X), gz = (int)Math.Floor(g.Value.Z);
        if (sq.State == SquadState.InHangar)
        {
            if (!CitySim.CanDeploy(f, sq, _sim.CityData)) { Notice($"{sq.Name}: 要員が足りない(徴兵率を上げる)"); return; }
            if (!Armies.HasCommander(f, sq)) { Notice($"{sq.Name}: 隊長がいない(軍の画面で任命)"); return; }
            var hb = f.Get(sq.HomeBuilding);
            _sim.Enqueue(new Deploy(sq.Id, (hb?.X ?? 0) + 14, (hb?.Y ?? 0) + 6)); _sim.Enqueue(new OrderSquad(sq.Id, SquadOrder.Move, gx, gz)); _sim.Step();
            Notice($"{sq.Name} が出撃して ({gx}, {gz}) へ"); _sideTick = -1; return;
        }
        int target = PickSquad(screen, out bool enemy);
        if (target >= 0 && enemy) { _sim.Enqueue(new OrderSquad(sq.Id, SquadOrder.Attack, TargetSquad: target)); _sim.Step(); Notice($"{sq.Name} → {f.Squads.First(q => q.Id == target).Name} を攻撃"); _sideTick = -1; return; }
        int bid = PickBuilding(screen); var b = bid >= 0 ? f.Get(bid) : null;
        if (b != null && b.OwnerPower != 0 && !b.Destroyed) { _sim.Enqueue(new OrderSquad(sq.Id, SquadOrder.Attack, TargetBuilding: bid)); _sim.Step(); Notice($"{sq.Name} → {b.Name} を攻撃"); _sideTick = -1; return; }
        _sim.Enqueue(new OrderSquad(sq.Id, SquadOrder.Move, gx, gz)); _sim.Step(); Notice($"{sq.Name} → ({gx}, {gz}) へ移動"); _sideTick = -1;
    }

    /// <summary>右の板: 選んだ隊。</summary>
    void RefreshSquadSide()
    {
        if (_sim.Tick == _sideTick) return;
        if (_sim.Tick - _sideTick < 30 && _sideTick >= 0) return;
        _sideTick = _sim.Tick;
        foreach (var ch in _side.GetChildren()) ch.QueueFree();
        var f = _sim.Factory; var sq = f.Squads.FirstOrDefault(q => q.Id == _selectedSquad);
        if (sq == null) { _sidePanel.Visible = false; _selectedSquad = -1; return; }
        _sidePanel.Visible = true; bool mine = sq.Faction == 1;
        var pw = f.Diplomacy?.Powers.FirstOrDefault(p => p.Id == sq.Faction);
        _side.AddChild(Lbl(sq.Name + (mine ? "" : $"  ({pw?.Name ?? "敵"})"), 17, mine ? Accent : Red));
        var cmdr = Armies.Commander(f, sq.CommanderId); var army = Armies.ArmyOf(f, sq);
        _side.AddChild(Lbl($"{sq.Archetype switch { "heavy" => "重装", "fast" => "高速", _ => "汎用" }} 第 {sq.Generation} 世代  {sq.Alive}/{sq.Members.Count} 機  {(sq.State == SquadState.InHangar ? "格納庫で待機" : "出撃中")}" + (mine ? $"  隊長 {(cmdr != null ? cmdr.Name : "なし")}  軍 {(army?.Name ?? "-")}" : ""), 13));
        double hp = sq.Members.Where(u => u.Alive).Sum(u => u.Hp), max = sq.Members.Where(u => u.Alive).Sum(u => u.MaxHp); int pct = max > 0 ? (int)Math.Round(hp / max * 100) : 0;
        _side.AddChild(Lbl($"耐久 {pct}%   士気 {sq.Morale:0}   練度 {sq.Exp:0}   下がった機体 {sq.Members.Count(u => u.Alive && u.Retreat != 0)}", 13, pct < 30 ? Red : pct < 70 ? Warm : Text));
        string order = sq.Order switch { SquadOrder.Attack => sq.TargetBuilding >= 0 ? $"攻撃 → {f.Get(sq.TargetBuilding)?.Name ?? "建物"}" : sq.TargetSquad >= 0 ? $"攻撃 → {f.Squads.FirstOrDefault(q => q.Id == sq.TargetSquad)?.Name ?? "隊"}" : "攻撃", SquadOrder.Move => $"移動 → ({sq.OrderX:0}, {sq.OrderY:0})", SquadOrder.Retreat => "帰還", SquadOrder.Collect => "部品を回収", SquadOrder.SelfDestruct => "自爆", _ => "待機" };
        _side.AddChild(Lbl($"命令 {order}", 13));
        if (!mine) { _side.AddChild(Lbl("自軍の隊を選んでから、この隊を右クリックで攻撃", 12, Line40)); return; }
        var hint = Lbl(sq.State == SquadState.InHangar ? "右クリック: 出撃してその地点へ" : "右クリック: 地面 = 移動、敵の機体 = その隊を攻撃、敵の建物 = 攻撃", 12, Line40); hint.AutowrapMode = TextServer.AutowrapMode.Word; _side.AddChild(hint);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); int sid = sq.Id; var hb = f.Get(sq.HomeBuilding);
        void Act(string text, Action a, bool enabled = true) { var b = Btn(text, () => { a(); _sim.Step(); _sideTick = -1; }); b.CustomMinimumSize = new Vector2(0, 26); b.Disabled = !enabled; row.AddChild(b); }
        if (sq.State == SquadState.InHangar) Act("出撃(格納庫の前)", () => _sim.Enqueue(new Deploy(sid, (hb?.X ?? 0) + 14, (hb?.Y ?? 0) + 6)), CitySim.CanDeploy(f, sq, _sim.CityData) && Armies.HasCommander(f, sq) && sq.Alive > 0);
        else { Act("待機", () => _sim.Enqueue(new OrderSquad(sid, SquadOrder.Hold))); if (f.Loot.Count > 0) Act("部品を回収", () => _sim.Enqueue(new OrderSquad(sid, SquadOrder.Collect))); Act("帰還", () => _sim.Enqueue(new OrderSquad(sid, SquadOrder.Retreat))); }
        var ab = Btn("軍の画面", () => OpenOverlay("army")); ab.CustomMinimumSize = new Vector2(0, 26); row.AddChild(ab);
        _side.AddChild(row);
        foreach (var u in sq.Members.Take(12))
        {
            var m = f.Mechs.FirstOrDefault(x => x.Id == u.MechId);
            _side.AddChild(Lbl($"  #{u.MechId}  {(u.Alive ? $"耐久 {u.Hp / Math.Max(1, u.MaxHp) * 100:0}%  電力 {(u.EnergyCap > 0 ? u.Energy * 100.0 / u.EnergyCap : 0):0}%  弾薬 {u.Ammo}" : "撃破")}" + (u.Alive && u.Retreat != 0 ? "  下がっている" : "") + (m?.Program != null ? "  基板あり" : ""), 12, u.Alive ? Text : Line40));
        }
    }

    /// <summary>選んだ隊の印(機体の足元の輪)。</summary>
    void RefreshSquadMark()
    {
        var sq = _selectedSquad >= 0 ? _sim.Factory.Squads.FirstOrDefault(q => q.Id == _selectedSquad) : null;
        bool show = sq != null && sq.State == SquadState.Field && sq.Alive > 0;
        if (_squadMark == null)
        {
            _squadMark = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 7f, OuterRadius = 8f }, MaterialOverride = new StandardMaterial3D { AlbedoColor = Accent, EmissionEnabled = true, Emission = Accent, EmissionEnergyMultiplier = 2f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } };
            _cityRoot.AddChild(_squadMark);
        }
        _squadMark.Visible = show;
        if (show) _squadMark.Position = new Vector3((float)sq!.X, Y + 0.3f, (float)sq.Y);
    }
}
