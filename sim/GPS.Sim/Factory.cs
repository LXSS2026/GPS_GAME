using System.Text.Json.Serialization;

namespace GPS.Sim;

/// <summary>鉱脈の 1 セル。品質は位置で決まり(30〜70)、採るたびに減る。</summary>
public sealed class OreCell
{
    public string Item { get; set; } = "iron_ore";
    public long Amount { get; set; }
    public int Quality { get; set; }
}

/// <summary>建物の 1 階、または平地の 1 ブロック。W × D × 3 セルの格子に設備を置く。</summary>
public sealed class Floor
{
    public const int H = 3;
    public int W { get; set; }
    public int D { get; set; }
    public List<Structure> Structures { get; set; } = new();
    /// <summary>鉱脈(平地ブロックの 1 階のみ)。index = x + W * y。</summary>
    public OreCell?[]? Ore { get; set; }
    [JsonIgnore] Structure?[]? _cells;

    public Structure? At(int x, int y, int z)
    {
        if (x < 0 || y < 0 || z < 0 || x >= W || y >= D || z >= H) return null;
        _cells ??= Reindex();
        return _cells[x + W * (y + D * z)];
    }

    public Structure? Ahead(Structure s, Direction dir) { var (dx, dy, dz) = dir.Delta(); return At(s.X + dx, s.Y + dy, s.Z + dz); }
    public Structure? Behind(Structure s) => Ahead(s, s.Dir.Opposite());

    public bool Fits(StructureDef def, int x, int y, int z)
    {
        if (x < 0 || y < 0 || z < 0 || x + def.W > W || y + def.D > D || z + def.H > H) return false;
        for (int i = 0; i < def.W; i++) for (int j = 0; j < def.D; j++) for (int k = 0; k < def.H; k++) if (At(x + i, y + j, z + k) != null) return false;
        return true;
    }

    public void Add(Structure s, StructureDef def)
    {
        Structures.Add(s);
        _cells ??= Reindex();
        for (int i = 0; i < def.W; i++) for (int j = 0; j < def.D; j++) for (int k = 0; k < def.H; k++) _cells[s.X + i + W * (s.Y + j + D * (s.Z + k))] = s;
    }

    public void Remove(Structure s) { Structures.Remove(s); _cells = null; }

    Structure?[] Reindex()
    {
        var cells = new Structure?[W * D * H];
        // 大きさは定義にあるが、ここでは定義を持たないので原点だけ置き、Factory.Reindex が定義付きで埋め直す
        foreach (var s in Structures) cells[s.X + W * (s.Y + D * s.Z)] = s;
        return cells;
    }

    /// <summary>定義を使って占有セルを埋め直す(読み込み後に呼ぶ)。</summary>
    public void Reindex(GameData data)
    {
        _cells = new Structure?[W * D * H];
        foreach (var s in Structures)
        {
            var def = data.Structures[s.DefId];
            for (int i = 0; i < def.W; i++) for (int j = 0; j < def.D; j++) for (int k = 0; k < def.H; k++) _cells[s.X + i + W * (s.Y + j + D * (s.Z + k))] = s;
        }
    }

    public OreCell? OreAt(int x, int y) => Ore == null || x < 0 || y < 0 || x >= W || y >= D ? null : Ore[x + W * y];
}

