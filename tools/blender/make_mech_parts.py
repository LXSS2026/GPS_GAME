"""機体の部位部品(キャラクリ用)を Blender で生成し GLB に出力する。
実行: blender.exe -b -P tools/blender/make_mech_parts.py -- <出力フォルダ> <mech_parts.json>
部位: torso(胴。腰・腹・胸・腰装甲) / head / arm(肩装甲・上腕・肘・前腕・手) / leg(腿・膝・脛・足) / back(背部装備) / weapon(小銃・大砲・刃・盾)
系統: heavy(重装) / fast(高速) / general(汎用)。各部位に 3 変種。
造形は曲面を主にする: 細分化曲面 + 折り目(subsurf + crease)、回転体(revolve)、球の切り出し(dome)、先細り(taper)、ブーリアンの抉り。直方体の組み合わせは使わない。
原点は各部位の取り付け口。胴は腰の中心(股関節の高さ)。腕は肩関節。脚は股関節。頭は首の付け根。背部は背面の取り付け口。武器は握り。
Blender は Z が上、正面 +Y。GLB 出力で Y 上。取り付け口の座標は JSON に Godot 座標(x, y=z, z=-y)で書く。
寸法は全高 17 m の汎用機を基準(H=17)。重装は太く、高速は細く長い。
"""
import bpy
import bmesh
import json
import math
import sys
from pathlib import Path

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = Path(args[0]) if args else Path("out")
JSON_OUT = Path(args[1]) if len(args) > 1 else OUT / "mech_parts.json"
OUT.mkdir(parents=True, exist_ok=True)
PARTS = []
M = {}
H = 17.0


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


def clear():
    global M
    bpy.ops.wm.read_factory_settings(use_empty=True)
    M = {
        "main": material("M_Main", (0.85, 0.87, 0.9), 0.35, 0.45),   # Godot で機体ごとの色に差し替える
        "sub": material("M_Sub", (0.2, 0.3, 0.55), 0.35, 0.5),
        "trim": material("M_Trim", (0.95, 0.75, 0.2), 0.3, 0.5),
        "frame": material("M_Frame", (0.12, 0.13, 0.15), 0.8, 0.55),
        "steel": material("M_Steel", (0.6, 0.62, 0.66), 0.9, 0.5),
        "rubber": material("M_Rubber", (0.08, 0.08, 0.09), 0, 0.95),
        "glow": material("M_GlowMech", (0.25, 0.85, 1), 0, 0.3, (0.25, 0.85, 1), 6),   # 機体ごとの発光色に差し替える
        "glass": material("M_Glass", (0.05, 0.08, 0.12), 0.6, 0.15),
        "reactor": material("M_Reactor", (1, 0.5, 0.1), 0, 0.3, (1, 0.5, 0.1), 8),   # 世代の色に差し替える(機体の世代はリアクターの色で表す)
    }


def G(p):
    """Blender 座標 → Godot 座標(JSON 用)"""
    return [round(p[0], 4), round(p[2], 4), round(-p[1], 4)]


# ---------- 造形の部品 ----------
def new_obj(name, me, mat, parent=None):
    obj = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(obj)
    if mat:
        obj.data.materials.append(mat)
    if parent is not None:
        obj.parent = parent
        obj.matrix_parent_inverse = parent.matrix_world.inverted()
    return obj


def subsurf(obj, levels=2):
    s = obj.modifiers.new("subsurf", "SUBSURF")
    s.levels = levels
    s.render_levels = levels
    return s


def bevel(obj, width, segments=4, angle=40):
    b = obj.modifiers.new("bevel", "BEVEL")
    b.width = width
    b.segments = segments
    b.limit_method = "ANGLE"
    b.angle_limit = math.radians(angle)
    return b


def taper(obj, factor, axis="Z", origin=None):
    sd = obj.modifiers.new("taper", "SIMPLE_DEFORM")
    sd.deform_method = "TAPER"
    sd.factor = factor
    sd.deform_axis = axis
    if origin is not None:
        sd.origin = origin
    return sd


def bend(obj, angle_deg, axis="X"):
    sd = obj.modifiers.new("bend", "SIMPLE_DEFORM")
    sd.deform_method = "BEND"
    sd.angle = math.radians(angle_deg)
    sd.deform_axis = axis
    return sd


def boolean(obj, cutter_obj):
    mod = obj.modifiers.new("cut", "BOOLEAN")
    mod.operation = "DIFFERENCE"
    mod.object = cutter_obj
    mod.solver = "EXACT"


def cutter_box(center, size, rot=None):
    bpy.ops.mesh.primitive_cube_add(size=1, location=center)
    c = bpy.context.active_object
    c.scale = size
    if rot:
        c.rotation_euler = rot
    c.hide_render = True
    c["cutter"] = True
    return c


def cutter_cyl(center, r, h, axis="Z", rot=None):
    bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=r, depth=h, location=center)
    c = bpy.context.active_object
    if axis == "X":
        c.rotation_euler = (0, math.radians(90), 0)
    elif axis == "Y":
        c.rotation_euler = (math.radians(90), 0, 0)
    if rot:
        c.rotation_euler = rot
    c.hide_render = True
    c["cutter"] = True
    return c


def shell(name, center, size, mat, crease=(1.0, 1.0, 0.5), levels=2, parent=None, cutters=None, rot=None):
    """細分化曲面の殻。crease = (x 方向の辺, y 方向の辺, z 方向の辺)の折り目。1 = 鋭い、0 = 丸い。
    例: (1, 1, 0.3) は上下の縁が丸く、縦の稜が立つ装甲。"""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x *= size[0]; v.co.y *= size[1]; v.co.z *= size[2]
    cl = bm.edges.layers.float.new("crease_edge")
    for e in bm.edges:
        d = (e.verts[1].co - e.verts[0].co)
        ax = 0 if abs(d.x) > abs(d.y) and abs(d.x) > abs(d.z) else 1 if abs(d.y) > abs(d.z) else 2
        e[cl] = crease[ax]
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = new_obj(name, me, mat, parent)
    obj.location = center
    if rot:
        obj.rotation_euler = rot
    if cutters:
        for c in cutters:
            boolean(obj, c)
    subsurf(obj, levels)
    return obj


def rounded(name, center, size, mat, r, parent=None, segs=4, rot=None, cutters=None):
    """角を大きく丸めた箱(面取りを多段にする)。関節周りや手に。"""
    bpy.ops.mesh.primitive_cube_add(size=1, location=center)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = size
    if rot:
        obj.rotation_euler = rot
    obj.data.materials.append(mat)
    if cutters:
        for c in cutters:
            boolean(obj, c)
    b = obj.modifiers.new("bevel", "BEVEL")
    b.width = r
    b.segments = segs
    b.limit_method = "NONE"
    if parent is not None:
        obj.parent = parent
        obj.matrix_parent_inverse = parent.matrix_world.inverted()
    return obj


def revolve(name, profile, mat, center=(0, 0, 0), seg=24, axis="Z", parent=None, rot=None):
    """回転体。profile は (半径, 高さ) の列。両端は半径 0 でなくてよい(蓋を付ける)。"""
    verts, faces = [], []
    n = len(profile)
    for i, (r, z) in enumerate(profile):
        for j in range(seg):
            a = j * 2 * math.pi / seg
            verts.append((r * math.cos(a), r * math.sin(a), z))
    for i in range(n - 1):
        for j in range(seg):
            a, b = i * seg + j, i * seg + (j + 1) % seg
            c, d = (i + 1) * seg + (j + 1) % seg, (i + 1) * seg + j
            faces.append((a, b, c, d))
    bottom = len(verts); verts.append((0, 0, profile[0][1]))
    top = len(verts); verts.append((0, 0, profile[-1][1]))
    for j in range(seg):
        faces.append((bottom, (j + 1) % seg, j))
        faces.append((top, (n - 1) * seg + j, (n - 1) * seg + (j + 1) % seg))
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)   # 面の向きを外側に揃える(裏返ると Godot で黒く見える)
    bm.to_mesh(me); bm.free()
    me.update()
    obj = new_obj(name, me, mat, parent)
    obj.location = center
    if axis == "X":
        obj.rotation_euler = (0, math.radians(90), 0)
    elif axis == "Y":
        obj.rotation_euler = (math.radians(-90), 0, 0)
    if rot:
        obj.rotation_euler = rot
    for p in me.polygons:
        p.use_smooth = True
    return obj


