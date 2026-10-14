using Godot;
using System;

/// <summary>部品メッシュ: 面取り付きの箱、窓 1 区画(ベイ)。SurfaceTool で生成し、MultiMesh で大量配置する。</summary>
public static class MeshParts
{
    public const float BayW = 0.9f, BayH = 0.9f;

    /// <summary>四角形を追加(Godot の表面は外から見て時計回り。向きは法線で自動判定)。</summary>
    static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
    {
        if ((b - a).Cross(c - a).Dot(n) > 0) { (b, d) = (d, b); }
        st.SetNormal(n);
        st.SetUV(new Vector2(0, 0)); st.AddVertex(a);
        st.SetUV(new Vector2(1, 0)); st.AddVertex(b);
        st.SetUV(new Vector2(1, 1)); st.AddVertex(c);
        st.SetUV(new Vector2(0, 0)); st.AddVertex(a);
        st.SetUV(new Vector2(1, 1)); st.AddVertex(c);
        st.SetUV(new Vector2(0, 1)); st.AddVertex(d);
    }

    static void Tri(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 n)
    {
        if ((b - a).Cross(c - a).Dot(n) > 0) { (b, c) = (c, b); }
        st.SetNormal(n);
        st.SetUV(new Vector2(0, 0)); st.AddVertex(a);
        st.SetUV(new Vector2(1, 0)); st.AddVertex(b);
        st.SetUV(new Vector2(0, 1)); st.AddVertex(c);
    }

