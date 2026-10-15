using System.Text.Json.Serialization;

namespace GPS.Sim;

/// <summary>マイコン(2026-10-15 確定: Stormworks 式のノード図)。制御室(設備)または機体の制御基板に 1 つのプログラムを載せる。
/// 入力ノードは設備の値を読み、演算ノードで加工し、出力ノードで設備の設定を変える。参照先は電気の通っている建物ならどこでもよい(範囲の制限なし)。</summary>
public sealed class Program
{
    public string Name { get; set; } = "";
    public List<McuNode> Nodes { get; set; } = new();
    public List<McuLink> Links { get; set; } = new();
    /// <summary>評価の間隔(ティック)。既定 6 = 10 回/秒。</summary>
    public int Interval { get; set; } = 6;
}

/// <summary>ノード。Type は McuCatalog の種類名。Target は参照する設備(入力・出力ノード)。Params は定数など。X, Y は図の位置(表示用)。</summary>
public sealed class McuNode
{
    public int Id { get; set; }
    public string Type { get; set; } = "";
    public int TargetBuilding { get; set; } = -1;
    public int TargetFloor { get; set; } = -1;
    public int TargetId { get; set; } = -1;
    public Dictionary<string, double> Params { get; set; } = new();
    public float X { get; set; }
    public float Y { get; set; }
    /// <summary>最後に評価した値(出力ポートごと)。保存はしない。</summary>
    [JsonIgnore] public double[] Values { get; set; } = new double[2];
}

/// <summary>線: 元ノードの出力ポート → 先ノードの入力ポート。</summary>
public sealed record McuLink(int FromNode, int FromPort, int ToNode, int ToPort);

/// <summary>ノードの種類の一覧(データ化の前段。入出力は適宜増やす)。</summary>
public static class McuCatalog
{
    public sealed record NodeType(string Type, string Name, string Group, int Inputs, int Outputs, string? TargetKind = null, string[]? ParamNames = null);

    public static readonly IReadOnlyList<NodeType> Types = new[]
    {
        // 入力(設備の値を読む)
        new NodeType("in.const", "定数", "入力", 0, 1, null, new[] { "value" }),
        new NodeType("in.tick", "時刻(ティック)", "入力", 0, 1),
        new NodeType("in.building.supply", "建物の供給 kW", "入力", 0, 1),
        new NodeType("in.building.demand", "建物の需要 kW", "入力", 0, 1),
        new NodeType("in.reactor.output", "リアクターの出力 kW", "入力", 0, 1, "reactor"),
        new NodeType("in.reactor.load", "リアクターの過負荷 %", "入力", 0, 1, "reactor"),
        new NodeType("in.reactor.setting", "リアクターの出力設定 %", "入力", 0, 1, "reactor"),
        new NodeType("in.battery.stored", "バッテリーの残量 %", "入力", 0, 1, "battery"),
        new NodeType("in.machine.active", "機械の稼働(1/0)", "入力", 0, 1, "machine"),
        new NodeType("in.machine.inputs", "機械の入力在庫", "入力", 0, 1, "machine"),
        new NodeType("in.machine.outputs", "機械の出力在庫", "入力", 0, 1, "machine"),
        new NodeType("in.chest.count", "箱の品数", "入力", 0, 1, "chest"),
        new NodeType("in.tank.amount", "タンクの量", "入力", 0, 1, "tank"),
        new NodeType("in.port.waiting", "口の待ち数", "入力", 0, 1, "port"),
        // 演算
        new NodeType("op.add", "足す", "演算", 2, 1), new NodeType("op.sub", "引く", "演算", 2, 1), new NodeType("op.mul", "掛ける", "演算", 2, 1), new NodeType("op.div", "割る", "演算", 2, 1),
        new NodeType("op.min", "小さい方", "演算", 2, 1), new NodeType("op.max", "大きい方", "演算", 2, 1), new NodeType("op.abs", "絶対値", "演算", 1, 1),
        new NodeType("op.clamp", "範囲に収める", "演算", 1, 1, null, new[] { "min", "max" }),
        new NodeType("op.gt", "より大きい", "比較", 2, 1), new NodeType("op.lt", "より小さい", "比較", 2, 1), new NodeType("op.eq", "等しい", "比較", 2, 1),
        new NodeType("op.and", "かつ", "論理", 2, 1), new NodeType("op.or", "または", "論理", 2, 1), new NodeType("op.not", "でない", "論理", 1, 1),
        new NodeType("op.select", "条件で選ぶ(条件, 真, 偽)", "論理", 3, 1),
        new NodeType("op.memory", "記憶(書込条件, 値)", "論理", 2, 1),
        // 出力(設備の設定を変える)
        new NodeType("out.reactor.setting", "リアクターの出力設定 %", "出力", 1, 0, "reactor"),
        new NodeType("out.battery.mode", "バッテリーの方式(0 ピーク/1 非常/2 停止)", "出力", 1, 0, "battery"),
        new NodeType("out.building.policy", "建物の不足時方針(0 比例/1 優先/2 停止)", "出力", 1, 0, "building"),
        new NodeType("out.structure.enabled", "設備の稼働(1/0)", "出力", 1, 0, "any"),
        new NodeType("out.trade.enabled", "交易所の輸入(1/0)", "出力", 1, 0, "trade"),
        // 機体の制御基板(索敵・戦闘。計算は戦闘の実装時。種類だけ先に定義)
        new NodeType("in.mech.hp", "自機の耐久 %", "機体", 0, 1), new NodeType("in.mech.power", "自機の電力 %", "機体", 0, 1), new NodeType("in.mech.ammo", "自機の弾薬 %", "機体", 0, 1),
        new NodeType("in.enemy.distance", "最寄りの敵との距離", "機体", 0, 1), new NodeType("in.enemy.count", "索敵範囲の敵数", "機体", 0, 1), new NodeType("in.enemy.generation", "最寄りの敵の世代", "機体", 0, 1),
        new NodeType("in.squad.order", "隊の命令(0 待機/1 前進/2 攻撃/3 退却)", "機体", 0, 1),
        new NodeType("out.mech.engage", "交戦する(1/0)", "機体", 1, 0), new NodeType("out.mech.retreat", "退却する(1/0)", "機体", 1, 0),
        new NodeType("out.mech.target", "優先目標(0 最寄り/1 最弱/2 最強/3 砲撃手)", "機体", 1, 0), new NodeType("out.mech.weapon", "使う武器(0 主/1 副/2 近接)", "機体", 1, 0),
        new NodeType("out.mech.search", "索敵範囲 m", "機体", 1, 0),
    };
    public static NodeType Of(string type) => Types.First(t => t.Type == type);
}

