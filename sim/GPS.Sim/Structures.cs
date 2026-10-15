using System.Text.Json.Serialization;

namespace GPS.Sim;

/// <summary>向き。East = +x、North = +y、Up = +z(階の中の上下。階高 3 セル)。</summary>
public enum Direction { East = 0, North = 1, West = 2, South = 3, Up = 4, Down = 5 }

public static class Directions
{
    public static (int dx, int dy, int dz) Delta(this Direction d) => d switch
    {
        Direction.East => (1, 0, 0), Direction.North => (0, 1, 0), Direction.West => (-1, 0, 0), Direction.South => (0, -1, 0), Direction.Up => (0, 0, 1), _ => (0, 0, -1),
    };
    public static Direction Opposite(this Direction d) => d switch
    {
        Direction.East => Direction.West, Direction.West => Direction.East, Direction.North => Direction.South, Direction.South => Direction.North, Direction.Up => Direction.Down, _ => Direction.Up,
    };
}

/// <summary>設備の定義(structures.json)。大きさはセル。Power は消費電力(kW)。Workers は必要な要員数。</summary>
public sealed record StructureDef(string Id, string Name, string Kind, int W, int D, int H, int Power, int Workers, int Priority, int TechLevel,
                                  int TicksPerStep = 10, int Ticks = 30, long Capacity = 100, int PerSecond = 4, int Generation = 0, int Output = 0,
                                  string? Fluid = null, int Amount = 0, int QualityTopMul = 100, int QualityHighMul = 100, int QualityDefectMul = 100, int ChargeRate = 0, int DischargeRate = 0);

/// <summary>動力不足時の挙動(2026-10-15 確定: プレイヤーが設定する)。</summary>
public enum PowerPolicy { Proportional = 0, Priority = 1, Stop = 2 }

/// <summary>要員。技能 0〜10 は失敗率だけに効く。IsRobot = 製造ロボット(品質から技能を決め、製造は通常品質に偏る)。</summary>
public sealed record Worker(int Id, string Name, int Skill, bool IsRobot = false, int RobotQuality = 0);

