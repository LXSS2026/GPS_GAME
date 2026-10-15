"""設備とアイテムのアイコン(自作 SVG、64x64)を game/assets/icons に書く。G8 の配色(濃紺・薄青の線・水色の光)。
実行: python tools/gen_icons.py
"""
import json
from pathlib import Path

OUT = Path(__file__).resolve().parent.parent / "game" / "assets" / "icons"
OUT.mkdir(parents=True, exist_ok=True)
LINE, FILL, GLOW, DIM, WARM = "#9fd8ff", "#10224a", "#38c6ff", "#5a7390", "#ff9a3c"
GEN = {0: "#ff3326", 1: "#ff8019", 2: "#ffe633", 3: "#99ff33", 4: "#33ff66", 5: "#4080ff", 6: "#bf59ff"}


def svg(body, w=64, h=64):
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w} {h}" width="{w}" height="{h}" fill="none" stroke="{LINE}" stroke-width="2.5" stroke-linejoin="round" stroke-linecap="round">{body}</svg>'


def write(name, body):
    (OUT / f"{name}.svg").write_text(svg(body), encoding="utf-8")


# ---------- 設備 ----------
write("belt", f'<rect x="8" y="22" width="48" height="20" rx="3" fill="{FILL}"/><path d="M14 32h10m8 0h10m8 0h6" stroke="{DIM}"/>'
              f'<path d="M22 26l6 6-6 6M40 26l6 6-6 6" stroke="{GLOW}"/>')
write("inserter", f'<rect x="20" y="46" width="24" height="10" rx="2" fill="{FILL}"/><circle cx="32" cy="42" r="5" fill="{FILL}"/>'
                  f'<path d="M32 42L24 22"/><path d="M24 22L44 16"/><path d="M44 16l4 6-8 2z" fill="{GLOW}" stroke="{GLOW}"/>')
write("miner", f'<path d="M14 54V22h36v32" fill="{FILL}"/><path d="M14 22l18-10 18 10"/><path d="M32 26v10"/><path d="M26 36h12l-6 14z" fill="{WARM}" stroke="{WARM}"/>'
               f'<path d="M14 54h36" stroke="{DIM}"/>')
write("chest", f'<rect x="10" y="20" width="44" height="34" rx="3" fill="{FILL}"/><path d="M10 30h44" /><rect x="27" y="30" width="10" height="8" fill="{GLOW}" stroke="none"/>')
write("furnace", f'<path d="M16 56V26l16-12 16 12v30z" fill="{FILL}"/><rect x="24" y="36" width="16" height="12" rx="2" fill="{WARM}" stroke="{WARM}"/>'
                 f'<path d="M26 14v-6h6v6" stroke="{DIM}"/><path d="M16 56h32"/>')
write("assembler", f'<rect x="12" y="24" width="40" height="32" rx="3" fill="{FILL}"/><rect x="18" y="10" width="28" height="14" rx="3"/><circle cx="32" cy="40" r="8" stroke="{GLOW}"/>'
                   f'<path d="M32 32v-4M32 48v4M24 40h-4M40 40h4" stroke="{GLOW}"/>')
write("assembler_l3", f'<rect x="8" y="20" width="48" height="36" rx="3" fill="{FILL}"/><rect x="14" y="8" width="36" height="12" rx="3"/><circle cx="24" cy="38" r="7" stroke="{GLOW}"/><circle cx="40" cy="38" r="7" stroke="{GLOW}"/>'
                      f'<path d="M20 52h24" stroke="{GLOW}"/>')
write("tank", f'<path d="M18 16h28v34a14 6 0 0 1-28 0z" fill="{FILL}"/><ellipse cx="32" cy="16" rx="14" ry="6"/><path d="M18 28a14 6 0 0 0 28 0M18 40a14 6 0 0 0 28 0" stroke="{DIM}"/>'
              f'<rect x="30" y="22" width="4" height="22" fill="{GLOW}" stroke="none"/>')
write("pipe", f'<path d="M6 36h22a6 6 0 0 0 6-6V14" stroke-width="8" stroke="{DIM}"/><path d="M6 36h22a6 6 0 0 0 6-6V14" stroke-width="4"/>'
              f'<path d="M34 44v8h-8M58 36h-14" stroke-width="4"/><rect x="40" y="30" width="6" height="12" fill="{FILL}"/>')
