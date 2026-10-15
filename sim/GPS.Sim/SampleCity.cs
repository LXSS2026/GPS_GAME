namespace GPS.Sim;

/// <summary>村の見本(G17 の都市画面用)。街区 100 m × 2 × 2、街路 16 m。地面のブロック(石・広場・街路)は sim.World に置き、Godot はそれをそのまま描く。</summary>
public static class SampleCity
{
    public const int Block = 100, Gap = 16, NB = 2, N = NB * Block + (NB + 1) * Gap;   // 248 m 四方
    public const int GroundTop = 2;   // 地面の最上段(y)

    public sealed record Lot(int Id, string Name, string Use, int X, int Z, int W, int Floors);
    public static readonly List<Lot> Lots = new()
    {
        new(1, "住居 A", "residence", 20, 20, 20, 2), new(2, "住居 B", "residence", 44, 20, 20, 2), new(3, "住居 C", "residence", 20, 44, 20, 3), new(4, "住居 D", "residence", 44, 44, 20, 2),
        new(5, "公園", "park", 70, 20, 20, 1), new(6, "市場", "market", 70, 44, 20, 1), new(7, "診療所", "clinic", 20, 70, 10, 1), new(8, "学校", "school", 44, 70, 20, 2),
        new(10, "畑", "farm", 136, 20, 40, 1), new(11, "井戸", "well", 180, 20, 10, 1), new(12, "畑 東", "farm", 136, 70, 30, 1),
        new(20, "工場棟 B-3", "factory", 20, 136, 30, 3), new(21, "工房", "factory", 60, 136, 20, 1), new(22, "格納庫", "factory", 90, 136, 12, 3),
        new(30, "警察", "police", 136, 136, 10, 2), new(31, "下水処理", "sewage", 160, 136, 20, 1), new(32, "住居 E", "residence", 190, 136, 20, 3),
    };

    static int Lane(int v) { int p = v % (Block + Gap); return p < Gap ? -1 : v / (Block + Gap); }

