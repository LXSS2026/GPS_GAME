"""大型人型機体(15〜20 m)を Blender で生成し GLB に出力する。本作のデザインの核心。
実行: blender.exe -b -P tools/blender/make_mechs.py -- <出力フォルダ> <mechs.json>
原点は足元の中心、Z が上、正面は +Y(GLB で Y 上に変換。Godot 側で 180 度回して +Z を正面に)。
目標: PSP 世代のガンダム作品程度の密度。明確なシルエット、頭部のセンサーと角、胸の吸気口、肩と腰の装甲、背部スラスター、関節の露出、武装。
機体ごとの色は材質名を機体別にして GLB に持たせる(Godot では差し替えない)。共通材質(M_Steel, M_Glow*, M_Hazard)は Godot で PBR に差し替える。
"""
import bpy
import json
import math
import sys
from mathutils import Euler, Vector
from pathlib import Path

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = Path(args[0]) if args else Path("out")
JSON_OUT = Path(args[1]) if len(args) > 1 else OUT / "mechs.json"
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


def build_materials(cid, main, sub, trim):
    return {
        "main": material(f"M_Main_{cid}", main, 0.35, 0.45),
        "sub": material(f"M_Sub_{cid}", sub, 0.35, 0.5),
        "trim": material(f"M_Trim_{cid}", trim, 0.3, 0.5),
        "frame": material("M_Frame", (0.12, 0.13, 0.15), 0.8, 0.55),
        "steel": material("M_Steel", (0.6, 0.62, 0.66), 0.9, 0.5),
        "rust": material("M_Rust", (0.5, 0.3, 0.18), 0.4, 0.8),
        "rubber": material("M_Rubber", (0.08, 0.08, 0.09), 0, 0.95),
        "hazard": material("M_Hazard", (0.85, 0.65, 0.1), 0, 0.7),
        "glass": material("M_Glass", (0.05, 0.08, 0.12), 0.6, 0.15),
        "glowcyan": material("M_GlowCyan", (0.25, 0.85, 1), 0, 0.3, (0.25, 0.85, 1), 6),
        "gloworange": material("M_GlowOrange", (1, 0.45, 0.1), 0, 0.3, (1, 0.45, 0.1), 6),
        "glowred": material("M_GlowRed", (1, 0.15, 0.1), 0, 0.3, (1, 0.15, 0.1), 6),
        "glowgreen": material("M_GlowGreen", (0.4, 1, 0.5), 0, 0.3, (0.4, 1, 0.5), 6),
    }


def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)


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


def frustum_data(center, bottom, top, h):
    """錐台: 底面 bottom(x, y)、上面 top(x, y)、高さ h。center は底面の中心。"""
    cx, cy, cz = center
    bx, by = bottom[0] / 2, bottom[1] / 2
    tx, ty = top[0] / 2, top[1] / 2
    verts = [(cx - bx, cy - by, cz), (cx + bx, cy - by, cz), (cx + bx, cy + by, cz), (cx - bx, cy + by, cz),
             (cx - tx, cy - ty, cz + h), (cx + tx, cy - ty, cz + h), (cx + tx, cy + ty, cz + h), (cx - tx, cy + ty, cz + h)]
    faces = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    return verts, faces


def mesh_obj(name, verts, faces, mat, bevel, parent=None, cutters=None):
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.update()
    obj = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(mat)
    if cutters:
        for i, c in enumerate(cutters):
            mod = obj.modifiers.new(f"cut{i}", "BOOLEAN")
            mod.operation = "DIFFERENCE"
            mod.object = c
            mod.solver = "EXACT"
    if bevel > 0:
        b = obj.modifiers.new("bevel", "BEVEL")
        b.width = bevel
        b.segments = 2
        b.limit_method = "ANGLE"
        b.angle_limit = math.radians(35)
    if parent is not None:
        obj.parent = parent
        obj.matrix_parent_inverse = parent.matrix_world.inverted()
    return obj


def add_boxes(name, boxes, mat, bevel, parent=None, cutters=None):
    v, f = box_mesh_data(boxes)
    return mesh_obj(name, v, f, mat, bevel, parent, cutters)


def add_box(name, center, size, mat, bevel, parent=None, cutters=None):
    return add_boxes(name, [(center, size)], mat, bevel, parent, cutters)


def add_frustum(name, center, bottom, top, h, mat, bevel, parent=None, cutters=None):
    v, f = frustum_data(center, bottom, top, h)
    return mesh_obj(name, v, f, mat, bevel, parent, cutters)


