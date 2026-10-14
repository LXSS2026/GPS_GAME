"""人型(ロボット 5 世代・人間 2 種)を Blender で生成し GLB に出力する。
実行: blender.exe -b -P tools/blender/make_characters.py -- <出力フォルダ> <characters.json>
原点は足元の中心。Z が上(GLB で Y 上に変換)。正面は +Y。身長は約 1.75(精鋭型は 1.95)。
材質名は Godot 側で PBR 材質に差し替える(M_Armor, M_PaintDark, M_Rubber, M_Steel, M_Rust, M_Hazard, M_Paint, M_Glass, M_GlowCyan, M_GlowOrange, M_GlowRed, M_Skin, M_Cloth)。
"""
import bpy
import json
import math
import sys
from pathlib import Path

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = Path(args[0]) if args else Path("out")
JSON_OUT = Path(args[1]) if len(args) > 1 else OUT / "characters.json"
OUT.mkdir(parents=True, exist_ok=True)
ENTRIES = []
M = {}


def material(name, color, metallic=0.0, rough=0.8, emit=None, strength=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*color, 1)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = rough
    if emit:
        bsdf.inputs["Emission Color"].default_value = (*emit, 1)
        bsdf.inputs["Emission Strength"].default_value = strength
    return m


def build_materials():
    return {
        "armor": material("M_Armor", (0.3, 0.32, 0.36), 0.7, 0.6),
        "dark": material("M_PaintDark", (0.2, 0.22, 0.26), 0.3, 0.6),
        "paint": material("M_Paint", (0.35, 0.42, 0.52), 0.3, 0.6),
        "steel": material("M_Steel", (0.6, 0.62, 0.66), 0.9, 0.5),
        "rust": material("M_Rust", (0.5, 0.3, 0.18), 0.4, 0.8),
        "rubber": material("M_Rubber", (0.08, 0.08, 0.09), 0, 0.95),
        "hazard": material("M_Hazard", (0.85, 0.65, 0.1), 0, 0.7),
        "glass": material("M_Glass", (0.05, 0.08, 0.12), 0.6, 0.15),
        "glowcyan": material("M_GlowCyan", (0.25, 0.85, 1), 0, 0.3, (0.25, 0.85, 1), 6),
        "gloworange": material("M_GlowOrange", (1, 0.45, 0.1), 0, 0.3, (1, 0.45, 0.1), 6),
        "glowred": material("M_GlowRed", (1, 0.15, 0.1), 0, 0.3, (1, 0.15, 0.1), 6),
        "skin": material("M_Skin", (0.75, 0.6, 0.5), 0, 0.7),
        "cloth": material("M_Cloth", (0.25, 0.27, 0.3), 0, 0.9),
    }


def clear():
    global M
    bpy.ops.wm.read_factory_settings(use_empty=True)
    M = build_materials()


# ---------- 幾何 ----------
def box_mesh_data(boxes):
    verts, faces = [], []
    for (cx, cy, cz), (sx, sy, sz) in boxes:
        b = len(verts)
        for dx in (-1, 1):
            for dy in (-1, 1):
                for dz in (-1, 1):
                    verts.append((cx + dx * sx / 2, cy + dy * sy / 2, cz + dz * sz / 2))
        faces += [(b + 0, b + 1, b + 3, b + 2), (b + 4, b + 6, b + 7, b + 5), (b + 0, b + 4, b + 5, b + 1),
                  (b + 2, b + 3, b + 7, b + 6), (b + 0, b + 2, b + 6, b + 4), (b + 1, b + 5, b + 7, b + 3)]
    return verts, faces


def add_boxes(name, boxes, mat, bevel=0.012, rot=None):
    verts, faces = box_mesh_data(boxes)
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.update()
    obj = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(mat)
    if rot:
        obj.rotation_euler = rot
    if bevel > 0:
        b = obj.modifiers.new("bevel", "BEVEL")
        b.width = bevel
        b.segments = 2
        b.limit_method = "ANGLE"
        b.angle_limit = math.radians(40)
    return obj