write("pump", f'<rect x="12" y="30" width="40" height="22" rx="4" fill="{FILL}"/><circle cx="32" cy="41" r="7" stroke="{GLOW}"/><path d="M32 30V12M26 18l6-6 6 6"/>'
              f'<path d="M52 41h8" stroke-width="4"/>')
write("port_lift", f'<rect x="16" y="8" width="32" height="48" rx="3" fill="{FILL}"/><path d="M24 46h16v-14H24z" stroke="{GLOW}"/><path d="M32 14v6M28 18l4-4 4 4M32 30v-4" />'
                   f'<path d="M20 50h24" stroke="{DIM}"/>')
write("port_bridge", f'<rect x="4" y="22" width="18" height="20" fill="{FILL}"/><rect x="42" y="22" width="18" height="20" fill="{FILL}"/><rect x="22" y="26" width="20" height="12" fill="{FILL}"/>'
                     f'<path d="M26 32h12" stroke="{GLOW}"/><path d="M34 29l4 3-4 3" stroke="{GLOW}"/>')
for g, c in GEN.items():
    write(f"reactor_g{g}", f'<rect x="16" y="14" width="32" height="42" rx="4" fill="{FILL}"/><circle cx="32" cy="34" r="11" stroke="{c}"/><circle cx="32" cy="34" r="5" fill="{c}" stroke="none"/>'
                           f'<path d="M22 14v-6h20v6" stroke="{DIM}"/><text x="32" y="60" font-size="9" fill="{c}" stroke="none" text-anchor="middle" font-family="sans-serif">G{g}</text>')

# ---------- アイテム ----------
def ore(name, c):
    write(name, f'<path d="M14 44l8-18 10-6 14 8 6 16-10 8H22z" fill="{FILL}"/><path d="M22 26l10 6 14-8M32 32l-2 20" stroke="{DIM}"/><circle cx="30" cy="36" r="3" fill="{c}" stroke="none"/><circle cx="40" cy="42" r="2.5" fill="{c}" stroke="none"/>')
ore("iron_ore", "#c9d2dc"); ore("copper_ore", "#e0905a")
write("stone", f'<path d="M14 42l6-16 14-8 16 10 2 14-12 8H22z" fill="{FILL}"/><path d="M20 26l14 6 16-8" stroke="{DIM}"/>')
write("coal", f'<path d="M16 40l6-14 12-8 14 8 2 14-10 8H24z" fill="#0a1226"/><path d="M22 26l12 6 14-8" stroke="{DIM}"/><circle cx="36" cy="38" r="2" fill="{WARM}" stroke="none"/>')
write("wood", f'<rect x="10" y="26" width="44" height="14" rx="7" fill="#3a2a1a"/><ellipse cx="54" cy="33" rx="4" ry="7" fill="#6b4a2b"/><path d="M18 30h20M18 36h14" stroke="{DIM}"/>')
write("water", f'<path d="M32 10c8 12 14 20 14 28a14 14 0 0 1-28 0c0-8 6-16 14-28z" fill="{FILL}" stroke="{GLOW}"/><path d="M26 40a6 6 0 0 0 6 6" stroke="{GLOW}"/>')
def plate(name, c):
    write(name, f'<path d="M10 24l18-8 26 10-18 8z" fill="{c}" stroke="{LINE}"/><path d="M10 24v12l26 10 18-8V26" fill="{FILL}"/><path d="M36 34v12" stroke="{DIM}"/>')
