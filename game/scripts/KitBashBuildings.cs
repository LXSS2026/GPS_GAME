using Godot;
using System;

/// <summary>
/// 名建築の造形を参考にした建物レシピ 10 種(部品メッシュ方式)。原点は基壇の中心、床面。
/// 0 段状の冠(アールデコ) / 1 板状カーテンウォール / 2 外骨格 / 3 外部設備 / 4 積層塔 / 5 先細りの尖塔 / 6 双塔 / 7 水平帯 / 8 ブルータリズム / 9 段状ピラミッド
/// </summary>
public sealed partial class KitBash
{
    public const int RefStyles = 10;

    public Node3D RefBuilding(Node3D parent, int style, float x, float y, float z, float w, float d, int floors, float rot = 0)
    {
        return style switch
        {
            0 => Deco(parent, x, y, z, w, d, floors, rot),
            1 => Curtain(parent, x, y, z, w, d, floors, rot),
            2 => Exo(parent, x, y, z, w, d, floors, rot),
            3 => Services(parent, x, y, z, w, d, floors, rot),
            4 => Pagoda(parent, x, y, z, w, d, floors, rot),
            5 => Needle(parent, x, y, z, w, d, floors, rot),
            6 => Twin(parent, x, y, z, w, d, floors, rot),
            7 => Ribbon(parent, x, y, z, w, d, floors, rot),
            8 => Brutal(parent, x, y, z, w, d, floors, rot),
            _ => Pyramid(parent, x, y, z, w, d, floors, rot),
        };
    }

    /// <summary>基壇と店舗階(共通)。上端の y を返す。</summary>
    float Base(Node3D n, float w, float d, Material lobby, bool colonnade = false)
    {
        Box(n, new Vector3(0, 0.1f, 0), new Vector3(w + 1.2f, 0.2f, d + 1.2f), Floor);
        float gh = 1.2f;
        if (colonnade)
        {
            // 列柱の下に引っ込んだロビー
            Box(n, new Vector3(0, 0.2f + gh / 2, 0), new Vector3(w - 0.8f, gh, d - 0.8f), lobby);
            for (int sx = -1; sx <= 1; sx += 2) for (int i = 0; i <= 3; i++)
            {
                Box(n, new Vector3(sx * w / 2, 0.2f + gh / 2, -d / 2 + i * d / 3), new Vector3(0.22f, gh, 0.22f), Steel);
                Box(n, new Vector3(-w / 2 + i * w / 3, 0.2f + gh / 2, sx * d / 2), new Vector3(0.22f, gh, 0.22f), Steel);
            }
        }
        else
        {
            Box(n, new Vector3(0, 0.2f + gh / 2, 0), new Vector3(w, gh, d), Concrete);
            Box(n, new Vector3(0, 0.2f + gh * 0.55f, d / 2 + 0.01f), new Vector3(w * 0.8f, gh * 0.5f, 0.02f), WinCool);
            Strip(n, new Vector3(0, 0.2f + gh * 0.82f, d / 2 + 0.02f), new Vector3(w * 0.8f, 0.04f, 0.02f), GlowCyan);
            Box(n, new Vector3(0, 0.2f + gh * 0.4f, d / 2 + 0.02f), new Vector3(0.6f, gh * 0.8f, 0.04f), Rubber);
        }
        Box(n, new Vector3(0, 0.2f + gh + 0.05f, 0), new Vector3(w + 0.3f, 0.1f, d + 0.3f), Steel);
        Light(n, new Vector3(0, 0.8f, d / 2 + 1.0f), new Color(0.9f, 0.95f, 1.0f), 4f, 1.2f);
        return 0.2f + gh + 0.1f;
    }

    /// <summary>窓付きの区画(壁の芯 + 近距離ベイ + 遠距離の箱)。</summary>
    float Section(Node3D n, float yBase, float w, float d, int floors, float ox, float oz, bool cool, float litRate = 0.5f)
    {
        float h = floors * Fh;
        Box(n, new Vector3(ox, yBase + h / 2, oz), new Vector3(w - 0.4f, h, d - 0.4f), PaintDark);
        Facade(n, yBase, w, d, floors, ox, oz, litRate, cool);
        FarBox(n, new Vector3(ox, yBase + h / 2, oz), new Vector3(w, h, d), cool ? WinCool : WinWarm);
        Box(n, new Vector3(ox, yBase + h + 0.02f, oz), new Vector3(w + 0.02f, 0.04f, d + 0.02f), PaintDark);
        return yBase + h;
    }