def add_box(name, center, size, mat, bevel=0.012, rot=None):
    return add_boxes(name, [(center, size)], mat, bevel, rot)


def add_cyl(name, center, r, h, mat, verts=16, axis="Z", r2=None, bevel=0.006):
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r, radius2=r if r2 is None else r2, depth=h, location=center)
    obj = bpy.context.active_object
    obj.name = name
    if axis == "X":
        obj.rotation_euler = (0, math.radians(90), 0)
    elif axis == "Y":
        obj.rotation_euler = (math.radians(90), 0, 0)
    obj.data.materials.append(mat)
    if bevel > 0:
        b = obj.modifiers.new("bevel", "BEVEL")
        b.width = bevel
        b.segments = 2
        b.limit_method = "ANGLE"
        b.angle_limit = math.radians(40)
    return obj


def add_sphere(name, center, r, mat, seg=12):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=r, location=center)
    obj = bpy.context.active_object
    obj.name = name
    obj.data.materials.append(mat)
    return obj


def export(cid, kind, height):
    for o in bpy.context.scene.objects:
        o.select_set(True)
    path = OUT / f"{cid}.glb"
    bpy.ops.export_scene.gltf(filepath=str(path), export_format="GLB", use_selection=True, export_apply=True, export_yup=True)
    ENTRIES.append({"id": cid, "kind": kind, "height": height})
    print("exported", path)


