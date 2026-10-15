namespace GPS.Sim;

/// <summary>航空戦艦(G1 確定要件・G2 第 5 節、2026-10-16 第 1 段階)。建物と同じ「階を持つ器」が空中を動く。格納庫として隊の帰還先になり、砲塔と防御壁(装甲)を持ち、耐久 0 で撃沈。</summary>
public static class Ships
{
    public sealed record Kind(string Id, string Name, int W, int D, int Floors, double Speed, double CostPerCell, int Altitude, int MinStage, int Bays);
    public static readonly Dictionary<string, Kind> Kinds = new()
    {
        ["small"] = new("small", "小型航空戦艦", 18, 102, 2, 12, 8, 24, 4, 2),   // 全長 102 m(ship_*_18 の区画)
        ["big"] = new("big", "大型航空戦艦", 30, 204, 3, 8, 10, 36, 4, 4),      // 全長 204 m(ship_*_30 の区画)
    };

    /// <summary>毎ティック: 目標へ向かって動く(機体は無視して通る。空中なので地形と建物に当たらない)。</summary>
    public static void Step(Factory f)
    {
        foreach (var b in f.Buildings)
        {
            if (!b.IsShip || b.Destroyed) continue;
            if (b.Hp <= 0) { b.Destroyed = true; Sunk(f, b); continue; }
            if (b.MoveTargetX == null) continue;
            double tx = b.MoveTargetX.Value, tz = b.MoveTargetZ!.Value, dx = tx - b.X, dz = tz - b.Y, d = Math.Sqrt(dx * dx + dz * dz);
            double step = b.ShipSpeed / Clock.TicksPerSecond;
            if (d <= step) { b.X = tx; b.Y = tz; b.MoveTargetX = null; b.MoveTargetZ = null; f.Events.Add(new FactoryEvent { Kind = "ship_arrived", Building = b.Id, X = (int)tx, Y = (int)tz }); }
            else { b.X += dx / d * step; b.Y += dz / d * step; }
            // 格納庫で待機中の隊は船と一緒に動く
            foreach (var s in f.Squads.Where(s => s.HomeBuilding == b.Id && s.State == SquadState.InHangar)) foreach (var u in s.Members) { u.X = b.X; u.Y = b.Y; }
        }
    }

    /// <summary>撃沈(Combat の建物の陥落から)。搭載していた隊は失われ、乗員は都市の人口から減らない(人間の戦闘は無い = 脱出)。</summary>
    public static void Sunk(Factory f, Building b)
    {
        int lost = f.Squads.Count(s => s.HomeBuilding == b.Id);
        f.Squads.RemoveAll(s => s.HomeBuilding == b.Id);
        f.Events.Add(new FactoryEvent { Kind = "ship_sunk", Building = b.Id, X = (int)b.X, Y = (int)b.Y, Destroyed = lost });
    }
}

/// <summary>戦艦を建造する(即時。費用 = 床面積 × 階数 × 単価、国庫から。国家の段階から。創造モードは制限なし)。</summary>
public sealed record BuildShip(int Id, string Name, string Kind, double X, double Z) : ICommand
{
    public void Apply(Simulation s)
    {
        var f = s.Factory; if (!Ships.Kinds.TryGetValue(Kind, out var k)) return;
        int id = Id > 0 ? Id : (f.Buildings.Count == 0 ? 1 : f.Buildings.Max(b => b.Id) + 1); if (f.Get(id) != null) return;
        if (!f.Creative && (f.City?.Stage ?? 1) < k.MinStage) { f.Events.Add(new FactoryEvent { Kind = "build_failed", Building = id }); return; }
        long cost = f.Creative ? 0 : (long)Math.Round(k.W * k.D * k.Floors * k.CostPerCell);
        if (f.Treasury < cost) { f.Events.Add(new FactoryEvent { Kind = "build_failed", Building = id, Power = (int)cost }); return; }
        f.Treasury -= cost;
        var b = new Building { Id = id, Name = Name, Use = "factory", IsShip = true, ShipKind = Kind, ShipSpeed = k.Speed, X = X, Y = Z, RoadConnected = true };
        for (int i = 0; i < k.Floors; i++) b.Floors.Add(new Floor { W = k.W, D = k.D });
        b.MaxHp = b.Hp = k.W * k.D * k.Floors * s.DiplomacyData.War["buildingHpPerCell"] * 2;   // 船体は建物の 2 倍の装甲
        f.Buildings.Add(b);
        f.Events.Add(new FactoryEvent { Kind = "ship_built", Building = id, Power = (int)cost });
    }
}

/// <summary>建物・戦艦の修復(陥落からの復旧を含む)。費用 = 失った耐久の割合 × 建設費 × 0.5(国庫)。即時。</summary>
public sealed record RepairBuilding(int Building) : ICommand
{
    public void Apply(Simulation s)
    {
        var f = s.Factory; var b = f.Get(Building); if (b == null || b.Floors.Count == 0 || b.Hp >= b.MaxHp) return;
        long baseCost = b.IsShip ? (long)Math.Round(b.Floors[0].W * b.Floors[0].D * b.Floors.Count * Ships.Kinds[b.ShipKind].CostPerCell) : Construction.Cost(s, b.Use, b.Floors[0].W, b.Floors.Count);
        long cost = f.Creative ? 0 : (long)Math.Round((b.MaxHp - b.Hp) / b.MaxHp * baseCost * 0.5);
        if (f.Treasury < cost) { f.Events.Add(new FactoryEvent { Kind = "build_failed", Building = Building, Power = (int)cost }); return; }
        f.Treasury -= cost; b.Hp = b.MaxHp; b.Destroyed = false;
        f.Events.Add(new FactoryEvent { Kind = "building_repaired", Building = Building, Power = (int)cost });
    }
}

/// <summary>戦艦を動かす(目標地点)。</summary>
public sealed record MoveShip(int Building, double X, double Z) : ICommand
{
    public void Apply(Simulation s) { var b = s.Factory.Get(Building); if (b == null || !b.IsShip || b.Destroyed) return; b.MoveTargetX = X; b.MoveTargetZ = Z; }
}
