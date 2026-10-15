"""設備(工場の機械)を 1 セル角(1 m)・高さ 1〜2 セルで生成し GLB に出力する。
実行: blender.exe -b -P tools/blender/make_equipment.py -- <出力フォルダ> <equipment.json>
原点はセルの床の中心。+Y が設備の正面(出口の向き)。材質名は M_* で Godot が差し替える。
種類: furnace(炉・高さ 2) / assembler(組立機・高さ 2) / miner(採掘機・高さ 2) / belt(ベルト・直線) / belt_turn(ベルト・曲がり)
      / inserter(インサータ・高さ 1) / chest(箱・高さ 1) / tank(タンク・高さ 2) / pole(電柱・高さ 2)
"""
import bpy
import json
import math
import sys
from pathlib import Path

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = Path(args[0]) if args else Path("out")
JSON_OUT = Path(args[1]) if len(args) > 1 else OUT / "equipment.json"
OUT.mkdir(parents=True, exist_ok=True)
ENTRIES = []
M = {}


def material(name, color, metallic=0.0, rough=0.8, emit=None, strength=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Metallic"].default_value = metallic
    b.inputs["Roughness"].default_value = rough
    if emit:
        b.inputs["Emission Color"].default_value = (*emit, 1)
        b.inputs["Emission Strength"].default_value = strength
    return m


def clear():
    global M
    bpy.ops.wm.read_factory_settings(use_empty=True)
    M = {
        "steel": material("M_Steel", (0.6, 0.62, 0.66), 0.9, 0.5),
        "paintdark": material("M_PaintDark", (0.2, 0.22, 0.25), 0.4, 0.6),
        "paint": material("M_Paint", (0.5, 0.55, 0.6), 0.4, 0.6),
        "rust": material("M_Rust", (0.45, 0.3, 0.2), 0.6, 0.8),
        "rubber": material("M_Rubber", (0.08, 0.08, 0.09), 0, 0.95),
        "hazard": material("M_Hazard", (0.9, 0.7, 0.1), 0.2, 0.7),
        "grate": material("M_Grate", (0.3, 0.32, 0.35), 0.7, 0.7),
        "concrete": material("M_Concrete", (0.5, 0.5, 0.5), 0, 0.9),
        "glowcyan": material("M_GlowCyan", (0.25, 0.85, 1), 0, 0.3, (0.25, 0.85, 1), 4),
        "gloworange": material("M_GlowOrange", (1, 0.5, 0.1), 0, 0.3, (1, 0.5, 0.1), 4),
        "glowred": material("M_GlowRed", (1, 0.2, 0.1), 0, 0.3, (1, 0.2, 0.1), 4),
        "glass": material("M_Glass", (0.05, 0.08, 0.12), 0.6, 0.15),
    }


def box(name, center, size, mat, bevel=0.015, rot=None):
    bpy.ops.mesh.primitive_cube_add(size=1, location=center)
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    if rot:
        o.rotation_euler = rot
    o.data.materials.append(mat)
    if bevel > 0:
        b = o.modifiers.new("bevel", "BEVEL"); b.width = bevel; b.segments = 2; b.limit_method = "ANGLE"; b.angle_limit = math.radians(40)
    return o


def cyl(name, center, r, h, mat, verts=20, axis="Z", r2=None):
    if r2 is None:
        bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=h, location=center)
    else:
        bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r, radius2=r2, depth=h, location=center)
    o = bpy.context.active_object
    o.name = name
    if axis == "X":
        o.rotation_euler = (0, math.radians(90), 0)
    elif axis == "Y":
        o.rotation_euler = (math.radians(90), 0, 0)
    o.data.materials.append(mat)
    for p in o.data.polygons:
        p.use_smooth = True
    return o


def frame_base(h=0.08):
    """共通の台座: 鋼の縁と格子の床。"""
    box("base", (0, 0, h / 2), (0.96, 0.96, h), M["paintdark"], 0.01)
    box("base_grate", (0, 0, h + 0.005), (0.86, 0.86, 0.01), M["grate"], 0)


def export(eid, h, meta=None):
    for o in bpy.context.scene.objects:
        o.select_set(True)
    path = OUT / f"{eid}.glb"
    bpy.ops.export_scene.gltf(filepath=str(path), export_format="GLB", use_selection=True, export_apply=True, export_yup=True)
    ENTRIES.append({"id": eid, "h": h, **(meta or {})})
    print("exported", path)


