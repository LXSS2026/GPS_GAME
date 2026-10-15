namespace GPS.Sim;

/// <summary>建物の新設・撤去と街路の敷設(都市の画面の操作。G17 の次段階、2026-10-15)。費用は国庫、床面積は 10 m 単位の正方形で段階の上限まで(G4)。</summary>
public static class Construction
{
    public static string[] RoadBlocks = { "RoadX", "RoadY", "Asphalt" };

    public static int MaxSize(Simulation s) { var st = Math.Clamp((s.Factory.City?.Stage ?? 1) - 1, 0, s.CityData.BuildMaxSize.Count - 1); return s.CityData.BuildMaxSize[st]; }
    public static int MaxFloors(Simulation s) { var st = Math.Clamp((s.Factory.City?.Stage ?? 1) - 1, 0, s.CityData.BuildMaxFloors.Count - 1); return s.CityData.BuildMaxFloors[st]; }
    public static long Cost(Simulation s, string use, int size, int floors) => (long)Math.Round(size * size * floors * s.CityData.BuildCostPerCell.GetValueOrDefault(use, 2.0));

    /// <summary>置けるか。地図の内側、他の建物と重ならない、街路の上でない。</summary>
    public static string? CheckSite(Simulation s, int x, int z, int size, int floors, string use, int ignoreBuilding = -1)
    {
        var f = s.Factory; var cd = s.CityData;
        if (!cd.Uses.ContainsKey(use)) return "用途が無い";
        if (size < 10 || size % 10 != 0 || size > MaxSize(s)) return $"床面積は 10 m 単位で {MaxSize(s)} m 角まで";
        if (floors < 1 || floors > MaxFloors(s)) return $"階数は {MaxFloors(s)} まで";
        if (cd.Uses[use].MaxFloors > 0 && floors > cd.Uses[use].MaxFloors) return $"{cd.Uses[use].Name} は {cd.Uses[use].MaxFloors} 階まで";
        if (x < 0 || z < 0 || x + size > cd.MapSize || z + size > cd.MapSize) return "地図の外";
        foreach (var b in f.Buildings)
        {
            if (b.Id == ignoreBuilding || b.Floors.Count == 0 || b.IsShip) continue;
            int bw = b.Floors[0].W, bd = b.Floors[0].D;
            if (x < b.X + bw && x + size > b.X && z < b.Y + bd && z + size > b.Y) return $"{b.Name} と重なる";
        }
        var roads = RoadIds(s);
        for (int i = 0; i < size; i++) for (int j = 0; j < size; j++) for (int y = 0; y <= 8; y++) if (roads.Contains(s.World.Get(x + i, y, z + j))) return "街路の上には建てられない";
        return null;
    }
    public static HashSet<byte> RoadIds(Simulation s) => new(RoadBlocks.Where(s.Blocks.Has).Select(s.Blocks.IdOf));

    /// <summary>地面の最上段の高さ(街路の敷設先)。見本の村は y = 2。</summary>
    public static int GroundY(Simulation s, int x, int z) { for (int y = 8; y >= 0; y--) if (s.World.Get(x, y, z) != BlockRegistry.Air) return y; return 0; }
}

/// <summary>建物を建てる。費用は国庫(創造モードでは無料)。Id は 0 以下なら自動。</summary>
public sealed record BuildBuilding(int Id, string Name, string Use, int X, int Z, int Size, int Floors) : ICommand
{
    public void Apply(Simulation s)
    {
        var f = s.Factory; int id = Id > 0 ? Id : (f.Buildings.Count == 0 ? 1 : f.Buildings.Max(b => b.Id) + 1);
        if (f.Get(id) != null) return;
        var err = Construction.CheckSite(s, X, Z, Size, Floors, Use);
        if (err != null) { f.Events.Add(new FactoryEvent { Kind = "build_failed", Building = id, X = X, Y = Z }); return; }
        long cost = f.Creative ? 0 : Construction.Cost(s, Use, Size, Floors);
        if (f.Treasury < cost) { f.Events.Add(new FactoryEvent { Kind = "build_failed", Building = id, X = X, Y = Z, Power = (int)cost }); return; }
        f.Treasury -= cost;
        new CreateBuilding(id, Name, Size, Size, Floors, Use == "factory").Apply(s);
        new SetBuildingPosition(id, X, Z).Apply(s); new SetBuildingUse(id, Use).Apply(s);
        if (f.City != null) f.Get(id)!.RoadConnected = false;
        CitySim.RefreshRoads(s);
        f.Events.Add(new FactoryEvent { Kind = "built", Building = id, X = X, Y = Z, Power = (int)cost });
    }
}