def cutter(name, boxes):
    v, f = box_mesh_data(boxes)
    me = bpy.data.meshes.new(name)
    me.from_pydata(v, [], f)
    me.update()
    obj = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(obj)
    obj.hide_render = True
    obj["cutter"] = True
    return obj


def add_cyl(name, center, r, h, mat, verts=16, axis="Z", r2=None, bevel=0.0, parent=None):
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
        b.angle_limit = math.radians(35)
    if parent is not None:
        obj.parent = parent
        obj.matrix_parent_inverse = parent.matrix_world.inverted()
    return obj


def add_sphere(name, center, r, mat, parent=None):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=r, location=center)
    obj = bpy.context.active_object
    obj.name = name
    obj.data.materials.append(mat)
    if parent is not None:
        obj.parent = parent
        obj.matrix_parent_inverse = parent.matrix_world.inverted()
    return obj


def empty(name, loc, parent=None):
    e = bpy.data.objects.new(name, None)
    e.location = loc
    bpy.context.collection.objects.link(e)
    if parent is not None:
        e.parent = parent
        e.matrix_parent_inverse = parent.matrix_world.inverted()
    bpy.context.view_layer.update()
    return e


def thruster(name, center, r, length, mat_shell, mat_glow, axis="Z", parent=None):
    """スラスター: 鐘形(円錐台)の筒と奥の発光。axis は噴射の向きの軸。"""
    add_cyl(name, center, r * 0.7, length, mat_shell, 16, axis=axis, r2=r, bevel=r * 0.08, parent=parent)
    off = {"Z": (0, 0, -length / 2 + 0.01), "X": (-length / 2 + 0.01, 0, 0), "Y": (0, -length / 2 + 0.01, 0)}[axis]
    add_cyl(name + "_glow", (center[0] + off[0], center[1] + off[1], center[2] + off[2]), r * 0.85, 0.02, mat_glow, 16, axis=axis, parent=parent)


def export(cid, height):
    for o in bpy.context.scene.objects:
        o.select_set(not o.get("cutter", False))
    path = OUT / f"{cid}.glb"
    bpy.ops.export_scene.gltf(filepath=str(path), export_format="GLB", use_selection=True, export_apply=True, export_yup=True)
    ENTRIES.append({"id": cid, "kind": "mech", "height": height})
    print("exported", path)