/// <summary>建物(または平地ブロック)。動力は建物内で自動供給し、上限はリアクターの合計出力。</summary>
public sealed class Building
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public PowerPolicy Policy { get; set; } = PowerPolicy.Proportional;
    /// <summary>地図上の位置(戦場の座標。機体の帰還先)。</summary>
    public double X { get; set; }
    public double Y { get; set; }
    /// <summary>用途(G17 #13): factory / residence / farm / well / market / school / clinic / police / park / sewage / waste。</summary>
    public string Use { get; set; } = "factory";
    public bool RoadConnected { get; set; } = true;
    /// <summary>建物の耐久(G20 #6)。0 で陥落。</summary>
    public double Hp { get; set; } = 1;
    public double MaxHp { get; set; } = 1;
    public bool Destroyed { get; set; }
    /// <summary>防御壁の段階(0〜3)。外周 × 400 × 段階が耐久に加わる。</summary>
    public int WallLevel { get; set; }
    /// <summary>航空戦艦(空中を動く建物)。</summary>
    public bool IsShip { get; set; }
    public string ShipKind { get; set; } = "";
    public double ShipSpeed { get; set; }
    public double? MoveTargetX { get; set; }
    public double? MoveTargetZ { get; set; }
    public int Perimeter => Floors.Count == 0 ? 0 : 2 * (Floors[0].W + Floors[0].D);
    /// <summary>荷下ろし場: 回収した部品・鹵獲したスクラップ・略奪の対象。箱へ移して工場で使う。</summary>
    public List<ItemStack> Cargo { get; set; } = new();
    [JsonIgnore] public Factory? Owner { get; set; }
    public List<Floor> Floors { get; set; } = new();
    /// <summary>設備のストック(撤去したものと支給されたもの)。</summary>
    public SortedDictionary<string, int> Inventory { get; set; } = new(StringComparer.Ordinal);
    public int NextStructureId { get; set; } = 1;
    /// <summary>見ていない建物の集計近似。null なら全計算。</summary>
    public AggregateState? Aggregate { get; set; }
    [JsonIgnore] public int LastSupply { get; private set; }
    [JsonIgnore] public int LastDemand { get; private set; }
    public void SetPowerSummary(int supply, int demand) { LastSupply = supply; LastDemand = demand; }

    public Structure? Find(int floor, int id) => floor < 0 || floor >= Floors.Count ? null : Floors[floor].Structures.FirstOrDefault(s => s.Id == id);

    // ---------- 動力 ----------
    public static int ReactorOutput(StructureDef def, int quality, GameData data) => def.Output * (data.ReactorPrecisionMin + quality * (100 - data.ReactorPrecisionMin) / 100) / 100;

    public void AssignPower(GameData data)
    {
        int supply = 0;
        var consumers = new List<(Structure s, StructureDef def)>();
        foreach (var fl in Floors)
            foreach (var s in fl.Structures)
            {
                var def = data.Structures[s.DefId];
                if (s is Reactor r) supply += ReactorOutput(def, r.Quality, data) * r.OutputPct / 100;
                else if (def.Power > 0 && s.Enabled && Wants(s, def, fl, data)) consumers.Add((s, def));
                else s.PowerPct = 100;
            }
        if (Owner != null) supply += GPS.Sim.Combat.HangarSupply(Owner, this, data);   // 格納庫で待機中の機体のリアクターが建物に給電(2026-10-15 確定)
        int demand = consumers.Sum(c => c.def.Power);
        int reactorSupply = supply;
        // バッテリー: 余剰で充電、不足で方式に応じて放電(1 ティック分)
        var batteries = Floors.SelectMany(fl => fl.Structures).OfType<Battery>().ToList();
        if (batteries.Count > 0)
        {
            int surplus = supply - demand;
            if (surplus > 0)
            {
                foreach (var bt in batteries.OrderBy(x => x.Id))
                {
                    if (bt.Mode == BatteryMode.Off || surplus <= 0) continue;
                    var bd = data.Structures[bt.DefId];
                    long room = bd.Capacity - bt.Stored; if (room <= 0) continue;
                    int take = (int)Math.Min(Math.Min(surplus, bd.ChargeRate), room * 100L / Math.Max(1, data.BatteryChargeEfficiency));
                    bt.Stored += take * data.BatteryChargeEfficiency / 100; surplus -= take;   // 充電効率 9 割: 入った電力の 9 割が蓄えられる
                }
            }
            else if (surplus < 0)
            {
                int deficit = -surplus;
                foreach (var bt in batteries.OrderBy(x => x.Id))
                {
                    if (deficit <= 0) break;
                    bool allow = bt.Mode == BatteryMode.Peak || (bt.Mode == BatteryMode.Emergency && reactorSupply == 0);
                    if (!allow || bt.Stored <= 0) continue;
                    var bd = data.Structures[bt.DefId];
                    int give = (int)Math.Min(Math.Min(deficit, bd.DischargeRate), bt.Stored);
                    bt.Stored -= give; supply += give; deficit -= give;
                }
            }
        }
        LastSupply = supply; LastDemand = demand;
        if (demand == 0) return;
        switch (Policy)
        {
            case PowerPolicy.Proportional:
                int pct = supply >= demand ? 100 : supply * 100 / demand;
                foreach (var (s, _) in consumers) s.PowerPct = pct;
                break;
            case PowerPolicy.Stop:
                foreach (var (s, _) in consumers) s.PowerPct = supply >= demand ? 100 : 0;
                break;
            case PowerPolicy.Priority:
                int remain = supply;
                foreach (var (s, def) in consumers.OrderBy(c => c.def.Priority).ThenBy(c => c.s.Id))
                {
                    if (remain >= def.Power) { s.PowerPct = 100; remain -= def.Power; }
                    else { s.PowerPct = remain * 100 / def.Power; remain = 0; }
                }
                break;
        }
    }

    static bool Wants(Structure s, StructureDef def, Floor fl, GameData data) => s switch
    {
        Machine m => m.Recipe != null && m.Staff.Count >= def.Workers && (m.Progress >= 0 || Factory.InputsReady(m, data.Recipes[m.Recipe])),
        Miner mi => mi.Output == null && fl.OreAt(mi.X, mi.Y) != null && mi.Staff(def),
        Trade td => td.Import != null && td.ImportEnabled,
        _ => true,
    };
}