def furnace():
    clear()
    frame_base()
    # 本体: 下が広い炉体、前面に炉口(発光)、上に煙突と配管
    box("body", (0, 0, 0.08 + 0.7), (0.84, 0.8, 1.4), M["rust"], 0.03)
    box("body_band", (0, 0, 0.08 + 0.5), (0.9, 0.86, 0.12), M["steel"], 0.01)
    box("body_band2", (0, 0, 0.08 + 1.1), (0.9, 0.86, 0.12), M["steel"], 0.01)
    box("mouth_frame", (0, 0.42, 0.08 + 0.45), (0.5, 0.06, 0.4), M["paintdark"], 0.01)
    box("mouth_glow", (0, 0.44, 0.08 + 0.45), (0.38, 0.02, 0.28), M["gloworange"], 0)
    box("mouth_grate", (0, 0.46, 0.08 + 0.45), (0.38, 0.01, 0.02), M["rubber"], 0)
    cyl("stack", (-0.2, -0.15, 0.08 + 1.4 + 0.2), 0.12, 0.5, M["steel"], 16)
    cyl("stack_top", (-0.2, -0.15, 0.08 + 1.4 + 0.46), 0.14, 0.05, M["rust"], 16)
    cyl("stack_glow", (-0.2, -0.15, 0.08 + 1.4 + 0.49), 0.08, 0.02, M["gloworange"], 16)
    cyl("pipe", (0.25, -0.25, 0.08 + 1.55), 0.05, 0.3, M["rust"], 10)
    cyl("pipe2", (0.25, -0.1, 0.08 + 1.7), 0.05, 0.3, M["rust"], 10, axis="Y")
    box("panel", (0.3, 0.42, 0.08 + 0.95), (0.2, 0.03, 0.14), M["paintdark"], 0)
    box("panel_glow", (0.3, 0.44, 0.08 + 0.95), (0.14, 0.01, 0.06), M["glowcyan"], 0)
    export("furnace", 2)


def assembler():
    clear()
    frame_base()
    # 本体: 角張った箱、上面に透明な覆いと腕、前面に窓と表示
    box("body", (0, 0, 0.08 + 0.55), (0.86, 0.86, 1.1), M["paint"], 0.03)
    box("body_trim", (0, 0, 0.08 + 1.1), (0.9, 0.9, 0.06), M["paintdark"], 0.01)
    box("hood", (0, 0, 0.08 + 1.45), (0.7, 0.7, 0.6), M["glass"], 0.02)
    box("hood_frame", (0, 0, 0.08 + 1.75), (0.76, 0.76, 0.06), M["steel"], 0.01)
    cyl("arm_base", (0, 0, 0.08 + 1.2), 0.1, 0.2, M["steel"], 12)
    box("arm", (0.12, 0, 0.08 + 1.45), (0.3, 0.06, 0.06), M["steel"], 0, rot=(0, math.radians(-30), 0))
    box("arm2", (0.26, 0, 0.08 + 1.58), (0.06, 0.06, 0.26), M["rust"], 0)
    box("window", (0, 0.44, 0.08 + 0.75), (0.5, 0.01, 0.3), M["glass"], 0)
    box("window_glow", (0, 0.445, 0.08 + 0.6), (0.5, 0.005, 0.02), M["glowcyan"], 0)
    box("display", (-0.25, 0.44, 0.08 + 0.3), (0.22, 0.01, 0.16), M["paintdark"], 0)
    box("display_glow", (-0.25, 0.445, 0.08 + 0.3), (0.18, 0.005, 0.1), M["glowcyan"], 0)
    box("hazard", (0, 0, 0.08 + 1.13), (0.92, 0.1, 0.02), M["hazard"], 0)
    export("assembler", 2)


def miner():
    clear()
    frame_base(0.06)
    # 骨組みの櫓、中央にドリル(円錐)、上に動力部、側面に排出口
    for sx in (-1, 1):
        for sy in (-1, 1):
            box("leg", (sx * 0.38, sy * 0.38, 0.06 + 0.7), (0.08, 0.08, 1.4), M["steel"], 0)
    box("frame_top", (0, 0, 0.06 + 1.42), (0.86, 0.86, 0.08), M["steel"], 0.01)
    box("frame_mid", (0, 0, 0.06 + 0.7), (0.86, 0.08, 0.08), M["steel"], 0)
    box("head", (0, 0, 0.06 + 1.75), (0.7, 0.6, 0.55), M["paint"], 0.03)
    box("head_hazard", (0, 0, 0.06 + 1.5), (0.72, 0.62, 0.05), M["hazard"], 0)
    cyl("drill", (0, 0, 0.06 + 0.75), 0.22, 1.3, M["rust"], 16, r2=0.04)
    cyl("drill_collar", (0, 0, 0.06 + 1.35), 0.26, 0.14, M["steel"], 16)
    box("chute", (0, 0.4, 0.06 + 1.1), (0.3, 0.3, 0.25), M["paintdark"], 0.01, rot=(math.radians(-25), 0, 0))
    box("chute_glow", (0, 0.55, 0.06 + 1.02), (0.22, 0.02, 0.06), M["gloworange"], 0)
    cyl("exhaust", (-0.25, -0.2, 0.06 + 2.1), 0.05, 0.25, M["rust"], 10)
    export("miner", 2)