/// <summary>建物を撤去する(費用の半分が戻る。拠点・AI 勢力の拠点・隊の格納庫は撤去できない)。</summary>
public sealed record Demolish(int Building) : ICommand
{
    public void Apply(Simulation s)
    {
        var f = s.Factory; var b = f.Get(Building); if (b == null) return;
        if (f.City?.Capital == Building || (f.Diplomacy?.Powers.Any(p => p.HomeBuilding == Building) ?? false) || f.Squads.Any(q => q.HomeBuilding == Building)) return;
        long refund = f.Creative || b.Floors.Count == 0 ? 0 : (long)Math.Round(Construction.Cost(s, b.Use, b.Floors[0].W, b.Floors.Count) * s.CityData.BuildRefund);
        f.Buildings.Remove(b); f.Treasury += refund;
        f.Events.Add(new FactoryEvent { Kind = "demolished", Building = Building, Power = (int)refund });
    }
}

/// <summary>街路を敷く(軸に沿った帯。幅 Width セル)。Remove なら広場に戻す。費用は 1 セルごと。</summary>
public sealed record BuildRoad(int X0, int Z0, int X1, int Z1, int Width = 4, bool Remove = false) : ICommand
{
    public void Apply(Simulation s)
    {
        var f = s.Factory; var cd = s.CityData;
        int x0 = Math.Min(X0, X1), x1 = Math.Max(X0, X1), z0 = Math.Min(Z0, Z1), z1 = Math.Max(Z0, Z1);
        bool horizontal = x1 - x0 >= z1 - z0;
        if (horizontal) { z1 = z0 + Width - 1; } else { x1 = x0 + Width - 1; }
        if (x0 < 0 || z0 < 0 || x1 >= cd.MapSize || z1 >= cd.MapSize) return;
        var roads = Construction.RoadIds(s);
        byte asphalt = s.Blocks.IdOf("Asphalt"), plaza = s.Blocks.IdOf("Plaza");
        var cells = new List<(int x, int z, int y)>();
        for (int x = x0; x <= x1; x++) for (int z = z0; z <= z1; z++)
        {
            if (f.Buildings.Any(b => b.Floors.Count > 0 && x >= b.X && x < b.X + b.Floors[0].W && z >= b.Y && z < b.Y + b.Floors[0].D)) continue;   // 建物の下は飛ばす
            int y = Construction.GroundY(s, x, z); bool isRoad = roads.Contains(s.World.Get(x, y, z));
            if (Remove ? isRoad : !isRoad) cells.Add((x, z, y));
        }
        long cost = f.Creative || Remove ? 0 : (long)Math.Round(cells.Count * cd.RoadCostPerCell);
        if (f.Treasury < cost) { f.Events.Add(new FactoryEvent { Kind = "build_failed", X = x0, Y = z0, Power = (int)cost }); return; }
        f.Treasury -= cost;
        foreach (var (x, z, y) in cells) s.World.Set(x, y, z, Remove ? plaza : asphalt);
        CitySim.RefreshRoads(s);
        f.Events.Add(new FactoryEvent { Kind = Remove ? "road_removed" : "road_built", X = x0, Y = z0, Destroyed = cells.Count, Power = (int)cost });
    }
}
