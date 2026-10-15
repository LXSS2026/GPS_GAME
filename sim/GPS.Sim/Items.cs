namespace GPS.Sim;

/// <summary>品質の階級(2026-10-15 確定): 製造不良 0〜39 / 通常 40〜89 / 高品質 90〜98 / 最高品質 99〜100。</summary>
public enum QualityTier { Defect = 0, Normal = 1, High = 2, Top = 3 }

/// <summary>Price は輸出入の基準価格(通常品質)。階級で倍率がかかる。</summary>
public sealed record ItemDef(string Id, string Name, bool Fluid, bool Recyclable, int TechLevel, int Price = 0);

/// <summary>アイテム 1 個。品質値(0〜100)を個体ごとに持つ。流体は量(単位)を Count に持つ。</summary>
public readonly record struct ItemStack(string Item, int Quality, int Count = 1)
{
    public QualityTier Tier => GPS.Sim.Quality.TierOf(Quality);
}

public static class Quality
{
    public const int NormalMin = 40, HighMin = 90, TopMin = 99;
    /// <summary>既定の率(1/10,000): 最高 0.1%、高 1%、不良 8.9%、残り通常 90%。</summary>
    public const int BaseTop = 10, BaseHigh = 100, BaseDefect = 890;

    public static QualityTier TierOf(int q) => q >= TopMin ? QualityTier.Top : q >= HighMin ? QualityTier.High : q >= NormalMin ? QualityTier.Normal : QualityTier.Defect;

    /// <summary>階級を率で引き、階級内の品質値を一様に決める。倍率はパーセント(100 = 既定)。normalBias は通常に寄せる度合い(%): 最高・高・不良の重みをその分だけ減らす(ロボット製造用)。</summary>
    public static int Roll(Rng rng, int topMul = 100, int highMul = 100, int defectMul = 100, int normalBias = 0)
    {
        int keep = Math.Clamp(100 - normalBias, 0, 100);
        int wTop = BaseTop * topMul / 100 * keep / 100;
        int wHigh = BaseHigh * highMul / 100 * keep / 100;
        int wDefect = BaseDefect * defectMul / 100 * keep / 100;
        int wNormal = Math.Max(0, 10_000 - wTop - wHigh - wDefect);
        int r = rng.Next(wTop + wHigh + wDefect + wNormal);
        if (r < wTop) return TopMin + rng.Next(100 - TopMin + 1);
        r -= wTop;
        if (r < wHigh) return HighMin + rng.Next(TopMin - HighMin);
        r -= wHigh;
        if (r < wDefect) return rng.Next(NormalMin);
        return NormalMin + rng.Next(HighMin - NormalMin);
    }
}