def dome(name, center, r, mat, cut_z=0.0, parent=None, scale=(1, 1, 1), rot=None):
    """球の切り出し(cut_z より下を落とす)。肩装甲や関節の覆いに。"""
    bpy.ops.mesh.primitive_uv_sphere_add(segments=28, ring_count=14, radius=r, location=center)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = scale
    if rot:
        obj.rotation_euler = rot
    obj.data.materials.append(mat)
    for p in obj.data.polygons:
        p.use_smooth = True
    c = cutter_box((center[0], center[1], center[2] + cut_z - r * 1.5), (r * 4, r * 4, r * 3))
    boolean(obj, c)
    if parent is not None:
        obj.parent = parent
        obj.matrix_parent_inverse = parent.matrix_world.inverted()
    return obj


def plate(name, center, size, mat, parent=None, rot=None, crease=(1.0, 0.4, 0.4), cutters=None):
    """装甲板: 薄い殻。短辺の縁を丸め、長辺の稜を立てる。"""
    return shell(name, center, size, mat, crease, 2, parent, cutters, rot)


def facet(name, center, bottom, top, h, mat, parent=None, rot=None, shift=(0, 0), bev=0.0, segs=2, cutters=None, skew=0.0):
    """面取りした台形塊(ガンダム系の基本形)。bottom/top は (幅, 奥行き)。shift は上面の (x, y) ずらし。
    skew は上面を前へ傾ける量(前面が前傾する)。bev は面取り幅(0 で無し)。原点は底面中心。"""
    (bw, bd), (tw, td) = bottom, top
    sx, sy = shift
    verts = [(-bw / 2, -bd / 2, 0), (bw / 2, -bd / 2, 0), (bw / 2, bd / 2, 0), (-bw / 2, bd / 2, 0),
             (sx - tw / 2, sy - td / 2 - skew, h), (sx + tw / 2, sy - td / 2 - skew, h), (sx + tw / 2, sy + td / 2 + skew, h), (sx - tw / 2, sy + td / 2 + skew, h)]
    faces = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    me.update()
    obj = new_obj(name, me, mat, parent)
    obj.location = center
    if rot:
        obj.rotation_euler = rot
    if cutters:
        for c in cutters:
            boolean(obj, c)
    if bev > 0:
        b = obj.modifiers.new("bevel", "BEVEL")
        b.width = bev; b.segments = segs; b.limit_method = "ANGLE"; b.angle_limit = math.radians(30)
    return obj


def wedge(name, center, w, d, h, mat, parent=None, rot=None, bev=0.0, front=True):
    """楔: 前(または後ろ)へ向かって薄くなる塊。装甲の先端・足の爪先・胸の吸気口に。"""
    return facet(name, center, (w, d), (w, d * 0.15), h, mat, parent, rot, shift=(0, d * 0.42 if front else -d * 0.42), bev=bev)


def slab(name, pts, z0, thick, mat, parent=None, bev=0.0):
    """XY 平面の多角形(pts)を Z 方向に厚み thick で押し出した板。翼・尾翼・刃に。"""
    n = len(pts)
    verts = [(x, y, z0) for x, y in pts] + [(x, y, z0 + thick) for x, y in pts]
    faces = [tuple(range(n))[::-1], tuple(range(n, 2 * n))]
    for i in range(n):
        j = (i + 1) % n
        faces.append((i, j, n + j, n + i))
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    me.update()
    obj = new_obj(name, me, mat, parent)
    if bev > 0:
        b = obj.modifiers.new("bevel", "BEVEL"); b.width = bev; b.segments = 2; b.limit_method = "ANGLE"; b.angle_limit = math.radians(30)
    return obj


def thruster(name, center, r, length, mat, glow, axis="Z", parent=None):
    """鐘形のスラスター(回転体)と奥の発光。"""
    prof = [(r * 0.55, 0), (r * 0.6, length * 0.15), (r * 0.75, length * 0.5), (r * 0.95, length * 0.85), (r, length)]
    o = revolve(name, prof, mat, center, 20, axis, parent)
    g = revolve(name + "_glow", [(r * 0.5, length * 0.08), (r * 0.5, length * 0.12)], glow, center, 20, axis, parent)
    return o


def joint(name, center, r, w, mat, axis="X", parent=None):
    """関節: 中央が膨らんだ回転体(樽形)と環。"""
    prof = [(r * 0.85, -w / 2), (r, -w * 0.3), (r, w * 0.3), (r * 0.85, w / 2)]
    o = revolve(name, prof, mat, center, 18, axis, parent)
    revolve(name + "_ring", [(r * 1.08, -w * 0.08), (r * 1.08, w * 0.08)], M["steel"], center, 18, axis, parent)
    return o


def empty(name, loc, parent=None):
    e = bpy.data.objects.new(name, None)
    e.location = loc
    bpy.context.collection.objects.link(e)
    if parent is not None:
        e.parent = parent
        e.matrix_parent_inverse = parent.matrix_world.inverted()
    bpy.context.view_layer.update()
    return e


def export(pid, slot, archetype, sockets, meta=None):
    for o in bpy.context.scene.objects:
        o.select_set(not o.get("cutter", False))
    path = OUT / f"{pid}.glb"
    bpy.ops.export_scene.gltf(filepath=str(path), export_format="GLB", use_selection=True, export_apply=True, export_yup=True)
    PARTS.append({"id": pid, "slot": slot, "archetype": archetype, "sockets": {k: G(v) for k, v in sockets.items()}, **(meta or {})})
    print("exported", path)


