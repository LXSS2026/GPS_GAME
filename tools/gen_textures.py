"""地形ブロック用の 32px テクスチャをアトラス(PNG)として生成する。
出力: game/assets/textures/atlas.png, atlas.json(名前 → 番地)。
実行: python tools/gen_textures.py
"""
import json
import random
import struct
import zlib
from pathlib import Path

T = 32
COLS = 8
OUT = Path(__file__).resolve().parent.parent / "game" / "assets" / "textures"


def hexrgb(h):
    return (int(h[1:3], 16), int(h[3:5], 16), int(h[5:7], 16))


def clamp(v):
    return max(0, min(255, int(v)))


def mix(c, k):
    return tuple(clamp(v * k) for v in c)


def lerp(a, b, t):
    return tuple(clamp(a[i] + (b[i] - a[i]) * t) for i in range(3))


def value_noise(seed, cell):
    """滑らかな値ノイズ(0..1)。T x T、周期 T でタイル可能。"""
    r = random.Random(seed)
    n = T // cell
    grid = [[r.random() for _ in range(n)] for _ in range(n)]
    out = [[0.0] * T for _ in range(T)]
    for y in range(T):
        for x in range(T):
            gx, gy = x / cell, y / cell
            x0, y0 = int(gx) % n, int(gy) % n
            x1, y1 = (x0 + 1) % n, (y0 + 1) % n
            fx, fy = gx - int(gx), gy - int(gy)
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy)
            a, b, c, d = grid[y0][x0], grid[y0][x1], grid[y1][x0], grid[y1][x1]
            out[y][x] = (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy
    return out


def tex(base, seed, amp=0.12, cell=8, detail=0.04):
    n1, n2 = value_noise(seed, cell), value_noise(seed + 1, 2)
    c = hexrgb(base)
    return [[mix(c, 1 + (n1[y][x] - 0.5) * 2 * amp + (n2[y][x] - 0.5) * 2 * detail) for x in range(T)] for y in range(T)]


def grass():
    rows = tex("#4f5c3a", 1, 0.10, 8)
    r = random.Random(2)
    for _ in range(24):  # 枯れた葉先
        x, y = r.randrange(T), r.randrange(T)
        rows[y][x] = lerp(rows[y][x], hexrgb("#7a7f4a"), 0.6)
    return rows


def grass_side():
    rows = dirt()
    g = grass()
    r = random.Random(3)
    for x in range(T):
        depth = 6 + r.randrange(3)
        for y in range(depth):
            rows[y][x] = g[y][x]
        rows[depth][x] = lerp(rows[depth][x], hexrgb("#3a4530"), 0.6)
    return rows


def dirt():
    rows = tex("#4a3626", 4, 0.12, 8)
    r = random.Random(5)
    for _ in range(14):  # 小石
        x, y = r.randrange(T), r.randrange(T)
        for dx in range(2):
            for dy in range(2):
                rows[(y + dy) % T][(x + dx) % T] = hexrgb("#2e2118") if (dx + dy) else hexrgb("#6a5240")
    return rows


def stone():
    rows = tex("#50555b", 6, 0.10, 8)
    r = random.Random(7)
    for _ in range(4):  # ひび
        x, y = r.randrange(T), r.randrange(T)
        for _ in range(10):
            rows[y % T][x % T] = hexrgb("#2b2e32")
            x += r.choice((0, 1, 1)); y += r.choice((0, 0, 1))
    return rows


def ore():
    rows = stone()
    r = random.Random(8)
    for _ in range(7):
        x, y = r.randrange(2, T - 3), r.randrange(2, T - 3)
        for dx in range(3):
            for dy in range(3):
                if (dx, dy) in ((0, 0), (2, 2), (0, 2), (2, 0)):
                    continue
                rows[y + dy][x + dx] = hexrgb("#d4701e") if (dx + dy) % 2 else hexrgb("#8f4a14")
    return rows


def sand():
    return tex("#7d745c", 9, 0.06, 8)


def water():
    rows = tex("#1d3a4c", 10, 0.05, 8)
    n = value_noise(11, 4)
    for y in range(T):
        for x in range(T):
            if n[y][x] > 0.68:
                rows[y][x] = lerp(rows[y][x], hexrgb("#4f8aa6"), 0.6)
    return rows


def asphalt():
    return tex("#202327", 12, 0.04, 4, 0.03)


def road_x():  # x 方向の車線(白の破線)
    rows = asphalt()
    for x in range(T):
        if x % 12 < 7:
            for y in (15, 16):
                rows[y][x] = hexrgb("#8f9aa6")
    return rows


def road_y():
    rows = asphalt()
    for y in range(T):
        if y % 12 < 7:
            for x in (15, 16):
                rows[y][x] = hexrgb("#8f9aa6")
    return rows


def concrete():
    return tex("#565c63", 13, 0.04, 8, 0.02)


def plaza():
    rows = tex("#454b53", 14, 0.03, 8, 0.02)
    for i in range(T):
        rows[0][i] = hexrgb("#2a2f35"); rows[16][i] = hexrgb("#2a2f35")
        rows[i][0] = hexrgb("#2a2f35"); rows[i][16] = hexrgb("#2a2f35")
    for i in range(2, 6):  # 金属板の鋲
        for j in range(2, 6):
            rows[i][j] = hexrgb("#6a727b"); rows[i + 16][j + 16] = hexrgb("#6a727b")
    return rows


def field():
    rows = tex("#3a2a1c", 15, 0.08, 8)
    for y in range(T):
        for x in range(T):
            if x % 8 in (2, 3):
                rows[y][x] = lerp(rows[y][x], hexrgb("#4f7a2e"), 0.8)
            elif x % 8 == 6:
                rows[y][x] = lerp(rows[y][x], hexrgb("#241a10"), 0.6)
    return rows


def path():
    return tex("#6b6253", 16, 0.07, 8)


TEXTURES = [
    ("grass", grass), ("grass_side", grass_side), ("dirt", dirt), ("stone", stone), ("ore", ore), ("sand", sand), ("water", water),
    ("asphalt", asphalt), ("road_x", road_x), ("road_y", road_y), ("concrete", concrete), ("plaza", plaza), ("field", field), ("path", path),
]


def png(width, height, rows):
    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
    raw = b"".join(b"\x00" + bytes(v for px in row for v in px) for row in rows)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    rows_n = (len(TEXTURES) + COLS - 1) // COLS
    atlas = [[(0, 0, 0)] * (COLS * T) for _ in range(rows_n * T)]
    index = {}
    for i, (name, fn) in enumerate(TEXTURES):
        cx, cy = (i % COLS) * T, (i // COLS) * T
        for y, row in enumerate(fn()):
            atlas[cy + y][cx:cx + T] = row
        index[name] = i
    (OUT / "atlas.png").write_bytes(png(COLS * T, rows_n * T, atlas))
    (OUT / "atlas.json").write_text(json.dumps({"tile": T, "cols": COLS, "index": index}, ensure_ascii=False, indent=2), encoding="utf-8")
    print("atlas", COLS * T, "x", rows_n * T, len(TEXTURES), "textures")


if __name__ == "__main__":
    main()