/// <summary>制御室(設備)。建物に電気が通っているときだけ動く。</summary>
public sealed class Mcu : Structure
{
    public Program Program { get; set; } = new();
    public int Timer { get; set; }
    public long Evaluations { get; set; }
}

/// <summary>プログラムの評価。ノードは Id 順に評価し、線は元ノードの今回の値(未評価なら前回の値)を読む(ループは 1 評価遅れ = Stormworks と同じ)。</summary>
public static class McuRunner
{
    public static void Step(Mcu mcu, Building home, Factory f, GameData data)
    {
        if (home.LastSupply <= 0) return;                   // 電気が無いと動かない
        if (++mcu.Timer < Math.Max(1, mcu.Program.Interval)) return;
        mcu.Timer = 0; mcu.Evaluations++;
        Evaluate(mcu.Program, f, data, home, (type, node) => ReadFactory(type, node, f, data, home), (type, node, v) => WriteFactory(type, node, v, f, data, home));
    }

    public static void Evaluate(Program p, Factory f, GameData data, Building home, Func<string, McuNode, double> read, Action<string, McuNode, double> write)
    {
        var byId = p.Nodes.ToDictionary(n => n.Id);
        var inputs = new Dictionary<int, double[]>();
        foreach (var n in p.Nodes.OrderBy(n => n.Id))
        {
            var t = McuCatalog.Of(n.Type);
            var args = new double[Math.Max(t.Inputs, 1)];
            foreach (var l in p.Links.Where(l => l.ToNode == n.Id && l.ToPort < args.Length))
                if (byId.TryGetValue(l.FromNode, out var src) && l.FromPort < src.Values.Length) args[l.ToPort] = src.Values[l.FromPort];
            double v = n.Type switch
            {
                "in.const" => n.Params.GetValueOrDefault("value"),
                "in.tick" => read(n.Type, n),
                var s when s.StartsWith("in.") => read(s, n),
                "op.add" => args[0] + args[1], "op.sub" => args[0] - args[1], "op.mul" => args[0] * args[1], "op.div" => args[1] == 0 ? 0 : args[0] / args[1],
                "op.min" => Math.Min(args[0], args[1]), "op.max" => Math.Max(args[0], args[1]), "op.abs" => Math.Abs(args[0]),
                "op.clamp" => Math.Clamp(args[0], n.Params.GetValueOrDefault("min"), n.Params.GetValueOrDefault("max", 100)),
                "op.gt" => args[0] > args[1] ? 1 : 0, "op.lt" => args[0] < args[1] ? 1 : 0, "op.eq" => args[0] == args[1] ? 1 : 0,
                "op.and" => args[0] != 0 && args[1] != 0 ? 1 : 0, "op.or" => args[0] != 0 || args[1] != 0 ? 1 : 0, "op.not" => args[0] == 0 ? 1 : 0,
                "op.select" => args[0] != 0 ? args[1] : args[2],
                "op.memory" => args[0] != 0 ? args[1] : n.Values[0],
                var s when s.StartsWith("out.") => args[0],
                _ => 0,
            };
            n.Values[0] = v;
            if (n.Type.StartsWith("out.")) write(n.Type, n, v);
        }
    }

