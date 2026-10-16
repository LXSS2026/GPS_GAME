namespace GPS.Sim;

public sealed class PathPoint
{
    public double X { get; set; }
    public double Y { get; set; }
}

/// <summary>経路探索(G21 第 7 節)。4 m の障害物格子(立っている建物の床)で、隊(単位)の目標までの直線が通るなら直進、
/// 塞がれていれば A*(8 方向・角を切らない)で経路を求め、先の中継点へ直線が通るたびに手前を飛ばす。経路は隊の記録に残す(写しで同じ進行)。
/// 機体ごとの目標機は射線の通る相手なので直進で足りる(索敵が射線を要求する)。</summary>
/// <summary>隊の経路の記録(写しに残す)。</summary>
public sealed class PathCache
{
    public List<PathPoint>? Path { get; set; }
    public double WpX { get; set; }
    public double WpY { get; set; }
    public double WpGoalX { get; set; }
    public double WpGoalY { get; set; }
    public bool WpValid { get; set; }
    public long WpTick { get; set; } = -1;   // 評価したティック(同じ隊の機体が何度も呼んでも 1 回)
}

/// <summary>A* の作業領域(工場ごとに 1 つ、格子の大きさが変わったら作り直す)。</summary>
public sealed class PathScratch
{
    public readonly int Size; public int Gen; public readonly double[] G; public readonly int[] From, Seen, Closed; public readonly PriorityQueue<int, double> Open = new();
    public PathScratch(int size) { Size = size; G = new double[size]; From = new int[size]; Seen = new int[size]; Closed = new int[size]; }
}

public static class Pathing
{
    public const int Cell = 4;
    const int ReplanEvery = 600;   // 中継点をたどっている間の引き直し(10 秒)
    const int CheckEvery = 3;      // 直線の確認は 3 ティックに 1 回(10 万規模向け)
    const double LookAhead = 256;  // 直線の確認は手前 256 m まで(遠い目標は近づいてから)
    const double WallPenalty = 0.75;   // 壁ぎわのセルの加点(1 セル分の遠回りより小さく、2 セル分より大きい)

    /// <summary>障害物格子を建物の変化に合わせて作り直す(遮蔽の格子と同じ合図)。</summary>
    public static void Refresh(Factory f, int mapSize)
    {
        int key = f.Buildings.Count * 1000003 + f.Buildings.Sum(b => b.Id * (b.Destroyed ? 7 : 1) + b.Floors.Count * 31);
        if (f.PathGrid != null && key == f.PathKey) return;
        f.PathKey = key;
        int n = Math.Max(mapSize, 1); foreach (var b in f.Buildings) if (!b.IsShip && b.Floors.Count > 0) n = Math.Max(n, (int)Math.Max(b.X + b.Floors[0].W, b.Y + b.Floors[0].D));
        int cells = n / Cell + 2; var grid = new int[cells, cells];
        foreach (var b in f.Buildings)
        {
            if (b.IsShip || b.Destroyed || b.Floors.Count == 0) continue;
            // セルの中心が床の中にあるセルを塞ぐ(4 m の街路はセルの中心が通る)
            for (int x = (int)Math.Floor(b.X / Cell); x <= (int)Math.Floor((b.X + b.Floors[0].W) / Cell); x++) for (int y = (int)Math.Floor(b.Y / Cell); y <= (int)Math.Floor((b.Y + b.Floors[0].D) / Cell); y++)
            {
                if (x < 0 || y < 0 || x >= cells || y >= cells) continue;
                double mx = x * Cell + Cell / 2.0, my = y * Cell + Cell / 2.0;
                if (mx >= b.X && mx < b.X + b.Floors[0].W && my >= b.Y && my < b.Y + b.Floors[0].D) grid[x, y] = b.Id;
            }
        }
        f.PathGrid = grid;
        // 壁ぎわ(塞がれたセルに隣接)の印: 経路はこれを避けて街路の中央寄りを通る(見た目で壁に接しない)
        var near = new bool[cells, cells];
        for (int x = 0; x < cells; x++) for (int y = 0; y < cells; y++) { if (grid[x, y] != 0) continue; for (int dx = -1; dx <= 1 && !near[x, y]; dx++) for (int dy = -1; dy <= 1; dy++) if (x + dx >= 0 && y + dy >= 0 && x + dx < cells && y + dy < cells && grid[x + dx, y + dy] != 0) { near[x, y] = true; break; } }
        f.PathNearWall = near;
    }

