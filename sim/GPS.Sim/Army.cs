namespace GPS.Sim;

/// <summary>軍(G21 #1): 隊の束。司令官(人物)が要る。命令は配下の隊へ配る。</summary>
public sealed class Army
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int CommanderId { get; set; } = -1;
    public List<int> SquadIds { get; set; } = new();
    public Formation Formation { get; set; }
}

public static class Armies
{
    public static Person? Commander(Factory f, int personId) => personId < 0 ? null : f.City?.Persons.FirstOrDefault(p => p.Id == personId);
    public static Army? ArmyOf(Factory f, Squad s) => f.Armies.FirstOrDefault(a => a.SquadIds.Contains(s.Id));
    /// <summary>指揮官の効果: 隊長の指揮 ±2% / 点、司令官 ±1% / 点(G21 #1)。都市が無ければ 1。</summary>
    public static double CommandMul(Factory f, Squad s) => CommandMul(f, s, f.City == null || s.Faction != 1 ? null : ArmyOf(f, s));
    public static double CommandMul(Factory f, Squad s, Army? a)
    {
        if (f.City == null || s.Faction != 1) return 1;
        double m = 1;
        var c = Commander(f, s.CommanderId); if (c != null) m *= 1 + (c.Command - 5) * 0.02;
        var ac = a == null ? null : Commander(f, a.CommanderId); if (ac != null) m *= 1 + (ac.Command - 5) * 0.01;
        return Math.Clamp(m, 0.5, 1.5);
    }
    public static bool HasCommander(Factory f, Squad s) => f.City == null || s.Faction != 1 || Commander(f, s.CommanderId) != null;
    /// <summary>軍の中の隊の位置(軍の陣形)。</summary>
    public static (double x, double y) Slot(Army a, int idx, double cx, double cy)
    {
        const double sp = 30; int n = a.SquadIds.Count;
        return a.Formation switch
        {
            Formation.Line => (cx + (idx - n / 2.0) * sp, cy),
            Formation.Column => (cx, cy - idx * sp),
            _ => (cx + (idx % 3 - 1) * sp * 1.5, cy - (idx / 3) * sp * 1.5),
        };
    }
}

// ---------- 命令 ----------
/// <summary>指揮官の任命: 隊(kind = "squad")か軍(kind = "army")に人物を置く。人物は役職も他の指揮も持っていないこと。</summary>
public sealed record AssignCommander(string Kind, int TargetId, int PersonId) : ICommand
{
    public void Apply(Simulation s)
    {
        var f = s.Factory; var city = f.City; if (city == null) return;
        var p = city.Persons.FirstOrDefault(x => x.Id == PersonId); if (p == null || p.Office != null) return;
        if (Kind == "squad") { var sq = f.Squads.FirstOrDefault(x => x.Id == TargetId && x.Faction == 1); if (sq == null) return; if (sq.CommanderId >= 0) DismissCommander.Do(f, "squad", TargetId); sq.CommanderId = PersonId; p.Office = $"squad:{TargetId}"; }
        else { var a = f.Armies.FirstOrDefault(x => x.Id == TargetId); if (a == null) return; if (a.CommanderId >= 0) DismissCommander.Do(f, "army", TargetId); a.CommanderId = PersonId; p.Office = $"army:{TargetId}"; }
        f.Events.Add(new FactoryEvent { Kind = "commander", Building = TargetId, Power = PersonId, Destroyed = Kind == "army" ? 1 : 0 });
    }
}

public sealed record DismissCommander(string Kind, int TargetId) : ICommand
{
    public void Apply(Simulation s) => Do(s.Factory, Kind, TargetId);
    internal static void Do(Factory f, string kind, int id)
    {
        int pid = -1;
        if (kind == "squad") { var sq = f.Squads.FirstOrDefault(x => x.Id == id); if (sq == null) return; pid = sq.CommanderId; sq.CommanderId = -1; }
        else { var a = f.Armies.FirstOrDefault(x => x.Id == id); if (a == null) return; pid = a.CommanderId; a.CommanderId = -1; }
        var p = Armies.Commander(f, pid); if (p != null) p.Office = null;
    }
}

