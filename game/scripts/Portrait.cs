using Godot;
using GPS.Sim;
using System;

/// <summary>人物の似顔絵(自前の線画。肌・髪・服の色と髪型・目の形は人物の Id と能力から決める)。</summary>
public partial class Portrait : Control
{
    Person? _p;
    public Portrait(Person p, int size = 40) { _p = p; CustomMinimumSize = new Vector2(size, size); }
    public override void _Draw()
    {
        if (_p == null) return;
        float s = CustomMinimumSize.X; int h = _p.Id * 2654435761u.GetHashCode() ^ (_p.Name.GetHashCode());
        var skin = new[] { new Color(0.95f, 0.82f, 0.7f), new Color(0.85f, 0.66f, 0.5f), new Color(0.62f, 0.45f, 0.33f) }[Math.Abs(h) % 3];
        var hair = new[] { new Color(0.12f, 0.1f, 0.1f), new Color(0.35f, 0.2f, 0.1f), new Color(0.6f, 0.5f, 0.3f), new Color(0.5f, 0.5f, 0.55f) }[Math.Abs(h >> 2) % 4];
        var cloth = _p.Job switch { "worker" or "artisan" => new Color(0.3f, 0.35f, 0.45f), "militia" => new Color(0.3f, 0.4f, 0.3f), "merchant" => new Color(0.5f, 0.35f, 0.25f), _ => new Color(0.4f, 0.45f, 0.35f) };
        DrawRect(new Rect2(0, 0, s, s), new Color(0.04f, 0.1f, 0.22f));
        DrawRect(new Rect2(s * 0.2f, s * 0.68f, s * 0.6f, s * 0.32f), cloth);               // 肩
        DrawCircle(new Vector2(s / 2, s * 0.42f), s * 0.24f, skin);                          // 顔
        int style = Math.Abs(h >> 4) % 4;
        if (style == 0) DrawRect(new Rect2(s * 0.26f, s * 0.16f, s * 0.48f, s * 0.14f), hair);                        // 短髪
        else if (style == 1) { DrawRect(new Rect2(s * 0.24f, s * 0.16f, s * 0.52f, s * 0.14f), hair); DrawRect(new Rect2(s * 0.24f, s * 0.3f, s * 0.08f, s * 0.3f), hair); DrawRect(new Rect2(s * 0.68f, s * 0.3f, s * 0.08f, s * 0.3f), hair); }   // 長髪
        else if (style == 2) DrawCircle(new Vector2(s / 2, s * 0.26f), s * 0.2f, hair);                              // 丸い髪
        else DrawRect(new Rect2(s * 0.3f, s * 0.14f, s * 0.4f, s * 0.1f), hair);                                      // 薄い髪
        var eye = new Color(0.1f, 0.1f, 0.12f); float ey = s * 0.42f;
        if (_p.Ambition >= 7) { DrawLine(new Vector2(s * 0.38f, ey - 2), new Vector2(s * 0.46f, ey + 1), eye, 2); DrawLine(new Vector2(s * 0.62f, ey - 2), new Vector2(s * 0.54f, ey + 1), eye, 2); }   // 野心が高い: つり目
        else { DrawCircle(new Vector2(s * 0.42f, ey), s * 0.03f, eye); DrawCircle(new Vector2(s * 0.58f, ey), s * 0.03f, eye); }
        var mouth = _p.Loyalty >= 7 ? new Color(0.7f, 0.3f, 0.3f) : new Color(0.5f, 0.3f, 0.3f);
        DrawLine(new Vector2(s * 0.44f, s * 0.54f), new Vector2(s * 0.56f, s * 0.54f + (_p.Discontent >= 6 ? -s * 0.03f : 0)), mouth, 1.5f);   // 不満が高い: への字
    }
}