    /// <summary>塞がれているか。allow は通ってよい建物(目的の格納庫など)の番号。</summary>
    public static bool Blocked(Factory f, double x, double y, int allow = 0)
    {
        var g = f.PathGrid; if (g == null) return false;
        return BlockedCell(g, (int)Math.Floor(x / Cell), (int)Math.Floor(y / Cell), allow);
    }
    static bool SameCell(double x0, double y0, double x1, double y1) => (int)Math.Floor(x0 / Cell) == (int)Math.Floor(x1 / Cell) && (int)Math.Floor(y0 / Cell) == (int)Math.Floor(y1 / Cell);
    static bool BlockedCell(int[,] g, int cx, int cy, int allow) { if (cx < 0 || cy < 0 || cx >= g.GetLength(0) || cy >= g.GetLength(1)) return false; int v = g[cx, cy]; return v != 0 && v != allow; }

    /// <summary>直線が塞がれていないか(始点と終点のセルは除く: 置かれた位置が床の中でも出られ、格納庫の角など床の中の目標へも寄れる)。2 m 刻みで標本を取る。</summary>
    public static bool LineClear(Factory f, double x0, double y0, double x1, double y1, int allow = 0, double maxLen = 0)
    {
        var g = f.PathGrid; if (g == null) return true;
        double len = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        if (maxLen > 0 && len > maxLen) { double k = maxLen / len; x1 = x0 + (x1 - x0) * k; y1 = y0 + (y1 - y0) * k; len = maxLen; }   // 手前だけ見る(近づけば見直す)
        int n = Math.Max(1, (int)(len / Cell));
        int sx = (int)Math.Floor(x0 / Cell), sy = (int)Math.Floor(y0 / Cell), ex = (int)Math.Floor(x1 / Cell), ey = (int)Math.Floor(y1 / Cell);
        for (int i = 1; i < n; i++)
        {
            double t = (double)i / n; int cx = (int)Math.Floor((x0 + (x1 - x0) * t) / Cell), cy = (int)Math.Floor((y0 + (y1 - y0) * t) / Cell);
            if ((cx != sx || cy != sy) && (cx != ex || cy != ey) && BlockedCell(g, cx, cy, allow)) return false;
        }
        return true;
    }