    void Pilasters(Node3D n, float yBase, float h, float w, float d, float ox, float oz, float pitch, Material m)
    {
        int cx = Math.Max(1, (int)(w / pitch)), cz = Math.Max(1, (int)(d / pitch));
        for (int c = 0; c <= cx; c++) { float t = (float)c / cx - 0.5f; Box(n, new Vector3(ox + t * w, yBase + h / 2, oz + d / 2 + 0.04f), new Vector3(0.08f, h, 0.08f), m); Box(n, new Vector3(ox + t * w, yBase + h / 2, oz - d / 2 - 0.04f), new Vector3(0.08f, h, 0.08f), m); }
        for (int c = 0; c <= cz; c++) { float t = (float)c / cz - 0.5f; Box(n, new Vector3(ox + w / 2 + 0.04f, yBase + h / 2, oz + t * d), new Vector3(0.08f, h, 0.08f), m); Box(n, new Vector3(ox - w / 2 - 0.04f, yBase + h / 2, oz + t * d), new Vector3(0.08f, h, 0.08f), m); }
    }

    void Mast(Node3D n, float top, float ox, float oz, float h, Material tip)
    {
        Cyl(n, new Vector3(ox, top + h * 0.3f, oz), 0.12f, h * 0.6f, Steel, topR: 0.06f, seg: 12);
        Cyl(n, new Vector3(ox, top + h * 0.8f, oz), 0.03f, h * 0.4f, Steel, seg: 8);
        Strip(n, new Vector3(ox, top + h + 0.05f, oz), new Vector3(0.14f, 0.14f, 0.14f), tip);
        Light(n, new Vector3(ox, top + h, oz), tip == GlowRed ? new Color(1, 0.2f, 0.15f) : new Color(0.9f, 0.95f, 1f), 6f, 2f);
    }

    // 0: 段状の冠(アールデコ)。基壇 + 4 段の後退 + 縦の柱形 + 段状の冠と尖塔
    Node3D Deco(Node3D parent, float x, float y, float z, float w, float d, int floors, float rot)
    {
        var n = Root(parent, x, y, z, rot);
        float top = Base(n, w, d, WinWarm);
        float[] widths = { 1.0f, 0.78f, 0.6f, 0.44f };
        int[] parts = { floors * 4 / 10, floors * 3 / 10, floors * 2 / 10, floors - floors * 4 / 10 - floors * 3 / 10 - floors * 2 / 10 };
        for (int i = 0; i < 4; i++)
        {
            if (parts[i] <= 0) continue;
            float tw = w * widths[i], td = d * widths[i];
            float y0 = top;
            top = Section(n, top, tw, td, parts[i], 0, 0, false, 0.55f);
            Pilasters(n, y0, top - y0, tw, td, 0, 0, 0.45f, Rust);
            Parapet(n, top, tw, td, 0, 0);
            Strip(n, new Vector3(0, top + 0.3f, td / 2 + 0.04f), new Vector3(tw * 0.9f, 0.08f, 0.02f), GlowWhite);   // 段の縁の装飾光
            Strip(n, new Vector3(0, top + 0.3f, -td / 2 - 0.04f), new Vector3(tw * 0.9f, 0.08f, 0.02f), GlowWhite);
        }
        float cw = w * widths[3];
        Box(n, new Vector3(0, top + 0.4f, 0), new Vector3(cw * 0.8f, 0.8f, cw * 0.8f), Paint);
        Box(n, new Vector3(0, top + 1.1f, 0), new Vector3(cw * 0.55f, 0.6f, cw * 0.55f), Paint);
        for (int i = 0; i < 4; i++) Strip(n, new Vector3(0, top + 0.8f + i * 0.3f, 0), new Vector3(cw * (0.82f - i * 0.1f), 0.04f, cw * (0.82f - i * 0.1f)), GlowWhite);
        Mast(n, top + 1.4f, 0, 0, 3.0f, GlowWhite);
        FlushBays(n);
        return n;
    }