# ================= 胴 =================
def torso(arch):
    clear()
    k = {"heavy": 1.25, "fast": 0.85, "general": 1.0}[arch]
    slim = arch == "fast"   # 高速機は胴を低く絞る(戦闘機モードで胸の上側が張り出さないように)
    pelvis_h, abd_h, chest_h = 0.07 * H, 0.07 * H, 0.17 * H
    z_abd = pelvis_h
    z_chest = pelvis_h + abd_h
    bv = 0.008 * H
    # 腰: 節 waist(骨盤の上端)の子に骨盤・股・腰装甲を置く。変形時はこの節で腰から下を後ろへ折る
    waist = empty("waist", (0, 0, pelvis_h))
    facet("pelvis", (0, 0, 0), (0.17 * H * k, 0.12 * H), (0.22 * H * k, 0.14 * H), pelvis_h, M["frame"], parent=waist, bev=bv)
    facet("crotch", (0, 0.06 * H, -0.03 * H), (0.06 * H, 0.04 * H), (0.09 * H * k, 0.05 * H), pelvis_h * 1.0, M["main"], parent=waist, bev=bv * 0.6)
    for sx in (-1, 1):
        facet("skirt_front", (sx * 0.075 * H * k, 0.08 * H, -0.08 * H), (0.07 * H * k, 0.02 * H), (0.1 * H * k, 0.025 * H), 0.13 * H, M["main"],
              parent=waist, rot=(math.radians(-12), 0, math.radians(sx * -5)), bev=bv * 0.6)
        facet("skirt_side", (sx * 0.13 * H * k, 0, -0.08 * H), (0.02 * H, 0.08 * H), (0.025 * H, 0.1 * H), 0.14 * H, M["main"], parent=waist, rot=(0, math.radians(sx * 14), 0), bev=bv * 0.6)
    facet("skirt_rear", (0, -0.085 * H, -0.08 * H), (0.17 * H * k, 0.02 * H), (0.2 * H * k, 0.025 * H), 0.12 * H, M["sub"], parent=waist, rot=(math.radians(12), 0, 0), bev=bv * 0.6)
    # 腹: 回転体の蛇腹 + 前面の装甲
    revolve("waist", [(0.075 * H * k, 0), (0.09 * H * k, abd_h * 0.25), (0.08 * H * k, abd_h * 0.5), (0.09 * H * k, abd_h * 0.75), (0.085 * H * k, abd_h)], M["frame"], (0, 0, z_abd), 24)
    facet("abdomen_plate", (0, 0.075 * H * k, z_abd + abd_h * 0.05), (0.08 * H * k, 0.03 * H), (0.1 * H * k, 0.03 * H), abd_h * 0.9, M["sub"], bev=bv * 0.5)
    # 胸: 下胸の赤い台形塊(下が狭い) + 左右の胸郭塊(前傾・外開き・上面前方に黄の吸気口) + 中央の芯 + 肩の横梁 + 襟
    lower = facet("chest_lower", (0, 0.03 * H, z_chest), (0.16 * H * k, 0.13 * H), (0.26 * H * k, 0.17 * H), chest_h * 0.42, M["main"], bev=bv)
    facet("chest_core", (0, -0.01 * H, z_chest + chest_h * 0.38), (0.12 * H * k, 0.17 * H), (0.11 * H * k, 0.16 * H), chest_h * 0.62, M["sub"], bev=bv * 0.6)
    for sx in (-1, 1):
        cx = sx * 0.095 * H * k
        if slim:
            pec = facet("pectoral", (cx, 0.03 * H, z_chest + chest_h * 0.4), (0.12 * H * k, 0.13 * H), (0.1 * H * k, 0.1 * H), chest_h * 0.42, M["main"],
                        rot=(0, 0, math.radians(sx * -6)), shift=(sx * 0.005 * H, 0.02 * H), skew=0.004 * H, bev=bv)   # 低く、上へ細る
        else:
            pec = facet("pectoral", (cx, 0.02 * H, z_chest + chest_h * 0.4), (0.12 * H * k, 0.15 * H), (0.14 * H * k, 0.16 * H), chest_h * 0.58, M["sub"],
                        rot=(0, 0, math.radians(sx * -6)), shift=(sx * 0.012 * H, 0.015 * H), skew=0.012 * H, bev=bv)
        # 吸気口: 胸郭塊の前上面に黄の台形を載せ、暗い羽板を刻む
        iz = z_chest + chest_h * (0.5 if slim else 0.62)
        ih = chest_h * (0.22 if slim else 0.34)
        facet("intake", (cx, 0.085 * H, iz), (0.085 * H * k, 0.05 * H), (0.08 * H * k, 0.07 * H), ih, M["trim"], rot=(math.radians(-10), 0, math.radians(sx * -6)), shift=(0, 0.012 * H), bev=bv * 0.4)
        for i in range(3):
            facet("intake_slat", (cx, 0.11 * H + i * 0.003 * H, iz + ih * (0.2 + i * 0.27)), (0.065 * H * k, 0.01 * H), (0.065 * H * k, 0.01 * H), 0.012 * H, M["frame"], rot=(math.radians(-10), 0, math.radians(sx * -6)))
    br = 0.022 * H if slim else 0.04 * H
    revolve("shoulder_bar", [(br * 0.9, 0), (br, 0.03 * H), (br, 0.33 * H * k), (br * 0.9, 0.36 * H * k)], M["frame"], (-0.18 * H * k, -0.02 * H, z_chest + chest_h * (0.72 if slim else 0.82)), 18, "X")
    cr = 0.75 if slim else 1.0
    revolve("collar", [(0.06 * H * cr, 0), (0.075 * H * cr, 0.015 * H), (0.075 * H * cr, 0.03 * H), (0.055 * H * cr, 0.04 * H)], M["frame"], (0, -0.01 * H, z_chest + chest_h - 0.01 * H), 24)
    if slim:
        # 胸の上の覆い: 襟の前から前上へ細る楔。戦闘機モードでは機首へ流れる面になる
        facet("chest_fairing", (0, 0.03 * H, z_chest + chest_h * 0.78), (0.2 * H * k, 0.13 * H), (0.07 * H * k, 0.03 * H), chest_h * 0.24, M["main"], shift=(0, 0.04 * H), bev=bv * 0.6)
    else:
        facet("collar_plate", (0, 0.02 * H, z_chest + chest_h * 0.9), (0.12 * H * k, 0.1 * H), (0.1 * H * k, 0.08 * H), chest_h * 0.1, M["main"], bev=bv * 0.5)
    # コックピット扉(赤い下胸の中央) とリアクター(青い芯の中央・色が世代)
    facet("cockpit", (0, 0.095 * H, z_chest + chest_h * 0.05), (0.07 * H * k, 0.02 * H), (0.08 * H * k, 0.025 * H), chest_h * 0.3, M["main"], bev=bv * 0.4)
    rz = z_chest + chest_h * 0.6
    revolve("reactor_ring", [(0.04 * H, 0), (0.046 * H, 0.008 * H), (0.046 * H, 0.022 * H), (0.038 * H, 0.03 * H)], M["frame"], (0, 0.095 * H, rz), 28, "Y")   # 環は暗色(リアクターの色を立たせる)
    revolve("reactor_core", [(0.036 * H, 0), (0.034 * H, 0.006 * H), (0.022 * H, 0.016 * H), (0.008 * H, 0.022 * H)], M["reactor"], (0, 0.118 * H, rz), 28, "Y")
    for i in range(4):
        a = i * math.pi / 2 + math.pi / 4
        facet("reactor_rib", (0.046 * H * math.cos(a), 0.118 * H, rz + 0.046 * H * math.sin(a) - 0.01 * H), (0.006 * H, 0.014 * H), (0.006 * H, 0.014 * H), 0.02 * H, M["frame"], rot=(0, a + math.pi / 2, 0))
    # 背面の板
    facet("back", (0, -0.09 * H, z_chest + chest_h * 0.1), (0.22 * H * k, 0.025 * H if slim else 0.04 * H), (0.26 * H * k, 0.025 * H if slim else 0.04 * H), chest_h * 0.8, M["sub"], bev=bv * 0.6)
    if arch == "heavy":
        for sx in (-1, 1):
            facet("chest_armor2", (sx * 0.1 * H * k, 0.1 * H, z_chest + chest_h * 0.05), (0.1 * H * k, 0.02 * H), (0.12 * H * k, 0.025 * H), chest_h * 0.32, M["main"], bev=bv * 0.5)
    if arch == "fast":
        for sx in (-1, 1):
            facet("chest_fin", (sx * 0.16 * H * k, 0.0, z_chest + chest_h * 0.7), (0.05 * H, 0.015 * H), (0.03 * H, 0.012 * H), chest_h * 0.4, M["sub"], rot=(0, 0, math.radians(sx * -25)), bev=bv * 0.3)
    sockets = {
        "neck": (0, 0.0, z_chest + chest_h + 0.02 * H),
        "shoulder_l": (-0.19 * H * k, 0, z_chest + chest_h - 0.035 * H),
        "shoulder_r": (0.19 * H * k, 0, z_chest + chest_h - 0.035 * H),
        "hip_l": (-0.09 * H * (1 + (k - 1) * 0.5), 0, 0),
        "hip_r": (0.09 * H * (1 + (k - 1) * 0.5), 0, 0),
        "back": (0, -0.12 * H, z_chest + chest_h * 0.55),
        "waist": (0, 0, pelvis_h),                                # 腰の節の位置。脚と腰の武装はこの節の子
        "waist_rear": (0, -0.1 * H, pelvis_h * 0.9),            # 予備の武器 1(装備数の上限に数えない)
        "waist_l": (-0.15 * H * k, -0.02 * H, pelvis_h * 0.9),   # 近接武器(腰左)
        "waist_r": (0.15 * H * k, -0.02 * H, pelvis_h * 0.9),    # 近接武器(腰右)
    }
    if arch == "heavy":   # 肩上のバズーカ架(重装のみ。腕と合わせて左右計 4 本まで)
        for sx, nm in ((-1, "shoulder_mount_l"), (1, "shoulder_mount_r")):
            facet("shoulder_rail", (sx * 0.17 * H * k, -0.03 * H, z_chest + chest_h + 0.07 * H), (0.04 * H, 0.1 * H), (0.035 * H, 0.09 * H), 0.03 * H, M["frame"], bev=0.004 * H)
            sockets[nm] = (sx * 0.17 * H * k, -0.03 * H, z_chest + chest_h + 0.1 * H)
    export(f"torso_{arch}", "torso", arch, sockets)