# ---------- 人型の骨格(共通)。spec で部位の寸法と装備を変える ----------
def humanoid(cid, kind, spec):
    clear()
    s = spec
    H = s.get("height", 1.75)
    k = H / 1.75                      # 身長に対する倍率
    armor = M[s.get("armor", "armor")]
    inner = M[s.get("inner", "dark")]
    joint = M[s.get("joint", "rubber")]
    accent = M[s.get("accent", "steel")]
    glow = M[s.get("glow", "glowcyan")]
    bulk = s.get("bulk", 1.0)          # 胴と四肢の太さ
    plate = s.get("plate", True)       # 装甲板を重ねるか
    exposed = s.get("exposed", False)  # 骨組みと駆動部を露出(試作機)
    # 寸法(Z は足元から)
    foot_h = 0.07 * k
    shin_h, thigh_h = 0.32 * k, 0.38 * k
    pelvis_h = 0.14 * k
    torso_h = 0.55 * k
    neck_h = 0.06 * k
    head_h = 0.24 * k
    z_hip = foot_h + shin_h + thigh_h
    z_chest = z_hip + pelvis_h
    z_neck = z_chest + torso_h
    z_head = z_neck + neck_h
    shoulder_w = 0.56 * k * bulk
    # 脚
    for sx in (-1, 1):
        x = sx * 0.11 * k
        add_box(f"foot{sx+1}", (x, 0.04 * k, foot_h / 2), (0.15 * k * bulk, 0.3 * k, foot_h), inner, 0.01)
        add_box(f"toe{sx+1}", (x, 0.17 * k, foot_h * 0.6), (0.13 * k * bulk, 0.08 * k, foot_h * 0.8), armor, 0.008)
        add_box(f"shin{sx+1}", (x, 0, foot_h + shin_h / 2), (0.13 * k * bulk, 0.15 * k * bulk, shin_h), inner, 0.01)
        if plate:
            add_box(f"shin_plate{sx+1}", (x, 0.07 * k, foot_h + shin_h * 0.55), (0.15 * k * bulk, 0.05 * k, shin_h * 0.8), armor, 0.01)
        add_cyl(f"knee{sx+1}", (x, 0, foot_h + shin_h), 0.085 * k * bulk, 0.17 * k * bulk, joint, 12, axis="X")
        add_box(f"thigh{sx+1}", (x, 0, foot_h + shin_h + thigh_h / 2), (0.16 * k * bulk, 0.17 * k * bulk, thigh_h), inner, 0.012)
        if plate:
            add_box(f"thigh_plate{sx+1}", (x, 0.08 * k, foot_h + shin_h + thigh_h * 0.5), (0.18 * k * bulk, 0.05 * k, thigh_h * 0.75), armor, 0.012)
            add_box(f"thigh_side{sx+1}", (x + sx * 0.09 * k * bulk, 0, foot_h + shin_h + thigh_h * 0.5), (0.04 * k, 0.16 * k * bulk, thigh_h * 0.7), armor, 0.01)
        if exposed:
            add_cyl(f"piston{sx+1}", (x + sx * 0.08 * k, -0.08 * k, foot_h + shin_h * 0.5 + thigh_h * 0.3), 0.02 * k, thigh_h * 0.9, M["steel"], 8)
    # 骨盤と胴
    add_box("pelvis", (0, 0, z_hip + pelvis_h / 2), (0.34 * k * bulk, 0.22 * k * bulk, pelvis_h), inner, 0.012)
    add_box("belt", (0, 0, z_hip + pelvis_h - 0.02 * k), (0.36 * k * bulk, 0.24 * k * bulk, 0.04 * k), accent, 0.006)
    add_box("abdomen", (0, 0, z_chest + torso_h * 0.18), (0.3 * k * bulk, 0.2 * k * bulk, torso_h * 0.36), joint, 0.01)
    add_box("chest", (0, 0, z_chest + torso_h * 0.66), (0.42 * k * bulk, 0.26 * k * bulk, torso_h * 0.62), inner, 0.014)
    if plate:
        add_box("chest_plate", (0, 0.14 * k * bulk, z_chest + torso_h * 0.68), (0.38 * k * bulk, 0.06 * k, torso_h * 0.5), armor, 0.014)
        add_box("back_plate", (0, -0.14 * k * bulk, z_chest + torso_h * 0.66), (0.38 * k * bulk, 0.05 * k, torso_h * 0.55), armor, 0.012)
        add_box("chest_seam", (0, 0.175 * k * bulk, z_chest + torso_h * 0.72), (0.02 * k, 0.01, torso_h * 0.3), glow, 0)
    if exposed:
        add_boxes("ribs", [((0, 0.12 * k, z_chest + torso_h * (0.45 + i * 0.12)), (0.36 * k, 0.03, 0.03)) for i in range(4)], M["rust"], 0)
        add_cyl("spine", (0, -0.1 * k, z_chest + torso_h * 0.5), 0.03 * k, torso_h * 0.9, M["steel"], 8)
    # 肩と腕
    for sx in (-1, 1):
        x = sx * shoulder_w / 2
        z_sh = z_neck - 0.05 * k
        add_sphere(f"shoulder_joint{sx+1}", (x, 0, z_sh), 0.07 * k * bulk, joint, 8)
        if plate:
            add_box(f"pauldron{sx+1}", (x + sx * 0.02 * k, 0, z_sh + 0.03 * k), (0.17 * k * bulk, 0.2 * k * bulk, 0.12 * k), armor, 0.014)
        up_h, lo_h = 0.3 * k, 0.28 * k
        add_box(f"upperarm{sx+1}", (x, 0, z_sh - up_h / 2 - 0.02 * k), (0.11 * k * bulk, 0.12 * k * bulk, up_h), inner, 0.01)
        add_cyl(f"elbow{sx+1}", (x, 0, z_sh - up_h - 0.02 * k), 0.06 * k * bulk, 0.13 * k * bulk, joint, 12, axis="X")
        add_box(f"forearm{sx+1}", (x, 0.01 * k, z_sh - up_h - 0.02 * k - lo_h / 2), (0.1 * k * bulk, 0.11 * k * bulk, lo_h), inner, 0.01)
        if plate:
            add_box(f"forearm_plate{sx+1}", (x + sx * 0.055 * k * bulk, 0, z_sh - up_h - 0.02 * k - lo_h / 2), (0.03 * k, 0.12 * k * bulk, lo_h * 0.8), armor, 0.008)
        hz = z_sh - up_h - 0.02 * k - lo_h - 0.05 * k
        if s.get("clamp"):
            add_boxes(f"clamp{sx+1}", [((x + sx * 0.03 * k, 0.02 * k, hz), (0.03 * k, 0.1 * k, 0.1 * k)), ((x - sx * 0.03 * k, 0.02 * k, hz), (0.03 * k, 0.1 * k, 0.1 * k))], M["steel"], 0.004)
        else:
            add_box(f"hand{sx+1}", (x, 0.01 * k, hz), (0.08 * k * bulk, 0.06 * k, 0.1 * k), joint, 0.006)
    # 首と頭
    add_cyl("neck", (0, 0, z_neck + neck_h / 2), 0.05 * k, neck_h, joint, 10)
    head = s.get("head", "visor")
    if head == "sensor":
        add_box("head", (0, 0, z_head + head_h * 0.45), (0.2 * k, 0.22 * k, head_h * 0.8), inner, 0.012)
        add_cyl("sensor", (0, 0.12 * k, z_head + head_h * 0.5), 0.045 * k, 0.04 * k, glow, 12, axis="Y", bevel=0)
        add_box("antenna", (0.07 * k, -0.04 * k, z_head + head_h * 1.0), (0.015 * k, 0.015 * k, head_h * 0.5), M["steel"], 0)
    elif head == "human":
        add_box("head", (0, 0, z_head + head_h * 0.5), (0.17 * k, 0.19 * k, head_h * 0.9), M["skin"], 0.02)
        add_box("helmet", (0, -0.01 * k, z_head + head_h * 0.62), (0.21 * k, 0.22 * k, head_h * 0.55), armor, 0.014)
        add_box("visor", (0, 0.1 * k, z_head + head_h * 0.5), (0.16 * k, 0.015, head_h * 0.3), M["glass"], 0)
    else:
        add_box("head", (0, 0, z_head + head_h * 0.5), (0.2 * k, 0.22 * k, head_h * 0.9), armor, 0.014)
        add_box("jaw", (0, 0.03 * k, z_head + head_h * 0.15), (0.14 * k, 0.16 * k, head_h * 0.25), inner, 0.008)
        add_box("visor", (0, 0.11 * k, z_head + head_h * 0.58), (0.16 * k, 0.015, head_h * (0.12 if s.get("thin_visor") else 0.22)), glow, 0)
        if s.get("crest"):
            add_box("crest", (0, -0.02 * k, z_head + head_h * 1.05), (0.03 * k, 0.18 * k, head_h * 0.3), accent, 0.006)
        if s.get("ears"):
            add_boxes("ears", [((sx * 0.115 * k, 0, z_head + head_h * 0.55), (0.03 * k, 0.08 * k, head_h * 0.3)) for sx in (-1, 1)], accent, 0.006)
    # 背面装備
    back = s.get("back")
    zb = z_chest + torso_h * 0.6
    if back == "toolbox":
        add_box("backpack", (0, -0.2 * k, zb), (0.3 * k, 0.12 * k, 0.3 * k), M["rust"], 0.012)
        add_box("backpack_stripe", (0, -0.27 * k, zb), (0.26 * k, 0.01, 0.05 * k), M["hazard"], 0)
    elif back == "pack":
        add_box("backpack", (0, -0.19 * k, zb), (0.26 * k, 0.1 * k, 0.28 * k), inner, 0.012)
        add_box("antenna", (0.1 * k, -0.2 * k, zb + 0.3 * k), (0.012 * k, 0.012 * k, 0.4 * k), M["steel"], 0)
        add_box("antenna_tip", (0.1 * k, -0.2 * k, zb + 0.51 * k), (0.03 * k, 0.03 * k, 0.03 * k), M["glowred"], 0)
    elif back == "booster":
        add_box("backpack", (0, -0.2 * k, zb), (0.34 * k, 0.14 * k, 0.34 * k), armor, 0.014)
        for sx in (-1, 1):
            add_cyl(f"nozzle{sx+1}", (sx * 0.1 * k, -0.24 * k, zb - 0.22 * k), 0.05 * k, 0.14 * k, M["steel"], 12, r2=0.06 * k)
            add_cyl(f"nozzle_glow{sx+1}", (sx * 0.1 * k, -0.24 * k, zb - 0.3 * k), 0.045 * k, 0.02 * k, M["gloworange"], 12, bevel=0)
            add_box(f"wing{sx+1}", (sx * 0.3 * k, -0.2 * k, zb + 0.05 * k), (0.3 * k, 0.03 * k, 0.12 * k), armor, 0.008, rot=(0, 0, sx * math.radians(-20)))
    elif back == "fins":
        add_boxes("fins", [((sx * (shoulder_w / 2 + 0.04 * k), -0.02 * k, z_neck + 0.08 * k), (0.03 * k, 0.12 * k, 0.16 * k)) for sx in (-1, 1)], accent, 0.006)
    # 継ぎ目の発光(上位型)
    if s.get("seams"):
        add_boxes("seams", [((sx * 0.11 * k, 0.085 * k, foot_h + shin_h + thigh_h * 0.5), (0.1 * k, 0.005, 0.012 * k)) for sx in (-1, 1)]
                  + [((sx * shoulder_w / 2, 0.1 * k * bulk, z_neck - 0.05 * k), (0.1 * k, 0.005, 0.012 * k)) for sx in (-1, 1)], glow, 0)
    # 肩の標識(作業型の危険帯、兵士型の階級)
    if s.get("stripes"):
        add_boxes("stripes", [((sx * shoulder_w / 2 + sx * 0.03 * k, 0.1 * k * bulk, z_neck - 0.02 * k), (0.12 * k, 0.005, 0.02 * k)) for sx in (-1, 1)], M["hazard"], 0)
    # 武器(右手)
    if s.get("rifle"):
        x = shoulder_w / 2
        hz = z_neck - 0.05 * k - 0.3 * k - 0.02 * k - 0.28 * k - 0.05 * k
        add_box("rifle_body", (x, 0.18 * k, hz + 0.02 * k), (0.06 * k, 0.5 * k, 0.09 * k), inner, 0.008)
        add_cyl("rifle_barrel", (x, 0.5 * k, hz + 0.04 * k), 0.015 * k, 0.3 * k, M["steel"], 8, axis="Y")
        add_box("rifle_stock", (x, -0.1 * k, hz + 0.01 * k), (0.05 * k, 0.1 * k, 0.07 * k), M["rust"], 0.006)
        add_box("rifle_glow", (x, 0.3 * k, hz + 0.07 * k), (0.02 * k, 0.1 * k, 0.01), glow, 0)
    export(cid, kind, H)


SPECS = {
    # 市民: 作業服、小さな兜とバイザー、装甲なし
    "citizen": dict(height=1.7, bulk=0.9, plate=False, inner="cloth", joint="cloth", accent="dark", glow="glass", head="human"),
    # 人間の兵士: 作業服の上に胸甲と肩当て、兜、小銃
    "soldier": dict(height=1.72, bulk=0.95, plate=True, armor="paint", inner="cloth", joint="cloth", accent="dark", glow="glowcyan", head="human", back="pack", rifle=True),
}

if __name__ == "__main__":
    for cid, spec in SPECS.items():
        humanoid(cid, "robot" if cid.startswith("robot") else "human", spec)
    JSON_OUT.parent.mkdir(parents=True, exist_ok=True)
    JSON_OUT.write_text(json.dumps({"characters": ENTRIES}, ensure_ascii=False, indent=1), encoding="utf-8")
    print("characters:", len(ENTRIES))