plate("iron_plate", "#8a98a8"); plate("copper_plate", "#c27a4a"); plate("steel", "#5c6c80"); plate("armor_plate", "#3c4a5c"); plate("alloy", "#8fa0c0")
write("concrete", f'<rect x="12" y="20" width="40" height="26" rx="2" fill="#4a5058"/><path d="M12 33h40M32 20v26" stroke="{DIM}"/>')
write("plank", f'<rect x="8" y="24" width="48" height="8" rx="2" fill="#7a5a36"/><rect x="8" y="34" width="48" height="8" rx="2" fill="#6b4a2b"/>')
write("gear", f'<circle cx="32" cy="32" r="14" fill="{FILL}"/><circle cx="32" cy="32" r="5"/>' + "".join(f'<rect x="30" y="10" width="4" height="8" fill="{LINE}" stroke="none" transform="rotate({a} 32 32)"/>' for a in range(0, 360, 45)))
write("copper_wire", f'<circle cx="32" cy="32" r="16" fill="{FILL}" stroke="#c27a4a"/><circle cx="32" cy="32" r="10" stroke="#c27a4a"/><circle cx="32" cy="32" r="4" stroke="#c27a4a"/><path d="M48 32l8-6" stroke="#c27a4a"/>')
write("circuit", f'<rect x="12" y="16" width="40" height="32" rx="3" fill="#0f2a1e"/><path d="M18 24h10v8h8v-8h10M18 40h28" stroke="{GLOW}"/><rect x="28" y="30" width="8" height="8" fill="{GLOW}" stroke="none"/>')
write("motor", f'<rect x="14" y="22" width="30" height="22" rx="4" fill="{FILL}"/><path d="M44 30h10v6H44" fill="{DIM}"/><path d="M20 22v-4h18v4" stroke="{DIM}"/><circle cx="29" cy="33" r="5" stroke="{GLOW}"/>')
write("reactor_core", f'<circle cx="32" cy="32" r="16" fill="{FILL}"/><circle cx="32" cy="32" r="8" stroke="{GLOW}"/><circle cx="32" cy="32" r="3" fill="{GLOW}" stroke="none"/><path d="M32 8v8M32 48v8M8 32h8M48 32h8" stroke="{DIM}"/>')
write("sensor", f'<rect x="18" y="28" width="28" height="20" rx="3" fill="{FILL}"/><circle cx="32" cy="38" r="6" stroke="{GLOW}"/><path d="M20 22a16 10 0 0 1 24 0M24 16a22 14 0 0 1 16 0" stroke="{GLOW}"/>')
write("mech_head", f'<path d="M20 20h24v20l-6 10H26l-6-10z" fill="{FILL}"/><rect x="24" y="30" width="16" height="4" fill="{GLOW}" stroke="none"/><path d="M26 20l-4-10M38 20l4-10"/>')
write("mech_torso", f'<path d="M16 16h32l-4 20H20z" fill="{FILL}"/><path d="M22 36h20v12H22z" fill="{FILL}"/><circle cx="32" cy="28" r="4" fill="{GLOW}" stroke="none"/>')
write("mech_arm", f'<circle cx="20" cy="18" r="6" fill="{FILL}"/><path d="M20 24l6 14" stroke-width="7"/><path d="M26 38l12 12" stroke-width="6"/><rect x="36" y="48" width="12" height="8" rx="2" fill="{FILL}"/>')
write("mech_leg", f'<rect x="24" y="8" width="14" height="22" rx="3" fill="{FILL}"/><rect x="26" y="30" width="12" height="18" rx="3" fill="{FILL}"/><path d="M20 56h22l-4-8H26z" fill="{FILL}"/>')
write("mech_back", f'<rect x="22" y="14" width="20" height="30" rx="3" fill="{FILL}"/><path d="M22 20L6 36M42 20l16 16" stroke="{GLOW}"/><circle cx="27" cy="50" r="4" stroke="{WARM}"/><circle cx="37" cy="50" r="4" stroke="{WARM}"/>')
write("mech_weapon", f'<path d="M8 36h34v-6h14v10H42v6H26l-4 8h-6l2-8h-10z" fill="{FILL}"/><path d="M42 33h12" stroke="{GLOW}"/>')
write("beam_emitter", f'<path d="M12 22h24l10 10-10 10H12z" fill="{FILL}"/><circle cx="30" cy="32" r="4" fill="{GLOW}" stroke="none"/><path d="M46 32h12M48 26l8-4M48 38l8 4" stroke="{GLOW}"/>')

# 索引(Godot 側は id.svg を直接読むので参考用)
(OUT / "index.json").write_text(json.dumps(sorted(p.stem for p in OUT.glob("*.svg")), ensure_ascii=False, indent=1), encoding="utf-8")
print("icons:", len(list(OUT.glob("*.svg"))))
