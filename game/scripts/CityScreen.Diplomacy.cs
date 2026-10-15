using Godot;
using GPS.Sim;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>政治画面の右半分(外交。G7)と、都市の画面に描く AI 勢力の拠点・出撃中の隊。計算は GPS.Sim の DiplomacySim。</summary>
public partial class CityScreen
{
    int _pickPower = -1;
    readonly Dictionary<int, Node3D> _fieldMechs = new();
    readonly Dictionary<int, Node3D> _fieldFighters = new();   // 高速機の戦闘機モード
    bool _drawAll;   // 村(段階 1)は全機、町以降は隊の代表機 1 体(G2 の粒度)。--all で全機
    readonly Dictionary<int, Label3D> _fieldTags = new();
    readonly List<(Node3D n, int life)> _fieldFx = new();
    readonly Dictionary<Loot, Node3D> _lootMarks = new();
    int _fxCursor;

    static string RelationName(string st) => st switch { "vassal" => "属国", "hostile" => "敵対", "wary" => "警戒", "friendly" => "友好", _ => "中立" };
    static string TreatyName(string k) => k switch { "trade" => "通商", "nonaggression" => "不可侵", "alliance" => "同盟", _ => k };
    static string OfferName(Offer o) => o.Kind switch { "demand_tribute" => $"貢納の要求(月 {o.Amount:0} を 12 か月)", "demand_trade" => "通商の打ち切りの要求", "peace" => o.Amount > 0 ? $"和平の申し出(相手から月 {o.Amount:0} の貢納)" : "和平の申し出", "propose_trade" => "通商の提案", "propose_nonaggression" => "不可侵の提案", "propose_alliance" => "同盟の提案", _ => o.Kind };
    static string PersonalityName(string p) => p switch { "trade" => "通商型", "warlike" => "好戦型", "raider" => "略奪型", _ => "孤立型" };