    // 1: 板状カーテンウォール。列柱の上に引っ込んだロビー、青銅色の縦桟が最上部まで連続、平らな屋根
    Node3D Curtain(Node3D parent, float x, float y, float z, float w, float d, int floors, float rot)
    {
        var n = Root(parent, x, y, z, rot);
        float top = Base(n, w, d, WinCool, colonnade: true);
        float y0 = top;
        top = Section(n, top, w, d, floors, 0, 0, true, 0.6f);
        Pilasters(n, y0, top - y0 + 0.3f, w, d, 0, 0, 0.45f, Rust);
        for (int f = 0; f < floors; f += 1) Box(n, new Vector3(0, y0 + f * Fh, 0), new Vector3(w + 0.1f, 0.03f, d + 0.1f), Rust);
        Box(n, new Vector3(0, top + 0.35f, 0), new Vector3(w + 0.1f, 0.7f, d + 0.1f), PaintDark);     // 機械階(暗い帯)
        Strip(n, new Vector3(0, top + 0.35f, d / 2 + 0.07f), new Vector3(w * 0.95f, 0.04f, 0.02f), GlowCyan);
        Parapet(n, top + 0.7f, w, d, 0, 0);
        for (int i = 0; i < 3; i++) Box(n, new Vector3(-w * 0.3f + i * w * 0.3f, top + 0.95f, 0), new Vector3(0.35f, 0.3f, 0.35f), Steel);
        FlushBays(n);
        return n;
    }