# ================= 頭(顔のレパートリー) =================
def head(style):
    """style: twin(双眼) / visor(バイザー) / fast(鋭い双眼とセンサー翼) / mono(単眼) / quad(4 目) / tall(縦長の単スリット)"""
    clear()
    hh = {"visor": 0.1 * H, "fast": 0.085 * H, "tall": 0.13 * H}.get(style, 0.095 * H)
    w = {"visor": 0.1 * H, "fast": 0.075 * H, "mono": 0.09 * H, "tall": 0.06 * H}.get(style, 0.085 * H)
    bv = 0.004 * H
    revolve("neck", [(0.03 * H, 0), (0.026 * H, 0.015 * H), (0.03 * H, 0.03 * H)], M["frame"], (0, 0, -0.005 * H), 16)
    dep = 0.1 * H if style != "tall" else 0.08 * H
    facet("skull", (0, -0.01 * H, hh * 0.15), (w, dep), (w * 0.8, dep * 0.8), hh * 0.85, M["main"], shift=(0, -0.005 * H), bev=bv)
    facet("brow", (0, 0.04 * H, hh * 0.62), (w * 0.9, 0.03 * H), (w * 0.95, 0.04 * H), hh * 0.2, M["main"], shift=(0, 0.01 * H), bev=bv * 0.7)
    if style in ("twin", "fast", "quad"):
        facet("faceplate", (0, 0.045 * H, hh * 0.18), (w * 0.5, 0.02 * H), (w * 0.62, 0.025 * H), hh * 0.46, M["frame"], bev=bv * 0.5)
        if style == "quad":
            eyes = [(sx * 0.018 * H, hh * 0.54) for sx in (-1, 1)] + [(sx * 0.012 * H, hh * 0.42) for sx in (-1, 1)]
            ew = 0.011 * H
        else:
            eyes = [(sx * 0.016 * H, hh * 0.5) for sx in (-1, 1)]
            ew = 0.017 * H
        for ex, ez in eyes:
            facet("eye", (ex, 0.057 * H, ez), (ew, 0.004), (ew * 0.8, 0.004), hh * 0.07, M["glow"])
        for sx in (-1, 1):
            facet("cheek", (sx * w * 0.42, 0.025 * H, hh * 0.15), (0.025 * H, 0.05 * H), (0.03 * H, 0.055 * H), hh * 0.45, M["main"], rot=(0, 0, math.radians(sx * -8)), bev=bv * 0.6)
            facet("cheek_vent", (sx * w * 0.42, 0.052 * H, hh * 0.3), (0.012 * H, 0.004), (0.012 * H, 0.004), hh * 0.1, M["frame"])
        facet("chin", (0, 0.035 * H, hh * 0.0), (w * 0.4, 0.04 * H), (w * 0.5, 0.045 * H), hh * 0.18, M["frame"], bev=bv * 0.5)
        facet("mouth_vent", (0, 0.058 * H, hh * 0.26), (0.03 * H, 0.004), (0.026 * H, 0.004), hh * 0.08, M["frame"])
        fl = 0.045 * H if style == "fast" else 0.035 * H
        for sx in (-1, 1):
            facet("sensor_blade", (sx * 0.012 * H, 0.04 * H, hh * 0.84), (fl, 0.006 * H), (fl * 0.5, 0.004 * H), 0.008 * H, M["sub"],
                  rot=(0, math.radians(sx * -15), math.radians(sx * -20)), shift=(sx * fl * 0.1, 0), bev=bv * 0.3)
        facet("sensor_base", (0, 0.04 * H, hh * 0.8), (0.025 * H, 0.015 * H), (0.02 * H, 0.012 * H), 0.03 * H, M["frame"], bev=bv * 0.4)
        if style == "fast":
            facet("rear_fin", (0, -0.05 * H, hh * 0.6), (0.012 * H, 0.05 * H), (0.008 * H, 0.03 * H), hh * 0.5, M["sub"], shift=(0, -0.015 * H), bev=bv * 0.3)
    elif style == "visor":
        facet("visor_slot", (0, 0.045 * H, hh * 0.4), (w * 0.85, 0.02 * H), (w * 0.85, 0.02 * H), hh * 0.2, M["frame"], bev=bv * 0.3)
        facet("visor", (0, 0.056 * H, hh * 0.44), (w * 0.75, 0.004), (w * 0.7, 0.004), hh * 0.1, M["glow"])
        facet("jaw", (0, 0.03 * H, hh * 0.0), (w * 0.85, 0.06 * H), (w * 0.9, 0.07 * H), hh * 0.35, M["sub"], bev=bv * 0.6)
        facet("crest", (0, 0.02 * H, hh * 0.95), (0.012 * H, 0.07 * H), (0.008 * H, 0.05 * H), 0.03 * H, M["sub"], bev=bv * 0.3)
        for sx in (-1, 1):
            revolve("sensor", [(0.012 * H, 0), (0.016 * H, 0.01 * H), (0.012 * H, 0.02 * H)], M["trim"], (sx * w * 0.6, 0.0, hh * 0.55), 12, "X")
    elif style == "mono":
        # 単眼: 顔の中央に大きなレンズ(発光)と環、顎の覆い、動力管、アンテナ
        facet("face_slot", (0, 0.04 * H, hh * 0.3), (w * 0.8, 0.025 * H), (w * 0.85, 0.03 * H), hh * 0.3, M["frame"], bev=bv * 0.3)
        revolve("mono_ring", [(0.022 * H, 0), (0.024 * H, 0.012 * H), (0.018 * H, 0.02 * H)], M["sub"], (0, 0.052 * H, hh * 0.46), 20, "Y")
        revolve("mono_eye", [(0.016 * H, 0), (0.012 * H, 0.012 * H), (0.004 * H, 0.016 * H)], M["glow"], (0, 0.06 * H, hh * 0.46), 20, "Y")
        facet("jaw", (0, 0.03 * H, hh * 0.0), (w * 0.8, 0.06 * H), (w * 0.85, 0.07 * H), hh * 0.3, M["sub"], bev=bv * 0.6)
        facet("pipe", (0, 0.055 * H, hh * 0.1), (w * 0.5, 0.012 * H), (w * 0.45, 0.012 * H), hh * 0.08, M["frame"])
        revolve("antenna", [(0.005 * H, 0), (0.004 * H, 0.06 * H)], M["frame"], (w * 0.35, -0.02 * H, hh * 0.95), 8)
    elif style == "tall":
        # 縦長: 狭い兜に縦のスリット眼、額の稜、側面の覆い
        facet("face_slot", (0, 0.03 * H, hh * 0.2), (w * 0.5, 0.02 * H), (w * 0.45, 0.02 * H), hh * 0.55, M["frame"], bev=bv * 0.3)
        facet("slit_eye", (0, 0.04 * H, hh * 0.3), (0.006 * H, 0.004), (0.005 * H, 0.004), hh * 0.35, M["glow"])
        facet("ridge", (0, 0.035 * H, hh * 0.75), (0.012 * H, 0.03 * H), (0.008 * H, 0.02 * H), hh * 0.3, M["sub"], bev=bv * 0.3)
        facet("chin", (0, 0.025 * H, hh * 0.0), (w * 0.6, 0.04 * H), (w * 0.7, 0.045 * H), hh * 0.18, M["frame"], bev=bv * 0.5)
        for sx in (-1, 1):
            facet("ear", (sx * w * 0.5, -0.01 * H, hh * 0.35), (0.012 * H, 0.04 * H), (0.01 * H, 0.03 * H), hh * 0.4, M["sub"], bev=bv * 0.3)
    for sx in (-1, 1):
        revolve("vulcan", [(0.006 * H, 0), (0.006 * H, 0.02 * H)], M["frame"], (sx * w * 0.32, 0.045 * H, hh * 0.78), 10, "Y")
    export(f"head_{style}", "head", {"visor": "heavy", "fast": "fast"}.get(style, "general"), {})