# ---------- 機体 ----------
def mech(cid, s):
    clear()
    global M
    M = build_materials(cid, s["main"], s["sub"], s["trim"])
    H = s["height"]
    glow = M[s.get("glow", "glowcyan")]
    bv = 0.004 * H            # 面取り幅
    gv = 0.006 * H            # 溝(パネルライン)の深さ

    def groove_lines(center, size, axis, count, mat_unused=None):
        """面に平行な浅い溝のカッター(縦 or 横の線)。"""
        boxes = []
        cx, cy, cz = center
        sx, sy, sz = size
        for i in range(count):
            t = (i + 0.5) / count - 0.5
            if axis == "x":
                boxes.append(((cx + t * sx * 0.8, cy, cz), (gv * 0.6, sy + 0.02, sz * 0.7)))
            else:
                boxes.append(((cx, cy, cz + t * sz * 0.8), (sx * 0.8, sy + 0.02, gv * 0.6)))
        return cutter("cutter_groove", boxes)

    # ===== 寸法 =====
    foot_l, foot_w, foot_h = 0.17 * H, 0.085 * H, 0.045 * H
    shin_h, thigh_h = 0.215 * H, 0.215 * H
    z_knee = foot_h + shin_h
    z_hip = z_knee + thigh_h
    pelvis_h = 0.07 * H
    abd_h = 0.07 * H
    chest_h = 0.17 * H
    z_chest0 = z_hip + pelvis_h + abd_h
    z_chest1 = z_chest0 + chest_h
    z_neck = z_chest1 + 0.02 * H
    head_h = 0.095 * H
    leg_x = 0.09 * H
    sh_x = 0.2 * H * s.get("shoulder_w", 1.0)
    z_sh = z_chest1 - 0.03 * H
    exposed = s.get("exposed", False)
    plate_mat = M["sub"] if exposed else M["main"]

    # ===== 脚 =====
    for sx in (-1, 1):
        hip = empty(f"leg{sx+1}", (sx * leg_x, 0, z_hip))
        hip.rotation_euler = (0, math.radians(sx * 3), 0)
        # 足: 本体・つま先の楔・かかと・足首の覆い・足裏の噴射口
        add_box("foot", (sx * leg_x, 0.02 * H, foot_h / 2), (foot_w, foot_l * 0.75, foot_h), M["sub"], bv, hip)
        add_frustum("toe", (sx * leg_x, foot_l * 0.42, 0), (foot_w * 0.95, foot_l * 0.4), (foot_w * 0.7, foot_l * 0.2), foot_h * 0.9, M["main"], bv * 0.6, hip)
        add_box("heel", (sx * leg_x, -foot_l * 0.42, foot_h * 0.45), (foot_w * 0.8, foot_l * 0.25, foot_h * 0.9), M["frame"], bv * 0.5, hip)
        add_box("ankle_guard", (sx * leg_x, 0.03 * H, foot_h + 0.025 * H), (foot_w * 0.9, 0.06 * H, 0.05 * H), M["main"], bv * 0.6, hip)
        add_cyl("ankle", (sx * leg_x, 0, foot_h + 0.02 * H), 0.03 * H, foot_w * 1.05, M["frame"], 14, axis="X", parent=hip)
        if s.get("foot_thrusters"):
            for k in (-1, 1):
                add_cyl("sole_vent", (sx * leg_x + k * foot_w * 0.25, -foot_l * 0.2, foot_h * 0.3), 0.012 * H, 0.01 * H, glow, 10, parent=hip)
        # 脛: 錐台(膝で太い)+ 前面の装甲板(溝)+ ふくらはぎ + 側面の板
        add_frustum("shin", (sx * leg_x, 0, foot_h + 0.04 * H), (0.075 * H, 0.09 * H), (0.085 * H, 0.1 * H), shin_h - 0.04 * H, M["frame"], bv * 0.5, hip)
        cg = groove_lines((sx * leg_x, 0.055 * H, foot_h + shin_h * 0.5), (0.09 * H, 0.02 * H, shin_h * 0.8), "z", 2)
        add_box("shin_plate", (sx * leg_x, 0.055 * H, foot_h + shin_h * 0.5), (0.09 * H, 0.025 * H, shin_h * 0.82), plate_mat, bv, hip, [cg])
        add_box("calf", (sx * leg_x, -0.05 * H, foot_h + shin_h * 0.45), (0.07 * H, 0.03 * H, shin_h * 0.6), M["sub"], bv * 0.8, hip)
        if s.get("leg_thrusters"):
            thruster("calf_thruster", (sx * leg_x, -0.075 * H, foot_h + shin_h * 0.3), 0.022 * H, 0.06 * H, M["frame"], glow, "Z", hip)
        # 膝: 関節の円筒 + 膝当て
        add_cyl("knee", (sx * leg_x, 0, z_knee), 0.042 * H, 0.1 * H, M["frame"], 16, axis="X", parent=hip)
        add_box("knee_cap", (sx * leg_x, 0.045 * H, z_knee + 0.005 * H), (0.075 * H, 0.03 * H, 0.07 * H), M["main"], bv, hip)
        add_box("knee_cap2", (sx * leg_x, 0.062 * H, z_knee + 0.02 * H), (0.05 * H, 0.012 * H, 0.035 * H), M["trim"], bv * 0.5, hip)
        # 腿: 錐台(股で太い)+ 前面板 + 側面板
        add_frustum("thigh", (sx * leg_x, 0, z_knee + 0.03 * H), (0.085 * H, 0.1 * H), (0.1 * H, 0.115 * H), thigh_h - 0.03 * H, M["frame"], bv * 0.5, hip)
        tg = groove_lines((sx * leg_x, 0.06 * H, z_knee + thigh_h * 0.55), (0.1 * H, 0.02 * H, thigh_h * 0.7), "z", 1)
        add_box("thigh_plate", (sx * leg_x, 0.06 * H, z_knee + thigh_h * 0.55), (0.1 * H, 0.025 * H, thigh_h * 0.7), plate_mat, bv, hip, [tg])
        add_box("thigh_side", (sx * (leg_x + 0.055 * H), 0, z_knee + thigh_h * 0.55), (0.02 * H, 0.1 * H, thigh_h * 0.6), M["sub"], bv * 0.8, hip)
        add_sphere("hip_joint", (sx * leg_x, 0, z_hip), 0.045 * H, M["frame"], hip)
        if exposed:
            add_cyl("piston", (sx * (leg_x + 0.07 * H), -0.02 * H, z_knee + thigh_h * 0.5), 0.012 * H, thigh_h * 0.8, M["steel"], 8, parent=hip)
            add_cyl("piston_rod", (sx * (leg_x + 0.07 * H), -0.02 * H, foot_h + shin_h * 0.55), 0.008 * H, shin_h * 0.7, M["steel"], 8, parent=hip)

    # ===== 腰と胴 =====
    add_box("pelvis", (0, 0, z_hip + pelvis_h / 2), (0.24 * H, 0.14 * H, pelvis_h), M["frame"], bv * 0.6)
    add_box("crotch", (0, 0.05 * H, z_hip + pelvis_h * 0.35), (0.07 * H, 0.05 * H, pelvis_h * 0.7), M["main"], bv * 0.6)
    if s.get("skirt", True):
        # 腰の装甲: 前 2 枚・横 2 枚・後 1 枚(少し外へ傾ける)
        for sx in (-1, 1):
            p = add_box("skirt_front", (sx * 0.06 * H, 0.085 * H, z_hip - 0.02 * H), (0.09 * H, 0.02 * H, 0.11 * H), M["main"], bv)
            p.rotation_euler = (math.radians(-12), 0, 0)
            p = add_box("skirt_side", (sx * 0.145 * H, 0, z_hip - 0.01 * H), (0.02 * H, 0.1 * H, 0.12 * H), M["main"], bv)
            p.rotation_euler = (0, math.radians(sx * 14), 0)
        p = add_box("skirt_rear", (0, -0.085 * H, z_hip - 0.02 * H), (0.2 * H, 0.02 * H, 0.1 * H), M["sub"], bv)
        p.rotation_euler = (math.radians(12), 0, 0)
    add_cyl("waist_ring", (0, 0, z_hip + pelvis_h + abd_h * 0.5), 0.085 * H, abd_h, M["frame"], 16)
    add_box("abdomen_plate", (0, 0.07 * H, z_hip + pelvis_h + abd_h * 0.5), (0.1 * H, 0.03 * H, abd_h * 0.9), M["sub"], bv * 0.7)
    if exposed:
        add_cyl("spine", (0, -0.05 * H, z_chest0 - abd_h * 0.5), 0.02 * H, abd_h * 1.6, M["steel"], 10)
    # 胸: 本体(錐台で上が広い)+ 中央のコックピット扉 + 左右の吸気口 + 襟
    add_frustum("chest", (0, 0, z_chest0), (0.26 * H, 0.17 * H), (0.34 * H, 0.2 * H), chest_h, M["main"], bv)
    cg = groove_lines((0, 0.1 * H, z_chest0 + chest_h * 0.45), (0.1 * H, 0.02 * H, chest_h * 0.6), "z", 1)
    add_box("cockpit", (0, 0.1 * H, z_chest0 + chest_h * 0.45), (0.1 * H, 0.025 * H, chest_h * 0.6), M["sub"], bv * 0.8, cutters=[cg])
    add_box("cockpit_line", (0, 0.115 * H, z_chest0 + chest_h * 0.55), (0.015 * H, 0.005, chest_h * 0.35), glow, 0)
    for sx in (-1, 1):
        iv = add_box("intake", (sx * 0.1 * H, 0.1 * H, z_chest0 + chest_h * 0.72), (0.09 * H, 0.03 * H, chest_h * 0.4), M["trim"], bv * 0.8)
        iv.rotation_euler = (math.radians(-15), 0, math.radians(sx * -8))
        slats = [((sx * 0.1 * H, 0.118 * H, z_chest0 + chest_h * (0.6 + i * 0.08)), (0.07 * H, 0.004, 0.006 * H)) for i in range(4)]
        add_boxes("intake_slats", slats, M["frame"], 0)
    add_box("collar", (0, -0.02 * H, z_chest1 - 0.01 * H), (0.2 * H, 0.12 * H, 0.03 * H), M["sub"], bv * 0.7)
    add_box("back_plate", (0, -0.095 * H, z_chest0 + chest_h * 0.5), (0.28 * H, 0.02 * H, chest_h * 0.8), M["sub"], bv)
    if s.get("seams"):
        add_boxes("chest_seams", [((sx * 0.14 * H, 0.085 * H, z_chest0 + chest_h * 0.3), (0.06 * H, 0.005, 0.008 * H)) for sx in (-1, 1)], glow, 0)

    # ===== 背部装備 =====
    bp_y = -0.14 * H
    bp = add_box("backpack", (0, bp_y, z_chest0 + chest_h * 0.55), (0.2 * H, 0.07 * H, chest_h * 0.9), M["sub"], bv)
    n_th = s.get("thrusters", 2)
    if n_th:
        xs = [-0.065 * H, 0.065 * H] if n_th == 2 else [-0.075 * H, -0.025 * H, 0.025 * H, 0.075 * H] if n_th == 4 else [-0.08 * H, -0.045 * H, 0, 0.045 * H, 0.08 * H, 0][:n_th]
        for i, x in enumerate(xs):
            z = z_chest0 + chest_h * (0.25 if n_th <= 2 else (0.2 if i % 2 == 0 else 0.5))
            thruster(f"thruster{i}", (x, bp_y - 0.02 * H, z), 0.03 * H, 0.08 * H, M["frame"], glow, "Z")
    if s.get("sabers"):
        for sx in (-1, 1):
            sb = add_cyl("saber", (sx * 0.11 * H, bp_y, z_chest1 + 0.02 * H), 0.012 * H, 0.12 * H, M["frame"], 8)
            sb.rotation_euler = (0, math.radians(sx * 20), 0)
            add_cyl("saber_tip", (sx * 0.11 * H + sx * 0.02 * H, bp_y, z_chest1 + 0.08 * H), 0.012 * H, 0.012 * H, M["trim"], 8)
    if s.get("wings"):
        for sx in (-1, 1):
            w = add_box("wing", (sx * 0.22 * H, bp_y - 0.01 * H, z_chest0 + chest_h * 0.8), (0.3 * H, 0.025 * H, 0.08 * H), M["main"], bv)
            w.rotation_euler = (0, math.radians(sx * -25), math.radians(sx * 10))
            w2 = add_box("wing_edge", (sx * 0.22 * H, bp_y - 0.025 * H, z_chest0 + chest_h * 0.8), (0.28 * H, 0.006 * H, 0.015 * H), glow, 0)
            w2.rotation_euler = (0, math.radians(sx * -25), math.radians(sx * 10))
    if s.get("cannon"):
        cn = empty("cannon_mount", (0.16 * H, bp_y, z_chest1))
        add_box("cannon_base", (0.16 * H, bp_y, z_chest1 + 0.02 * H), (0.06 * H, 0.06 * H, 0.06 * H), M["frame"], bv * 0.6, cn)
        add_cyl("cannon_barrel", (0.16 * H, bp_y + 0.2 * H, z_chest1 + 0.06 * H), 0.02 * H, 0.5 * H, M["sub"], 12, axis="Y", parent=cn)
        add_cyl("cannon_muzzle", (0.16 * H, bp_y + 0.44 * H, z_chest1 + 0.06 * H), 0.028 * H, 0.05 * H, M["frame"], 12, axis="Y", parent=cn)
        add_box("cannon_body", (0.16 * H, bp_y + 0.02 * H, z_chest1 + 0.06 * H), (0.05 * H, 0.14 * H, 0.05 * H), M["main"], bv * 0.6, cn)
        cn.rotation_euler = (math.radians(-10), 0, 0)
    if s.get("toolbox"):
        add_box("toolbox", (0, bp_y - 0.03 * H, z_chest0 + chest_h * 0.4), (0.22 * H, 0.08 * H, chest_h * 0.6), M["rust"], bv * 0.6)
        add_box("toolbox_stripe", (0, bp_y - 0.075 * H, z_chest0 + chest_h * 0.4), (0.2 * H, 0.004, 0.02 * H), M["hazard"], 0)

    # ===== 肩と腕 =====
    kind = s.get("shoulders", "pauldron")
    for sx in (-1, 1):
        arm = empty(f"arm{sx+1}", (sx * sh_x, 0, z_sh))
        add_sphere("shoulder_joint", (sx * sh_x, 0, z_sh), 0.04 * H, M["frame"], arm)
        add_cyl("shoulder_axle", (sx * (sh_x - 0.05 * H), 0, z_sh), 0.025 * H, 0.1 * H, M["frame"], 12, axis="X", parent=arm)
        if kind == "pauldron":
            pg = groove_lines((sx * (sh_x + 0.02 * H), 0, z_sh + 0.03 * H), (0.13 * H, 0.15 * H, 0.11 * H), "x", 1)
            add_box("pauldron", (sx * (sh_x + 0.02 * H), 0, z_sh + 0.03 * H), (0.13 * H, 0.15 * H, 0.11 * H), M["main"], bv, arm, [pg])
            add_box("pauldron_trim", (sx * (sh_x + 0.085 * H), 0, z_sh + 0.07 * H), (0.02 * H, 0.1 * H, 0.03 * H), M["trim"], bv * 0.5, arm)
        elif kind == "binder":
            add_box("binder", (sx * (sh_x + 0.05 * H), -0.02 * H, z_sh + 0.05 * H), (0.17 * H, 0.2 * H, 0.16 * H), M["main"], bv, arm)
            add_box("binder_inner", (sx * (sh_x + 0.05 * H), 0.08 * H, z_sh + 0.05 * H), (0.12 * H, 0.05 * H, 0.1 * H), M["sub"], bv * 0.7, arm)
            thruster("binder_thruster", (sx * (sh_x + 0.05 * H), -0.12 * H, z_sh + 0.05 * H), 0.035 * H, 0.08 * H, M["frame"], glow, "Y", arm)
            add_box("binder_glow", (sx * (sh_x + 0.135 * H), -0.02 * H, z_sh + 0.05 * H), (0.004, 0.14 * H, 0.012 * H), glow, 0, arm)
        elif kind == "spike":
            add_box("pauldron", (sx * (sh_x + 0.02 * H), 0, z_sh + 0.03 * H), (0.14 * H, 0.15 * H, 0.1 * H), M["main"], bv, arm)
            for k in range(3):
                sp = add_cyl("spike", (sx * (sh_x + 0.04 * H + k * 0.025 * H), -0.03 * H + k * 0.03 * H, z_sh + 0.11 * H), 0.012 * H, 0.06 * H, M["trim"], 8, r2=0.002 * H, parent=arm)
        else:  # plain
            add_box("pauldron", (sx * (sh_x + 0.01 * H), 0, z_sh + 0.02 * H), (0.1 * H, 0.12 * H, 0.08 * H), M["sub"], bv, arm)
        # 上腕・肘・前腕・手
        up_h, lo_h = 0.16 * H, 0.16 * H
        add_frustum("upper_arm", (sx * sh_x, 0, z_sh - up_h), (0.06 * H, 0.065 * H), (0.07 * H, 0.075 * H), up_h, M["frame"], bv * 0.5, arm)
        add_box("upper_arm_plate", (sx * (sh_x + 0.04 * H), 0, z_sh - up_h * 0.5), (0.02 * H, 0.07 * H, up_h * 0.7), plate_mat, bv * 0.8, arm)
        elbow = empty(f"elbow{sx+1}", (sx * sh_x, 0, z_sh - up_h), arm)
        add_cyl("elbow_joint", (sx * sh_x, 0, z_sh - up_h), 0.035 * H, 0.085 * H, M["frame"], 14, axis="X", parent=elbow)
        add_frustum("forearm", (sx * sh_x, 0, z_sh - up_h - lo_h), (0.07 * H, 0.075 * H), (0.06 * H, 0.065 * H), lo_h, M["frame"], bv * 0.5, elbow)
        fg = groove_lines((sx * sh_x, 0.045 * H, z_sh - up_h - lo_h * 0.5), (0.075 * H, 0.02 * H, lo_h * 0.8), "z", 1)
        add_box("forearm_plate", (sx * sh_x, 0.045 * H, z_sh - up_h - lo_h * 0.5), (0.075 * H, 0.025 * H, lo_h * 0.8), plate_mat, bv * 0.8, elbow, [fg])
        add_box("forearm_side", (sx * (sh_x + 0.045 * H), 0, z_sh - up_h - lo_h * 0.5), (0.02 * H, 0.08 * H, lo_h * 0.75), M["sub"], bv * 0.8, elbow)
        hz = z_sh - up_h - lo_h - 0.02 * H
        add_cyl("wrist", (sx * sh_x, 0, hz + 0.01 * H), 0.025 * H, 0.03 * H, M["frame"], 12, parent=elbow)
        if s.get("hands") == "claw":
            for k in (-1, 1):
                add_box("claw", (sx * sh_x + k * 0.025 * H, 0.02 * H, hz - 0.03 * H), (0.02 * H, 0.05 * H, 0.07 * H), M["steel"], bv * 0.4, elbow)
        else:
            add_box("palm", (sx * sh_x, 0.01 * H, hz - 0.025 * H), (0.055 * H, 0.04 * H, 0.05 * H), M["frame"], bv * 0.5, elbow)
            for k in range(4):
                add_box("finger", (sx * sh_x - 0.02 * H + k * 0.0135 * H, 0.03 * H, hz - 0.05 * H), (0.011 * H, 0.025 * H, 0.035 * H), M["frame"], 0, elbow)
            add_box("thumb", (sx * (sh_x + 0.03 * H), 0.015 * H, hz - 0.03 * H), (0.012 * H, 0.03 * H, 0.03 * H), M["frame"], 0, elbow)
        # 武装(右手に小銃、左前腕に盾)
        weapon = s.get("weapon")
        if weapon in ("rifle", "rifle_shield") and sx == 1:
            rz = hz - 0.03 * H
            add_box("rifle_body", (sh_x, 0.12 * H, rz + 0.01 * H), (0.035 * H, 0.26 * H, 0.05 * H), M["frame"], bv * 0.5, elbow)
            add_cyl("rifle_barrel", (sh_x, 0.33 * H, rz + 0.02 * H), 0.012 * H, 0.2 * H, M["steel"], 10, axis="Y", parent=elbow)
            add_box("rifle_scope", (sh_x, 0.1 * H, rz + 0.05 * H), (0.02 * H, 0.1 * H, 0.02 * H), M["sub"], bv * 0.4, elbow)
            add_box("rifle_stock", (sh_x, -0.04 * H, rz), (0.03 * H, 0.08 * H, 0.04 * H), M["sub"], bv * 0.4, elbow)
            add_box("rifle_grip", (sh_x, 0.04 * H, rz - 0.03 * H), (0.02 * H, 0.025 * H, 0.04 * H), M["rubber"], 0, elbow)
            add_box("rifle_glow", (sh_x, 0.2 * H, rz + 0.037 * H), (0.012 * H, 0.08 * H, 0.004), glow, 0, elbow)
        if weapon == "rifle_shield" and sx == -1:
            sh = add_box("shield", (-(sh_x + 0.075 * H), 0.03 * H, z_sh - up_h - lo_h * 0.5), (0.025 * H, 0.16 * H, 0.32 * H), M["main"], bv, elbow)
            add_box("shield_rim", (-(sh_x + 0.09 * H), 0.03 * H, z_sh - up_h - lo_h * 0.5), (0.008 * H, 0.13 * H, 0.28 * H), M["sub"], bv * 0.5, elbow)
            add_box("shield_emblem", (-(sh_x + 0.095 * H), 0.03 * H, z_sh - up_h - lo_h * 0.5 + 0.06 * H), (0.004, 0.04 * H, 0.04 * H), M["trim"], 0, elbow)
            add_box("shield_glow", (-(sh_x + 0.095 * H), 0.03 * H, z_sh - up_h - lo_h * 0.5 - 0.08 * H), (0.004, 0.08 * H, 0.008 * H), glow, 0, elbow)
        # 構え: 腕を少し外へ、右腕を少し前へ
        arm.rotation_euler = (math.radians(s.get("arm_fwd", 6) if sx == 1 else 0), math.radians(sx * -s.get("arm_out", 7)), 0)
        elbow.rotation_euler = (math.radians(s.get("elbow_bend", 10)), 0, 0)

    # ===== 首と頭 =====
    add_cyl("neck", (0, 0, z_neck - 0.01 * H), 0.028 * H, 0.04 * H, M["frame"], 12)
    head = empty("head", (0, 0, z_neck))
    hh = head_h
    hg = groove_lines((0, 0, z_neck + hh * 0.55), (0.085 * H, 0.1 * H, hh * 0.8), "x", 1)
    add_box("skull", (0, 0, z_neck + hh * 0.55), (0.085 * H, 0.1 * H, hh * 0.8), M["main"], bv * 0.8, head, [hg])
    add_box("chin", (0, 0.03 * H, z_neck + hh * 0.15), (0.05 * H, 0.05 * H, hh * 0.3), M["sub"], bv * 0.5, head)
    kind = s.get("head", "twin")
    if kind == "mono":
        add_box("face_slot", (0, 0.05 * H, z_neck + hh * 0.55), (0.065 * H, 0.012 * H, hh * 0.3), M["frame"], 0, head)
        add_cyl("mono_eye", (0, 0.058 * H, z_neck + hh * 0.55), 0.016 * H, 0.01 * H, glow, 12, axis="Y", parent=head)
    elif kind == "visor":
        add_box("visor", (0, 0.052 * H, z_neck + hh * 0.58), (0.07 * H, 0.008 * H, hh * 0.18), glow, 0, head)
    else:
        add_box("faceplate", (0, 0.05 * H, z_neck + hh * 0.5), (0.055 * H, 0.012 * H, hh * 0.45), M["sub"], bv * 0.4, head)
        for sx in (-1, 1):
            add_box("eye", (sx * 0.018 * H, 0.058 * H, z_neck + hh * 0.6), (0.016 * H, 0.004, hh * 0.1), glow, 0, head)
        add_box("face_vent", (0, 0.058 * H, z_neck + hh * 0.38), (0.03 * H, 0.004, hh * 0.08), M["frame"], 0, head)
    for sx in (-1, 1):
        add_cyl("vulcan", (sx * 0.03 * H, 0.045 * H, z_neck + hh * 0.85), 0.006 * H, 0.02 * H, M["frame"], 8, axis="Y", parent=head)
        add_box("cheek", (sx * 0.045 * H, 0.02 * H, z_neck + hh * 0.5), (0.012 * H, 0.05 * H, hh * 0.45), M["sub"], bv * 0.4, head)
    if s.get("vfin"):
        for sx in (-1, 1):
            f = add_box("vfin", (sx * 0.035 * H, 0.04 * H, z_neck + hh * 1.0), (0.07 * H, 0.008 * H, 0.014 * H), M["trim"], 0, head)
            f.rotation_euler = (0, 0, math.radians(sx * 28))
        add_box("vfin_base", (0, 0.042 * H, z_neck + hh * 0.9), (0.02 * H, 0.012 * H, 0.025 * H), M["sub"], 0, head)
    if s.get("crest"):
        add_box("crest", (0, -0.01 * H, z_neck + hh * 1.05), (0.012 * H, 0.09 * H, hh * 0.3), M["trim"], bv * 0.4, head)
    if s.get("antenna"):
        add_cyl("antenna", (0.03 * H, -0.02 * H, z_neck + hh * 1.2), 0.004 * H, hh * 0.6, M["steel"], 6, parent=head)
    if s.get("sensor_pod"):
        add_box("sensor_pod", (0, -0.045 * H, z_neck + hh * 0.7), (0.05 * H, 0.03 * H, hh * 0.25), M["sub"], bv * 0.4, head)
        add_box("sensor_pod_glow", (0, -0.062 * H, z_neck + hh * 0.7), (0.03 * H, 0.004, hh * 0.06), glow, 0, head)
    export(cid, H)