    static Structure? Target(McuNode n, Factory f, Building home, out Building? b)
    {
        b = n.TargetBuilding < 0 ? home : f.Get(n.TargetBuilding);
        return b?.Find(n.TargetFloor < 0 ? 0 : n.TargetFloor, n.TargetId);
    }

    static double ReadFactory(string type, McuNode n, Factory f, GameData data, Building home)
    {
        if (type == "in.tick") return f.TickCount;
        if (type == "in.building.supply" || type == "in.building.demand") { var b = n.TargetBuilding < 0 ? home : f.Get(n.TargetBuilding); return b == null ? 0 : type.EndsWith("supply") ? b.LastSupply : b.LastDemand; }
        var s = Target(n, f, home, out var tb);
        if (s == null || tb == null || tb.LastSupply <= 0) return 0;   // 電気の無い建物の設備は読めない
        var def = data.Structures[s.DefId];
        return (type, s) switch
        {
            ("in.reactor.output", Reactor r) => Building.ReactorOutput(def, r.Quality, data) * r.OutputPct / 100,
            ("in.reactor.load", Reactor r) => r.Load * 100.0 / Math.Max(1, data.OverloadLimit),
            ("in.reactor.setting", Reactor r) => r.OutputPct,
            ("in.battery.stored", Battery bt) => bt.Stored * 100.0 / Math.Max(1, def.Capacity),
            ("in.machine.active", Machine m) => m.Progress >= 0 && m.PowerPct > 0 ? 1 : 0,
            ("in.machine.inputs", Machine m) => m.Inputs.Sum(i => i.Count),
            ("in.machine.outputs", Machine m) => m.Outputs.Sum(i => i.Count),
            ("in.chest.count", Chest c) => c.Items.Count,
            ("in.tank.amount", Tank t) => t.Amount,
            ("in.port.waiting", Port p) => p.Buffer.Count + p.Inbox.Count,
            _ => 0,
        };
    }

    static void WriteFactory(string type, McuNode n, double v, Factory f, GameData data, Building home)
    {
        if (type == "out.building.policy") { var b = n.TargetBuilding < 0 ? home : f.Get(n.TargetBuilding); if (b != null) b.Policy = (PowerPolicy)Math.Clamp((int)v, 0, 2); return; }
        var s = Target(n, f, home, out var tb);
        if (s == null || tb == null || tb.LastSupply <= 0) return;
        switch (type, s)
        {
            case ("out.reactor.setting", Reactor r): r.OutputPct = Math.Clamp((int)v, 0, data.OverloadMax); break;
            case ("out.battery.mode", Battery bt): bt.Mode = (BatteryMode)Math.Clamp((int)v, 0, 2); break;
            case ("out.structure.enabled", _): s.Enabled = v != 0; break;
            case ("out.trade.enabled", Trade td): td.ImportEnabled = v != 0; break;
        }
    }
}

/// <summary>制御室のプログラムを置き換える(命令)。</summary>
public sealed record SetProgram(int Building, int Floor, int Id, Program Program) : ICommand
{
    public void Apply(Simulation s)
    {
        foreach (var n in Program.Nodes) McuCatalog.Of(n.Type);   // 未知の種類は例外(データの誤り)。対象の有無より先に検査する
        if (s.Factory.Get(Building)?.Find(Floor, Id) is not Mcu m) return;
        m.Program = Program; m.Timer = 0;
    }
}

/// <summary>機体の制御基板のプログラム(索敵・戦闘)。評価は戦闘の実装時。</summary>
public sealed record SetMechProgram(int MechId, Program Program) : ICommand
{
    public void Apply(Simulation s)
    {
        var m = s.Factory.Mechs.FirstOrDefault(x => x.Id == MechId); if (m == null) return;
        foreach (var n in Program.Nodes) McuCatalog.Of(n.Type);
        m.Program = Program;
    }
}