# ================= 腕(肩装甲 + 腕 + 手) =================
def arm(arch, side):
    clear()
    sx = -1 if side == "l" else 1
    k = {"heavy": 1.2, "fast": 0.85, "general": 1.0}[arch]
    up_h, lo_h = 0.16 * H, 0.17 * H
    bv = 0.006 * H
    dome("shoulder_joint", (0, 0, 0), 0.04 * H, M["frame"], cut_z=-0.05 * H)
    joint("shoulder_axle", (sx * 0.03 * H, 0, 0), 0.025 * H, 0.08 * H, M["frame"], "X")
    # 肩装甲: 角張った台形塊(上が広く張り出す・外側面が斜め) + 上板 + 外側の縁
    if arch == "heavy":
        facet("pauldron", (sx * 0.05 * H, 0, -0.03 * H), (0.1 * H, 0.13 * H), (0.15 * H, 0.17 * H), 0.11 * H, M["main"], shift=(sx * 0.02 * H, 0), bev=bv)
        facet("pauldron_top", (sx * 0.06 * H, 0, 0.08 * H), (0.16 * H, 0.18 * H), (0.13 * H, 0.15 * H), 0.025 * H, M["sub"], bev=bv * 0.7)
        facet("pauldron_slot", (sx * 0.09 * H, 0, 0.1 * H), (0.03 * H, 0.14 * H), (0.025 * H, 0.12 * H), 0.012 * H, M["trim"])
        for i in range(2):
            revolve("spike", [(0.012 * H, 0), (0.002 * H, 0.05 * H)], M["steel"], (sx * 0.1 * H, -0.04 * H + i * 0.08 * H, 0.1 * H), 10)
    elif arch == "fast":
        # 小さな羽根の肩(節 pauldron_pivot で回す)。人型では立てて肩当てに、戦闘機モードでは水平に広げて前翼にする
        piv = empty("pauldron_pivot", (sx * 0.03 * H, 0, 0.03 * H))
        facet("pauldron_base", (sx * 0.03 * H, 0, 0.0), (0.06 * H, 0.08 * H), (0.05 * H, 0.07 * H), 0.04 * H, M["sub"], bev=bv * 0.5)
        pts = [(sx * 0.0, 0.03 * H), (sx * 0.0, -0.05 * H), (sx * 0.2 * H, -0.13 * H), (sx * 0.2 * H, -0.09 * H)]
        if sx < 0:
            pts = pts[::-1]
        w = slab("shoulder_wing", pts, -0.006 * H, 0.012 * H, M["main"], parent=piv, bev=bv * 0.4)
        w.location = (sx * 0.03 * H, 0, 0.03 * H)
        edge = [(sx * 0.01 * H, 0.03 * H), (sx * 0.01 * H, 0.018 * H), (sx * 0.2 * H, -0.1 * H), (sx * 0.2 * H, -0.09 * H)]
        if sx < 0:
            edge = edge[::-1]
        e = slab("shoulder_wing_edge", edge, 0.006 * H, 0.004 * H, M["glow"], parent=piv)
        e.location = (sx * 0.03 * H, 0, 0.03 * H)
        thruster("pauldron_thruster", (sx * 0.06 * H, -0.08 * H, 0.03 * H), 0.025 * H, 0.06 * H, M["frame"], M["glow"], "Y")
    else:
        facet("pauldron", (sx * 0.045 * H, 0, -0.03 * H), (0.09 * H, 0.12 * H), (0.13 * H, 0.15 * H), 0.1 * H, M["main"], shift=(sx * 0.02 * H, 0), bev=bv)
        facet("pauldron_top", (sx * 0.055 * H, 0, 0.07 * H), (0.14 * H, 0.16 * H), (0.11 * H, 0.13 * H), 0.025 * H, M["main"], bev=bv * 0.7)
        facet("pauldron_trim", (sx * 0.1 * H, 0, 0.045 * H), (0.02 * H, 0.08 * H), (0.015 * H, 0.06 * H), 0.03 * H, M["sub"], bev=bv * 0.4)
    # 上腕: 骨組みの回転体 + 外側の装甲板(台形)
    revolve("upper_arm", [(0.03 * H * k, 0), (0.034 * H * k, -up_h * 0.3), (0.03 * H * k, -up_h)], M["frame"], (0, 0, 0), 16)
    facet("upper_arm_plate", (sx * 0.03 * H * k, 0, -up_h * 0.85), (0.025 * H, 0.06 * H * k), (0.03 * H, 0.075 * H * k), up_h * 0.7, M["main"], bev=bv * 0.6)
    facet("upper_arm_front", (0, 0.035 * H * k, -up_h * 0.85), (0.045 * H * k, 0.02 * H), (0.055 * H * k, 0.02 * H), up_h * 0.6, M["sub"], bev=bv * 0.5)
    joint("elbow_cyl", (0, 0, -up_h), 0.035 * H * k, 0.085 * H * k, M["frame"], "X")   # 節の空 "elbow" と名前が衝突しないように
    elbow = empty("elbow", (0, 0, -up_h))
    # 前腕: 肘側が細く手首へ広がる台形塊(ガンダム系の前腕)、前面と外側の装甲、肘当て
    facet("forearm", (0, 0, -up_h - lo_h * 0.95), (0.075 * H * k, 0.08 * H * k), (0.06 * H * k, 0.065 * H * k), lo_h * 0.85, M["main"], parent=elbow, bev=bv)
    facet("forearm_inner", (0, 0, -up_h - lo_h), (0.05 * H * k, 0.06 * H * k), (0.055 * H * k, 0.065 * H * k), lo_h * 0.3, M["frame"], parent=elbow)
    facet("forearm_outer", (sx * 0.04 * H * k, 0, -up_h - lo_h * 0.9), (0.02 * H, 0.06 * H * k), (0.015 * H, 0.05 * H * k), lo_h * 0.6, M["sub"], parent=elbow, bev=bv * 0.5)
    facet("elbow_guard", (0, -0.035 * H * k, -up_h - 0.03 * H), (0.05 * H * k, 0.02 * H), (0.045 * H * k, 0.02 * H), 0.06 * H, M["main"], parent=elbow, bev=bv * 0.5)
    if arch == "heavy":
        facet("forearm_shield", (sx * 0.065 * H * k, 0.01 * H, -up_h - lo_h * 0.95), (0.025 * H, 0.12 * H), (0.025 * H, 0.14 * H), lo_h * 0.9, M["main"], parent=elbow, bev=bv * 0.6)
    if arch == "fast":
        facet("forearm_fin", (sx * 0.05 * H * k, -0.03 * H, -up_h - lo_h * 0.6), (0.01 * H, 0.05 * H), (0.008 * H, 0.02 * H), lo_h * 0.45, M["trim"], parent=elbow, rot=(math.radians(15), 0, 0), bev=bv * 0.3)
    # 手首と手
    hz = -up_h - lo_h - 0.015 * H
    revolve("wrist", [(0.028 * H * k, 0), (0.024 * H * k, 0.015 * H), (0.028 * H * k, 0.03 * H)], M["frame"], (0, 0, hz - 0.01 * H), 14, parent=elbow)
    facet("palm", (0, 0.01 * H, hz - 0.055 * H), (0.05 * H * k, 0.038 * H), (0.055 * H * k, 0.04 * H), 0.05 * H, M["frame"], parent=elbow, bev=bv * 0.5)
    facet("hand_back", (0, -0.005 * H, hz - 0.035 * H), (0.05 * H * k, 0.02 * H), (0.045 * H * k, 0.02 * H), 0.03 * H, M["main"], parent=elbow, rot=(math.radians(10), 0, 0), bev=bv * 0.4)
    for i in range(4):
        rounded("finger", (-0.02 * H * k + i * 0.0135 * H * k, 0.03 * H, hz - 0.055 * H), (0.011 * H, 0.025 * H, 0.03 * H), M["frame"], 0.003 * H, parent=elbow, segs=2)
    rounded("thumb", (sx * 0.03 * H * k, 0.015 * H, hz - 0.035 * H), (0.012 * H, 0.03 * H, 0.025 * H), M["frame"], 0.003 * H, parent=elbow, segs=2)
    sockets = {"hand": (0, 0.03 * H, hz - 0.04 * H), "elbow": (0, 0, -up_h)}
    export(f"arm_{arch}_{side}", f"arm_{side}", arch, sockets, {"length": round(up_h + lo_h + 0.07 * H, 3)})