static class MinerExt
{
    // 採掘機の要員は第 1 段階では建物の要員として扱わず、設備の定義の workers を満たしたとみなす(担当者の割当は Machine のみ)
    public static bool Staff(this Miner _, StructureDef __) => true;
}

/// <summary>工場全体: 建物の集合と、毎ティックの処理。処理順は 動力 → 採掘・揚水 → 機械 → インサータ → ベルト → 口 → 流体 で固定(決定論)。</summary>
public sealed class Factory
{
    public List<Building> Buildings { get; set; } = new();
    public int NextBuildingId { get; set; } = 1;
    /// <summary>国庫(輸出入の収支)。</summary>
    public long Treasury { get; set; }
    /// <summary>設計図(コピー&ペースト用)。</summary>
    public Dictionary<int, Blueprint> Blueprints { get; set; } = new();
    public int NextBlueprintId { get; set; } = 1;
    /// <summary>true なら在庫なしで設備を置ける(試験・見本用)。</summary>
    public bool Creative { get; set; }

    public Building? Get(int id) => Buildings.FirstOrDefault(b => b.Id == id);

    public void Reindex(GameData data) { foreach (var b in Buildings) foreach (var f in b.Floors) f.Reindex(data); }

    /// <summary>爆発などの出来事(描画・通知用)。</summary>
    public List<FactoryEvent> Events { get; set; } = new();
    /// <summary>製造した機体(手順 3)。軍の Unit へ渡すのは手順 4。</summary>
    public List<Mech> Mechs { get; set; } = new();
    public int NextMechId { get; set; } = 1;
    public List<Squad> Squads { get; set; } = new();
    public int NextSquadId { get; set; } = 1;
    public List<Loot> Loot { get; set; } = new();
    public City? City { get; set; }
    public Politics? Politics { get; set; }
    public Diplomacy? Diplomacy { get; set; }
    /// <summary>通商条約の価格補正(%)。売値 +、買値 −。</summary>
    public int TradeBonusPct { get; set; }
    /// <summary>役職の効果(G19 #2)。失敗率の倍率(%)・加工速度(%)・命中率(%)・既定の退却閾値・売値(%)・機体の高品質率(%)・弾薬補給の可否。</summary>
    public int FailureMulPct { get; set; } = 100;
    public int SpeedMulPct { get; set; } = 100;
    public int HitMulPct { get; set; } = 100;
    public double RetreatHp { get; set; } = 0.25;
    public int PriceMulPct { get; set; } = 100;
    public int HighQualityMulPct { get; set; } = 100;
    public bool AmmoResupply { get; set; } = true;
    /// <summary>生産効率(%)。暴動で 70 に落ちる(G17 #18)。</summary>
    public int EfficiencyPct { get; set; } = 100;
    public long TickCount { get; set; }