SPECS = {
    # 第 0 世代 工作機(15 m): 露出した骨組みとピストン、単眼、クランプの手、工具箱、装甲は最小。錆と黄色の作業色
    "mech_g0": dict(height=15, main=(0.72, 0.55, 0.18), sub=(0.35, 0.33, 0.3), trim=(0.5, 0.3, 0.18), glow="gloworange",
                    head="mono", exposed=True, skirt=False, shoulders="plain", hands="claw", thrusters=0, toolbox=True, antenna=True, arm_out=5),
    # 第 1 世代 量産機(16 m): 灰緑の軍用色、簡素な頭部とバイザー、小銃、背部スラスター 2 基
    "mech_g1": dict(height=16, main=(0.36, 0.42, 0.36), sub=(0.25, 0.27, 0.26), trim=(0.6, 0.55, 0.2), glow="glowgreen",
                    head="visor", shoulders="pauldron", weapon="rifle", thrusters=2, antenna=True),
    # 第 2 世代 主力機(17 m): 白と青の主役色、角(V 字)と双眼、黄の吸気口、腰装甲、盾と小銃、サーベル
    "mech_g2": dict(height=17, main=(0.85, 0.87, 0.9), sub=(0.2, 0.3, 0.55), trim=(0.95, 0.75, 0.2), glow="glowcyan",
                    head="twin", vfin=True, shoulders="pauldron", weapon="rifle_shield", thrusters=2, sabers=True, foot_thrusters=True),
    # 第 3 世代 上位機(18 m): 濃紺と白、大型の背部(スラスター 4 基と翼)、頭部のクレスト、肩のバインダー、脚のスラスター、継ぎ目の発光
    "mech_g3": dict(height=18, main=(0.16, 0.2, 0.36), sub=(0.8, 0.82, 0.86), trim=(0.9, 0.8, 0.3), glow="glowcyan",
                    head="twin", vfin=True, crest=True, shoulders="binder", weapon="rifle_shield", thrusters=4, wings=True, leg_thrusters=True, foot_thrusters=True, seams=True, shoulder_w=1.05),
    # 第 4 世代 精鋭機(20 m): 黒と赤、肩の棘、6 基のスラスターと翼、肩の大砲、赤の双眼と角とクレスト
    "mech_g4": dict(height=20, main=(0.12, 0.11, 0.13), sub=(0.55, 0.1, 0.12), trim=(0.9, 0.75, 0.3), glow="glowred",
                    head="twin", vfin=True, crest=True, sensor_pod=True, shoulders="spike", weapon="rifle_shield", thrusters=6, wings=True, cannon=True, leg_thrusters=True, foot_thrusters=True, seams=True, shoulder_w=1.1),
}

if __name__ == "__main__":
    for cid, spec in SPECS.items():
        mech(cid, spec)
    JSON_OUT.parent.mkdir(parents=True, exist_ok=True)
    JSON_OUT.write_text(json.dumps({"mechs": ENTRIES}, ensure_ascii=False, indent=1), encoding="utf-8")
    print("mechs:", len(ENTRIES))