# ================= 脚(腿 + 膝 + 脛 + 足) =================
def leg(arch, side):
    clear()
    sx = -1 if side == "l" else 1
    k = {"heavy": 1.2, "fast": 0.88, "general": 1.0}[arch]
    lk = {"heavy": 0.95, "fast": 1.15, "general": 1.0}[arch]
    thigh_h, shin_h = 0.235 * H * lk, 0.235 * H * lk
    foot_h = 0.045 * H
    bv = 0.007 * H
    dome("hip_joint", (0, 0, 0), 0.045 * H, M["frame"], cut_z=-0.06 * H)
    # 腿: 股側が太い台形塊 + 前面と外側の装甲
    facet("thigh", (0, 0, -thigh_h), (0.075 * H * k, 0.09 * H * k), (0.095 * H * k, 0.11 * H * k), thigh_h * 0.95, M["main"], bev=bv)
    facet("thigh_front", (0, 0.05 * H * k, -thigh_h * 0.95), (0.08 * H * k, 0.025 * H), (0.1 * H * k, 0.03 * H), thigh_h * 0.75, M["main"], bev=bv * 0.6)
    facet("thigh_side", (sx * 0.05 * H * k, 0, -thigh_h * 0.9), (0.025 * H, 0.07 * H * k), (0.03 * H, 0.09 * H * k), thigh_h * 0.6, M["sub"], bev=bv * 0.5)
    joint("knee_cyl", (0, 0, -thigh_h), 0.042 * H * k, 0.1 * H * k, M["frame"], "X")   # 節の空 "knee" と名前が衝突しないように
    knee = empty("knee", (0, 0, -thigh_h))
    # 膝: 角張った膝当て(台形)と差し色
    facet("knee_cap", (0, 0.045 * H * k, -thigh_h - 0.05 * H), (0.07 * H * k, 0.035 * H), (0.08 * H * k, 0.04 * H), 0.1 * H, M["main"], parent=knee, shift=(0, 0.005 * H), bev=bv * 0.7)
    facet("knee_trim", (0, 0.07 * H * k, -thigh_h - 0.01 * H), (0.045 * H * k, 0.012 * H), (0.04 * H * k, 0.012 * H), 0.03 * H, M["trim"], parent=knee, bev=bv * 0.3)
    # 脛: 膝側が太く足首へ細る台形塊 + 前面の稜(縦の楔) + ふくらはぎ
    facet("shin", (0, 0, -thigh_h - shin_h), (0.07 * H * k, 0.085 * H * k), (0.085 * H * k, 0.1 * H * k), shin_h * 0.95, M["main"], parent=knee, bev=bv)
    facet("shin_ridge", (0, 0.05 * H * k, -thigh_h - shin_h * 0.95), (0.02 * H, 0.02 * H), (0.035 * H * k, 0.03 * H), shin_h * 0.8, M["main"], parent=knee, bev=bv * 0.4)
    facet("shin_vent", (sx * 0.03 * H * k, 0.048 * H * k, -thigh_h - shin_h * 0.7), (0.02 * H, 0.008 * H), (0.02 * H, 0.008 * H), shin_h * 0.25, M["sub"], parent=knee, rot=(0, 0, math.radians(sx * -15)))
    facet("calf", (0, -0.045 * H * k, -thigh_h - shin_h * 0.75), (0.055 * H * k, 0.04 * H), (0.07 * H * k, 0.045 * H), shin_h * 0.6, M["sub"], parent=knee, bev=bv * 0.6)
    if arch == "fast":
        thruster("calf_thruster", (0, -0.075 * H * k, -thigh_h - shin_h * 0.6), 0.025 * H, 0.07 * H, M["frame"], M["glow"], "Z", parent=knee)
        facet("shin_fin", (sx * 0.05 * H * k, -0.02 * H, -thigh_h - shin_h * 0.55), (0.01 * H, 0.05 * H), (0.008 * H, 0.025 * H), shin_h * 0.4, M["trim"], parent=knee, bev=bv * 0.3)
    if arch == "heavy":
        # ホバー: 足の外側に下向きのスラスター(重装は飛べず、燃料を使って陸上をホバーで移動する)
        thruster("hover_pod", (sx * 0.075 * H * k, 0.0, -thigh_h - shin_h + 0.02 * H), 0.03 * H, 0.07 * H, M["frame"], M["glow"], "Z", parent=knee)
        facet("hover_fairing", (sx * 0.075 * H * k, 0.0, -thigh_h - shin_h + 0.085 * H), (0.07 * H, 0.08 * H), (0.05 * H, 0.06 * H), 0.05 * H, M["main"], parent=knee, bev=bv * 0.5)
        facet("shin_armor2", (sx * 0.055 * H * k, 0.02 * H, -thigh_h - shin_h * 0.9), (0.03 * H, 0.08 * H * k), (0.03 * H, 0.09 * H * k), shin_h * 0.7, M["main"], parent=knee, bev=bv * 0.5)
    # 足首と足: 角張った足(爪先の楔・かかと) + 足首の覆い
    z_ankle = -thigh_h - shin_h
    joint("ankle", (0, 0, z_ankle + 0.02 * H), 0.03 * H * k, 0.09 * H * k, M["frame"], "X", parent=knee)
    fz = z_ankle - 0.02 * H
    facet("foot", (0, 0.0, fz), (0.085 * H * k, 0.1 * H), (0.075 * H * k, 0.09 * H), foot_h, M["main"], parent=knee, bev=bv * 0.7)
    facet("toe", (0, 0.085 * H, fz), (0.085 * H * k, 0.07 * H), (0.07 * H * k, 0.03 * H), foot_h * 0.8, M["main"], parent=knee, shift=(0, -0.02 * H), bev=bv * 0.6)
    facet("heel", (0, -0.065 * H, fz), (0.075 * H * k, 0.04 * H), (0.065 * H * k, 0.03 * H), foot_h * 0.85, M["frame"], parent=knee, shift=(0, 0.005 * H), bev=bv * 0.5)
    facet("ankle_guard", (0, 0.03 * H, fz + foot_h - 0.005 * H), (0.075 * H * k, 0.06 * H), (0.085 * H * k, 0.065 * H), 0.045 * H, M["sub"], parent=knee, bev=bv * 0.5)
    if arch != "heavy":
        for s2 in (-1, 1):
            revolve("sole_vent", [(0.012 * H, 0), (0.012 * H, 0.01 * H)], M["glow"], (s2 * 0.025 * H * k, -0.04 * H, z_ankle - 0.02 * H), 10)
    length = thigh_h + shin_h + foot_h - 0.02 * H
    export(f"leg_{arch}_{side}", f"leg_{side}", arch, {"knee": (0, 0, -thigh_h)}, {"length": round(length, 3)})


# ================= 背部 =================
def back(arch):
    clear()
    bv = 0.006 * H
    if arch == "heavy":
        facet("pack", (0, -0.03 * H, -0.08 * H), (0.22 * H, 0.07 * H), (0.24 * H, 0.08 * H), 0.16 * H, M["sub"], bev=bv)
        for sx in (-1, 1):
            thruster("thruster", (sx * 0.05 * H, -0.06 * H, -0.06 * H), 0.03 * H, 0.08 * H, M["frame"], M["glow"], "Z")
            facet("ammo_box", (sx * 0.09 * H, -0.05 * H, -0.02 * H), (0.05 * H, 0.05 * H), (0.045 * H, 0.045 * H), 0.1 * H, M["frame"], bev=bv * 0.5)
    elif arch == "fast":
        facet("pack", (0, -0.015 * H, -0.07 * H), (0.14 * H, 0.03 * H), (0.16 * H, 0.035 * H), 0.14 * H, M["sub"], bev=bv)   # 薄い背嚢
        for sx in (-1, 1):
            for i in range(2):
                thruster("thruster", (sx * (0.035 + i * 0.04) * H, -0.035 * H, -0.05 * H), 0.02 * H, 0.08 * H, M["frame"], M["glow"], "Z")
    else:
        facet("pack", (0, -0.025 * H, -0.075 * H), (0.18 * H, 0.06 * H), (0.2 * H, 0.07 * H), 0.15 * H, M["sub"], bev=bv)
        for sx in (-1, 1):
            thruster("thruster", (sx * 0.065 * H, -0.05 * H, -0.06 * H), 0.03 * H, 0.08 * H, M["frame"], M["glow"], "Z")
            sb = revolve("saber", [(0.011 * H, 0), (0.012 * H, 0.1 * H), (0.009 * H, 0.12 * H)], M["frame"], (sx * 0.11 * H, -0.02 * H, 0.06 * H), 10)
            sb.rotation_euler = (0, math.radians(sx * 20), 0)
    export(f"back_{arch}", "back", arch, {})



# ================= 翼(高速機。フラッグの変形を参考: 主翼は背面の架に付き、人型では畳み、飛行形態では水平に広げる) =================
def wing(arch):
    clear()
    bv = 0.003 * H
    facet("wing_root", (0, -0.02 * H, -0.03 * H), (0.16 * H, 0.08 * H), (0.14 * H, 0.07 * H), 0.06 * H, M["sub"], bev=bv)
    for sx in (-1, 1):
        piv = empty("wing_l" if sx < 0 else "wing_r", (sx * 0.06 * H, 0, 0))
        pts = [(sx * 0.0, 0.04 * H), (sx * 0.0, -0.14 * H), (sx * 0.5 * H, -0.4 * H), (sx * 0.5 * H, -0.32 * H)]
        if sx < 0:
            pts = pts[::-1]
        w = slab("wing", pts, -0.01 * H, 0.02 * H, M["main"], parent=piv, bev=bv)
        w.location = (sx * 0.06 * H, 0, 0)
        edge = [(sx * 0.01 * H, 0.04 * H), (sx * 0.01 * H, 0.025 * H), (sx * 0.5 * H, -0.315 * H), (sx * 0.5 * H, -0.32 * H)]
        if sx < 0:
            edge = edge[::-1]
        e = slab("wing_edge", edge, 0.01 * H, 0.004 * H, M["glow"], parent=piv)
        e.location = (sx * 0.06 * H, 0, 0)
        facet("wing_pod", (sx * 0.3 * H, -0.2 * H, -0.025 * H), (0.035 * H, 0.12 * H), (0.03 * H, 0.1 * H), 0.05 * H, M["sub"], parent=piv, bev=bv)
    # 尾翼: XY で描き Y 軸回りに +90 度回して立てる(x → 高さ、y → 前後。-y が後方)。-90 だと下向きに出る
    fin = slab("tail_fin", [(0.0, 0.02 * H), (0.0, -0.14 * H), (-0.2 * H, -0.2 * H), (-0.2 * H, -0.11 * H)], -0.006 * H, 0.012 * H, M["main"], bev=bv)
    fin.rotation_euler = (0, math.radians(90), 0)
    fin.location = (0, 0, 0)
    export(f"wing_{arch}", "wing", arch, {})