    public void Tick(GameData data, Rng rng)
    {
        TickCount++;
        foreach (var b in Buildings)
        {
            b.Owner = this;
            if (b.Destroyed) { b.SetPowerSummary(0, 0); continue; }   // 陥落した建物は動かない
            if (b.Aggregate != null && !Aggregation.BeforeTick(b, this, data, rng)) continue;   // 集計近似で済ませた
            StepOverload(b, data);
            b.AssignPower(data);
            foreach (var fl in b.Floors) foreach (var s in fl.Structures) if (s is Mcu mcu && mcu.Enabled) McuRunner.Step(mcu, b, this, data);   // マイコンは動力の割り当ての後に評価(出力は次ティックに効く)
            foreach (var fl in b.Floors)
            {
                foreach (var s in fl.Structures) if (s is Miner mi) StepMiner(mi, fl, data);
                foreach (var s in fl.Structures) if (s is Pump p) StepPump(p, fl, data);
                foreach (var s in fl.Structures) if (s is Machine m) StepMachine(m, fl, data, rng);
                foreach (var s in fl.Structures) if (s is Inserter ins) StepInserter(ins, fl, data);
                StepBelts(fl, data);
                foreach (var s in fl.Structures) if (s is Port po) StepPort(po, fl, data);
                foreach (var s in fl.Structures) if (s is Trade td) StepTrade(td, fl, data);
            }
            if (b.Aggregate != null) Aggregation.AfterTick(b);
        }
    }

    // ---------- 受け渡し ----------
    public bool TryInsert(Structure target, ItemStack it, GameData data)
    {
        var def = data.Structures[target.DefId];
        switch (target)
        {
            case Belt b: if (b.Slots[0] != null) return false; b.Slots[0] = it; return true;
            case Chest c: if (c.Items.Count >= def.Capacity) return false; c.Items.Add(it); return true;
            case Port p: if (p.Buffer.Count >= 8) return false; p.Buffer.Add(it); return true;
            case Trade td:   // 輸出: 入った瞬間に売る
                Treasury += Price(it, data) * PriceMulPct / 100 * (100 + TradeBonusPct) / 100; td.Sold += it.Count; return true;   // 交渉役と通商条約の効果   // 交渉役の効果
            case Machine m:
            {
                if (m.Recipe == null) return false;
                var r = data.Recipes[m.Recipe];
                foreach (var inp in r.Inputs)
                {
                    if (inp.Item != it.Item || it.Tier < inp.MinTier) continue;
                    if (data.Items[inp.Item].Fluid) return false;   // 流体は配管から
                    int have = m.Inputs.Where(x => x.Item == inp.Item).Sum(x => x.Count);
                    if (have >= inp.Count * 2) return false;        // 2 回分まで溜める
                    m.Inputs.Add(it); return true;
                }
                return false;
            }
            default: return false;
        }
    }

    public ItemStack? TryTake(Structure src)
    {
        switch (src)
        {
            case Belt b: { var it = b.Slots[3]; if (it != null) b.Slots[3] = null; return it; }
            case Chest c: { if (c.Items.Count == 0) return null; var it = c.Items[^1]; c.Items.RemoveAt(c.Items.Count - 1); return it; }
            case Machine m: { if (m.Outputs.Count == 0) return null; var it = m.Outputs[0]; m.Outputs.RemoveAt(0); return it; }
            case Miner mi: { var it = mi.Output; mi.Output = null; return it; }
            case Port p: { if (p.Inbox.Count == 0) return null; var it = p.Inbox[0]; p.Inbox.RemoveAt(0); return it; }
            case Trade td: { if (td.Outbox.Count == 0) return null; var it = td.Outbox[0]; td.Outbox.RemoveAt(0); return it; }
            default: return null;
        }
    }

    /// <summary>過負荷の蓄積と爆発。設定 100% 超: 超過分が毎ティック溜まる。100% 未満: 不足分 × 倍率が回復する。</summary>
    void StepOverload(Building b, GameData data)
    {
        for (int fi = 0; fi < b.Floors.Count; fi++)
        {
            var fl = b.Floors[fi];
            foreach (var r in fl.Structures.OfType<Reactor>().OrderBy(x => x.Id).ToList())
            {
                if (r.OutputPct > 100) r.Load += r.OutputPct - 100;
                else if (r.OutputPct < 100) r.Load = Math.Max(0, r.Load - (long)(100 - r.OutputPct) * data.OverloadRecoveryMul);
                if (r.Load < data.OverloadLimit) continue;
                Explode(b, fi, r, data);
            }
        }
    }