    public static void Build(Simulation s)
    {
        byte stone = s.Blocks.IdOf("Stone"), plaza = s.Blocks.IdOf("Plaza"), rx = s.Blocks.IdOf("RoadX"), rz = s.Blocks.IdOf("RoadY"), asphalt = s.Blocks.IdOf("Asphalt"), grass = s.Blocks.IdOf("Grass"), conc = s.Blocks.IdOf("Concrete");
        for (int x = 0; x < N; x++)
            for (int z = 0; z < N; z++)
            {
                s.World.Set(x, 0, z, stone); s.World.Set(x, 1, z, stone);
                bool roadX = Lane(z) < 0, roadZ = Lane(x) < 0;
                byte top = roadX && roadZ ? asphalt : roadX ? rx : roadZ ? rz : plaza;
                s.World.Set(x, GroundTop, z, top);
            }
        // 街区の中の小路(4 m 幅)。奥の区画も道路に接する
        for (int bi = 0; bi < NB; bi++) for (int bj = 0; bj < NB; bj++)
        {
            int x0 = Gap + bi * (Block + Gap), z0 = Gap + bj * (Block + Gap);
            for (int k = 0; k < Block; k++) foreach (int o in new[] { 40, 41, 66, 67 }) { s.World.Set(x0 + o, GroundTop, z0 + k, asphalt); s.World.Set(x0 + k, GroundTop, z0 + o, asphalt); }
        }
        foreach (var l in Lots)
        {
            byte fill = l.Use is "farm" or "park" ? grass : l.Use == "factory" ? conc : plaza;
            for (int x = l.X; x < l.X + l.W; x++) for (int z = l.Z; z < l.Z + l.W; z++) s.World.Set(x, GroundTop, z, fill);
        }
        s.World.Dirty.Clear();

        s.Factory.Creative = true;
        var cmds = new List<ICommand> { new FoundCity("アカネ", 48, 62) };
        foreach (var l in Lots) { cmds.Add(new CreateBuilding(l.Id, l.Name, l.W, l.W, l.Floors, l.Use == "factory")); cmds.Add(new SetBuildingPosition(l.Id, l.X, l.Z)); cmds.Add(new SetBuildingUse(l.Id, l.Use)); }
        // 井戸にリアクター(村の電力)
        cmds.Add(new PlaceStructure(11, 0, "reactor_g0", 4, 4, 0, Direction.East));
        // 工場棟: 1 階に生産線 3 本とリアクター、2 階に組立機
        for (int x = 0; x < 3; x++)
        {
            cmds.Add(new SetOre(20, 2 + x * 2, 10, "iron_ore", 20_000_000, 45));
            cmds.Add(new PlaceStructure(20, 0, "miner", 2 + x * 2, 10, 0, Direction.South)); cmds.Add(new PlaceStructure(20, 0, "inserter", 2 + x * 2, 9, 0, Direction.South));
            cmds.Add(new PlaceStructure(20, 0, "furnace", 2 + x * 2, 8, 0, Direction.South)); cmds.Add(new PlaceStructure(20, 0, "inserter", 2 + x * 2, 7, 0, Direction.South)); cmds.Add(new PlaceStructure(20, 0, "chest", 2 + x * 2, 6, 0, Direction.South));
        }
        cmds.Add(new PlaceStructure(20, 0, "reactor_g1", 12, 10, 0, Direction.East));
        cmds.Add(new PlaceStructure(20, 1, "assembler", 4, 4, 0, Direction.South)); cmds.Add(new PlaceStructure(20, 1, "reactor_g1", 10, 10, 0, Direction.East));
        cmds.Add(new PlaceStructure(21, 0, "furnace", 4, 4, 0, Direction.South)); cmds.Add(new PlaceStructure(21, 0, "reactor_g0", 8, 8, 0, Direction.East));
        // 格納庫: 第 2 世代の汎用機 2 機の隊
        cmds.Add(new CreateMech(1, 2, "general", 72)); cmds.Add(new CreateMech(2, 2, "general", 68)); cmds.Add(new FormSquad(1, 1, "自警団 第 1 隊", new[] { 1, 2 }, 22));
        foreach (var c in cmds) s.Enqueue(c);
        s.Step();
        // レシピと要員(要員は都市の労働人口から)
        var b20 = s.Factory.Get(20)!;
        foreach (var m in b20.Floors[0].Structures.OfType<Machine>()) { s.Enqueue(new SetRecipe(20, 0, m.Id, "iron_plate")); s.Enqueue(new HireStaff(20, 0, m.Id)); }
        foreach (var m in b20.Floors[1].Structures.OfType<Machine>()) { s.Enqueue(new SetRecipe(20, 1, m.Id, "gear")); s.Enqueue(new HireStaff(20, 1, m.Id)); }
        foreach (var m in s.Factory.Get(21)!.Floors[0].Structures.OfType<Machine>()) { s.Enqueue(new SetRecipe(21, 0, m.Id, "iron_plate")); s.Enqueue(new HireStaff(21, 0, m.Id)); }
        s.Step();
        s.Factory.Creative = false;
        // 登用候補(人物は本来 30 日ごとに現れる。見本では最初から 6 人)
        var city = s.Factory.City!;
        foreach (var (name, g, p, c, n, loy, amb, job) in new[] { ("ハルカ", 6, 3, 2, 5, 7, 3, "farmer"), ("コウタ", 3, 7, 2, 3, 6, 4, "artisan"), ("タケル", 3, 2, 8, 4, 6, 7, "militia"), ("ミオ", 5, 3, 6, 5, 9, 2, "farmer"), ("ゲン", 2, 6, 5, 2, 7, 3, "artisan"), ("サクラ", 4, 4, 3, 8, 5, 6, "merchant") })
        { city.Persons.Add(new Person { Id = city.NextPersonId, Name = $"{name} #{city.NextPersonId}", Govern = g, Produce = p, Command = c, Negotiate = n, Loyalty = loy, Ambition = amb, Job = job, Age = 25 + city.NextPersonId * 3 }); city.NextPersonId++; }
        s.Enqueue(new Appoint("head", 1)); s.Enqueue(new Appoint("factory", 2)); s.Step();   // 村長 ハルカ、工場長 コウタ。自警団長と交渉役は空席
    }
}