# ================= 武装 =================
def weapon(kind, ammo):
    """kind: bazooka / rifle / smg / sword / whip / shield。ammo: live(実弾) / beam(ビーム)。原点は握り、銃口は +Y。"""
    clear()
    bv = 0.004 * H
    beam = ammo == "beam"
    bmat = M["glow"]
    if kind == "bazooka":
        facet("grip", (0, 0, -0.045 * H), (0.02 * H, 0.03 * H), (0.018 * H, 0.028 * H), 0.05 * H, M["frame"], bev=bv * 0.5)
        r = 0.032 * H if not beam else 0.026 * H
        revolve("tube", [(r, 0), (r, 0.46 * H), (r * 1.25, 0.47 * H), (r * 1.25, 0.52 * H)], M["sub"], (0, -0.16 * H, 0.05 * H), 18, "Y")
        if beam:
            for i in range(3):
                revolve("coil", [(r * 1.15, 0), (r * 1.15, 0.02 * H)], bmat, (0, 0.04 * H + i * 0.09 * H, 0.05 * H), 18, "Y")
            revolve("emitter", [(r * 0.6, 0), (r * 0.3, 0.02 * H)], bmat, (0, 0.36 * H, 0.05 * H), 18, "Y")
        else:
            facet("magazine", (0, -0.04 * H, -0.03 * H), (0.045 * H, 0.09 * H), (0.04 * H, 0.08 * H), 0.06 * H, M["frame"], bev=bv * 0.5)
            facet("shell_rack", (0, -0.1 * H, 0.08 * H), (0.03 * H, 0.1 * H), (0.03 * H, 0.1 * H), 0.025 * H, M["steel"], bev=bv * 0.3)
        facet("sight", (0, 0.08 * H, 0.085 * H), (0.015 * H, 0.05 * H), (0.012 * H, 0.04 * H), 0.03 * H, M["frame"], bev=bv * 0.3)
        facet("shoulder_rest", (0, -0.14 * H, 0.0), (0.03 * H, 0.08 * H), (0.03 * H, 0.08 * H), 0.03 * H, M["sub"], bev=bv * 0.4)
    elif kind == "rifle":
        facet("body", (0, 0.02 * H, 0.0), (0.035 * H, 0.32 * H), (0.03 * H, 0.3 * H), 0.05 * H, M["frame"], bev=bv)   # 握りから銃身の付け根まで切れ目なく
        if beam:
            revolve("barrel", [(0.014 * H, 0), (0.012 * H, 0.22 * H), (0.018 * H, 0.23 * H), (0.018 * H, 0.26 * H)], M["sub"], (0, 0.17 * H, 0.03 * H), 14, "Y")
            revolve("emitter", [(0.012 * H, 0), (0.005 * H, 0.015 * H)], bmat, (0, 0.42 * H, 0.03 * H), 14, "Y")
            facet("coil", (0, 0.12 * H, 0.047 * H), (0.014 * H, 0.1 * H), (0.012 * H, 0.09 * H), 0.006 * H, bmat)
        else:
            revolve("barrel", [(0.012 * H, 0), (0.012 * H, 0.24 * H), (0.016 * H, 0.25 * H), (0.016 * H, 0.28 * H)], M["steel"], (0, 0.17 * H, 0.03 * H), 14, "Y")
            facet("magazine", (0, 0.05 * H, -0.06 * H), (0.025 * H, 0.05 * H), (0.022 * H, 0.045 * H), 0.07 * H, M["frame"], bev=bv * 0.4)
        revolve("scope", [(0.012 * H, 0), (0.012 * H, 0.1 * H)], M["sub"], (0, 0.06 * H, 0.06 * H), 12, "Y")
        facet("stock", (0, -0.09 * H, -0.01 * H), (0.03 * H, 0.08 * H), (0.025 * H, 0.07 * H), 0.04 * H, M["sub"], bev=bv)
    elif kind == "smg":
        facet("body", (0, 0.0, 0.0), (0.04 * H, 0.14 * H), (0.035 * H, 0.13 * H), 0.05 * H, M["frame"], bev=bv)
        if beam:
            revolve("barrel", [(0.012 * H, 0), (0.012 * H, 0.07 * H)], M["sub"], (0, 0.07 * H, 0.03 * H), 12, "Y")
            revolve("emitter", [(0.01 * H, 0), (0.004 * H, 0.012 * H)], bmat, (0, 0.14 * H, 0.03 * H), 12, "Y")
            revolve("cell", [(0.02 * H, 0), (0.02 * H, 0.04 * H)], bmat, (0, 0.01 * H, -0.03 * H), 12, "Y")
        else:
            revolve("barrel", [(0.011 * H, 0), (0.011 * H, 0.08 * H)], M["steel"], (0, 0.07 * H, 0.03 * H), 12, "Y")
            revolve("drum", [(0.03 * H, 0), (0.03 * H, 0.035 * H)], M["sub"], (-0.0175 * H, 0.02 * H, -0.02 * H), 16, "X")
        facet("foregrip", (0, 0.06 * H, -0.03 * H), (0.015 * H, 0.02 * H), (0.015 * H, 0.02 * H), 0.03 * H, M["frame"])
        facet("stock", (0, -0.08 * H, 0.01 * H), (0.02 * H, 0.04 * H), (0.02 * H, 0.04 * H), 0.02 * H, M["sub"], bev=bv * 0.4)
    elif kind == "sword":
        facet("grip", (0, 0, -0.07 * H), (0.02 * H, 0.03 * H), (0.02 * H, 0.03 * H), 0.08 * H, M["frame"], bev=bv * 0.5)
        facet("guard", (0, 0, 0.01 * H), (0.07 * H, 0.03 * H), (0.06 * H, 0.025 * H), 0.015 * H, M["sub"], bev=bv * 0.4)
        if beam:
            facet("emitter", (0, 0, 0.025 * H), (0.03 * H, 0.015 * H), (0.025 * H, 0.012 * H), 0.03 * H, M["frame"], bev=bv * 0.3)
            facet("blade", (0, 0, 0.05 * H), (0.028 * H, 0.012 * H), (0.008 * H, 0.004 * H), 0.42 * H, bmat)
        else:
            facet("blade", (0, 0, 0.025 * H), (0.035 * H, 0.012 * H), (0.008 * H, 0.004 * H), 0.42 * H, M["steel"], bev=bv * 0.3)
            facet("fuller", (0, 0.0065 * H, 0.06 * H), (0.008 * H, 0.002), (0.004 * H, 0.002), 0.3 * H, M["frame"])
    elif kind == "whip":
        facet("grip", (0, 0, -0.07 * H), (0.02 * H, 0.03 * H), (0.02 * H, 0.03 * H), 0.08 * H, M["frame"], bev=bv * 0.5)
        facet("emitter", (0, 0, 0.01 * H), (0.03 * H, 0.03 * H), (0.025 * H, 0.025 * H), 0.03 * H, M["sub"], bev=bv * 0.4)
        # 鞭: 握りから前へ出て垂れる 14 節(放物線)。ビームは発光する節、実弾(物理)は金属の節と継ぎ目
        n = 14
        for i in range(n):
            t = i / (n - 1)
            y = 0.04 * H + t * 0.4 * H
            z = 0.04 * H - t * t * 0.42 * H
            r = (0.012 - 0.006 * t) * H
            if beam:
                revolve("whip_seg", [(r * 0.8, 0), (r * 0.8, 0.026 * H)], bmat, (0, y, z), 10, "Y")
            else:
                revolve("whip_seg", [(r, 0), (r * 0.9, 0.012 * H), (r, 0.024 * H)], M["steel"], (0, y, z), 10, "Y")
                revolve("whip_joint", [(r * 0.6, 0), (r * 0.6, 0.008 * H)], M["frame"], (0, y + 0.026 * H, z - 0.002 * H), 8, "Y")
    elif kind == "shield":
        if beam:
            # ビーム盾: 前腕の外側に発振器の板、そこから発光する面が広がる
            facet("emitter", (-0.05 * H, 0.03 * H, -0.06 * H), (0.03 * H, 0.08 * H), (0.025 * H, 0.07 * H), 0.12 * H, M["frame"], rot=(0, math.radians(90), 0), bev=bv)
            for i in range(3):
                facet("emitter_node", (-0.066 * H, 0.03 * H, -0.04 * H + i * 0.04 * H), (0.012 * H, 0.03 * H), (0.01 * H, 0.025 * H), 0.012 * H, bmat, rot=(0, math.radians(90), 0))
            facet("field", (-0.075 * H, 0.03 * H, -0.16 * H), (0.2 * H, 0.004), (0.16 * H, 0.004), 0.34 * H, bmat, rot=(0, math.radians(90), 0))
        else:
            facet("shield", (-0.05 * H, 0.03 * H, -0.17 * H), (0.17 * H, 0.03 * H), (0.14 * H, 0.03 * H), 0.34 * H, M["main"], rot=(0, math.radians(90), 0), bev=bv * 1.5)
            facet("rim", (-0.065 * H, 0.03 * H, -0.15 * H), (0.14 * H, 0.008 * H), (0.11 * H, 0.008 * H), 0.3 * H, M["sub"], rot=(0, math.radians(90), 0), bev=bv)
            facet("emblem", (-0.07 * H, 0.03 * H, 0.04 * H), (0.04 * H, 0.004), (0.03 * H, 0.004), 0.04 * H, M["trim"], rot=(0, math.radians(90), 0))
            facet("glow", (-0.07 * H, 0.03 * H, -0.09 * H), (0.08 * H, 0.004), (0.07 * H, 0.004), 0.008 * H, bmat, rot=(0, math.radians(90), 0))
    export(f"weapon_{kind}_{ammo}", "weapon", "general", {}, {"weapon": kind, "ammo": ammo, "melee": kind in ("sword", "whip")})
    if kind == "whip":
        # 腰に吊る用の巻いた鞭(握り + 発振部 + 3 巻きの輪)。伸びた形は吊ると向きが崩れるため別部品にする
        clear()
        bmat = M["glow"]   # clear() で材質が作り直されるため取り直す
        facet("grip", (0, 0, -0.07 * H), (0.02 * H, 0.03 * H), (0.02 * H, 0.03 * H), 0.08 * H, M["frame"], bev=bv * 0.5)
        facet("emitter", (0, 0, 0.01 * H), (0.03 * H, 0.03 * H), (0.025 * H, 0.025 * H), 0.03 * H, M["sub"], bev=bv * 0.4)
        R, r = 0.05 * H, 0.008 * H
        ring = [(R + r * math.cos(a), r * math.sin(a)) for a in [i * 2 * math.pi / 10 for i in range(11)]]
        for i in range(3):
            revolve("coil", ring, bmat if beam else M["steel"], (0, 0.01 * H + i * 0.018 * H, 0.09 * H), 20, "Y")
        export(f"weapon_{kind}_{ammo}_coiled", "weapon", "general", {}, {"weapon": kind, "ammo": ammo, "melee": True, "coiled": True})