    /// <summary>リアクターの爆発: 出力(kW)× radiusPerKw セルの半径内の設備を消す(在庫には戻らない)。爆発の面積は発電量の 2 乗に比例。</summary>
    public void Explode(Building b, int floorIndex, Reactor r, GameData data)
    {
        var fl = b.Floors[floorIndex];
        var def = data.Structures[r.DefId];
        int output = Building.ReactorOutput(def, r.Quality, data) * r.OutputPct / 100;
        double radius = output * data.BlastRadiusPerKw;
        float cx = r.X + def.W / 2f, cy = r.Y + def.D / 2f;
        var gone = fl.Structures.Where(s => { var d = data.Structures[s.DefId]; float sx = s.X + d.W / 2f, sy = s.Y + d.D / 2f; return (sx - cx) * (sx - cx) + (sy - cy) * (sy - cy) <= radius * radius; }).ToList();
        if (!gone.Contains(r)) gone.Add(r);
        foreach (var s in gone) fl.Remove(s);
        Events.Add(new FactoryEvent { Kind = "explosion", Building = b.Id, Floor = floorIndex, X = r.X, Y = r.Y, Power = output, Radius = radius, Destroyed = gone.Count });
    }

    int BuildingOf(Structure s) => Buildings.FirstOrDefault(b => b.Floors.Any(fl => fl.Structures.Contains(s)))?.Id ?? -1;

    public static long Price(ItemStack it, GameData data) => (long)data.Items[it.Item].Price * data.TierPriceMul[(int)it.Tier] / 100 * it.Count;

    void StepTrade(Trade td, Floor fl, GameData data)
    {
        if (td.Import == null || !td.Enabled || !td.ImportEnabled) return;
        var def = data.Structures[td.DefId];
        if (td.Outbox.Count >= def.Capacity) return;
        td.Timer += td.PowerPct;
        if (td.Timer < def.Ticks * 100) return;
        td.Timer = 0;
        var it = new ItemStack(td.Import, data.ImportQuality);
        long cost = Price(it, data) * data.ImportMul / 100 * (100 - TradeBonusPct) / 100;
        if (Treasury < cost) return;   // 国庫が足りなければ買わない
        Treasury -= cost; td.Bought++; td.Outbox.Add(it);
    }

    // ---------- 各設備 ----------
    void StepMiner(Miner mi, Floor fl, GameData data)
    {
        var ore = fl.OreAt(mi.X, mi.Y);
        if (!mi.Enabled || ore == null || mi.Output != null) return;
        var def = data.Structures[mi.DefId];
        mi.Timer += mi.PowerPct;
        if (mi.Timer < def.Ticks * 100) return;
        mi.Timer = 0;
        mi.Output = new ItemStack(ore.Item, ore.Quality);
        if (--ore.Amount <= 0) fl.Ore![mi.X + fl.W * mi.Y] = null;
    }

    void StepPump(Pump p, Floor fl, GameData data)
    {
        if (!p.Enabled) return;
        var def = data.Structures[p.DefId];
        p.Timer += p.PowerPct;
        if (p.Timer < def.Ticks * 100) return;
        p.Timer = 0;
        foreach (var t in Network(p, fl).OfType<Tank>())
        {
            var tdef = data.Structures[t.DefId];
            if (t.Fluid != null && t.Fluid != def.Fluid) continue;
            int room = (int)(tdef.Capacity - t.Amount);
            if (room <= 0) continue;
            t.Fluid = def.Fluid; t.Amount += Math.Min(room, def.Amount);
            return;
        }
    }

    public static bool InputsReady(Machine m, RecipeDef r)
    {
        foreach (var inp in r.Inputs)
            if (m.Inputs.Where(x => x.Item == inp.Item && x.Tier >= inp.MinTier).Sum(x => x.Count) < inp.Count) return false;
        return true;
    }

