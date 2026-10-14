using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// ロフト生成: 任意の平面形(2D 多角形)を階ごとに拡縮・回転・移動しながら押し出す。
/// ねじれ塔・先細りの三角塔・レンズ形とダイアグリッド・星形の段状塔・傾斜と片持ち・くびれた曲面殻の 6 様式(10〜15)。
/// </summary>
public sealed partial class KitBash
{
    public const int LoftStyles = 6;

    /// <summary>斜めの部材: a から b へ向く細い角材(一括描画)。</summary>
    public void Bar(Node3D p, Vector3 a, Vector3 b, float t, Material m)
    {
        var d = b - a; float len = d.Length();
        if (len < 1e-4f) return;
        var dir = d / len;
        var up = Mathf.Abs(dir.Dot(Vector3.Up)) > 0.95f ? Vector3.Right : Vector3.Up;
        var basis = Basis.LookingAt(dir, up);   // -Z が dir を向く
        basis = new Basis(basis.X * t, basis.Y * t, basis.Z * len);
        var global = p.GlobalTransform * new Transform3D(basis, (a + b) / 2);
        if (!_batch.TryGetValue(m, out var list)) _batch[m] = list = new List<Transform3D>();
        list.Add(global);
    }

    readonly Dictionary<Material, StandardMaterial3D> _uvVariant = new();

    /// <summary>三平面投影ではなく UV で貼る版(曲面用)。</summary>
    StandardMaterial3D Uv(Material src)
    {
        if (_uvVariant.TryGetValue(src, out var m)) return m;
        m = (StandardMaterial3D)((StandardMaterial3D)src).Duplicate();
        m.Uv1Triplanar = false; m.Uv1WorldTriplanar = false; m.Uv1Scale = Vector3.One;
        _uvVariant[src] = m;
        return m;
    }

    public delegate (float scale, float rotDeg, Vector2 offset) RingXf(int ring, float y, float t);