if __name__ == "__main__":
    for style in ("twin", "visor", "fast", "mono", "quad", "tall"):
        head(style)
    for arch in ("heavy", "fast", "general"):
        torso(arch)
        back(arch)
        for side in ("l", "r"):
            arm(arch, side)
            leg(arch, side)
    wing("fast")
    for w in ("bazooka", "rifle", "smg", "sword", "whip", "shield"):
        for am in ("live", "beam"):
            weapon(w, am)
    JSON_OUT.parent.mkdir(parents=True, exist_ok=True)
    presets = {
        "heavy": {"torso": "torso_heavy", "head": "head_visor", "arm_l": "arm_heavy_l", "arm_r": "arm_heavy_r", "leg_l": "leg_heavy_l", "leg_r": "leg_heavy_r", "back": "back_heavy", "wing": None,
                  "weapon_r": "weapon_bazooka_live", "weapon_l": "weapon_bazooka_beam", "shoulder_l": "weapon_bazooka_live", "shoulder_r": "weapon_bazooka_beam",
                  "melee_l": "weapon_sword_live", "melee_r": "weapon_whip_live", "spare": "weapon_rifle_live", "colors": {"main": [0.3, 0.33, 0.3], "sub": [0.18, 0.2, 0.2], "trim": [0.85, 0.6, 0.15], "glow": [1.0, 0.45, 0.1]}},
        "fast": {"torso": "torso_fast", "head": "head_fast", "arm_l": "arm_fast_l", "arm_r": "arm_fast_r", "leg_l": "leg_fast_l", "leg_r": "leg_fast_r", "back": "back_fast", "wing": "wing_fast",
                 "weapon_r": "weapon_rifle_beam", "weapon_l": None, "shoulder_l": None, "shoulder_r": None,
                 "melee_l": "weapon_sword_beam", "melee_r": "weapon_whip_beam", "spare": "weapon_rifle_beam", "colors": {"main": [0.85, 0.2, 0.2], "sub": [0.15, 0.15, 0.18], "trim": [0.95, 0.85, 0.3], "glow": [0.4, 1.0, 0.6]}},
        "general": {"torso": "torso_general", "head": "head_twin", "arm_l": "arm_general_l", "arm_r": "arm_general_r", "leg_l": "leg_general_l", "leg_r": "leg_general_r", "back": "back_general", "wing": None,
                    "weapon_r": "weapon_rifle_beam", "weapon_l": "weapon_shield_beam", "shoulder_l": None, "shoulder_r": None,
                    "melee_l": "weapon_sword_beam", "melee_r": "weapon_whip_live", "spare": "weapon_bazooka_live", "colors": {"main": [0.88, 0.9, 0.93], "sub": [0.2, 0.3, 0.55], "trim": [0.95, 0.75, 0.2], "glow": [0.25, 0.85, 1.0]}},
    }
    generations = {   # 世代はリアクターの色で表す(2026-10-14 指示: 赤・橙・黄・黄緑・緑・青・紫。第 0 = 工作機、第 1〜6 が世代。第 5 不完全ステルス、第 6 完全ステルス)
        "g0": {"name": "工作機", "reactor": [1.0, 0.2, 0.15]},
        "g1": {"name": "第 1 世代", "reactor": [1.0, 0.5, 0.1]},
        "g2": {"name": "第 2 世代", "reactor": [1.0, 0.9, 0.2]},
        "g3": {"name": "第 3 世代", "reactor": [0.6, 1.0, 0.2]},
        "g4": {"name": "第 4 世代", "reactor": [0.2, 1.0, 0.4]},
        "g5": {"name": "第 5 世代", "reactor": [0.25, 0.5, 1.0]},
        "g6": {"name": "第 6 世代", "reactor": [0.75, 0.35, 1.0]},
    }
    weapons = {   # 2026-10-14 指示。装備数の上限(腕 = 左右の手、肩 = 重装の肩上)。予備 1(腰背部)は上限に数えない。近接武器は腰左右に 1 つずつ
        "ammo": ["live", "beam"],
        "kinds": {
            "bazooka": {"name": "バズーカ", "limit": {"heavy": {"arm": 2, "shoulder": 2}, "general": {"arm": 1, "shoulder": 0}, "fast": {"arm": 0, "shoulder": 0}}},
            "rifle": {"name": "ライフル", "limit": {"heavy": {"arm": 2, "shoulder": 0}, "general": {"arm": 2, "shoulder": 0}, "fast": {"arm": 1, "shoulder": 0}}},
            "smg": {"name": "サブマシンガン", "limit": {"heavy": {"arm": 2, "shoulder": 0}, "general": {"arm": 2, "shoulder": 0}, "fast": {"arm": 2, "shoulder": 0}}},
            "sword": {"name": "剣", "melee": True, "damage": "speed*weight", "limit": {"heavy": {"arm": 2, "shoulder": 0}, "general": {"arm": 2, "shoulder": 0}, "fast": {"arm": 2, "shoulder": 0}}},
            "whip": {"name": "鞭", "melee": True, "damage": "speed", "limit": {"heavy": {"arm": 2, "shoulder": 0}, "general": {"arm": 2, "shoulder": 0}, "fast": {"arm": 2, "shoulder": 0}}},
            "shield": {"name": "盾", "limit": None},   # 物理とビームの 2 種(2026-10-14)。装備上限は未指定
        },
        "spare": {"socket": "waist_rear", "count": 1, "counts_toward_limit": False},
        "melee_holsters": {"sockets": ["waist_l", "waist_r"], "count_each": 1},
    }
    JSON_OUT.write_text(json.dumps({"height": H, "parts": PARTS, "presets": presets, "generations": generations, "weapons": weapons}, ensure_ascii=False, indent=1), encoding="utf-8")
    print("parts:", len(PARTS))