    void BuildDiplomacy(Control parent)
    {
        var f = _sim.Factory; var dip = f.Diplomacy; var d = _sim.DiplomacyData; var city = f.City!;
        if (dip == null || dip.Powers.Count == 0) { parent.AddChild(Lbl("勢力なし", 13)); return; }
        parent.AddChild(Lbl("外交(同じ地図上の AI 勢力)", 14, Accent));
        var g = new GridContainer { Columns = 6 }; g.AddThemeConstantOverride("h_separation", 14); g.AddThemeConstantOverride("v_separation", 4); parent.AddChild(g);
        foreach (var h in new[] { "勢力", "関係", "状態", "条約", "兵力比", "" }) g.AddChild(Lbl(h, 12, Line40));
        if (!dip.Powers.Any(p => p.Id == _pickPower)) _pickPower = dip.Powers[0].Id;
        foreach (var p in dip.Powers)
        {
            string st = p.State(d);
            var nb = Btn(p.Name, () => { _pickPower = p.Id; RefreshOverlay(true); }, p.Id == _pickPower); nb.CustomMinimumSize = new Vector2(130, 26); nb.Alignment = HorizontalAlignment.Left; g.AddChild(nb);
            g.AddChild(Lbl($"{p.Relation:+0;-0}", 13, p.Relation < 0 ? Warm : Green));
            g.AddChild(Lbl(p.AtWar ? "交戦中" : RelationName(st), 13, p.AtWar ? Red : Text));
            g.AddChild(Lbl(p.Treaties.Count == 0 ? "なし" : string.Join("・", p.Treaties.Select(TreatyName)), 12));
            double ratio = DiplomacySim.Ratio(f, p); g.AddChild(Lbl(ratio >= 99 ? "∞" : $"{ratio:0.0}", 13, ratio > 1.3 ? Red : ratio > 1 ? Warm : Green));
            g.AddChild(Lbl(p.Offers.Count > 0 ? $"申し出 {p.Offers.Count}" : "", 12, Warm));
        }
        var pick = dip.Powers.First(p => p.Id == _pickPower);
        var pd = d.Personalities[pick.Personality]; int neg = NegotiatorAbility();
        parent.AddChild(Lbl($"{pick.Name}    {PersonalityName(pick.Personality)}    人口 {pick.Population:0}  技術 L{pick.Tech}  残りの戦力 {pick.Strength:0}  蓄え {pick.Scrap:0}" + (neg >= 5 ? "" : "  (詳細は交渉役の能力 5 以上で)"), 13, Accent));
        // 申し出への回答
        foreach (var o in pick.Offers)
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10);
            row.AddChild(Lbl($"{OfferName(o)}  あと {o.DaysLeft} 日", 13, Warm));
            int oid = o.Id, pidd = pick.Id;
            row.AddChild(Btn("受ける", () => { _sim.Enqueue(new AnswerOffer(pidd, oid, true)); _sim.Step(); RefreshOverlay(true); }, true));
            row.AddChild(Btn("断る", () => { _sim.Enqueue(new AnswerOffer(pidd, oid, false)); _sim.Step(); RefreshOverlay(true); }));
            parent.AddChild(row);
        }
        // 行動
        if (pick.Vassal) { parent.AddChild(Lbl("属国(関係 +50 固定。制覇に数える)", 13, Green)); return; }
        var acts = new HBoxContainer(); acts.AddThemeConstantOverride("separation", 8); parent.AddChild(acts);
        int day = city.Days; int pid = pick.Id;
        Button Act(string text, Action a, bool enabled, string tip = "") { var b = Btn(text, () => { a(); _sim.Step(); RefreshOverlay(true); }); b.CustomMinimumSize = new Vector2(0, 28); b.Disabled = !enabled; b.TooltipText = tip; acts.AddChild(b); return b; }
        if (pick.AtWar)
        {
            Act($"和平を提案 {DiplomacySim.PeaceChance(_sim, pick):0}%", () => _sim.Enqueue(new ProposePeace(pid)), pick.Cooldown.GetValueOrDefault("peace") <= day, "損耗比(相手の撃破数 / 自分の撃破数)で成功率");
            parent.AddChild(Lbl($"戦争 {pick.WarDays} 日目   撃破 相手 {pick.LossesMine} / 自分 {pick.LossesEnemy}   相手の拠点: 建物 #{pick.HomeBuilding}(耐久 {f.Get(pick.HomeBuilding)?.Hp:0} / {f.Get(pick.HomeBuilding)?.MaxHp:0})", 12));
            var mine = f.Squads.Where(s => s.Faction == 1 && s.State == SquadState.InHangar && s.Alive > 0).ToList();
            foreach (var sq in mine)
            {
                int sid = sq.Id; var hb = f.Get(sq.HomeBuilding);
                var b = Btn($"{sq.Name} を出撃 → {pick.Name} の拠点", () => { _sim.Enqueue(new Deploy(sid, (hb?.X ?? 0) + 14, (hb?.Y ?? 0) + 6)); _sim.Enqueue(new OrderSquad(sid, SquadOrder.Attack, TargetBuilding: pick.HomeBuilding)); _sim.Step(); RefreshOverlay(true); });
                b.CustomMinimumSize = new Vector2(0, 28); b.Disabled = !CitySim.CanDeploy(f, sq, _sim.CityData); b.TooltipText = b.Disabled ? "要員が足りない(徴兵率)" : ""; parent.AddChild(b);
            }
        }
        else
        {
            foreach (var k in new[] { "trade", "nonaggression", "alliance" })
            {
                bool has = pick.Treaties.Contains(k); double ch = DiplomacySim.ProposalChance(_sim, pick, k);
                if (has) Act($"{TreatyName(k)}を破棄", () => _sim.Enqueue(new BreakTreaty(pid, k)), true, k == "nonaggression" ? "関係 −50、他の勢力 −20" : "関係 −10");
                else Act($"{TreatyName(k)}を提案 {ch:0}%", () => _sim.Enqueue(new Propose(pid, k)), pick.Cooldown.GetValueOrDefault("propose_" + k) <= day, "失敗で関係 −5、30 日は再提案できない");
            }
            long giftCost = (long)(pick.Population * d.Gift["costPerPopulation"]);
            Act($"贈り物 {giftCost:N0}", () => _sim.Enqueue(new Gift(pid)), pick.Cooldown.GetValueOrDefault("gift") <= day && f.Treasury >= giftCost, "関係 +10。月 1 回");
            Act("威圧", () => _sim.Enqueue(new Threaten(pid)), pick.Cooldown.GetValueOrDefault("threat") <= day, "兵力比が 0.67 以下なら貢納を飲む。足りなければ関係 −20");
            Act("宣戦", () => _sim.Enqueue(new DeclareWar(pid)), !pick.Treaties.Contains("nonaggression"), pick.Treaties.Contains("nonaggression") ? "不可侵中は宣戦できない" : "関係 −60。同盟国が参戦する");
            if (pick.TributeFromThem > 0 || pick.TributeToMe > 0) parent.AddChild(Lbl($"貢納  {(pick.TributeFromThem > 0 ? $"相手から月 {pick.TributeFromThem:0}" : $"相手へ月 {pick.TributeToMe:0}")}  あと {pick.TributeMonths} か月", 12, Green));
        }
        parent.AddChild(Lbl(neg > 0 ? $"交渉役の能力 {neg}: 提案の成功率 +{neg * d.NegotiatorPerPoint:0}%" : "交渉役が空席のため、提案の成功率に補正なし。登用画面で任命できる", 11, Line40));
        parent.AddChild(Lbl(dip.Outcome switch { "conquest" => "大陸の制覇(全ての勢力が属国)", "alliance" => "同盟網による支配(生存する全ての勢力と同盟)", "conquered" => "拠点が落ち、征服された(計算は続く)", _ => "終わり方: 大陸の制覇、または同盟網による支配。敗北は征服か、支持率の低下による失脚" }, 11, dip.Outcome == "" ? Line40 : dip.Outcome == "conquered" ? Red : Green));
    }

    int NegotiatorAbility()
    {
        var f = _sim.Factory; var pol = PoliticsSim.Of(f); var slot = PoliticsSim.Slots(f, _sim.PoliticsData).FirstOrDefault(s => s.Kind == "negotiate");
        return slot != null && pol.Offices.TryGetValue(slot.Id, out var pid) && f.City!.Persons.FirstOrDefault(x => x.Id == pid) is Person p ? p.Negotiate : 0;
    }

    // ---------- 都市の画面: 出撃中の隊と射線 ----------
    void RefreshField()
    {
        var f = _sim.Factory;
        foreach (var l in f.Loot) if (!_lootMarks.ContainsKey(l))
        {
            var n = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(2.4f, 0.8f, 2.4f) }, Position = new Vector3(l.X, Y + 0.4f, l.Y), MaterialOverride = new StandardMaterial3D { AlbedoColor = Warm, EmissionEnabled = true, Emission = Warm, EmissionEnergyMultiplier = 1.5f } };
            var tag = new Label3D { Text = $"部品 {l.Parts.Count}", FontSize = 32, PixelSize = 0.03f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, OutlineSize = 8, Modulate = Warm, Position = new Vector3(0, 3f, 0) };
            n.AddChild(tag); _cityRoot.AddChild(n); _lootMarks[l] = n;
        }
        foreach (var (l, n) in _lootMarks.ToList()) if (!f.Loot.Contains(l)) { n.QueueFree(); _lootMarks.Remove(l); }
        bool representative = !_drawAll && (f.City?.Stage ?? 1) >= 2;
        foreach (var sq in f.Squads)
        {
            var lead = sq.Members.FirstOrDefault(u => u.Alive);
            foreach (var u in sq.Members)
            {
                bool show = sq.State == SquadState.Field && u.Alive && (!representative || u == lead);
                if (!_fieldMechs.TryGetValue(u.MechId, out var node))
                {
                    if (!show) continue;
                    var m = f.Mechs.FirstOrDefault(x => x.Id == u.MechId); if (m == null) continue;
                    node = _mb.Build(_cityRoot, m.Archetype, $"g{m.Generation}", 0, Y, 0, 0); _fieldMechs[u.MechId] = node;
                    var tag = new Label3D { FontSize = 36, PixelSize = 0.03f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, OutlineSize = 10 }; _cityRoot.AddChild(tag); _fieldTags[u.MechId] = tag;
                }
                var m0 = f.Mechs.FirstOrDefault(x => x.Id == u.MechId);
                if (u.Flying && !_fieldFighters.ContainsKey(u.MechId) && m0 != null) _fieldFighters[u.MechId] = _mb.BuildFighter(_cityRoot, _mb.Presets[m0.Archetype], $"g{m0.Generation}", 0, Y, 0, 0);
                bool fly = show && u.Flying && _fieldFighters.ContainsKey(u.MechId);
                node.Visible = show && !fly; if (_fieldFighters.TryGetValue(u.MechId, out var fn)) fn.Visible = fly;
                _fieldTags[u.MechId].Visible = show;
                if (!show) continue;
                var tgt = u.TargetMech >= 0 ? f.Squads.SelectMany(s => s.Members).FirstOrDefault(x => x.MechId == u.TargetMech) : null;
                double dx = tgt != null ? tgt.X - u.X : (sq.Faction == 1 ? 1 : -1), dy = tgt != null ? tgt.Y - u.Y : 0;
                float yaw = Mathf.RadToDeg((float)Math.Atan2(dx, dy));
                node.Position = new Vector3((float)u.X, Y, (float)u.Y); node.RotationDegrees = new Vector3(0, yaw, 0);
                if (fly) { var fnode = _fieldFighters[u.MechId]; fnode.Position = new Vector3((float)u.X, Y + 18f, (float)u.Y); fnode.RotationDegrees = new Vector3(0, yaw, 0); }   // 戦闘機モードは高度 18 m
                int pct = representative ? (int)Math.Round(sq.Members.Where(x => x.Alive).Sum(x => x.Hp) * 100 / Math.Max(1, sq.Members.Where(x => x.Alive).Sum(x => x.MaxHp))) : (int)Math.Round(u.Hp * 100 / Math.Max(1, u.MaxHp));
                var t = _fieldTags[u.MechId]; t.Position = (fly ? _fieldFighters[u.MechId].Position : node.Position) + new Vector3(0, 9.5f, 0); t.Text = representative ? $"{sq.Name} {sq.Alive} 機\n{pct}%" : $"{sq.Name}\n{pct}%" + (fly ? " 飛行" : ""); t.Modulate = sq.Faction == 1 ? Cyan : Red;
            }
        }
        // 射線と閃光(Battle と同じ見せ方)
        var ev = f.Events;
        for (; _fxCursor < ev.Count; _fxCursor++)
        {
            var e = ev[_fxCursor];
            if (e.Kind is "hit" or "miss" or "hit_building")
            {
                var sq = f.Squads.FirstOrDefault(s => s.Id == e.Building);
                Vector3 a = new(e.X, Y + 2.6f, e.Y), b;
                if (e.Kind == "hit_building") { var tb = f.Get(e.Floor); if (tb == null) continue; b = new Vector3((float)tb.X + tb.Floors[0].W / 2f, Y + 3f, (float)tb.Y + tb.Floors[0].D / 2f); }
                else { var to = f.Squads.SelectMany(s => s.Members).FirstOrDefault(u => u.MechId == e.Floor); if (to == null) continue; b = new Vector3((float)to.X, Y + 2.2f, (float)to.Y); }
                bool beam = sq != null && _sim.Combat.Archetype[sq.Archetype].Primary.EndsWith("beam");
                FieldBeam(a, b, beam ? new Color(0.4f, 0.9f, 1f) : new Color(1f, 0.85f, 0.5f), beam ? 0.3f : 0.15f, 10);
                if (e.Kind != "miss") FieldFlash(b, 1.8f, new Color(1f, 0.6f, 0.2f), 12);
            }
            else if (e.Kind is "destroyed" or "building_destroyed") FieldFlash(new Vector3(e.X, Y + 2f, e.Y), 4f, new Color(1f, 0.45f, 0.1f), 30);
        }
        for (int i = _fieldFx.Count - 1; i >= 0; i--) { var (n, life) = _fieldFx[i]; if (--life <= 0) { n.QueueFree(); _fieldFx.RemoveAt(i); } else _fieldFx[i] = (n, life); }
    }
    void FieldBeam(Vector3 a, Vector3 b, Color c, float r, int life)
    {
        var mesh = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = r, BottomRadius = r, Height = a.DistanceTo(b), RadialSegments = 6 }, MaterialOverride = new StandardMaterial3D { AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = 4f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded }, Position = (a + b) / 2 };
        var dir = (b - a).Normalized(); var up = Mathf.Abs(dir.Dot(Vector3.Up)) > 0.99f ? Vector3.Right : Vector3.Up;
        mesh.Basis = Basis.LookingAt(dir, up) * new Basis(Vector3.Right, Mathf.Pi / 2); _cityRoot.AddChild(mesh); _fieldFx.Add((mesh, life));
    }
    void FieldFlash(Vector3 at, float radius, Color c, int life)
    {
        var mesh = new MeshInstance3D { Mesh = new SphereMesh { Radius = radius, Height = radius * 2 }, Position = at, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(c, 0.55f), EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = 3f, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } };
        _cityRoot.AddChild(mesh); _fieldFx.Add((mesh, life));
    }
}