    void StepMachine(Machine m, Floor fl, GameData data, Rng rng)
    {
        if (m.Recipe == null || !m.Enabled) return;
        var def = data.Structures[m.DefId];
        var r = data.Recipes[m.Recipe];
        if (m.Staff.Count < def.Workers) return;
        if (m.Progress < 0)
        {
            PullFluids(m, r, fl, data);
            if (!InputsReady(m, r)) return;
            m.Consumed.Clear();
            foreach (var inp in r.Inputs)
            {
                int need = inp.Count;
                // 受け入れられる中で品質の低いものから使う
                foreach (var it in m.Inputs.Where(x => x.Item == inp.Item && x.Tier >= inp.MinTier).OrderBy(x => x.Quality).ToList())
                {
                    if (need <= 0) break;
                    int take = Math.Min(need, it.Count);
                    m.Inputs.Remove(it);
                    if (it.Count > take) m.Inputs.Add(it with { Count = it.Count - take });
                    m.Consumed.Add(it with { Count = take });
                    need -= take;
                }
            }
            m.Progress = 0;
            return;
        }
        if (m.Outputs.Count >= 20) return;   // 出口が詰まっている
        m.Progress += m.PowerPct * EfficiencyPct / 100;
        if (m.Progress < r.Ticks * 100) return;
        m.Progress = -1;
        int skill = m.Staff.Count == 0 ? 0 : m.Staff.Max(w => w.Skill);
        int staffMul = Math.Max(0, 100 - skill * data.DefectReductionPerSkill) * FailureMulPct / 100;   // 工場長の効果(G19)
        int bias = m.Staff.Any(w => w.IsRobot) ? data.RobotNormalBias : 0;   // 製造ロボットは通常品質に偏る(成功も失敗もしにくい)
        int highMul = def.QualityHighMul * r.QualityHighMul / 100 * (r.MechGeneration > 0 ? HighQualityMulPct : 100) / 100;   // ロボット技師は機体の部品だけ
        int q = Quality.Roll(rng, def.QualityTopMul * r.QualityTopMul / 100, highMul, def.QualityDefectMul * r.QualityDefectMul / 100 * staffMul / 100, bias);
        if (r.MechGeneration > 0)
        {   // 機体: 失敗なし(部品の品質が規定を満たしていれば必ず組み上がる)。品質は部品の平均。世代はレシピ
            var mech = new Mech { Id = NextMechId++, Generation = r.MechGeneration, Archetype = m.Archetype, PowerSource = r.PowerSource ?? "reactor", Building = BuildingOf(m), MadeTick = 0 };
            foreach (var c in m.Consumed) for (int i = 0; i < c.Count; i++) mech.Parts.Add(new MechPart { Item = c.Item, Quality = c.Quality });   // 1 個ずつ(腕 2・脚 2)
            mech.Quality = (int)Math.Round(mech.Parts.Average(x => x.Quality));
            Mechs.Add(mech); m.Made++;
            Events.Add(new FactoryEvent { Kind = "mech", Building = mech.Building, Floor = 0, X = m.X, Y = m.Y, Power = mech.Generation, Destroyed = mech.Quality });
            return;
        }
        if (Quality.TierOf(q) == QualityTier.Defect)
        {
            m.Failed++;
            foreach (var c in m.Consumed)
            {
                if (!data.Items[c.Item].Recyclable) continue;
                int back = c.Count * r.ScrapRate / 10_000;
                if (back > 0) m.Outputs.Add(new ItemStack(c.Item, Quality.NormalMin, back));   // スクラップ: 原材料を通常品質の下限で回収
            }
            return;
        }
        foreach (var o in r.Outputs) { for (int i = 0; i < o.Count; i++) m.Outputs.Add(new ItemStack(o.Item, q)); m.Made += o.Count; }
    }

    void PullFluids(Machine m, RecipeDef r, Floor fl, GameData data)
    {
        foreach (var inp in r.Inputs)
        {
            if (!data.Items[inp.Item].Fluid) continue;
            int have = m.Inputs.Where(x => x.Item == inp.Item).Sum(x => x.Count);
            int want = inp.Count * 2 - have;
            if (want <= 0) continue;
            foreach (var t in Network(m, fl).OfType<Tank>())
            {
                if (t.Fluid != inp.Item || t.Amount <= 0) continue;
                int take = Math.Min(want, t.Amount);
                t.Amount -= take; want -= take;
                m.Inputs.Add(new ItemStack(inp.Item, Quality.NormalMin, take));
                if (t.Amount == 0) t.Fluid = null;
                if (want <= 0) break;
            }
        }
    }

    /// <summary>配管でつながる設備(配管・タンク・揚水機・機械)。6 方向の隣接で辿る。</summary>
    static List<Structure> Network(Structure from, Floor fl)
    {
        var seen = new HashSet<Structure> { from };
        var queue = new Queue<Structure>(); queue.Enqueue(from);
        var result = new List<Structure>();
        while (queue.Count > 0)
        {
            var s = queue.Dequeue();
            result.Add(s);
            if (s is Machine && s != from) continue;   // 機械は端点
            foreach (Direction d in Enum.GetValues<Direction>())
            {
                var n = fl.Ahead(s, d);
                if (n == null || seen.Contains(n)) continue;
                if (n is Pipe or Tank or Pump or Machine) { seen.Add(n); queue.Enqueue(n); }
            }
        }
        return result;
    }

