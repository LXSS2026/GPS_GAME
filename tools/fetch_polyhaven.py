"""Poly Haven (CC0) の PBR テクスチャを 1K JPG で取得する。
出力: game/assets/pbr/<asset>/{diffuse,normal,rough}.jpg
実行: python tools/fetch_polyhaven.py
"""
import json
import urllib.request
from pathlib import Path

ASSETS = ["metal_plate", "rusty_metal_02", "painted_metal_shutter", "concrete_wall_008", "concrete_floor_painted", "rusty_metal_grid"]
MAPS = {"diffuse": ["Diffuse", "diffuse"], "normal": ["nor_gl", "normal_gl"], "rough": ["Rough", "rough"]}
OUT = Path(__file__).resolve().parent.parent / "game" / "assets" / "pbr"


def fetch(url):
    req = urllib.request.Request(url, headers={"User-Agent": "GPS-fetch/1.0"})
    with urllib.request.urlopen(req, timeout=120) as r:
        return r.read()


def main():
    for a in ASSETS:
        files = json.loads(fetch(f"https://api.polyhaven.com/files/{a}"))
        d = OUT / a
        d.mkdir(parents=True, exist_ok=True)
        for name, keys in MAPS.items():
            entry = next((files[k] for k in keys if k in files), None)
            if entry is None:
                print(a, name, "missing", list(files)[:12]); continue
            url = entry["1k"]["jpg"]["url"]
            (d / f"{name}.jpg").write_bytes(fetch(url))
            print(a, name, "ok")
    (OUT / "LICENSE.txt").write_text("Textures from Poly Haven (https://polyhaven.com), CC0 1.0 Universal.\n" + "\n".join(ASSETS) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