    /// <summary>
    /// ロフト塔の共通部: 基壇、側面(窓 UV)、階の帯、冠と尖塔。xf は階ごとの変形。diagrid で斜め格子。
    /// 原点は基壇の中心。
    /// </summary>
    Node3D LoftTower(Node3D parent, float x, float y, float z, float rot, Vector2[] profile, int floors, RingXf xf, Material win, bool diagrid, int bandEvery, float plinth)
    {
        var n = Root(parent, x, y, z, rot);
        Box(n, new Vector3(0, 0.1f, 0), new Vector3(plinth, 0.2f, plinth), Floor);
        float y0 = 0.2f;
        var ringY = new float[floors + 1];
        for (int i = 0; i <= floors; i++) ringY[i] = y0 + i * Fh;
        var xfs = new (float, float, Vector2)[floors + 1];
        for (int i = 0; i <= floors; i++) xfs[i] = xf(i, ringY[i], (float)i / floors);
        var mesh = MeshParts.Loft(profile, ringY, xfs, true, false, 1f / (Fh * 8));
        n.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = Uv(win) });
        // 階の帯(少し外側のロフトを薄く)
        for (int i = 0; i <= floors; i += bandEvery)
        {
            var band = MeshParts.Loft(profile, new[] { ringY[i] - 0.04f, ringY[i] + 0.04f }, new[] { Scaled(xfs[i], 1.03f), Scaled(xfs[i], 1.03f) }, true, true, 1f);
            n.AddChild(new MeshInstance3D { Mesh = band, MaterialOverride = Steel });
        }
        // 斜め格子
        if (diagrid)
        {
            int m = profile.Length;
            for (int i = 0; i < floors; i += 2)
                for (int j = 0; j < m; j++)
                {
                    var a = MeshParts.LoftPoint(profile, j, ringY[i], xfs[i], 1.015f);
                    var b1 = MeshParts.LoftPoint(profile, (j + 1) % m, ringY[Math.Min(i + 2, floors)], xfs[Math.Min(i + 2, floors)], 1.015f);
                    var b2 = MeshParts.LoftPoint(profile, (j - 1 + m) % m, ringY[Math.Min(i + 2, floors)], xfs[Math.Min(i + 2, floors)], 1.015f);
                    Bar(n, a, b1, 0.07f, Steel);
                    Bar(n, a, b2, 0.07f, Steel);
                }
        }
        // 冠: 上面の縁の発光、機器、尖塔
        var top = xfs[floors];
        float topY = ringY[floors];
        var crown = MeshParts.Loft(profile, new[] { topY, topY + 0.5f }, new[] { Scaled(top, 0.75f), Scaled(top, 0.6f) }, true, false, 1f);
        n.AddChild(new MeshInstance3D { Mesh = crown, MaterialOverride = PaintDark });
        var ring = MeshParts.Loft(profile, new[] { topY + 0.02f, topY + 0.08f }, new[] { Scaled(top, 1.02f), Scaled(top, 1.02f) }, true, true, 1f);
        n.AddChild(new MeshInstance3D { Mesh = ring, MaterialOverride = GlowCyan });
        var c = top.Item3;
        Mast(n, topY + 0.5f, c.X, c.Y, 2.6f, GlowRed);
        return n;
    }

    static (float, float, Vector2) Scaled((float, float, Vector2) t, float k) => (t.Item1 * k, t.Item2, t.Item3);

    public Node3D LoftBuilding(Node3D parent, int style, float x, float y, float z, float w, int floors, float rot = 0)
    {
        float r = w * 0.55f;
        switch (style)
        {
            case 0: // ねじれ塔: 角丸の四角形が 1 階あたり 2 度回り、上で少し細くなる
                return LoftTower(parent, x, y, z, rot, MeshParts.RoundedRect(w * 0.95f, w * 0.95f, w * 0.2f, 5), floors,
                    (i, yy, t) => (1f - 0.18f * t, i * 2.0f, Vector2.Zero), WinCool, false, 4, w + 1.2f);
            case 1: // 先細りの三角塔: 丸めた三角形が上で 0.35 倍まで細くなり、側面に支柱
                return LoftTower(parent, x, y, z, rot, MeshParts.RoundedPolygon(3, r * 1.15f, 0.25f, 6), floors,
                    (i, yy, t) => (1f - 0.65f * t, 0, Vector2.Zero), WinWarm, true, 6, w + 1.4f);
            case 2: // レンズ形とダイアグリッド: 中央で膨らみ、先端で絞る
                return LoftTower(parent, x, y, z, rot, MeshParts.Lens(w * 1.1f, w * 0.7f, 10), floors,
                    (i, yy, t) => (0.75f + 0.45f * Mathf.Sin(t * Mathf.Pi) - 0.35f * Mathf.Max(0, t - 0.8f) * 5f, 0, Vector2.Zero), WinCool, true, 5, w + 1.2f);
            case 3: // 星形の段状塔: 8 つ星の平面が 3 段で縮む
                return LoftTower(parent, x, y, z, rot, MeshParts.Star(8, r * 1.1f, r * 0.72f), floors,
                    (i, yy, t) => (t < 0.45f ? 1f : t < 0.75f ? 0.78f : 0.56f, 0, Vector2.Zero), WinMixed, false, 3, w + 1.4f);
            case 4: // 傾斜と片持ち: 六角形の塔が傾き、上部に片持ちの箱と支柱
            {
                var n = LoftTower(parent, x, y, z, rot, MeshParts.RoundedPolygon(6, r * 0.95f, 0.2f, 4), floors,
                    (i, yy, t) => (1f - 0.1f * t, 0, new Vector2(t * w * 0.9f, 0)), WinWarm, false, 4, w + 1.4f);
                float topY = 0.2f + floors * Fh;
                Box(n, new Vector3(w * 0.9f + w * 0.9f, topY - Fh * 3.5f, 0), new Vector3(w * 1.8f, Fh * 3, w * 0.9f), WinCool);
                Box(n, new Vector3(w * 0.9f + w * 0.9f, topY - Fh * 2.0f, 0), new Vector3(w * 1.85f, 0.08f, w * 0.95f), Steel);
                Box(n, new Vector3(w * 0.9f + w * 0.9f, topY - Fh * 5.0f, 0), new Vector3(w * 1.85f, 0.08f, w * 0.95f), Steel);
                Strip(n, new Vector3(w * 0.9f + w * 0.9f, topY - Fh * 5.0f, w * 0.48f), new Vector3(w * 1.8f, 0.05f, 0.02f), GlowCyan);
                Bar(n, new Vector3(w * 0.6f, 0.2f, 0), new Vector3(w * 2.6f, topY - Fh * 5.1f, 0), 0.16f, Steel);
                Bar(n, new Vector3(w * 0.6f, 0.2f, w * 0.3f), new Vector3(w * 2.6f, topY - Fh * 5.1f, w * 0.3f), 0.12f, Steel);
                Box(n, new Vector3(w * 0.6f, 0.2f, 0.15f), new Vector3(0.6f, 0.4f, 0.9f), Concrete);
                return n;
            }
            default: // くびれた曲面殻: 楕円の平面が中央でくびれ、上で広がる。横の帯を密に
                return LoftTower(parent, x, y, z, rot, MeshParts.Ellipse(w * 1.1f, w * 0.8f, 28), floors,
                    (i, yy, t) => (1f - 0.32f * Mathf.Sin(t * Mathf.Pi) + 0.1f * t, 0, Vector2.Zero), WinCool, false, 2, w + 1.4f);
        }
    }
}