    void StepInserter(Inserter ins, Floor fl, GameData data)
    {
        if (!ins.Enabled) return;
        var def = data.Structures[ins.DefId];
        ins.Timer += ins.PowerPct;
        if (ins.Timer < def.Ticks * 100) return;
        if (ins.Hand == null)
        {
            var src = fl.Behind(ins);
            if (src == null) return;
            ins.Hand = TryTake(src);
            if (ins.Hand != null) ins.Timer = 0;
            return;
        }
        var dst = fl.Ahead(ins, ins.Dir);
        if (dst != null && TryInsert(dst, ins.Hand.Value, data)) { ins.Hand = null; ins.Timer = 0; }
    }

    void StepBelts(Floor fl, GameData data)
    {
        var belts = fl.Structures.OfType<Belt>().ToList();
        // 1) コマの前進(出口側から詰める)
        foreach (var b in belts)
        {
            var def = data.Structures[b.DefId];
            if (++b.Timer < def.TicksPerStep) continue;
            b.Timer = 0;
            for (int i = 2; i >= 0; i--) if (b.Slots[i] != null && b.Slots[i + 1] == null) { b.Slots[i + 1] = b.Slots[i]; b.Slots[i] = null; }
            b.Slots[3] ??= null;
        }
        // 2) 出口から次のセルへ(ベルトの入口・口・箱・機械)
        foreach (var b in belts)
        {
            if (b.Slots[3] == null || b.Timer != 0) continue;
            var next = fl.Ahead(b, b.Dir);
            if (next == null || next is Belt nb && nb.Dir == b.Dir.Opposite()) continue;
            if (TryInsert(next, b.Slots[3]!.Value, data)) b.Slots[3] = null;
        }
    }

    void StepPort(Port po, Floor fl, GameData data)
    {
        var def = data.Structures[po.DefId];
        // 到着したものを前のセルへ出す
        if (po.Inbox.Count > 0)
        {
            var dst = fl.Ahead(po, po.Dir);
            if (dst != null && dst is not Port && TryInsert(dst, po.Inbox[0], data)) po.Inbox.RemoveAt(0);
        }
        // 受けたものを対の口へ送る(毎秒 PerSecond 個)
        if (po.Buffer.Count == 0 || po.LinkBuilding < 0) return;
        po.Timer += po.PowerPct;
        int interval = Clock.TicksPerSecond * 100 / Math.Max(1, def.PerSecond);
        if (po.Timer < interval) return;
        var target = Get(po.LinkBuilding)?.Find(po.LinkFloor, po.LinkPort) as Port;
        if (target == null || target.Inbox.Count >= 8) return;
        po.Timer = 0;
        target.Inbox.Add(po.Buffer[0]); po.Buffer.RemoveAt(0);
    }
}

/// <summary>設計図: 設備の相対配置とレシピ。コピー&ペーストと引っ越しに使う。</summary>
public sealed class Blueprint
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int W { get; set; }
    public int D { get; set; }
    public List<BlueprintEntry> Entries { get; set; } = new();
}
public sealed class BlueprintEntry { public string Def { get; set; } = ""; public int X { get; set; } public int Y { get; set; } public int Z { get; set; } public Direction Dir { get; set; } public string? Recipe { get; set; } public BatteryMode? BatteryMode { get; set; } public string? Import { get; set; } public Program? Program { get; set; } public string Label { get; set; } = ""; }

public sealed class FactoryEvent { public string Kind { get; set; } = ""; public int Building { get; set; } public int Floor { get; set; } public int X { get; set; } public int Y { get; set; } public int Power { get; set; } public double Radius { get; set; } public int Destroyed { get; set; } }

/// <summary>機体(手順 3)。世代はレシピ、系統は組立機の設定、動力源はレシピ、品質は部品の平均。</summary>
public sealed class Mech
{
    public int Id { get; set; }
    public int Generation { get; set; }
    public string Archetype { get; set; } = "general";
    public string PowerSource { get; set; } = "reactor";
    public int Quality { get; set; }
    public int Building { get; set; }
    public long MadeTick { get; set; }
    public List<MechPart> Parts { get; set; } = new();
    /// <summary>制御基板(索敵・戦闘のプログラム)。評価は戦闘の実装時。</summary>
    public Program? Program { get; set; }
}
public sealed class MechPart { public string Item { get; set; } = ""; public int Quality { get; set; } }
