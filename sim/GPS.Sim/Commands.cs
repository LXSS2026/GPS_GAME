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
        var city = s.Factory.City;
        if (city != null && !s.Factory.Creative && !Worker.IsRobot && CitySim.HumanStaff(s.Factory) + city.Conscripts >= city.Population * s.CityData.LaborRate) return;   // 労働人口を超えて雇えない(G17 #2・#7)
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

/// <summary>交易所の輸入品を設定(null で輸出のみ)。</summary>
public sealed record SetImport(int Building, int Floor, int Id, string? Item) : ICommand
{
    public void Apply(Simulation s)
    {
        if (s.Factory.Get(Building)?.Find(Floor, Id) is not Trade t) return;
        if (Item != null && !s.Data.Items.ContainsKey(Item)) return;
        t.Import = Item; t.Timer = 0;
    }
}

/// <summary>国庫への入金(開始資金・試験用)。</summary>
public sealed record AddTreasury(long Amount) : ICommand
{
    public void Apply(Simulation s) => s.Factory.Treasury += Amount;
}

/// <summary>製造ロボットを箱から 1 体取り、機械の要員にする。技能 = ロボットの品質 / 10。</summary>
public sealed record AssignRobot(int Building, int Floor, int MachineId, int ChestId) : ICommand
{
    public void Apply(Simulation s)
    {
        var b = s.Factory.Get(Building); if (b == null) return;
        if (b.Find(Floor, MachineId) is not Machine m || b.Find(Floor, ChestId) is not Chest c) return;
        int idx = c.Items.FindLastIndex(it => it.Item == "factory_robot");
        if (idx < 0) return;
        var robot = c.Items[idx]; c.Items.RemoveAt(idx);
        int wid = m.Staff.Count == 0 ? 1 : m.Staff.Max(x => x.Id) + 1;
        m.Staff.Add(new Worker(wid, "製造ロボット", robot.Quality / 10, true, robot.Quality));
    }
}

/// <summary>要員を外す(人もロボットも)。ロボットは箱へ戻す(箱が無ければ消える)。</summary>
public sealed record RemoveStaff(int Building, int Floor, int MachineId, int WorkerId, int ChestId = -1) : ICommand
{
    public void Apply(Simulation s)
    {
        var b = s.Factory.Get(Building); if (b?.Find(Floor, MachineId) is not Machine m) return;
        var w = m.Staff.FirstOrDefault(x => x.Id == WorkerId); if (w == null) return;
        m.Staff.Remove(w);
        if (w.IsRobot && b.Find(Floor, ChestId) is Chest c) c.Items.Add(new ItemStack("factory_robot", w.RobotQuality));
    }
}

/// <summary>範囲をコピーして設計図にする(X0..X1, Y0..Y1 は含む。原点がその範囲内にある設備)。</summary>
public sealed record CopyRegion(int Building, int Floor, int X0, int Y0, int X1, int Y1, int BlueprintId, string Name) : ICommand
{
    public void Apply(Simulation s)
    {
        var b = s.Factory.Get(Building); if (b == null || Floor < 0 || Floor >= b.Floors.Count) return;
        var bp = new Blueprint { Id = BlueprintId, Name = Name, W = X1 - X0 + 1, D = Y1 - Y0 + 1 };
        foreach (var st in b.Floors[Floor].Structures.OrderBy(x => x.Id))
        {
            if (st.X < X0 || st.X > X1 || st.Y < Y0 || st.Y > Y1) continue;
            bp.Entries.Add(new BlueprintEntry { Def = st.DefId, X = st.X - X0, Y = st.Y - Y0, Z = st.Z, Dir = st.Dir, Recipe = (st as Machine)?.Recipe, BatteryMode = (st as Battery)?.Mode, Import = (st as Trade)?.Import, Program = (st as Mcu)?.Program, Label = st.Label });
        }
        s.Factory.Blueprints[BlueprintId] = bp;
        s.Factory.NextBlueprintId = Math.Max(s.Factory.NextBlueprintId, BlueprintId + 1);
    }
}

