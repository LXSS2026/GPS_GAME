namespace GPS.Sim;

/// <summary>ミクロの地図の見本工場(描画の Equipment.SampleFactory と同じ配置)を命令列で組む。6 行周期の生産ライン(ベルト・インサータ・機械・インサータ・ベルト)、20 列ごとの通路、縁に採掘機。試験と負荷計測に使う。</summary>
public static class SampleFactory
{
    /// <summary>平地ブロック w × d(鉱脈付き)。戻り値は投入した命令。</summary>
    public static List<ICommand> Ground(int building, int w, int d, int reactors = 8, string reactor = "reactor_g1")
    {
        var cmds = new List<ICommand> { new CreateBuilding(building, "見本工場", w, d, 1, true) };
        int sid = 1;
        var machines = new List<int>();
        for (int j = 0; j + 4 < d; j += 6)
        {
            for (int i = 0; i < w; i++)
            {
                if (i % 20 == 19 || i % 20 == 0) continue;   // 通路
                cmds.Add(new PlaceStructure(building, 0, "belt", i, j, 0, Direction.East)); sid++;
                cmds.Add(new PlaceStructure(building, 0, "inserter", i, j + 1, 0, Direction.North)); sid++;
                cmds.Add(new PlaceStructure(building, 0, (i & 1) == 0 ? "furnace" : "assembler", i, j + 2, 0, Direction.South)); machines.Add(sid++);
                cmds.Add(new PlaceStructure(building, 0, "inserter", i, j + 3, 0, Direction.North)); sid++;
                cmds.Add(new PlaceStructure(building, 0, "belt", i, j + 4, 0, Direction.West)); sid++;
            }
            for (int i = 0; i < w; i += 20)
            {
                if (i + 19 < w) { cmds.Add(new PlaceStructure(building, 0, "chest", i + 19, j + 2, 0, Direction.North)); sid++; }
            }
        }
        for (int i = 2; i < w; i += 7)
        {
            cmds.Add(new SetOre(building, i, d - 1, "iron_ore", 20_000_000, 30 + (i * 7) % 41));
            cmds.Add(new PlaceStructure(building, 0, "miner", i, d - 1, 0, Direction.South)); sid++;
        }
        for (int k = 0; k < reactors; k++) { cmds.Add(new PlaceStructure(building, 0, reactor, (k * 20) % Math.Max(1, w - 1), d - 2, 0, Direction.North)); sid++; }
        // レシピと要員: 炉は鉄板、組立機は歯車
        foreach (var id in machines)
        {
            bool furnace = (id % 2) == 1;   // 並びは belt, inserter, machine... なので id の偶奇では判別できない。定義で判断する
            cmds.Add(new SetRecipeByKind(building, 0, id));
            cmds.Add(new AssignStaff(building, 0, id, new Worker(id, "作業員", 3)));
        }
        return cmds;
    }
}

/// <summary>独立した生産線 n 本(採掘機 → インサータ → 炉 → インサータ → 箱)。試験の精度比較用。</summary>
public static class SampleLines
{
    public static List<ICommand> Build(int building, int n, int reactors = 4, string reactor = "reactor_g3")
    {
        var cmds = new List<ICommand> { new CreateBuilding(building, "生産線", Math.Max(n, 8), 6, 1, true) };
        int sid = 1; var machines = new List<int>();
        for (int x = 0; x < n; x++)
        {
            cmds.Add(new SetOre(building, x, 4, "iron_ore", 20_000_000, 30 + (x * 7) % 41));
            cmds.Add(new PlaceStructure(building, 0, "miner", x, 4, 0, Direction.South)); sid++;
            cmds.Add(new PlaceStructure(building, 0, "inserter", x, 3, 0, Direction.South)); sid++;
            cmds.Add(new PlaceStructure(building, 0, "furnace", x, 2, 0, Direction.South)); machines.Add(sid++);
            cmds.Add(new PlaceStructure(building, 0, "inserter", x, 1, 0, Direction.South)); sid++;
            cmds.Add(new PlaceStructure(building, 0, "chest", x, 0, 0, Direction.South)); sid++;
        }
        for (int k = 0; k < reactors; k++) { cmds.Add(new PlaceStructure(building, 0, reactor, k, 5, 0, Direction.East)); sid++; }
        foreach (var id in machines) { cmds.Add(new SetRecipe(building, 0, id, "iron_plate")); cmds.Add(new AssignStaff(building, 0, id, new Worker(id, "作業員", 3))); }
        return cmds;
    }
}

/// <summary>設備の種類に応じて既定のレシピを付ける(炉 = 鉄板、組立機 = 歯車)。見本専用。</summary>
public sealed record SetRecipeByKind(int Building, int Floor, int Id) : ICommand
{
    public void Apply(Simulation s)
    {
        if (s.Factory.Get(Building)?.Find(Floor, Id) is not Machine m) return;
        m.Recipe = s.Data.Structures[m.DefId].Kind == "furnace" ? "iron_plate" : "gear";
        m.Progress = -1;
    }
}
