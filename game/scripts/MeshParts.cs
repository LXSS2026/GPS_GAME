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