    /// <summary>A*。見つからなければ null。目標のセルが塞がれていれば周り(3 セルまで)の空いたセルへ。</summary>
    public static List<PathPoint>? Find(Factory f, double x0, double y0, double x1, double y1, int allow = 0, int maxNodes = 60000)
    {
        var g = f.PathGrid; if (g == null) return null;
        int w = g.GetLength(0), h = g.GetLength(1);
        int sx = (int)Math.Floor(x0 / Cell), sy = (int)Math.Floor(y0 / Cell);
        if (sx < 0 || sy < 0 || sx >= w || sy >= h) return null;   // 格子の外(建物の無い所)からは直進(中に入ってから探す)
        int gx = Math.Clamp((int)Math.Floor(x1 / Cell), 0, w - 1), gy = Math.Clamp((int)Math.Floor(y1 / Cell), 0, h - 1);
        if (BlockedCell(g, gx, gy, allow))
        {
            bool found = false;
            for (int r = 1; r <= 3 && !found; r++) for (int dx = -r; dx <= r && !found; dx++) for (int dy = -r; dy <= r && !found; dy++)
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == r && gx + dx >= 0 && gy + dy >= 0 && gx + dx < w && gy + dy < h && !BlockedCell(g, gx + dx, gy + dy, allow)) { gx += dx; gy += dy; found = true; }
            if (!found) return null;
        }
        if (sx == gx && sy == gy) return new List<PathPoint> { new() { X = x1, Y = y1 } };
        var sc = f.PathScratch; if (sc == null || sc.Size != w * h) f.PathScratch = sc = new PathScratch(w * h);
        sc.Gen++; int gen = sc.Gen; var gScore = sc.G; var from = sc.From; var seen = sc.Seen; var closedGen = sc.Closed;   // 世代印で「未訪問」を表す(配列を埋め直さない)
        var open = sc.Open; open.Clear();
        int start = sy * w + sx, goal = gy * w + gx; gScore[start] = 0; seen[start] = gen; from[start] = -1; open.Enqueue(start, Heur(sx, sy, gx, gy));
        int expanded = 0;
        while (open.Count > 0)
        {
            int cur = open.Dequeue(); if (closedGen[cur] == gen) continue; closedGen[cur] = gen;
            if (cur == goal) break;
            if (++expanded > maxNodes) return null;
            int cx = cur % w, cy = cur / w;
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue; int nx = cx + dx, ny = cy + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h || BlockedCell(g, nx, ny, allow)) continue;
                if (dx != 0 && dy != 0 && (BlockedCell(g, cx + dx, cy, allow) || BlockedCell(g, cx, cy + dy, allow))) continue;   // 角を切らない
                int ni = ny * w + nx; if (closedGen[ni] == gen) continue;
                double ng = gScore[cur] + (dx != 0 && dy != 0 ? 1.41421356 : 1) + (f.PathNearWall != null && f.PathNearWall[nx, ny] ? WallPenalty : 0);
                if (seen[ni] != gen || ng < gScore[ni]) { seen[ni] = gen; gScore[ni] = ng; from[ni] = cur; open.Enqueue(ni, ng + Heur(nx, ny, gx, gy)); }
            }
        }
        if (closedGen[goal] != gen) return null;
        var cellsPath = new List<int>(); for (int i = goal; i >= 0; i = from[i]) { cellsPath.Add(i); if (i == start) break; }
        cellsPath.Reverse();
        // 中継点はセルの中心、最後は目標そのもの。先へ直線が通るものは飛ばして減らす
        var pts = new List<PathPoint>();
        foreach (var i in cellsPath) pts.Add(new PathPoint { X = (i % w) * Cell + Cell / 2.0, Y = (i / w) * Cell + Cell / 2.0 });
        pts[^1] = new PathPoint { X = x1, Y = y1 };
        var outp = new List<PathPoint>(); int k = 0;
        while (k < pts.Count - 1)
        {
            int j = pts.Count - 1; while (j > k + 1 && !LineClear(f, pts[k].X, pts[k].Y, pts[j].X, pts[j].Y, allow)) j--;
            outp.Add(pts[j]); k = j;
        }
        if (outp.Count == 0) outp.Add(pts[^1]);
        return outp;
    }
    /// <summary>機体の 1 歩(個機): 次の位置が建物の床なら向きを ±45°, ±90°, ±135° とずらして空いている方へ。全て塞がれていれば動かない。</summary>
    public static (double x, double y) Step(Factory f, double x, double y, double dx, double dy, double gx, double gy, int allow = 0)
    {
        if (!Blocked(f, x + dx, y + dy, allow) || Blocked(f, x, y, allow) || SameCell(x + dx, y + dy, gx, gy)) return (x + dx, y + dy);   // 今いる所が床の中なら真っ直ぐ出る。目標のセル(格納庫の角など)へは入る
        foreach (var deg in new[] { 45, -45, 90, -90, 135, -135 })
        {
            double a = deg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a), rx = dx * c - dy * s, ry = dx * s + dy * c;
            if (!Blocked(f, x + rx, y + ry, allow)) return (x + rx, y + ry);
        }
        return (x, y);
    }
    /// <summary>陣形の置き場所(集約): 床の中なら中心へ向かって 4 m ずつ寄せる。</summary>
    public static (double x, double y) Nudge(Factory f, double px, double py, double ax, double ay, int allow = 0)
    {
        if (!Blocked(f, px, py, allow)) return (px, py);
        double dx = ax - px, dy = ay - py, d = Math.Sqrt(dx * dx + dy * dy); if (d < 1e-6) return (px, py);
        for (double t = Cell; t < d; t += Cell) { double x = px + dx * t / d, y = py + dy * t / d; if (!Blocked(f, x, y, allow)) return (x, y); }
        return (ax, ay);
    }
    static double Heur(int x, int y, int gx, int gy) { int dx = Math.Abs(x - gx), dy = Math.Abs(y - gy); return dx + dy + (1.41421356 - 2) * Math.Min(dx, dy); }

    /// <summary>隊(単位)の次に向かう点。直線が通れば目標そのもの。確認は 3 ティックに 1 回で、間は前の答えを使う。</summary>
    public static (double x, double y) Waypoint(Factory f, PathCache s, int id, double cx, double cy, double gx, double gy, long tick, int allow = 0)
    {
        bool goalMoved = Math.Abs(s.WpGoalX - gx) > Cell || Math.Abs(s.WpGoalY - gy) > Cell;
        if (s.WpValid && !goalMoved && (s.WpTick == tick || (tick + id) % CheckEvery != 0)) return (s.WpX, s.WpY);
        s.WpGoalX = gx; s.WpGoalY = gy; s.WpValid = true; s.WpTick = tick;
        if (LineClear(f, cx, cy, gx, gy, allow, LookAhead)) { s.Path = null; s.WpX = gx; s.WpY = gy; return (gx, gy); }
        if (s.Path == null || s.Path.Count == 0 || goalMoved || (tick + id) % ReplanEvery == 0) s.Path = Find(f, cx, cy, gx, gy, allow);
        if (s.Path == null || s.Path.Count == 0) { s.WpX = gx; s.WpY = gy; return (gx, gy); }   // 道が無ければ直進(届かないまま)
        while (s.Path.Count > 1 && Math.Abs(s.Path[0].X - cx) + Math.Abs(s.Path[0].Y - cy) < Cell) s.Path.RemoveAt(0);   // 通過した中継点
        while (s.Path.Count > 1 && LineClear(f, cx, cy, s.Path[1].X, s.Path[1].Y, allow)) s.Path.RemoveAt(0);   // 先へ直線が通るなら飛ばす
        s.WpX = s.Path[0].X; s.WpY = s.Path[0].Y; return (s.WpX, s.WpY);
    }
}