/// <summary>設備の実体。保存のため多相 JSON。</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$t")]
[JsonDerivedType(typeof(Belt), "belt")]
[JsonDerivedType(typeof(Inserter), "inserter")]
[JsonDerivedType(typeof(Miner), "miner")]
[JsonDerivedType(typeof(Chest), "chest")]
[JsonDerivedType(typeof(Machine), "machine")]
[JsonDerivedType(typeof(Reactor), "reactor")]
[JsonDerivedType(typeof(Tank), "tank")]
[JsonDerivedType(typeof(Pipe), "pipe")]
[JsonDerivedType(typeof(Pump), "pump")]
[JsonDerivedType(typeof(Port), "port")]
[JsonDerivedType(typeof(Battery), "battery")]
[JsonDerivedType(typeof(Trade), "trade")]
[JsonDerivedType(typeof(Mcu), "mcu")]
public abstract class Structure
{
    public int Id { get; set; }
    public string DefId { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public Direction Dir { get; set; }
    /// <summary>このティックに動力を得た割合(%)。動力の割り当てで毎ティック決まる。</summary>
    [JsonIgnore] public int PowerPct { get; set; } = 100;
    /// <summary>稼働の有効/無効(マイコンの出力や手動で止める)。false の設備は動かず電力も使わない。</summary>
    public bool Enabled { get; set; } = true;

    public static Structure Create(StructureDef def) => def.Kind switch
    {
        "belt" => new Belt(), "inserter" => new Inserter(), "miner" => new Miner(), "chest" => new Chest(),
        "furnace" or "assembler" or "mech_assembler" => new Machine(), "reactor" => new Reactor(), "battery" => new Battery(), "trade" => new Trade(), "mcu" => new Mcu(), "tank" => new Tank(), "pipe" => new Pipe(), "pump" => new Pump(), "port" => new Port(),
        _ => throw new InvalidDataException($"unknown structure kind {def.Kind}"),
    };
}

/// <summary>ベルト: 1 レーン 4 コマ。入口 0 → 出口 3。</summary>
public sealed class Belt : Structure
{
    public ItemStack?[] Slots { get; set; } = new ItemStack?[4];
    public int Timer { get; set; }
}

public sealed class Inserter : Structure
{
    public ItemStack? Hand { get; set; }
    public int Timer { get; set; }
}

public sealed class Miner : Structure
{
    public int Timer { get; set; }
    public ItemStack? Output { get; set; }
}

public sealed class Chest : Structure
{
    public List<ItemStack> Items { get; set; } = new();
}

/// <summary>炉・組立機。レシピを 1 つ選んで動く。要員が揃わないと動かない。</summary>
public sealed class Machine : Structure
{
    public string? Recipe { get; set; }
    public List<ItemStack> Inputs { get; set; } = new();
    public List<ItemStack> Outputs { get; set; } = new();
    public List<Worker> Staff { get; set; } = new();
    /// <summary>進捗(ティック × 100。動力 50% なら 1 ティックで 50 進む)。</summary>
    public int Progress { get; set; } = -1;   // -1 = 未着手
    public List<ItemStack> Consumed { get; set; } = new();   // 着手時に消費した入力(失敗時のスクラップ計算用)
    /// <summary>機体組立機の系統(heavy / fast / general)。</summary>
    public string Archetype { get; set; } = "general";
    public long Made { get; set; }
    public long Failed { get; set; }
}

/// <summary>リアクター: 世代の上限出力 × 精度(品質)。燃料は要らない。</summary>
public sealed class Reactor : Structure
{
    public int Quality { get; set; } = 70;
    /// <summary>出力設定(%)。100〜OverloadMax。100 超で負荷が溜まり、100 未満で回復する。</summary>
    public int OutputPct { get; set; } = 100;
    /// <summary>過負荷の蓄積(%·ティック)。OverloadLimit に達すると爆発。</summary>
    public long Load { get; set; }
}

public sealed class Tank : Structure
{
    public string? Fluid { get; set; }
    public int Amount { get; set; }
}

public sealed class Pipe : Structure { }

/// <summary>交易所(2026-10-15: 輸出入は無制限)。Export に入った品は売る。Import を設定すると ticks ごとに 1 個買って出口に置く。</summary>
public sealed class Trade : Structure
{
    public string? Import { get; set; }
    public bool ImportEnabled { get; set; } = true;
    public List<ItemStack> Outbox { get; set; } = new();   // 買った品(インサータが取る)
    public int Timer { get; set; }
    public long Sold { get; set; }
    public long Bought { get; set; }
}

/// <summary>バッテリーの方式(2026-10-15 指示): ピーク用 = 需要が供給を超えたときだけ放電、非常用 = リアクターの供給が止まったときだけ放電、停止 = 充電も放電もしない。</summary>
public enum BatteryMode { Peak = 0, Emergency = 1, Off = 2 }

/// <summary>バッテリー: 余剰電力で充電(kW·ティック)、方式に応じて放電。機体はリアクターの代わりに搭載できる(機体側のデータ)。</summary>
public sealed class Battery : Structure
{
    public long Stored { get; set; }
    public BatteryMode Mode { get; set; } = BatteryMode.Peak;
}

/// <summary>揚水機: 一定ティックごとに流体を出す(つながる配管網へ)。</summary>
public sealed class Pump : Structure
{
    public int Timer { get; set; }
}

/// <summary>昇降機・渡り廊下の口。対になる口(別の階・別の建物)へ毎秒 PerSecond 個を送る。入口側は後ろのセルから受け、出口側は前のセルへ出す。</summary>
public sealed class Port : Structure
{
    public int LinkBuilding { get; set; } = -1;
    public int LinkFloor { get; set; } = -1;
    public int LinkPort { get; set; } = -1;
    public List<ItemStack> Buffer { get; set; } = new();   // 自分の階で受けたもの(対の口へ送る)
    public List<ItemStack> Inbox { get; set; } = new();    // 対の口から届いたもの(前のセルへ出す)
    public int Timer { get; set; }
}