def belt(turn=False):
    clear()
    # 低い枠、2 本の側壁、走行面(暗い)、進行方向の発光矢印
    box("frame", (0, 0, 0.06), (0.96, 0.96, 0.12), M["paintdark"], 0.01)
    box("surface", (0, 0, 0.125), (0.72, 0.96, 0.01), M["rubber"], 0)
    box("rail_l", (-0.42, 0, 0.14), (0.08, 0.96, 0.16), M["steel"], 0.01)
    box("rail_r", (0.42, 0, 0.14), (0.08, 0.96, 0.16), M["steel"], 0.01)
    for i in range(3):
        y = -0.3 + i * 0.3
        box(f"arrow{i}", (-0.09, y, 0.132), (0.2, 0.03, 0.004), M["glowcyan"], 0, rot=(0, 0, math.radians(45)))
        box(f"arrow{i}b", (0.09, y, 0.132), (0.2, 0.03, 0.004), M["glowcyan"], 0, rot=(0, 0, math.radians(-45)))
    for sy in (-1, 1):
        cyl("roller", (0, sy * 0.44, 0.1), 0.05, 0.8, M["steel"], 10, axis="X")
    if turn:
        # 曲がり: -X から入って +Y へ出る。外側の壁を丸く見せる
        box("corner_wall", (0.42, -0.42, 0.14), (0.08, 0.96, 0.16), M["steel"], 0.01, rot=(0, 0, math.radians(90)))
    export("belt_turn" if turn else "belt", 1, {"flat": True})


def inserter():
    clear()
    frame_base(0.06)
    cyl("pivot", (0, 0, 0.06 + 0.15), 0.14, 0.3, M["paintdark"], 14)
    cyl("pivot_ring", (0, 0, 0.06 + 0.3), 0.16, 0.04, M["hazard"], 14)
    box("arm1", (0, 0.1, 0.06 + 0.55), (0.08, 0.08, 0.6), M["steel"], 0, rot=(math.radians(-20), 0, 0))
    box("arm2", (0, 0.35, 0.06 + 0.72), (0.08, 0.5, 0.08), M["steel"], 0, rot=(math.radians(25), 0, 0))
    box("hand", (0, 0.56, 0.06 + 0.58), (0.2, 0.12, 0.12), M["rust"], 0.01)
    box("hand_glow", (0, 0.56, 0.06 + 0.51), (0.14, 0.08, 0.01), M["glowcyan"], 0)
    box("counter", (0, -0.3, 0.06 + 0.12), (0.3, 0.3, 0.24), M["paintdark"], 0.01)
    export("inserter", 1)


def chest():
    clear()
    frame_base(0.06)
    box("body", (0, 0, 0.06 + 0.4), (0.84, 0.84, 0.8), M["paint"], 0.03)
    box("lid", (0, 0, 0.06 + 0.84), (0.88, 0.88, 0.08), M["paintdark"], 0.01)
    box("band", (0, 0, 0.06 + 0.4), (0.88, 0.1, 0.84), M["steel"], 0.01)
    box("band2", (0, 0, 0.06 + 0.4), (0.1, 0.88, 0.84), M["steel"], 0.01)
    box("label", (0, 0.43, 0.06 + 0.5), (0.3, 0.01, 0.16), M["paintdark"], 0)
    box("label_glow", (0, 0.435, 0.06 + 0.5), (0.24, 0.005, 0.08), M["glowcyan"], 0)
    export("chest", 1)


def tank():
    clear()
    frame_base(0.06)
    cyl("tank", (0, 0, 0.06 + 0.95), 0.4, 1.7, M["paint"], 24)
    cyl("tank_top", (0, 0, 0.06 + 1.82), 0.4, 0.06, M["paintdark"], 24)
    for z in (0.5, 1.0, 1.5):
        cyl(f"ring{int(z*10)}", (0, 0, 0.06 + z), 0.42, 0.05, M["steel"], 24)
    box("ladder", (0.42, 0, 0.06 + 0.9), (0.04, 0.2, 1.5), M["steel"], 0)
    box("gauge", (0, 0.41, 0.06 + 0.9), (0.06, 0.02, 1.0), M["glass"], 0)
    box("gauge_glow", (0, 0.42, 0.06 + 0.6), (0.04, 0.01, 0.4), M["glowcyan"], 0)
    cyl("valve", (0, 0.45, 0.06 + 0.25), 0.05, 0.2, M["rust"], 10, axis="Y")
    export("tank", 2)


def pole():
    clear()
    box("base", (0, 0, 0.06), (0.4, 0.4, 0.12), M["concrete"], 0.01)
    cyl("mast", (0, 0, 1.0), 0.05, 1.9, M["steel"], 10)
    box("cross", (0, 0, 1.85), (0.7, 0.06, 0.06), M["steel"], 0)
    for sx in (-1, 1):
        cyl(f"insulator{sx+1}", (sx * 0.3, 0, 1.93), 0.04, 0.1, M["glowcyan"], 8)
    export("pole", 2)


if __name__ == "__main__":
    furnace(); assembler(); miner(); belt(); belt(True); inserter(); chest(); tank(); pole()
    JSON_OUT.parent.mkdir(parents=True, exist_ok=True)
    JSON_OUT.write_text(json.dumps({"cell": 1.0, "equipment": ENTRIES}, ensure_ascii=False, indent=1), encoding="utf-8")
    print("equipment:", len(ENTRIES))