/// <summary>軍を編成する(自軍の隊だけ。他の軍に属する隊は外してから)。</summary>
public sealed record FormArmy(int Id, string Name, IReadOnlyList<int> SquadIds) : ICommand
{
    public void Apply(Simulation s)
    {
        var f = s.Factory; if (f.Armies.Any(a => a.Id == Id) || SquadIds.Count == 0) return;
        var first = f.Squads.FirstOrDefault(q => q.Id == SquadIds[0]); if (first == null) return;
        var ids = SquadIds.Where(id => f.Squads.Any(q => q.Id == id && q.Faction == first.Faction)).Distinct().ToList(); if (ids.Count == 0) return;   // 先頭の隊と同じ陣営だけ
        foreach (var a in f.Armies) a.SquadIds.RemoveAll(ids.Contains);
        f.Armies.RemoveAll(a => a.SquadIds.Count == 0);
        f.Armies.Add(new Army { Id = Id, Name = Name, SquadIds = ids }); f.NextArmyId = Math.Max(f.NextArmyId, Id + 1);
    }
}

public sealed record DisbandArmy(int Id) : ICommand
{
    public void Apply(Simulation s) { var f = s.Factory; var a = f.Armies.FirstOrDefault(x => x.Id == Id); if (a == null) return; DismissCommander.Do(f, "army", Id); f.Armies.Remove(a); }
}

/// <summary>軍への命令(司令官が要る)。軍の中心を基準に隊ごとの陣形の位置へ配る。攻撃の目標は全隊で同じ。</summary>
public sealed record OrderArmy(int Id, SquadOrder Order, double X = 0, double Y = 0, int TargetSquad = -1, int TargetBuilding = -1, Formation? Formation = null) : ICommand
{
    public void Apply(Simulation s)
    {
        var f = s.Factory; var a = f.Armies.FirstOrDefault(x => x.Id == Id); if (a == null) return;
        if (Armies.Commander(f, a.CommanderId) == null && f.City != null) { f.Events.Add(new FactoryEvent { Kind = "no_commander", Building = Id, Destroyed = 1 }); return; }
        if (Formation != null) a.Formation = Formation.Value;
        for (int i = 0; i < a.SquadIds.Count; i++)
        {
            var (sx, sy) = Order == SquadOrder.Move ? Armies.Slot(a, i, X, Y) : (X, Y);
            new OrderSquad(a.SquadIds[i], Order, sx, sy, TargetSquad, null, TargetBuilding).Apply(s);
        }
    }
}

/// <summary>軍を出撃させる(司令官が要る。隊ごとに隊長と要員の条件は Deploy と同じ)。</summary>
public sealed record DeployArmy(int Id, double X, double Y) : ICommand
{
    public void Apply(Simulation s)
    {
        var f = s.Factory; var a = f.Armies.FirstOrDefault(x => x.Id == Id); if (a == null) return;
        if (Armies.Commander(f, a.CommanderId) == null && f.City != null) { f.Events.Add(new FactoryEvent { Kind = "no_commander", Building = Id, Destroyed = 1 }); return; }
        for (int i = 0; i < a.SquadIds.Count; i++) { var (sx, sy) = Armies.Slot(a, i, X, Y); new Deploy(a.SquadIds[i], sx, sy).Apply(s); }
    }
}

/// <summary>注視範囲(G21 #3): この中の隊は個機で、外は隊どうしの式で解く。半径 0 で全て個機。</summary>
public sealed record SetBattleFocus(double X, double Y, double Radius) : ICommand
{
    public void Apply(Simulation s) { s.Factory.FocusX = X; s.Factory.FocusY = Y; s.Factory.FocusR = Radius; }
}