/// <summary>設計図を貼り付ける。在庫から設備を使う(Creative なら不要)。置けない設備は飛ばす。レシピ・方式・輸入品も写す。</summary>
public sealed record PasteBlueprint(int Building, int Floor, int X, int Y, int BlueprintId) : ICommand
{
    public void Apply(Simulation s)
    {
        if (!s.Factory.Blueprints.TryGetValue(BlueprintId, out var bp)) return;
        var b = s.Factory.Get(Building); if (b == null || Floor < 0 || Floor >= b.Floors.Count) return;
        foreach (var e in bp.Entries)
        {
            int before = b.NextStructureId;
            new PlaceStructure(Building, Floor, e.Def, X + e.X, Y + e.Y, e.Z, e.Dir).Apply(s);
            if (b.NextStructureId == before) continue;   // 置けなかった
            var st = b.Find(Floor, before)!;
            if (st is Machine m && e.Recipe != null) m.Recipe = e.Recipe;
            if (st is Battery bt && e.BatteryMode != null) bt.Mode = e.BatteryMode.Value;
            if (st is Trade td) td.Import = e.Import;
            if (st is Mcu mc && e.Program != null) mc.Program = e.Program;
            st.Label = e.Label;
        }
    }
}

/// <summary>引っ越し: 範囲の設備を撤去して(在庫へ)、別の場所へ同じ配置で置き直す。レシピ・方式・輸入品・要員は引き継ぐ。中の品は消える(第 2 段階)。</summary>
public sealed record MoveRegion(int Building, int Floor, int X0, int Y0, int X1, int Y1, int ToBuilding, int ToFloor, int ToX, int ToY) : ICommand
{
    public void Apply(Simulation s)
    {
        var b = s.Factory.Get(Building); var tb = s.Factory.Get(ToBuilding);
        if (b == null || tb == null || Floor < 0 || Floor >= b.Floors.Count || ToFloor < 0 || ToFloor >= tb.Floors.Count) return;
        var src = b.Floors[Floor].Structures.Where(st => st.X >= X0 && st.X <= X1 && st.Y >= Y0 && st.Y <= Y1).OrderBy(st => st.Id).ToList();
        // 先に全て撤去(在庫へ)、次に置き直す。元の建物と先の建物が違えば在庫を移す
        foreach (var st in src) new RemoveStructure(Building, Floor, st.Id).Apply(s);
        if (tb != b) foreach (var st in src) { b.Inventory[st.DefId]--; tb.Inventory[st.DefId] = tb.Inventory.GetValueOrDefault(st.DefId) + 1; }
        foreach (var st in src)
        {
            int before = tb.NextStructureId;
            new PlaceStructure(ToBuilding, ToFloor, st.DefId, ToX + st.X - X0, ToY + st.Y - Y0, st.Z, st.Dir).Apply(s);
            if (tb.NextStructureId == before) continue;   // 置けなかった分は在庫に残る
            var ns = tb.Find(ToFloor, before)!;
            ns.Label = st.Label; ns.Enabled = st.Enabled;
            switch (st)
            {
                case Machine m when ns is Machine nm: nm.Recipe = m.Recipe; nm.Staff.AddRange(m.Staff); break;
                case Battery bt when ns is Battery nb: nb.Mode = bt.Mode; nb.Stored = bt.Stored; break;
                case Trade td when ns is Trade nt: nt.Import = td.Import; break;
                case Reactor r when ns is Reactor nr: nr.Quality = r.Quality; nr.OutputPct = r.OutputPct; break;
                case Mcu mc when ns is Mcu nmc: nmc.Program = mc.Program; break;
            }
        }
    }
}

/// <summary>リアクターの出力設定(%)。100 未満で負荷が回復し、100 超で負荷が溜まる(上限 OverloadMax)。</summary>
public sealed record SetReactorOutput(int Building, int Floor, int Id, int Pct) : ICommand
{
    public void Apply(Simulation s) { if (s.Factory.Get(Building)?.Find(Floor, Id) is Reactor r) r.OutputPct = Math.Clamp(Pct, 0, s.Data.OverloadMax); }
}

/// <summary>機体組立機の系統(heavy / fast / general)。</summary>
public sealed record SetArchetype(int Building, int Floor, int Id, string Archetype) : ICommand
{
    public void Apply(Simulation s) { if (s.Factory.Get(Building)?.Find(Floor, Id) is Machine m && Archetype is "heavy" or "fast" or "general") m.Archetype = Archetype; }
}

/// <summary>設備の稼働の有効/無効(手動)。</summary>
public sealed record SetEnabled(int Building, int Floor, int Id, bool Enabled) : ICommand
{
    public void Apply(Simulation s) { var st = s.Factory.Get(Building)?.Find(Floor, Id); if (st != null) st.Enabled = Enabled; }
}

/// <summary>設備の名札を付ける(空で外す)。</summary>
public sealed record SetLabel(int Building, int Floor, int Id, string Label) : ICommand
{
    public void Apply(Simulation s) { var st = s.Factory.Get(Building)?.Find(Floor, Id); if (st != null) st.Label = Label.Trim(); }
}