    /// <summary>中心原点・大きさ size の面取り箱を st に追加する。c は面取りの幅。</summary>
    public static void AppendChamferBox(SurfaceTool st, Vector3 center, Vector3 size, float c)
    {
        var h = size / 2;
        var cv = new Vector3(Mathf.Min(c, h.X * 0.45f), Mathf.Min(c, h.Y * 0.45f), Mathf.Min(c, h.Z * 0.45f));
        var axes = new[] { Vector3.Right, Vector3.Up, Vector3.Back };
        // 6 面
        foreach (var n in new[] { Vector3.Right, Vector3.Left, Vector3.Up, Vector3.Down, Vector3.Back, Vector3.Forward })
        {
            var u = n.Y != 0 ? Vector3.Right : Vector3.Up;
            var v = n.Cross(u);
            Vector3 P(float su, float sv) => center + n * (n.Abs().Dot(h)) + u * su * (u.Abs().Dot(h - cv)) + v * sv * (v.Abs().Dot(h - cv));
            Quad(st, P(-1, -1), P(1, -1), P(1, 1), P(-1, 1), n);
        }
        // 12 辺
        for (int i = 0; i < 3; i++)
            for (int j = i + 1; j < 3; j++)
            {
                var e = axes[3 - i - j];
                foreach (int si in new[] { -1, 1 })
                    foreach (int sj in new[] { -1, 1 })
                    {
                        var nA = axes[i] * si; var nB = axes[j] * sj;
                        var n = (nA + nB).Normalized();
                        float hA = axes[i].Abs().Dot(h), hB = axes[j].Abs().Dot(h), hE = e.Abs().Dot(h - cv);
                        float cA = axes[i].Abs().Dot(cv), cB = axes[j].Abs().Dot(cv);
                        var a = center + nA * hA + nB * (hB - cB) + e * hE;
                        var b = center + nA * hA + nB * (hB - cB) - e * hE;
                        var c2 = center + nA * (hA - cA) + nB * hB - e * hE;
                        var d = center + nA * (hA - cA) + nB * hB + e * hE;
                        Quad(st, a, b, c2, d, n);
                    }
            }
        // 8 角
        foreach (int sx in new[] { -1, 1 }) foreach (int sy in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 })
        {
            var n = new Vector3(sx, sy, sz).Normalized();
            var px = center + new Vector3(sx * h.X, sy * (h.Y - cv.Y), sz * (h.Z - cv.Z));
            var py = center + new Vector3(sx * (h.X - cv.X), sy * h.Y, sz * (h.Z - cv.Z));
            var pz = center + new Vector3(sx * (h.X - cv.X), sy * (h.Y - cv.Y), sz * h.Z);
            Tri(st, px, py, pz, n);
        }
    }

    /// <summary>四角錐台: 底面 bottom(x, z)、上面 top(x, z)、高さ h。原点は底面の中心。</summary>
    public static ArrayMesh Frustum(Vector3 bottom, Vector3 top, float h)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 B(int sx, int sz) => new(sx * bottom.X / 2, 0, sz * bottom.Z / 2);
        Vector3 T(int sx, int sz) => new(sx * top.X / 2, h, sz * top.Z / 2);
        Quad(st, T(-1, -1), T(1, -1), T(1, 1), T(-1, 1), Vector3.Up);
        Quad(st, B(-1, -1), B(1, -1), B(1, 1), B(-1, 1), Vector3.Down);
        foreach (var (sx, sz, n) in new[] { (1, 0, Vector3.Right), (-1, 0, Vector3.Left), (0, 1, Vector3.Back), (0, -1, Vector3.Forward) })
        {
            Vector3 b0, b1, t0, t1;
            if (sx != 0) { b0 = B(sx, -1); b1 = B(sx, 1); t0 = T(sx, -1); t1 = T(sx, 1); }
            else { b0 = B(-1, sz); b1 = B(1, sz); t0 = T(-1, sz); t1 = T(1, sz); }
            var nn = ((b1 - b0).Cross(t0 - b0)).Normalized();
            if (nn.Dot(n) < 0) nn = -nn;
            Quad(st, b0, b1, t1, t0, nn);
        }
        return st.Commit();
    }

    // ---------- 平面形(閉じた多角形、原点中心、反時計回り) ----------
    public static Vector2[] RegularPolygon(int n, float r, float phaseDeg = 0)
    {
        var pts = new Vector2[n];
        for (int i = 0; i < n; i++) { float a = Mathf.DegToRad(phaseDeg) + i * Mathf.Tau / n; pts[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r; }
        return pts;
    }

    /// <summary>角を丸めた正多角形。round は角の丸めの割合(0〜0.5)、seg は角ごとの分割。</summary>
    public static Vector2[] RoundedPolygon(int n, float r, float round, int seg)
    {
        var corners = RegularPolygon(n, r);
        var pts = new System.Collections.Generic.List<Vector2>();
        for (int i = 0; i < n; i++)
        {
            var prev = corners[(i - 1 + n) % n]; var c = corners[i]; var next = corners[(i + 1) % n];
            var a = c + (prev - c) * round; var b = c + (next - c) * round;
            for (int k = 0; k <= seg; k++) { float t = (float)k / seg; pts.Add(a.Lerp(c, t).Lerp(c.Lerp(b, t), t)); }
        }
        return pts.ToArray();
    }

    public static Vector2[] RoundedRect(float w, float d, float r, int seg)
    {
        var pts = new System.Collections.Generic.List<Vector2>();
        var centers = new[] { new Vector2(w / 2 - r, d / 2 - r), new Vector2(-w / 2 + r, d / 2 - r), new Vector2(-w / 2 + r, -d / 2 + r), new Vector2(w / 2 - r, -d / 2 + r) };
        for (int c = 0; c < 4; c++)
            for (int k = 0; k <= seg; k++) { float a = c * Mathf.Pi / 2 + k * (Mathf.Pi / 2) / seg; pts.Add(centers[c] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r); }
        return pts.ToArray();
    }

    public static Vector2[] Ellipse(float w, float d, int n)
    {
        var pts = new Vector2[n];
        for (int i = 0; i < n; i++) { float a = i * Mathf.Tau / n; pts[i] = new Vector2(Mathf.Cos(a) * w / 2, Mathf.Sin(a) * d / 2); }
        return pts;
    }

    /// <summary>レンズ形: 2 つの円弧。w が長径、d が厚み。</summary>
    public static Vector2[] Lens(float w, float d, int seg)
    {
        var pts = new System.Collections.Generic.List<Vector2>();
        float R = (w * w / 4 + d * d / 4) / d;      // 弧の半径
        float cy = d / 2 - R;                        // 上弧の中心
        float half = Mathf.Asin((w / 2) / R);
        for (int k = 0; k <= seg; k++) { float a = Mathf.Pi / 2 + half - k * 2 * half / seg; pts.Add(new Vector2(Mathf.Cos(a) * R, cy + Mathf.Sin(a) * R)); }
        for (int k = 0; k <= seg; k++) { float a = -Mathf.Pi / 2 + half - k * 2 * half / seg; pts.Add(new Vector2(Mathf.Cos(a) * R, -cy + Mathf.Sin(a) * R)); }
        return pts.ToArray();
    }

    public static Vector2[] Star(int n, float rOut, float rIn)
    {
        var pts = new Vector2[n * 2];
        for (int i = 0; i < n * 2; i++) { float a = i * Mathf.Pi / n; float r = i % 2 == 0 ? rOut : rIn; pts[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r; }
        return pts;
    }

    public static Vector3 LoftPoint(Vector2[] profile, int j, float y, (float scale, float rotDeg, Vector2 offset) xf, float extra = 1f)
    {
        var p = profile[j] * xf.scale * extra;
        p = p.Rotated(Mathf.DegToRad(xf.rotDeg)) + xf.offset;
        return new Vector3(p.X, y, p.Y);
    }

    /// <summary>
    /// ロフト: 平面形を ringY の各高さに xfs の変形で置き、隣り合う環を四角形でつなぐ。
    /// UV は u = 外周の距離 × uvScale、v = 高さ × uvScale(窓テクスチャの 1 タイル = 1/uvScale m)。
    /// </summary>
    public static ArrayMesh Loft(Vector2[] profile, float[] ringY, (float scale, float rotDeg, Vector2 offset)[] xfs, bool capTop, bool capBottom, float uvScale)
    {
        int m = profile.Length, rings = ringY.Length;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var perim = new float[m + 1];
        for (int j = 0; j < m; j++) perim[j + 1] = perim[j] + (profile[(j + 1) % m] - profile[j]).Length();
        for (int i = 0; i < rings - 1; i++)
            for (int j = 0; j < m; j++)
            {
                int j1 = (j + 1) % m;
                var a = LoftPoint(profile, j, ringY[i], xfs[i]);
                var b = LoftPoint(profile, j1, ringY[i], xfs[i]);
                var c = LoftPoint(profile, j1, ringY[i + 1], xfs[i + 1]);
                var d = LoftPoint(profile, j, ringY[i + 1], xfs[i + 1]);
                var n = (b - a).Cross(d - a).Normalized();
                // 外向きに揃える(平面形は反時計回り)
                var outward = new Vector3(profile[j].X + profile[j1].X, 0, profile[j].Y + profile[j1].Y);
                outward = new Vector3(Mathf.Cos(Mathf.DegToRad(xfs[i].rotDeg)) * outward.X - Mathf.Sin(Mathf.DegToRad(xfs[i].rotDeg)) * outward.Z, 0, Mathf.Sin(Mathf.DegToRad(xfs[i].rotDeg)) * outward.X + Mathf.Cos(Mathf.DegToRad(xfs[i].rotDeg)) * outward.Z);
                if (n.Dot(outward) < 0) n = -n;
                float u0 = perim[j] * uvScale, u1 = perim[j + 1] * uvScale, v0 = ringY[i] * uvScale, v1 = ringY[i + 1] * uvScale;
                QuadUv(st, a, b, c, d, n, new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1));
            }
        if (capTop)
        {
            int i = rings - 1;
            var c0 = Vector3.Zero; for (int j = 0; j < m; j++) c0 += LoftPoint(profile, j, ringY[i], xfs[i]); c0 /= m;
            for (int j = 0; j < m; j++) Tri(st, c0, LoftPoint(profile, j, ringY[i], xfs[i]), LoftPoint(profile, (j + 1) % m, ringY[i], xfs[i]), Vector3.Up);
        }
        if (capBottom)
        {
            var c0 = Vector3.Zero; for (int j = 0; j < m; j++) c0 += LoftPoint(profile, j, ringY[0], xfs[0]); c0 /= m;
            for (int j = 0; j < m; j++) Tri(st, c0, LoftPoint(profile, j, ringY[0], xfs[0]), LoftPoint(profile, (j + 1) % m, ringY[0], xfs[0]), Vector3.Down);
        }
        return st.Commit();
    }

    static void QuadUv(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
    {
        if ((b - a).Cross(c - a).Dot(n) > 0) { (b, d) = (d, b); (ub, ud) = (ud, ub); }
        st.SetNormal(n);
        st.SetUV(ua); st.AddVertex(a); st.SetUV(ub); st.AddVertex(b); st.SetUV(uc); st.AddVertex(c);
        st.SetUV(ua); st.AddVertex(a); st.SetUV(uc); st.AddVertex(c); st.SetUV(ud); st.AddVertex(d);
    }

    public static ArrayMesh ChamferBox(float c)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        AppendChamferBox(st, Vector3.Zero, Vector3.One, c);
        return st.Commit();
    }

    /// <summary>
    /// 窓 1 区画。原点は壁面上の区画の下端中央、+z が外向き。幅 BayW、高さ BayH。
    /// 表面 0 = 枠と腰壁(frame)、1 = ガラス(glass)、2 = 庇と桟(trim)。
    /// </summary>
    public static ArrayMesh Bay(Material frame, Material glass, Material trim)
    {
        const float w = BayW, h = BayH, sill = 0.28f, depth = 0.16f;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        // 枠: 左右の方立、上枠、腰壁(少し張り出す)
        AppendChamferBox(st, new Vector3(-w / 2 + 0.04f, sill + (h - sill) / 2, -depth / 2), new Vector3(0.08f, h - sill, depth), 0.015f);
        AppendChamferBox(st, new Vector3(w / 2 - 0.04f, sill + (h - sill) / 2, -depth / 2), new Vector3(0.08f, h - sill, depth), 0.015f);
        AppendChamferBox(st, new Vector3(0, h - 0.04f, -depth / 2), new Vector3(w, 0.08f, depth), 0.015f);
        AppendChamferBox(st, new Vector3(0, sill / 2, -depth / 2 + 0.03f), new Vector3(w, sill, depth - 0.06f + 0.06f), 0.02f);
        var mesh = st.Commit();
        mesh.SurfaceSetMaterial(0, frame);
        // ガラス: 奥まった面
        st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        float gz = -depth + 0.03f;
        Quad(st, new Vector3(-w / 2 + 0.08f, sill, gz), new Vector3(w / 2 - 0.08f, sill, gz), new Vector3(w / 2 - 0.08f, h - 0.08f, gz), new Vector3(-w / 2 + 0.08f, h - 0.08f, gz), Vector3.Back);
        st.Commit(mesh);
        mesh.SurfaceSetMaterial(1, glass);
        // 庇(腰壁の上の張り出し)と桟(縦 1 本・横 1 本)
        st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        AppendChamferBox(st, new Vector3(0, sill + 0.02f, 0.05f), new Vector3(w, 0.04f, 0.14f), 0.01f);
        AppendChamferBox(st, new Vector3(0, sill + (h - sill) / 2, -depth / 2), new Vector3(0.03f, h - sill, 0.04f), 0.008f);
        AppendChamferBox(st, new Vector3(0, sill + (h - sill) * 0.62f, -depth / 2), new Vector3(w - 0.16f, 0.03f, 0.04f), 0.008f);
        st.Commit(mesh);
        mesh.SurfaceSetMaterial(2, trim);
        return mesh;
    }
}
