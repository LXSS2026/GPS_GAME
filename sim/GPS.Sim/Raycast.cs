using System.Numerics;

namespace GPS.Sim;

public static class Raycast
{
    /// <summary>
    /// ボクセル格子を DDA で辿り最初の非空気ブロックを返す。
    /// <paramref name="before"/> は手前の空セル(設置先)。開始セルが固体なら hit と同じ。
    /// </summary>
    public static bool Cast(VoxelWorld w, Vector3 origin, Vector3 dir, float maxDist, out BlockPos hit, out BlockPos before)
    {
        int x = (int)MathF.Floor(origin.X), y = (int)MathF.Floor(origin.Y), z = (int)MathF.Floor(origin.Z);
        int sx = Math.Sign(dir.X), sy = Math.Sign(dir.Y), sz = Math.Sign(dir.Z);
        float tdx = dir.X != 0 ? MathF.Abs(1 / dir.X) : float.PositiveInfinity;
        float tdy = dir.Y != 0 ? MathF.Abs(1 / dir.Y) : float.PositiveInfinity;
        float tdz = dir.Z != 0 ? MathF.Abs(1 / dir.Z) : float.PositiveInfinity;
        float tx = dir.X > 0 ? (x + 1 - origin.X) * tdx : dir.X < 0 ? (origin.X - x) * tdx : float.PositiveInfinity;
        float ty = dir.Y > 0 ? (y + 1 - origin.Y) * tdy : dir.Y < 0 ? (origin.Y - y) * tdy : float.PositiveInfinity;
        float tz = dir.Z > 0 ? (z + 1 - origin.Z) * tdz : dir.Z < 0 ? (origin.Z - z) * tdz : float.PositiveInfinity;

        before = new(x, y, z);
        float t = 0;
        while (t <= maxDist)
        {
            hit = new(x, y, z);
            if (w.IsSolid(hit)) return true;
            before = hit;
            if (tx < ty && tx < tz) { x += sx; t = tx; tx += tdx; }
            else if (ty < tz) { y += sy; t = ty; ty += tdy; }
            else { z += sz; t = tz; tz += tdz; }
        }
        hit = default;
        return false;
    }
}
