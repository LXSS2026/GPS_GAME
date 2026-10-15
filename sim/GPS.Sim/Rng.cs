namespace GPS.Sim;

/// <summary>決定論的な乱数(xorshift64*)。描画側や実時間は使わない。状態は保存できる。</summary>
public sealed class Rng
{
    public ulong State { get; set; }
    public Rng(ulong seed) => State = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;

    public ulong NextU64()
    {
        ulong x = State;
        x ^= x >> 12; x ^= x << 25; x ^= x >> 27;
        State = x;
        return x * 0x2545F4914F6CDD1DUL;
    }

    /// <summary>0 以上 n 未満。</summary>
    public int Next(int n) => n <= 1 ? 0 : (int)(NextU64() % (ulong)n);
}

/// <summary>ゲーム内時間(2026-10-15 確定: 60 ティック/秒、現実 5 分 = ゲーム内 1 時間)。</summary>
public static class Clock
{
    public const int TicksPerSecond = 60;
    public const long TicksPerHour = 18_000;
    public const long TicksPerDay = TicksPerHour * 24;
    public const int DaysPerYear = 360;
    public const long TicksPerYear = TicksPerDay * DaysPerYear;

    public static (int year, int day, int hour, int minute) Of(long tick)
    {
        long y = tick / TicksPerYear, r = tick % TicksPerYear;
        long d = r / TicksPerDay; r %= TicksPerDay;
        long h = r / TicksPerHour; r %= TicksPerHour;
        return ((int)y + 1, (int)d + 1, (int)h, (int)(r * 60 / TicksPerHour));
    }
}
