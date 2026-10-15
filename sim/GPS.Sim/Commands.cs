namespace GPS.Sim;

/// <summary>建物(または平地ブロック)を作る。Ground = true なら 1 階に鉱脈を置ける。</summary>
public sealed record CreateBuilding(int Id, string Name, int W, int D, int Floors, bool Ground) : ICommand
{
    public void Apply(Simulation s)
    {
        if (s.Factory.Get(Id) != null) return;
        var b = new Building { Id = Id, Name = Name };
        for (int i = 0; i < Floors; i++) b.Floors.Add(new Floor { W = W, D = D, Ore = Ground && i == 0 ? new OreCell?[W * D] : null });
        s.Factory.Buildings.Add(b);
        s.Factory.Buildings.Sort((a, c) => a.Id.CompareTo(c.Id));
        s.Factory.NextBuildingId = Math.Max(s.Factory.NextBuildingId, Id + 1);
    }
}

/// <summary>設備を置く。在庫から 1 つ使う(Creative なら不要)。置けなければ何もしない。</summary>
public sealed record PlaceStructure(int Building, int Floor, string Def, int X, int Y, int Z, Direction Dir) : ICommand
{
    public void Apply(Simulation s)
    {
        var b = s.Factory.Get(Building); if (b == null || Floor < 0 || Floor >= b.Floors.Count) return;
        if (!s.Data.Structures.TryGetValue(Def, out var def)) return;
        var fl = b.Floors[Floor];
        if (!fl.Fits(def, X, Y, Z)) return;
        if (!s.Factory.Creative)
        {
            if (b.Inventory.GetValueOrDefault(Def) <= 0) return;
            b.Inventory[Def]--;
        }
        var st = Structure.Create(def);
        st.Id = b.NextStructureId++; st.DefId = Def; st.X = X; st.Y = Y; st.Z = Z; st.Dir = Dir;
        fl.Add(st, def);
    }
}

/// <summary>撤去。設備は在庫に戻る(全額)。中身のアイテムは消える(第 1 段階)。</summary>
public sealed record RemoveStructure(int Building, int Floor, int Id) : ICommand
{
    public void Apply(Simulation s)
    {
        var b = s.Factory.Get(Building); var st = b?.Find(Floor, Id); if (b == null || st == null) return;
        b.Floors[Floor].Remove(st);
        b.Inventory[st.DefId] = b.Inventory.GetValueOrDefault(st.DefId) + 1;
    }
}

public sealed record SetRecipe(int Building, int Floor, int Id, string? Recipe) : ICommand
{
    public void Apply(Simulation s)
    {
        if (s.Factory.Get(Building)?.Find(Floor, Id) is not Machine m) return;
        if (Recipe != null && (!s.Data.Recipes.TryGetValue(Recipe, out var r) || r.Machine != s.Data.Structures[m.DefId].Kind)) return;
        m.Recipe = Recipe; m.Progress = -1; m.Consumed.Clear();
    }
}

public sealed record AssignStaff(int Building, int Floor, int Id, Worker Worker) : ICommand
{
    public void Apply(Simulation s)
    {
        if (s.Factory.Get(Building)?.Find(Floor, Id) is not Machine m) return;
        m.Staff.RemoveAll(w => w.Id == Worker.Id);
        m.Staff.Add(Worker);
    }
}

public sealed record SetPolicy(int Building, PowerPolicy Policy) : ICommand
{
    public void Apply(Simulation s) { var b = s.Factory.Get(Building); if (b != null) b.Policy = Policy; }
}

/// <summary>口どうしを結ぶ(昇降機: 同じ建物の別の階、渡り廊下: 別の建物)。双方向。</summary>
public sealed record LinkPorts(int BuildingA, int FloorA, int PortA, int BuildingB, int FloorB, int PortB) : ICommand
{
    public void Apply(Simulation s)
    {
        if (s.Factory.Get(BuildingA)?.Find(FloorA, PortA) is not Port a || s.Factory.Get(BuildingB)?.Find(FloorB, PortB) is not Port b) return;
        a.LinkBuilding = BuildingB; a.LinkFloor = FloorB; a.LinkPort = PortB;
        b.LinkBuilding = BuildingA; b.LinkFloor = FloorA; b.LinkPort = PortA;
    }
}

public sealed record AddInventory(int Building, string Def, int Count) : ICommand
{
    public void Apply(Simulation s) { var b = s.Factory.Get(Building); if (b != null) b.Inventory[Def] = b.Inventory.GetValueOrDefault(Def) + Count; }
}

/// <summary>鉱脈を置く(平地ブロックの 1 階)。品質は 30〜70。</summary>
public sealed record SetOre(int Building, int X, int Y, string Item, long Amount, int Quality) : ICommand
{
    public void Apply(Simulation s)
    {
        var b = s.Factory.Get(Building); if (b == null || b.Floors[0].Ore == null) return;
        var fl = b.Floors[0]; if (X < 0 || Y < 0 || X >= fl.W || Y >= fl.D) return;
        fl.Ore![X + fl.W * Y] = Amount > 0 ? new OreCell { Item = Item, Amount = Amount, Quality = Math.Clamp(Quality, 30, 70) } : null;
    }
}

/// <summary>箱やタンクにアイテムを入れる(輸入・試験用)。</summary>
public sealed record AddItems(int Building, int Floor, int Id, string Item, int Quality, int Count) : ICommand
{
    public void Apply(Simulation s)
    {
        var st = s.Factory.Get(Building)?.Find(Floor, Id);
        switch (st)
        {
            case Chest c: for (int i = 0; i < Count; i++) c.Items.Add(new ItemStack(Item, Quality)); break;
            case Tank t: if (t.Fluid == null || t.Fluid == Item) { t.Fluid = Item; t.Amount += Count; } break;
        }
    }
}

public sealed record SetBatteryMode(int Building, int Floor, int Id, BatteryMode Mode) : ICommand
{
    public void Apply(Simulation s) { if (s.Factory.Get(Building)?.Find(Floor, Id) is Battery b) b.Mode = Mode; }
}