    // 2: 外骨格。両側面の双柱マスト、7 階ごとの水平トラスと X 支柱、吊られた階群、開放された 1 階
    Node3D Exo(Node3D parent, float x, float y, float z, float w, float d, int floors, float rot)
    {
        var n = Root(parent, x, y, z, rot);
        float top = Base(n, w, d, WinCool, colonnade: true);
        float y0 = top;
        int groups = Math.Max(1, floors / 7);
        for (int g = 0; g < groups; g++)
        {
            int f = g == groups - 1 ? floors - g * 7 : 7;
            float ys = top;
            top = Section(n, top, w - 0.5f, d, f, 0, 0, true, 0.55f);
            // 水平トラス(前後)と X 支柱
            float th = 0.5f;
            Box(n, new Vector3(0, top + th / 2, d / 2 + 0.12f), new Vector3(w + 0.6f, th, 0.14f), Steel);
            Box(n, new Vector3(0, top + th / 2, -d / 2 - 0.12f), new Vector3(w + 0.6f, th, 0.14f), Steel);
            float ang = Mathf.RadToDeg(Mathf.Atan2(th, 0.9f));
            for (int i = -2; i <= 2; i++) foreach (int sz in new[] { -1, 1 })
            {
                Box(n, new Vector3(i * 0.9f, top + th / 2, sz * (d / 2 + 0.2f)), new Vector3(0.06f, 1.05f, 0.06f), Steel, new Vector3(0, 0, 90 - ang));
                Box(n, new Vector3(i * 0.9f, top + th / 2, sz * (d / 2 + 0.2f)), new Vector3(0.06f, 1.05f, 0.06f), Steel, new Vector3(0, 0, ang - 90));
            }
            top += th;
        }
        // 双柱マスト(左右に 2 本ずつ、全高)と横桟
        float hAll = top - y0 + 1.5f;
        foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 })
        {
            Box(n, new Vector3(sx * (w / 2 + 0.15f), y0 + hAll / 2 - 0.3f, sz * d * 0.3f), new Vector3(0.3f, hAll, 0.3f), Paint);
            for (float yy = y0 + 1; yy < y0 + hAll - 1; yy += 1.8f) Box(n, new Vector3(sx * (w / 2 + 0.15f), yy, 0), new Vector3(0.12f, 0.12f, d * 0.6f + 0.3f), Steel);
        }
        Parapet(n, top, w - 0.5f, d, 0, 0);
        Box(n, new Vector3(0, top + 0.6f, 0), new Vector3(w * 0.4f, 1.2f, d * 0.5f), PaintDark);
        for (int i = 0; i < 4; i++) Cyl(n, new Vector3(-w * 0.3f + i * w * 0.2f, top + 1.5f, 0), 0.03f, 1.2f, Steel, seg: 6);
        Strip(n, new Vector3(0, top + 0.6f, d * 0.25f + 0.02f), new Vector3(w * 0.38f, 0.05f, 0.02f), GlowCyan);
        Mast(n, top + 1.2f, w * 0.3f, -d * 0.3f, 1.6f, GlowRed);
        FlushBays(n);
        return n;
    }

    // 3: 外部設備。主塔の外に 3 本の設備塔(円筒)、露出した配管とダクト、発光するガラスの昇降機、屋上のクレーン
    Node3D Services(Node3D parent, float x, float y, float z, float w, float d, int floors, float rot)
    {
        var n = Root(parent, x, y, z, rot);
        float top = Base(n, w, d, WinCool);
        float y0 = top;
        top = Section(n, top, w, d, floors, 0, 0, true, 0.5f);
        float h = top - y0;
        var towers = new[] { (w / 2 + 0.45f, d / 2 + 0.45f), (-w / 2 - 0.45f, d * 0.1f), (w * 0.1f, -d / 2 - 0.45f) };
        foreach (var (tx, tz) in towers)
        {
            Cyl(n, new Vector3(tx, y0 + h / 2 + 0.6f, tz), 0.42f, h + 1.2f, Steel, seg: 24);
            for (int f = 0; f < floors; f += 2) Cyl(n, new Vector3(tx, y0 + f * Fh, tz), 0.46f, 0.08f, Rust, seg: 24);
            Cyl(n, new Vector3(tx, y0 + h + 1.3f, tz), 0.5f, 0.2f, Rust, seg: 24);
            Pipe(n, new Vector3(tx + 0.5f, y0 + 0.3f, tz), new Vector3(tx + 0.5f, y0 + h - 0.3f, tz), 0.08f, Rust);
            Pipe(n, new Vector3(tx, y0 + 0.3f, tz + 0.5f), new Vector3(tx, y0 + h - 0.3f, tz + 0.5f), 0.06f, Steel);
        }
        // ガラスの昇降機(発光)と横の連絡通路
        Box(n, new Vector3(-w / 2 - 0.3f, y0 + h / 2, -d / 2 + 0.6f), new Vector3(0.5f, h, 0.7f), WinCool);
        Strip(n, new Vector3(-w / 2 - 0.56f, y0 + h / 2, -d / 2 + 0.6f), new Vector3(0.02f, h, 0.06f), GlowCyan);
        for (int f = 3; f < floors; f += 6) Box(n, new Vector3(w / 2 + 0.22f, y0 + f * Fh + 0.3f, d / 2 + 0.22f), new Vector3(0.9f, 0.5f, 0.9f), PaintDark);
        // 露出ダクト(前面を横断)
        for (int f = 2; f < floors; f += 5) Pipe(n, new Vector3(-w / 2 + 0.3f, y0 + f * Fh + 0.45f, d / 2 + 0.2f), new Vector3(w / 2 - 0.3f, y0 + f * Fh + 0.45f, d / 2 + 0.2f), 0.12f, Steel);
        Parapet(n, top, w, d, 0, 0);
        // 屋上クレーン
        Box(n, new Vector3(w * 0.2f, top + 1.0f, -d * 0.2f), new Vector3(0.2f, 2.0f, 0.2f), Rust);
        Box(n, new Vector3(w * 0.2f + 0.9f, top + 2.0f, -d * 0.2f), new Vector3(2.2f, 0.12f, 0.12f), Rust);
        Box(n, new Vector3(w * 0.2f + 1.8f, top + 1.5f, -d * 0.2f), new Vector3(0.04f, 1.0f, 0.04f), Rubber);
        Mast(n, top, -w * 0.3f, d * 0.3f, 1.8f, GlowRed);
        FlushBays(n);
        return n;
    }

    // 4: 積層塔。8 段の末広がりの節を積み、各節に軒と装飾、頂部に尖塔
    Node3D Pagoda(Node3D parent, float x, float y, float z, float w, float d, int floors, float rot)
    {
        var n = Root(parent, x, y, z, rot);
        float top = Base(n, w, d, WinCool);
        int segs = Math.Clamp(floors / 6, 3, 8);
        int per = Math.Max(2, floors / segs);
        float bw = w * 0.9f, bd = d * 0.9f;
        for (int s = 0; s < segs; s++)
        {
            float y0 = top;
            top = Section(n, top, bw, bd, per, 0, 0, true, 0.55f);
            // 末広がり: 節の上部を少し広げた枠(フラスタム)と軒
            Frustum(n, new Vector3(0, top - Fh * 1.2f, 0), new Vector3(bw + 0.05f, 0, bd + 0.05f), new Vector3(bw + 0.45f, 0, bd + 0.45f), Fh * 1.2f, Paint);
            Box(n, new Vector3(0, top + 0.06f, 0), new Vector3(bw + 0.7f, 0.12f, bd + 0.7f), Steel);
            Strip(n, new Vector3(0, top + 0.06f, (bd + 0.7f) / 2 + 0.01f), new Vector3(bw + 0.7f, 0.05f, 0.02f), GlowWarm());
            Strip(n, new Vector3(0, top + 0.06f, -(bd + 0.7f) / 2 - 0.01f), new Vector3(bw + 0.7f, 0.05f, 0.02f), GlowWarm());
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2) Box(n, new Vector3(sx * (bw / 2 + 0.3f), top + 0.25f, sz * (bd / 2 + 0.3f)), new Vector3(0.14f, 0.26f, 0.14f), Rust);
            top += 0.12f;
        }
        Box(n, new Vector3(0, top + 0.5f, 0), new Vector3(bw * 0.6f, 1.0f, bd * 0.6f), Paint);
        Box(n, new Vector3(0, top + 1.3f, 0), new Vector3(bw * 0.35f, 0.6f, bd * 0.35f), Paint);
        Mast(n, top + 1.6f, 0, 0, 4.0f, GlowWhite);
        FlushBays(n);
        return n;
    }

    // 5: 先細りの尖塔。中央コアに 3 枚の翼が付き、翼が段階的に短くなって螺旋状に後退、長い尖塔
    Node3D Needle(Node3D parent, float x, float y, float z, float w, float d, int floors, float rot)
    {
        var n = Root(parent, x, y, z, rot);
        float top = Base(n, Math.Max(w, d), Math.Max(w, d), WinCool);
        float y0 = top;
        float r = Math.Min(w, d) * 0.28f;
        float hAll = floors * Fh;
        Cyl(n, new Vector3(0, y0 + hAll / 2, 0), r, hAll, PickWindows(), seg: 24);
        for (int f = 0; f < floors; f += 3) Cyl(n, new Vector3(0, y0 + f * Fh, 0), r + 0.04f, 0.06f, Steel, seg: 24);
        int steps = 5;
        for (int wing = 0; wing < 3; wing++)
        {
            float ang = wing * 120f + 30f;
            for (int s = 0; s < steps; s++)
            {
                int f0 = 0, f1 = (int)(floors * (1f - (s + wing * 0.33f) / (steps + 1.5f)));
                if (f1 <= 0) continue;
                float len = Math.Max(w, d) * (0.55f - s * 0.09f);
                float hh = (f1 - f0) * Fh;
                var dir = new Vector3(Mathf.Cos(Mathf.DegToRad(ang)), 0, Mathf.Sin(Mathf.DegToRad(ang)));
                var c = dir * (r + len / 2 - 0.1f);
                Box(n, new Vector3(c.X, y0 + hh / 2, c.Z), new Vector3(len, hh, 0.9f - s * 0.08f), s % 2 == 0 ? WinCool : WinWarm, new Vector3(0, -ang, 0));
                Box(n, new Vector3(c.X, y0 + hh + 0.03f, c.Z), new Vector3(len + 0.1f, 0.06f, 1.0f - s * 0.08f), Steel, new Vector3(0, -ang, 0));
                Strip(n, new Vector3(c.X, y0 + hh + 0.08f, c.Z), new Vector3(len * 0.9f, 0.05f, 0.05f), GlowCyan, new Vector3(0, -ang, 0));
            }
        }
        Cyl(n, new Vector3(0, y0 + hAll + 0.4f, 0), r * 0.9f, 0.8f, Paint, topR: r * 0.6f, seg: 24);
        Cyl(n, new Vector3(0, y0 + hAll + 2.2f, 0), r * 0.5f, 3.0f, Steel, topR: 0.05f, seg: 16);
        Cyl(n, new Vector3(0, y0 + hAll + 5.0f, 0), 0.04f, 2.8f, Steel, seg: 8);
        Strip(n, new Vector3(0, y0 + hAll + 6.45f, 0), new Vector3(0.14f, 0.14f, 0.14f), GlowRed);
        Light(n, new Vector3(0, y0 + hAll + 6.4f, 0), new Color(1, 0.2f, 0.15f), 8f, 2f);
        return n;
    }

    // 6: 双塔。八角の 2 塔が 3 段で細くなり、中層で空中回廊、先端は円錐
    Node3D Twin(Node3D parent, float x, float y, float z, float w, float d, int floors, float rot)
    {
        var n = Root(parent, x, y, z, rot);
        float top0 = Base(n, w, d, WinCool);
        float gap = w * 0.52f;
        float topMax = top0;
        foreach (int sx in new[] { -1, 1 })
        {
            float top = top0;
            float r = w * 0.24f;
            int[] parts = { floors * 6 / 10, floors * 25 / 100, floors - floors * 6 / 10 - floors * 25 / 100 };
            for (int t = 0; t < 3; t++)
            {
                if (parts[t] <= 0) continue;
                float h = parts[t] * Fh;
                Cyl(n, new Vector3(sx * gap, top + h / 2, 0), r, h, t == 1 ? WinWarm : WinCool, seg: 8);
                for (int f = 0; f < parts[t]; f += 2) Cyl(n, new Vector3(sx * gap, top + f * Fh, 0), r + 0.05f, 0.06f, Steel, seg: 8);
                top += h;
                Cyl(n, new Vector3(sx * gap, top + 0.08f, 0), r + 0.12f, 0.16f, Steel, seg: 8);
                Cyl(n, new Vector3(sx * gap, top + 0.17f, 0), r + 0.14f, 0.03f, GlowCyan, seg: 8);
                r *= 0.78f;
            }
            Cyl(n, new Vector3(sx * gap, top + 1.0f, 0), r, 2.0f, Steel, topR: 0.04f, seg: 8);
            Cyl(n, new Vector3(sx * gap, top + 2.8f, 0), 0.03f, 1.6f, Steel, seg: 6);
            Strip(n, new Vector3(sx * gap, top + 3.65f, 0), new Vector3(0.14f, 0.14f, 0.14f), GlowRed);
            topMax = top;
        }
        float by = top0 + floors * 0.4f * Fh;
        SkyBridge(n, new Vector3(-gap + w * 0.2f, by, 0), new Vector3(gap - w * 0.2f, by, 0));
        for (int sx = -1; sx <= 1; sx += 2) Box(n, new Vector3(sx * gap * 0.5f, by - 1.6f, 0), new Vector3(0.12f, 3.2f, 0.12f), Steel, new Vector3(0, 0, sx * 28f));
        return n;
    }

    // 7: 水平帯。ガラスの帯と腰壁の帯が交互、角に丸みの代わりに面取り、屋上庭園
    Node3D Ribbon(Node3D parent, float x, float y, float z, float w, float d, int floors, float rot)
    {
        var n = Root(parent, x, y, z, rot);
        float top = Base(n, w, d, WinWarm);
        for (int f = 0; f < floors; f++)
        {
            float fy = top + f * Fh;
            Box(n, new Vector3(0, fy + 0.2f, 0), new Vector3(w + 0.12f, 0.4f, d + 0.12f), f % 5 == 4 ? Paint : PaintDark);
            Box(n, new Vector3(0, fy + 0.65f, 0), new Vector3(w, 0.5f, d), f % 3 == 0 ? WinCool : WinWarm);
            Box(n, new Vector3(0, fy + 0.4f, 0), new Vector3(w + 0.2f, 0.04f, d + 0.2f), Steel);
        }
        top += floors * Fh;
        Parapet(n, top, w, d, 0, 0);
        for (int i = 0; i < 6; i++) Box(n, new Vector3(-w * 0.35f + (i % 3) * w * 0.35f, top + 0.45f, -d * 0.25f + (i / 3) * d * 0.5f), new Vector3(0.5f, 0.6f, 0.5f), Foliage);
        Box(n, new Vector3(w * 0.3f, top + 0.5f, d * 0.3f), new Vector3(w * 0.3f, 1.0f, d * 0.3f), PaintDark);
        Strip(n, new Vector3(0, top + 0.3f, d / 2 + 0.06f), new Vector3(w * 0.95f, 0.04f, 0.02f), GlowCyan);
        Mast(n, top + 1.0f, w * 0.3f, d * 0.3f, 1.4f, GlowRed);
        return n;
    }

    // 8: ブルータリズム。コンクリートの板状棟と独立した設備塔を 3 階ごとの橋でつなぐ、屋上のボイラー室
    Node3D Brutal(Node3D parent, float x, float y, float z, float w, float d, int floors, float rot)
    {
        var n = Root(parent, x, y, z, rot);
        Box(n, new Vector3(0, 0.1f, 0), new Vector3(w + 1.6f, 0.2f, d + 1.2f), Floor);
        float top = 0.2f;
        float bw = w * 0.8f, bd = d;
        float y0 = top;
        Box(n, new Vector3(-w * 0.1f, top + floors * Fh / 2, 0), new Vector3(bw, floors * Fh, bd), Concrete);
        for (int f = 0; f < floors; f++)
        {
            float fy = top + f * Fh;
            Box(n, new Vector3(-w * 0.1f, fy + 0.02f, 0), new Vector3(bw + 0.3f, 0.12f, bd + 0.3f), Concrete);
            for (int c = 0; c < (int)(bw / 0.6f); c++)
            {
                float t = (c + 0.5f) / (int)(bw / 0.6f) - 0.5f;
                Box(n, new Vector3(-w * 0.1f + t * bw, fy + 0.55f, bd / 2 + 0.01f), new Vector3(0.3f, 0.4f, 0.04f), _rnd.NextDouble() < 0.4 ? WinWarm : Glass);
                Box(n, new Vector3(-w * 0.1f + t * bw, fy + 0.55f, -bd / 2 - 0.01f), new Vector3(0.3f, 0.4f, 0.04f), _rnd.NextDouble() < 0.4 ? WinWarm : Glass);
            }
        }
        top += floors * Fh;
        // 設備塔(独立)と橋
        float tx = w / 2 + 0.9f;
        Box(n, new Vector3(tx, y0 + (floors * Fh + 2.0f) / 2, 0), new Vector3(0.9f, floors * Fh + 2.0f, 0.9f), Concrete);
        for (int f = 0; f < floors; f++) Box(n, new Vector3(tx, y0 + f * Fh + 0.45f, 0.46f), new Vector3(0.5f, 0.3f, 0.02f), Glass);
        for (int f = 2; f < floors; f += 3) { Box(n, new Vector3((tx + w * 0.3f) / 2, y0 + f * Fh + 0.05f, 0), new Vector3(tx - w * 0.3f, 0.1f, 0.8f), Concrete); Box(n, new Vector3((tx + w * 0.3f) / 2, y0 + f * Fh + 0.5f, 0.35f), new Vector3(tx - w * 0.3f, 0.9f, 0.04f), Glass); }
        Box(n, new Vector3(tx, y0 + floors * Fh + 2.4f, 0), new Vector3(1.3f, 0.8f, 1.3f), Concrete);
        Strip(n, new Vector3(tx, y0 + floors * Fh + 2.85f, 0), new Vector3(0.12f, 0.12f, 0.12f), GlowRed);
        Box(n, new Vector3(-w * 0.2f, top + 0.6f, 0), new Vector3(bw * 0.4f, 1.2f, bd * 0.6f), Concrete);
        Cyl(n, new Vector3(-w * 0.35f, top + 1.6f, 0), 0.15f, 1.2f, Rust, seg: 16);
        FlushBays(n);
        return n;
    }

    // 9: 段状ピラミッド(アーコロジー)。6 段のテラス、縁の発光、格納庫の開口、中央の尖塔
    Node3D Pyramid(Node3D parent, float x, float y, float z, float w, float d, int floors, float rot)
    {
        var n = Root(parent, x, y, z, rot);
        Box(n, new Vector3(0, 0.1f, 0), new Vector3(w + 1.5f, 0.2f, d + 1.5f), Floor);
        float top = 0.2f;
        int tiers = 6;
        int per = Math.Max(2, floors / tiers);
        float tw = w + 1.0f, td = d + 1.0f;
        for (int t = 0; t < tiers; t++)
        {
            float h = per * Fh;
            Box(n, new Vector3(0, top + h / 2, 0), new Vector3(tw, h, td), t % 2 == 0 ? PaintDark : Paint);
            // 窓帯と格納庫の開口
            for (int f = 0; f < per; f++)
            {
                Box(n, new Vector3(0, top + f * Fh + 0.55f, td / 2 + 0.01f), new Vector3(tw * 0.9f, 0.4f, 0.02f), f % 2 == 0 ? WinCool : WinWarm);
                Box(n, new Vector3(tw / 2 + 0.01f, top + f * Fh + 0.55f, 0), new Vector3(0.02f, 0.4f, td * 0.9f), f % 2 == 0 ? WinWarm : WinCool);
            }
            if (t < 4)
            {
                Box(n, new Vector3(tw * 0.25f, top + 0.5f, td / 2 + 0.02f), new Vector3(1.2f, 0.9f, 0.06f), Rubber);
                Strip(n, new Vector3(tw * 0.25f, top + 0.95f, td / 2 + 0.04f), new Vector3(1.2f, 0.05f, 0.02f), GlowOrange);
            }
            top += h;
            // テラス: 縁の発光帯、手すり、緑
            Box(n, new Vector3(0, top + 0.05f, 0), new Vector3(tw + 0.2f, 0.1f, td + 0.2f), Steel);
            Strip(n, new Vector3(0, top + 0.05f, (td + 0.2f) / 2 + 0.01f), new Vector3(tw + 0.2f, 0.05f, 0.02f), GlowCyan);
            Strip(n, new Vector3((tw + 0.2f) / 2 + 0.01f, top + 0.05f, 0), new Vector3(0.02f, 0.05f, td + 0.2f), GlowCyan);
            for (int k = 0; k < 4; k++) Box(n, new Vector3(-tw * 0.4f + k * tw * 0.27f, top + 0.3f, td * 0.44f), new Vector3(0.4f, 0.4f, 0.4f), Foliage);
            top += 0.1f;
            tw *= 0.8f; td *= 0.8f;
        }
        Box(n, new Vector3(0, top + 0.5f, 0), new Vector3(tw * 0.8f, 1.0f, td * 0.8f), Paint);
        Cyl(n, new Vector3(0, top + 2.5f, 0), 0.35f, 3.0f, Steel, topR: 0.05f, seg: 12);
        Cyl(n, new Vector3(0, top + 4.6f, 0), 0.03f, 1.4f, Steel, seg: 6);
        Strip(n, new Vector3(0, top + 5.35f, 0), new Vector3(0.16f, 0.16f, 0.16f), GlowRed);
        Light(n, new Vector3(0, top + 5.3f, 0), new Color(1, 0.2f, 0.15f), 8f, 2f);
        return n;
    }

    StandardMaterial3D? _glowWarm;
    Material GlowWarm() => _glowWarm ??= Glow(new Color(1.0f, 0.75f, 0.45f), 3f);

    /// <summary>四角錐台(下底 bottom、上底 top の矩形、高さ h)。中心は底面の中心。</summary>
    public void Frustum(Node3D p, Vector3 baseCenter, Vector3 bottom, Vector3 top, float h, Material m)
    {
        p.AddChild(new MeshInstance3D { Mesh = MeshParts.Frustum(bottom, top, h), MaterialOverride = m, Position = baseCenter });
    }
}
