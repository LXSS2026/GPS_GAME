"""階モジュール(建物の 1 階分の部品)と戦艦の船体部品を Blender で生成し GLB に出力する。
実行: "D:\\software\\Blender_latest\\blender.exe" -b -P tools/blender/make_modules.py -- <出力フォルダ> <modules.json の出力先>
1 階 = 高さ 2.0(= 2 セル)。床面積は W×D セル(1 セル = 1.0)。原点は床の中心(底面)。Blender は Z が上、GLB 出力で Y 上に変換される。
物体名の接頭辞: ext_ = 外装(切り取り表示で消す)、int_ = 内部(床板・柱・格子。常に表示)。材質名は Godot 側で PBR 材質に差し替える。
"""
import bpy
import json
import math
import sys
from pathlib import Path

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = Path(args[0]) if args else Path("out")
JSON_OUT = Path(args[1]) if len(args) > 1 else OUT / "modules.json"
OUT.mkdir(parents=True, exist_ok=True)
FH = 3.0  # 階高(1 セル = 1 m。人 1.7 m、設備 1〜2 m の余裕)
ENTRIES = []


# ---------- 材質(名前だけが重要。Godot で差し替える) ----------
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
        "steel": material("M_Steel", (0.6, 0.62, 0.66), 0.9, 0.5),
        "concrete": material("M_Concrete", (0.55, 0.55, 0.55), 0, 0.9),
        "paintdark": material("M_PaintDark", (0.2, 0.22, 0.26), 0.3, 0.6),
        "paint": material("M_Paint", (0.35, 0.42, 0.52), 0.3, 0.6),
        "rust": material("M_Rust", (0.5, 0.3, 0.18), 0.4, 0.8),
        "glass": material("M_Glass", (0.05, 0.08, 0.12), 0.6, 0.15),
        "winlit": material("M_WinLit", (1, 0.82, 0.55), 0, 0.2, (1, 0.82, 0.55), 3),
        "wincool": material("M_WinCool", (0.55, 0.85, 1), 0, 0.2, (0.55, 0.85, 1), 3),
        "glowcyan": material("M_GlowCyan", (0.25, 0.85, 1), 0, 0.3, (0.25, 0.85, 1), 6),
        "gloworange": material("M_GlowOrange", (1, 0.45, 0.1), 0, 0.3, (1, 0.45, 0.1), 6),
        "glowred": material("M_GlowRed", (1, 0.15, 0.1), 0, 0.3, (1, 0.15, 0.1), 6),
        "rubber": material("M_Rubber", (0.08, 0.08, 0.09), 0, 0.95),
        "hazard": material("M_Hazard", (0.85, 0.65, 0.1), 0, 0.7),
        "grate": material("M_Grate", (0.4, 0.4, 0.42), 0.8, 0.6),
        "foliage": material("M_Foliage", (0.2, 0.3, 0.2), 0, 0.95),
        "armor": material("M_Armor", (0.3, 0.32, 0.36), 0.7, 0.6),
    }


M = {}


# ---------- 幾何の部品 ----------
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


def add_boxes(name, boxes, mat, bevel=0.02, cutters=None):
    verts, faces = box_mesh_data(boxes)
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
        b.angle_limit = math.radians(40)
    return obj


def add_box(name, center, size, mat, bevel=0.02, cutters=None):
    return add_boxes(name, [(center, size)], mat, bevel, cutters)


def cutter(name, boxes):
    verts, faces = box_mesh_data(boxes)
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.update()
    obj = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(obj)
    obj.hide_render = True
    obj.display_type = "WIRE"
    obj["cutter"] = True
    return obj


def add_cyl(name, center, r, h, mat, verts=24, r2=None, axis="Z", bevel=0.01):
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


def pipe(name, a, b, r, mat):
    (ax, ay, az), (bx, by, bz) = a, b
    length = math.dist(a, b)
    c = ((ax + bx) / 2, (ay + by) / 2, (az + bz) / 2)
    axis = "X" if abs(bx - ax) > 1e-6 else "Y" if abs(by - ay) > 1e-6 else "Z"
    add_cyl(name, c, r, length, mat, 16, axis=axis, bevel=0)
    for i, e in enumerate((a, b)):
        add_cyl(f"{name}_fl{i}", e, r * 1.5, r * 0.8, M["steel"], 16, axis=axis, bevel=0.005)


def clear():
    global M
    bpy.ops.wm.read_factory_settings(use_empty=True)
    M = build_materials()


def export(mid, kind, w, d, topw, topd, family="office", length=0):
    for o in bpy.context.scene.objects:
        o.select_set(not o.get("cutter", False))
    path = OUT / f"{mid}.glb"
    bpy.ops.export_scene.gltf(filepath=str(path), export_format="GLB", use_selection=True, export_apply=True, export_yup=True)
    ENTRIES.append({"id": mid, "kind": kind, "family": family, "w": w, "d": d, "topW": topw, "topD": topd, "length": length})
    print("exported", path)


# ---------- 共通の部品群(矩形 w × d) ----------
def faces(w, d):
    """各面: (法線 x, 法線 y, 接線 x, 接線 y, 面までの距離, 面の長さ)"""
    return [(1, 0, 0, 1, w / 2, d), (0, 1, -1, 0, d / 2, w), (-1, 0, 0, -1, w / 2, d), (0, -1, 1, 0, d / 2, w)]


def floor_slab(w, d, thick=0.12, extra=0.2, mat=None, grid=True):
    add_box("int_slab", (0, 0, thick / 2), (w + extra, d + extra, thick), mat or M["concrete"], bevel=0.03)
    add_box("int_slab_edge", (0, 0, thick + 0.02), (w + extra + 0.06, d + extra + 0.06, 0.04), M["steel"], bevel=0.01)
    if grid:
        # 内部格子: 設備を置く 1 セルの目盛り(切り取り表示で見える)
        lines = []
        for i in range(int(w) + 1):
            lines.append(((-w / 2 + i, 0, thick + 0.045), (0.02, d, 0.01)))
        for j in range(int(d) + 1):
            lines.append(((0, -d / 2 + j, thick + 0.045), (w, 0.02, 0.01)))
        add_boxes("int_grid", lines, M["glowcyan"], bevel=0)


def columns(w, d, h, z0=0.0, size=0.2, mat=None):
    boxes = [((sx * w / 2, sy * d / 2, z0 + h / 2), (size, size, h)) for sx in (-1, 1) for sy in (-1, 1)]
    add_boxes("int_columns", boxes, mat or M["steel"], bevel=0.015)


def ceiling(w, d):
    add_box("int_ceiling", (0, 0, FH - 0.06), (w, d, 0.12), M["concrete"], bevel=0.02)


def window_wall(w, d, z0, h, win_w, win_h, sill, depth=0.18, lit_mat=None, lit_pattern=None, wall_mat=None, frame_mat=None, pitch=1.0, prefix="ext_wall"):
    """4 面の壁に窓の凹みを切り、奥にガラス、縁に方立と庇を付ける。pitch は窓の間隔。"""
    cut_boxes, panes, frames = [], [], []
    for face, (nx, ny, tx, ty, dist, length) in enumerate(faces(w, d)):
        cols = max(1, int((length - 0.4) / pitch))
        for c in range(cols):
            t = ((c + 0.5) / cols - 0.5) * (length - 0.4)
            cx, cy = nx * dist + tx * t, ny * dist + ty * t
            zc = z0 + sill + win_h / 2
            along_x = nx == 0
            cut_boxes.append(((cx, cy, zc), (win_w if along_x else depth * 2, depth * 2 if along_x else win_w, win_h)))
            lit = lit_pattern(face, c) if lit_pattern else (c % 2 == 0)
            pane_sz = (win_w - 0.06 if along_x else 0.02, 0.02 if along_x else win_w - 0.06, win_h - 0.06)
            panes.append((((cx - nx * (depth - 0.02), cy - ny * (depth - 0.02), zc), pane_sz), lit))
            frames.append(((cx + nx * 0.06, cy + ny * 0.06, z0 + sill - 0.02), (win_w + 0.1 if along_x else 0.16, 0.16 if along_x else win_w + 0.1, 0.05)))
        for c in range(cols + 1):
            t = (c / cols - 0.5) * (length - 0.4)
            cx, cy = nx * (dist + 0.03) + tx * t, ny * (dist + 0.03) + ty * t
            frames.append(((cx, cy, z0 + h / 2), (0.08 if nx != 0 else 0.06, 0.06 if nx != 0 else 0.08, h - 0.1)))
    cut = cutter("cutter_windows", cut_boxes)
    add_box(prefix, (0, 0, z0 + h / 2), (w, d, h), wall_mat or M["paintdark"], bevel=0.02, cutters=[cut])
    lit_boxes = [b for b, lit in panes if lit]
    dark_boxes = [b for b, lit in panes if not lit]
    if lit_boxes:
        add_boxes("ext_panes_lit", lit_boxes, lit_mat or M["winlit"], bevel=0)
    if dark_boxes:
        add_boxes("ext_panes_dark", dark_boxes, M["glass"], bevel=0)
    add_boxes("ext_frames", frames, frame_mat or M["steel"], bevel=0.01)


def glow_ring(name, w, d, z, mat, inset=0.0, thick=0.05):
    boxes = [((0, d / 2 + inset, z), (w, 0.04, thick)), ((0, -(d / 2 + inset), z), (w, 0.04, thick)),
             ((w / 2 + inset, 0, z), (0.04, d, thick)), ((-(w / 2 + inset), 0, z), (0.04, d, thick))]
    add_boxes(name, boxes, mat, bevel=0)


def pipes_back(w, d, z0, h, count=2):
    for i in range(count):
        y = -d / 2 - 0.12
        x = -w / 2 + 0.3 + i * 0.35
        pipe(f"ext_pipe{i}", (x, y, z0 + 0.15), (x, y, z0 + h - 0.15), 0.05, M["rust"] if i % 2 else M["steel"])


def collar_rail(w, d, z, extra):
    rail = []
    for sx in (-1, 1):
        rail.append(((sx * (w + extra) / 2, 0, z + 0.45), (0.03, d + extra, 0.03)))
        rail.append(((0, sx * (d + extra) / 2, z + 0.45), (w + extra, 0.03, 0.03)))
        for k in range(-3, 4):
            rail.append(((sx * (w + extra) / 2, k * (d + extra) / 7, z + 0.22), (0.03, 0.03, 0.45)))
            rail.append(((k * (w + extra) / 7, sx * (d + extra) / 2, z + 0.22), (0.03, 0.03, 0.45)))
    add_boxes("ext_deck_rail", rail, M["rust"], bevel=0)


# ---------- 階モジュール ----------
def mod_base(w, d, family="office"):
    clear()
    conc = family == "concrete"
    floor_slab(w, d, 0.15, 0.8, M["concrete"], grid=False)
    if not conc:
        glow_ring("ext_base_glow", w + 0.8, d + 0.8, 0.16, M["glowcyan"])
    add_box("ext_lobby", (0, 0, 0.15 + (FH - 0.3) / 2), (w - 0.7, d - 0.7, FH - 0.3), M["concrete"] if conc else M["wincool"], bevel=0.02)
    cols = []
    for sx in (-1, 1):
        for sy in (-1, 1):
            cols.append(((sx * w / 2, sy * d / 2, FH / 2), (0.26, 0.26, FH)))
        for t in [(k + 0.5) / max(2, int(d / 1.2)) - 0.5 for k in range(max(2, int(d / 1.2)))]:
            cols.append(((sx * w / 2, t * d, FH / 2), (0.16, 0.16, FH)))
        for t in [(k + 0.5) / max(2, int(w / 1.2)) - 0.5 for k in range(max(2, int(w / 1.2)))]:
            cols.append(((t * w, sx * d / 2, FH / 2), (0.16, 0.16, FH)))
    add_boxes("int_columns", cols, M["concrete"] if conc else M["steel"], bevel=0.015)
    add_box("ext_entrance", (0, (d - 0.7) / 2 + 0.02, 0.15 + 1.1), (1.2, 0.06, 2.2), M["rubber"], bevel=0.01)
    add_box("ext_entrance_glow", (0, (d - 0.7) / 2 + 0.05, 0.15 + 2.22), (1.2, 0.03, 0.05), M["gloworange"] if conc else M["glowcyan"], bevel=0)
    add_box("ext_canopy", (0, d / 2 + 0.3, FH - 0.35), (w * 0.6, 0.8, 0.08), M["concrete"] if conc else M["steel"], bevel=0.015)
    add_box("ext_hazard", (0, d / 2 + 0.45, 0.17), (w * 0.5, 0.4, 0.03), M["hazard"], bevel=0)
    ceiling(w, d)
    add_box("ext_ledge", (0, 0, FH - 0.02), (w + 0.3, d + 0.3, 0.1), M["concrete"] if conc else M["steel"], bevel=0.02)
    export(f"base_{w}x{d}_{family}", "base", w, d, w, d, family)


def mod_standard(w, d, family, cool=False):
    """標準階。family: office / res / concrete / deco / exo / svc"""
    clear()
    floor_slab(w, d)
    if family == "office":
        columns(w, d, FH - 0.12, 0.12)
        window_wall(w, d, 0.12, FH - 0.24, 0.5, FH * 0.5, FH * 0.22, lit_mat=M["wincool"] if cool else M["winlit"], lit_pattern=lambda f, c: (f * 3 + c) % 5 != 1)
        add_box("ext_spandrel", (0, 0, 0.12 + FH * 0.11), (w + 0.04, d + 0.04, FH * 0.14), M["paint"], bevel=0.015)
        pipes_back(w, d, 0.12, FH - 0.24)
        add_boxes("ext_vent", [((w / 2 + 0.03, d * 0.3, 1.32 + i * 0.07), (0.06, 0.35, 0.03)) for i in range(5)], M["rubber"], bevel=0)
    elif family == "res":
        columns(w, d, FH - 0.12, 0.12)
        window_wall(w, d, 0.12, FH - 0.24, 0.45, FH * 0.42, FH * 0.25, lit_mat=M["winlit"], lit_pattern=lambda f, c: (f + c) % 3 != 0)
        for face, (nx, ny, tx, ty, dist, length) in enumerate(faces(w, d)):
            dd = dist + 0.3
            add_box(f"ext_balcony{face}", (nx * dd, ny * dd, 0.15), (0.6 if nx != 0 else length * 0.7, length * 0.7 if nx != 0 else 0.6, 0.06), M["concrete"], bevel=0.015)
            rail = [((nx * (dd + 0.27), ny * (dd + 0.27), 0.67), (0.03 if nx != 0 else length * 0.7, length * 0.7 if nx != 0 else 0.03, 0.03))]
            for k in range(-3, 4):
                rail.append(((nx * (dd + 0.27) + tx * k * length * 0.11, ny * (dd + 0.27) + ty * k * length * 0.11, 0.42), (0.03, 0.03, 0.5)))
            add_boxes(f"ext_rail{face}", rail, M["rust"], bevel=0)
            add_box(f"ext_plant{face}", (nx * (dd + 0.1) + tx * length * 0.25, ny * (dd + 0.1) + ty * length * 0.25, 0.37), (0.3, 0.3, 0.3), M["foliage"], bevel=0.03)
    elif family == "concrete":
        # コンクリート: 深い窓枠、各階の庇、小さめの窓、色は灰
        columns(w, d, FH - 0.12, 0.12, 0.24, M["concrete"])
        window_wall(w, d, 0.12, FH - 0.24, 0.42, FH * 0.36, FH * 0.28, depth=0.3, lit_mat=M["winlit"], lit_pattern=lambda f, c: (f * 2 + c) % 4 == 0,
                    wall_mat=M["concrete"], frame_mat=M["concrete"], pitch=0.8)
        add_box("ext_ledge_mid", (0, 0, 0.12 + 0.42), (w + 0.3, d + 0.3, 0.12), M["concrete"], bevel=0.02)
        add_box("ext_ledge_top", (0, 0, FH - 0.08), (w + 0.3, d + 0.3, 0.12), M["concrete"], bevel=0.02)
    elif family == "deco":
        # 縦リブの段状系: 密な縦の柱形、暖色の縦長窓
        columns(w, d, FH - 0.12, 0.12)
        window_wall(w, d, 0.12, FH - 0.24, 0.32, FH * 0.68, FH * 0.15, lit_mat=M["winlit"], lit_pattern=lambda f, c: (f + c) % 4 != 3, pitch=0.55, frame_mat=M["rust"])
        ribs = []
        for nx, ny, tx, ty, dist, length in faces(w, d):
            n = int(length / 0.55)
            for c in range(n + 1):
                t = (c / n - 0.5) * (length - 0.2)
                ribs.append(((nx * (dist + 0.08) + tx * t, ny * (dist + 0.08) + ty * t, FH / 2), (0.1 if nx != 0 else 0.08, 0.08 if nx != 0 else 0.1, FH - 0.1)))
        add_boxes("ext_ribs", ribs, M["rust"], bevel=0.01)
    elif family == "exo":
        # 外骨格: 細い内壁と、外側の X 支柱と水平トラス
        columns(w, d, FH - 0.12, 0.12, 0.28)
        window_wall(w, d, 0.12, FH - 0.24, 0.6, FH * 0.55, FH * 0.2, lit_mat=M["wincool"], lit_pattern=lambda f, c: (f + c) % 3 != 1, depth=0.1)
        for face, (nx, ny, tx, ty, dist, length) in enumerate(faces(w, d)):
            add_box(f"ext_truss{face}", (nx * (dist + 0.22), ny * (dist + 0.22), FH - 0.2), (0.14 if nx != 0 else length + 0.5, length + 0.5 if nx != 0 else 0.14, 0.3), M["steel"], bevel=0.015)
            segs = max(1, int(length / 1.5))
            for s in range(segs):
                t0 = (s / segs - 0.5) * length
                t1 = ((s + 1) / segs - 0.5) * length
                for (ta, tb) in ((t0, t1), (t1, t0)):
                    cx, cy = nx * (dist + 0.22) + tx * (ta + tb) / 2, ny * (dist + 0.22) + ty * (ta + tb) / 2
                    bpy.ops.mesh.primitive_cube_add(size=1, location=(cx, cy, 0.12 + (FH - 0.24) / 2))
                    bar = bpy.context.active_object
                    bar.name = f"ext_brace{face}_{s}_{int(ta > tb)}"
                    L = math.hypot(tb - ta, FH - 0.5)
                    bar.scale = (0.08, 0.08, L)
                    ang = math.atan2(tb - ta, FH - 0.5)
                    bar.rotation_euler = (0, 0, 0)
                    if nx != 0:
                        bar.rotation_euler = (ang, 0, 0)
                    else:
                        bar.rotation_euler = (0, -ang, 0)
                    bar.data.materials.append(M["steel"])
    elif family == "svc":
        # 外部設備: 露出配管・ダクト・設備塔の節・ガラスの昇降機
        columns(w, d, FH - 0.12, 0.12)
        window_wall(w, d, 0.12, FH - 0.24, 0.5, FH * 0.46, FH * 0.22, lit_mat=M["wincool"], lit_pattern=lambda f, c: (f + c) % 2 == 0)
        add_cyl("ext_svc_tower", (w / 2 + 0.5, d / 2 + 0.5, FH / 2), 0.42, FH, M["steel"], 24)
        add_cyl("ext_svc_ring", (w / 2 + 0.5, d / 2 + 0.5, 0.1), 0.47, 0.08, M["rust"], 24, bevel=0)
        add_cyl("ext_svc_ring2", (w / 2 + 0.5, d / 2 + 0.5, FH - 0.1), 0.47, 0.08, M["rust"], 24, bevel=0)
        pipe("ext_svc_pipe", (w / 2 + 1.0, d / 2 + 0.5, 0.2), (w / 2 + 1.0, d / 2 + 0.5, FH - 0.2), 0.08, M["rust"])
        pipe("ext_duct", (-w / 2 + 0.3, d / 2 + 0.25, 1.4), (w / 2 - 0.3, d / 2 + 0.25, 1.4), 0.12, M["steel"])
        add_box("ext_lift", (-w / 2 - 0.3, -d / 2 + 0.6, FH / 2), (0.5, 0.7, FH), M["wincool"], bevel=0.01)
        add_box("ext_lift_glow", (-w / 2 - 0.56, -d / 2 + 0.6, FH / 2), (0.02, 0.06, FH), M["glowcyan"], bevel=0)
    ceiling(w, d)
    export(f"{family}_{w}x{d}{'_c' if cool else ''}", "standard", w, d, w, d, family)


def mod_mech(w, d, family="office"):
    clear()
    floor_slab(w, d, 0.12, 0.3, M["steel"])
    columns(w, d, FH - 0.12, 0.12, 0.24)
    add_box("ext_core", (0, 0, 0.12 + (FH - 0.24) / 2), (w - 0.3, d - 0.3, FH - 0.24), M["concrete"] if family == "concrete" else M["paintdark"], bevel=0.02)
    for face, (nx, ny, tx, ty, dist, length) in enumerate(faces(w, d)):
        slats = [((nx * (dist - 0.02), ny * (dist - 0.02), 0.42 + i * 0.14), (0.08 if nx != 0 else length - 0.6, length - 0.6 if nx != 0 else 0.08, 0.05)) for i in range(int((FH - 0.9) / 0.14))]
        add_boxes(f"ext_louver{face}", slats, M["steel"], bevel=0)
    pipe("ext_bigpipe", (-w / 2 - 0.25, -d / 2 + 0.4, 0.5), (-w / 2 - 0.25, d / 2 - 0.4, 0.5), 0.12, M["rust"])
    pipe("ext_bigpipe2", (-w / 2 - 0.25, d / 2 - 0.4, 0.5), (-w / 2 - 0.25, d / 2 - 0.4, FH - 0.3), 0.12, M["rust"])
    add_cyl("ext_tank", (w / 2 + 0.35, -d / 2 + 0.5, 0.72), 0.32, 1.2, M["rust"], 24)
    add_cyl("ext_tank_cap", (w / 2 + 0.35, -d / 2 + 0.5, 1.38), 0.3, 0.12, M["steel"], 24, r2=0.18)
    glow_ring("ext_mech_glow", w - 0.3, d - 0.3, 0.3, M["gloworange"], inset=0.03, thick=0.04)
    ceiling(w, d)
    export(f"mech_{w}x{d}_{family}", "mech", w, d, w, d, family)


def mod_transfer(w, d, w2, d2, family="office"):
    clear()
    floor_slab(w, d, 0.14, 0.5, M["steel"], grid=False)
    glow_ring("ext_deck_glow", w + 0.5, d + 0.5, 0.15, M["glowcyan"])
    collar_rail(w, d, 0.14, 0.5)
    plants = [((sx * (w / 2 + 0.05), sy * (d / 2 + 0.05), 0.34), (0.4, 0.4, 0.4)) for sx in (-1, 1) for sy in (-1, 1)]
    add_boxes("ext_deck_plants", plants, M["foliage"], bevel=0.04)
    columns(w2, d2, FH - 0.14, 0.14)
    window_wall(w2, d2, 0.14, FH - 0.28, 0.5, FH * 0.5, FH * 0.22, lit_mat=M["wincool"], lit_pattern=lambda f, c: (f + c) % 2 == 0,
                wall_mat=M["concrete"] if family == "concrete" else None)
    ceiling(w2, d2)
    export(f"transfer_{w}x{d}_{w2}x{d2}_{family}", "transfer", w, d, w2, d2, family)


def mod_roof(w, d, kind, family="office"):
    clear()
    conc = family == "concrete"
    floor_slab(w, d, 0.12, 0.2, M["concrete"], grid=False)
    par = [((0, d / 2 + 0.08, 0.3), (w + 0.2, 0.08, 0.36)), ((0, -d / 2 - 0.08, 0.3), (w + 0.2, 0.08, 0.36)),
           ((w / 2 + 0.08, 0, 0.3), (0.08, d + 0.2, 0.36)), ((-w / 2 - 0.08, 0, 0.3), (0.08, d + 0.2, 0.36))]
    add_boxes("ext_parapet", par, M["concrete"], bevel=0.015)
    add_box("ext_penthouse", (-w * 0.2, -d * 0.2, 0.57), (w * 0.4, d * 0.4, 0.9), M["concrete"] if conc else M["paintdark"], bevel=0.02)
    add_box("ext_penthouse_door", (-w * 0.2, -d * 0.2 + d * 0.2 + 0.02, 0.52), (0.4, 0.04, 0.7), M["rubber"], bevel=0)
    add_boxes("ext_ac", [((w * 0.25, -d * 0.3 + i * d * 0.25, 0.32), (0.4, 0.4, 0.4)) for i in range(3)], M["steel"], bevel=0.02)
    add_cyl("ext_tank", (w * 0.25, d * 0.3, 1.02), 0.26, 0.6, M["rust"], 20)
    add_boxes("ext_tank_legs", [((w * 0.25 + sx * 0.18, d * 0.3 + sy * 0.18, 0.42), (0.05, 0.05, 0.6)) for sx in (-1, 1) for sy in (-1, 1)], M["steel"], bevel=0)
    if kind == "spire":
        add_cyl("ext_spire", (0, 0, 1.32), 0.3, 2.4, M["steel"], 12, r2=0.06)
        add_cyl("ext_mast", (0, 0, 3.52), 0.03, 2.0, M["steel"], 8, bevel=0)
        add_cyl("ext_beacon", (0, 0, 4.57), 0.09, 0.14, M["glowred"], 8, bevel=0)
    elif kind == "heli":
        add_cyl("ext_helipad", (-w * 0.15, d * 0.15, 0.84), min(w, d) * 0.3, 0.1, M["concrete"], 24)
        add_cyl("ext_helipad_ring", (-w * 0.15, d * 0.15, 0.9), min(w, d) * 0.31, 0.02, M["glowcyan"], 24, bevel=0)
        add_cyl("ext_mast", (w * 0.3, -d * 0.3, 0.92), 0.025, 1.4, M["steel"], 8, bevel=0)
        add_cyl("ext_beacon", (w * 0.3, -d * 0.3, 1.67), 0.07, 0.12, M["glowred"], 8, bevel=0)
    elif kind == "boiler":
        add_box("ext_boiler", (w * 0.15, d * 0.1, 0.72), (w * 0.45, d * 0.5, 1.2), M["concrete"], bevel=0.02)
        add_cyl("ext_stack", (w * 0.3, -d * 0.25, 1.4), 0.16, 1.6, M["rust"], 16)
        add_cyl("ext_stack_top", (w * 0.3, -d * 0.25, 2.22), 0.18, 0.06, M["steel"], 16, bevel=0)
    else:
        add_cyl("ext_mast", (w * 0.3, -d * 0.3, 0.92), 0.025, 1.4, M["steel"], 8, bevel=0)
        add_cyl("ext_beacon", (w * 0.3, -d * 0.3, 1.67), 0.07, 0.12, M["glowred"], 8, bevel=0)
        for i in range(3):
            add_cyl(f"ext_ant{i}", (-w * 0.35 + i * 0.3, d * 0.35, 0.62), 0.015, 0.8, M["steel"], 6, bevel=0)
    export(f"roof_{w}x{d}_{kind}_{family}", "roof", w, d, 0, 0, family)


def mod_hangar(w, d):
    clear()
    floor_slab(w, d, 0.12, 0.3, M["steel"])
    columns(w, d, FH - 0.12, 0.12, 0.26)
    cut = cutter("cutter_door", [((0, d / 2, 0.12 + (FH - 0.5) / 2), (w * 0.6, 0.6, FH - 0.5))])
    add_box("ext_wall", (0, 0, 0.12 + (FH - 0.24) / 2), (w, d, FH - 0.24), M["paintdark"], bevel=0.02, cutters=[cut])
    add_box("ext_door_glow", (0, d / 2 + 0.02, FH - 0.4), (w * 0.6, 0.04, 0.05), M["gloworange"], bevel=0)
    add_box("ext_door_hazard", (0, d / 2 + 0.06, 0.16), (w * 0.6, 0.1, 0.04), M["hazard"], bevel=0)
    add_box("int_floor_grate", (0, 0, 0.13), (w - 0.5, d - 0.5, 0.02), M["grate"], bevel=0)
    for face, (nx, ny, tx, ty, dist, length) in enumerate(faces(w, d)):
        if face == 1:
            continue
        add_box(f"ext_stripe{face}", (nx * (dist + 0.01), ny * (dist + 0.01), FH - 0.3), (0.02 if nx != 0 else length * 0.8, length * 0.8 if nx != 0 else 0.02, 0.06), M["gloworange"], bevel=0)
    ceiling(w, d)
    export(f"hangar_{w}x{d}", "hangar", w, d, w, d, "office")


def mod_bridge(length, wide=False):
    """空中の渡り廊下(長さ length セル、x 方向)。原点は中央、床面。wide は広幅(植栽と街灯)。"""
    clear()
    wd = 2.4 if wide else 1.4
    add_box("ext_bridge_floor", (0, 0, 0.1), (length, wd, 0.2), M["steel"], bevel=0.02)
    add_box("ext_bridge_under", (0, 0, -0.08), (length - 0.2, wd - 0.3, 0.16), M["paintdark"], bevel=0.02)
    add_box("ext_bridge_glow", (0, 0, -0.17), (length, wd * 0.7, 0.03), M["glowcyan"], bevel=0)
    if wide:
        rail = []
        for sy in (-1, 1):
            rail.append(((0, sy * (wd / 2 - 0.05), 0.65), (length, 0.03, 0.03)))
            for i in range(int(length) * 2 + 1):
                rail.append(((-length / 2 + i * 0.5, sy * (wd / 2 - 0.05), 0.42), (0.03, 0.03, 0.45)))
        add_boxes("ext_bridge_rail", rail, M["rust"], bevel=0)
        add_boxes("ext_bridge_plants", [((-length / 2 + 0.6 + i * 1.2, 0, 0.38), (0.5, 0.5, 0.36)) for i in range(int((length - 0.6) / 1.2))], M["foliage"], bevel=0.03)
        for i in range(max(1, int(length / 2))):
            x = -length / 2 + 1.0 + i * 2.0
            add_box(f"ext_lamp{i}", (x, wd / 2 - 0.2, 0.9), (0.05, 0.05, 1.4), M["steel"], bevel=0)
            add_box(f"ext_lamp_glow{i}", (x, wd / 2 - 0.2, 1.62), (0.14, 0.14, 0.06), M["wincool"], bevel=0)
        ribs = [((-length / 2 + 0.3 + i * (length - 0.6) / max(1, int(length)), 0, 0.25), (0.08, wd + 0.2, 0.5)) for i in range(int(length) + 1)]
        add_boxes("ext_bridge_ribs", ribs, M["steel"], bevel=0.01)
    else:
        add_box("ext_bridge_glass", (0, 0, 1.3), (length, wd - 0.2, 2.2), M["wincool"], bevel=0.01)
        add_box("ext_bridge_roof", (0, 0, 2.48), (length + 0.1, wd + 0.1, 0.12), M["steel"], bevel=0.02)
        n = max(2, int(length))
        add_boxes("ext_bridge_ribs", [((-length / 2 + 0.3 + i * (length - 0.6) / (n - 1), 0, 1.3), (0.08, wd + 0.1, 2.4)) for i in range(n)], M["steel"], bevel=0.01)
    export(f"bridge{'w' if wide else ''}_{length}", "bridge", 0, 0, 0, 0, "office", length)


def mod_sky_node():
    """空中の結節点: 2.2 角の台。周囲に手すり、四隅に街灯、中央に小屋。渡り廊下を放射状につなぐ。"""
    clear()
    s = 2.2
    add_box("ext_node_floor", (0, 0, 0.1), (s, s, 0.2), M["steel"], bevel=0.02)
    add_box("ext_node_under", (0, 0, -0.2), (s - 0.4, s - 0.4, 0.4), M["paintdark"], bevel=0.03)
    add_box("ext_node_glow", (0, 0, -0.41), (s - 0.6, s - 0.6, 0.03), M["glowcyan"], bevel=0)
    rail = []
    for sx in (-1, 1):
        rail.append(((sx * (s / 2 - 0.04), 0, 0.65), (0.03, s, 0.03)))
        rail.append(((0, sx * (s / 2 - 0.04), 0.65), (s, 0.03, 0.03)))
        for k in range(-2, 3):
            rail.append(((sx * (s / 2 - 0.04), k * s / 5, 0.42), (0.03, 0.03, 0.45)))
            rail.append(((k * s / 5, sx * (s / 2 - 0.04), 0.42), (0.03, 0.03, 0.45)))
    add_boxes("ext_node_rail", rail, M["rust"], bevel=0)
    for i, (sx, sy) in enumerate(((-1, -1), (1, -1), (-1, 1), (1, 1))):
        add_box(f"ext_node_lamp{i}", (sx * (s / 2 - 0.15), sy * (s / 2 - 0.15), 0.9), (0.05, 0.05, 1.4), M["steel"], bevel=0)
        add_box(f"ext_node_lamp_glow{i}", (sx * (s / 2 - 0.15), sy * (s / 2 - 0.15), 1.62), (0.14, 0.14, 0.06), M["wincool"], bevel=0)
    add_box("ext_node_kiosk", (0, 0, 0.65), (0.8, 0.8, 0.9), M["paintdark"], bevel=0.02)
    add_box("ext_node_kiosk_glow", (0, 0.41, 0.7), (0.5, 0.02, 0.3), M["gloworange"], bevel=0)
    export("sky_node", "node", 2, 2, 2, 2, "office", 2)


# ---------- 航空戦艦の船体部品(x 方向に並べる。幅 W、長さ L。船体は上甲板と下甲板の 2 層 = 高さ 3.8、下に船腹) ----------
HULL_H = 3.8   # 船体の高さ(2 層)
BELLY = 1.4    # 船腹(下に絞った部分)の高さ


def hull_body(W, L, name="ext_hull", cutters=None):
    """船体の基本形: 上部の箱 + 下に絞った船腹(台形) + 装甲帯 + 鋲 + 甲板 3 面 + 舷窓。"""
    add_box(name, (0, 0, BELLY + HULL_H / 2), (L, W, HULL_H), M["armor"], bevel=0.05, cutters=cutters)
    verts = [(-L / 2, -W * 0.275, 0), (-L / 2, W * 0.275, 0), (L / 2, -W * 0.275, 0), (L / 2, W * 0.275, 0),
             (-L / 2, -W / 2, BELLY), (-L / 2, W / 2, BELLY), (L / 2, -W / 2, BELLY), (L / 2, W / 2, BELLY)]
    faces_ = [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)]
    me = bpy.data.meshes.new(name + "_belly")
    me.from_pydata(verts, [], faces_)
    me.update()
    obj = bpy.data.objects.new(name + "_belly", me)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(M["paintdark"])
    b = obj.modifiers.new("bevel", "BEVEL")
    b.width = 0.04
    b.segments = 2
    add_boxes("ext_armor_plates", [((0, sy * (W / 2 + 0.03), BELLY + 0.9 + i * 1.6), (L - 0.4, 0.06, 0.5)) for sy in (-1, 1) for i in range(2)], M["steel"], bevel=0.01)
    add_boxes("ext_rivets", [((-L / 2 + 0.3 + i * (L - 0.6) / 5, sy * (W / 2 + 0.07), BELLY + 1.7), (0.1, 0.06, 0.1)) for sy in (-1, 1) for i in range(6)], M["rust"], bevel=0)
    add_box("ext_keel_glow", (0, 0, 0.02), (L, 0.2, 0.04), M["glowcyan"], bevel=0)
    add_box("int_deck_lower", (0, 0, BELLY + 0.08), (L, W - 0.3, 0.16), M["grate"], bevel=0.01)
    add_box("int_deck_upper", (0, 0, BELLY + HULL_H / 2), (L, W - 0.3, 0.16), M["grate"], bevel=0.01)
    add_box("ext_deck_top", (0, 0, BELLY + HULL_H - 0.08), (L, W, 0.16), M["steel"], bevel=0.02)
    lines = [((-L / 2 + i, 0, BELLY + HULL_H + 0.01), (0.02, W, 0.01)) for i in range(int(L) + 1)] + [((0, -W / 2 + j, BELLY + HULL_H + 0.01), (L, 0.02, 0.01)) for j in range(int(W) + 1)]
    add_boxes("ext_deck_grid", lines, M["rubber"], bevel=0)   # 最上甲板の目盛りは暗い線(遠景で光らせない)
    for z in (BELLY + 0.08, BELLY + HULL_H / 2):
        lines = [((-L / 2 + i, 0, z + 0.09), (0.02, W - 0.3, 0.01)) for i in range(int(L) + 1)] + [((0, -(W - 0.3) / 2 + j, z + 0.09), (L, 0.02, 0.01)) for j in range(int(W) + 1)]
        add_boxes(f"int_grid_{int(z*10)}", lines, M["glowcyan"], bevel=0)
    for z in (BELLY + 0.9, BELLY + 2.7):
        add_boxes(f"ext_ports_{int(z*10)}", [((-L / 2 + 0.5 + i * 0.8, sy * (W / 2 + 0.01), z), (0.3, 0.02, 0.2)) for sy in (-1, 1) for i in range(int((L - 0.8) / 0.8))], M["wincool"], bevel=0)


def ship_mid(W, L):
    clear()
    hull_body(W, L)
    pipe("ext_hull_pipe", (-L / 2 + 0.2, W / 2 + 0.2, BELLY + 2.0), (L / 2 - 0.2, W / 2 + 0.2, BELLY + 2.0), 0.1, M["rust"])
    add_boxes("ext_deck_vents", [((-L / 2 + 0.6 + i * 1.2, -W * 0.3, BELLY + HULL_H + 0.2), (0.5, 0.4, 0.4)) for i in range(int((L - 0.6) / 1.2))], M["steel"], bevel=0.02)
    export(f"ship_mid_{W}x{L}", "ship", W, L, W, L, "ship", L)


def ship_lift(W, L):
    """浮揚機関の区画: 両舷に大きな機関ポッド、下面に発光する噴射口。"""
    clear()
    hull_body(W, L)
    for sy in (-1, 1):
        add_cyl(f"ext_lift_pod{sy+1}", (0, sy * (W / 2 + 0.9), BELLY + 1.0), 0.9, L - 0.4, M["steel"], 24, axis="X", bevel=0.03)
        add_cyl(f"ext_lift_ring{sy+1}", (0, sy * (W / 2 + 0.9), BELLY + 1.0), 0.98, 0.3, M["rust"], 24, axis="X", bevel=0)
        add_cyl(f"ext_lift_glow{sy+1}", (0, sy * (W / 2 + 0.9), BELLY + 0.05), 0.5, 0.08, M["glowcyan"], 24, bevel=0)
        add_box(f"ext_lift_strut{sy+1}", (0, sy * (W / 2 + 0.45), BELLY + 1.0), (L * 0.6, 0.9, 0.5), M["armor"], bevel=0.03)
    add_cyl("ext_belly_glow", (0, 0, 0.0), 0.7, 0.06, M["glowcyan"], 24, bevel=0)
    export(f"ship_lift_{W}x{L}", "ship", W, L, W, L, "ship", L)


def ship_bow(W, L):
    """艦首: 平面でも立面でも先細る楔。"""
    clear()
    z1 = BELLY + HULL_H
    verts = [(-L / 2, -W / 2, BELLY), (-L / 2, W / 2, BELLY), (-L / 2, -W * 0.275, 0.0), (-L / 2, W * 0.275, 0.0),
             (-L / 2, -W / 2, z1), (-L / 2, W / 2, z1),
             (L / 2, -0.25, z1 - 0.6), (L / 2, 0.25, z1 - 0.6), (L / 2, -0.15, BELLY + 0.6), (L / 2, 0.15, BELLY + 0.6)]
    faces_ = [(0, 1, 3, 2), (2, 3, 9, 8), (0, 2, 8, 6, 4), (1, 5, 7, 9, 3), (4, 6, 7, 5), (0, 4, 5, 1), (6, 8, 9, 7)]
    me = bpy.data.meshes.new("ext_bow")
    me.from_pydata(verts, [], faces_)
    me.update()
    obj = bpy.data.objects.new("ext_bow", me)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(M["armor"])
    b = obj.modifiers.new("bevel", "BEVEL")
    b.width = 0.05
    b.segments = 2
    add_box("ext_bow_ram", (L / 2 - 0.2, 0, BELLY + 1.9), (0.8, 0.3, 1.0), M["steel"], bevel=0.02)
    add_box("ext_bow_glow", (L / 2 - 0.1, 0, BELLY + 2.2), (0.3, 0.34, 0.06), M["glowcyan"], bevel=0)
    add_boxes("ext_bow_plates", [((-L / 2 + 0.6, sy * (W / 2 - 0.1), BELLY + 1.9), (0.8, 0.06, 0.5)) for sy in (-1, 1)], M["steel"], bevel=0.01)
    add_box("int_deck_upper", (-L * 0.15, 0, BELLY + HULL_H / 2), (L * 0.6, W * 0.55, 0.16), M["grate"], bevel=0.01)
    add_box("ext_deck_top", (-L * 0.15, 0, BELLY + HULL_H - 0.08), (L * 0.6, W * 0.55, 0.16), M["steel"], bevel=0.02)
    export(f"ship_bow_{W}x{L}", "ship", W, L, W, L, "ship", L)


def ship_stern(W, L):
    clear()
    hull_body(W, L)
    for i, (y, z) in enumerate(((-W * 0.3, BELLY + 1.0), (W * 0.3, BELLY + 1.0), (0, BELLY + 2.6))):
        add_cyl(f"ext_engine{i}", (-L / 2 - 0.6, y, z), 0.7, 1.4, M["steel"], 24, r2=0.85, axis="X", bevel=0.02)
        add_cyl(f"ext_engine_ring{i}", (-L / 2 - 1.3, y, z), 0.9, 0.12, M["rust"], 24, axis="X", bevel=0)
        add_cyl(f"ext_engine_glow{i}", (-L / 2 - 1.37, y, z), 0.62, 0.08, M["gloworange"], 24, axis="X", bevel=0)
    add_boxes("ext_fins", [((0, sy * (W / 2 + 0.5), BELLY + 3.0), (L * 0.7, 1.0, 0.1)) for sy in (-1, 1)], M["armor"], bevel=0.02)
    add_box("ext_fin_top", (-L * 0.2, 0, BELLY + HULL_H + 1.0), (L * 0.5, 0.12, 2.0), M["armor"], bevel=0.02)
    export(f"ship_stern_{W}x{L}", "ship", W, L, W, L, "ship", L)


def ship_hangar(W, L):
    clear()
    cut = cutter("cutter_side", [((0, sy * W / 2, BELLY + 2.5), (L * 0.7, 0.6, 1.5)) for sy in (-1, 1)])
    hull_body(W, L, cutters=[cut])
    for sy in (-1, 1):
        add_box(f"ext_door_glow{sy+1}", (0, sy * (W / 2 + 0.02), BELLY + 3.28), (L * 0.7, 0.04, 0.05), M["gloworange"], bevel=0)
        add_box(f"ext_door_hazard{sy+1}", (0, sy * (W / 2 + 0.06), BELLY + 1.78), (L * 0.7, 0.1, 0.04), M["hazard"], bevel=0)
    add_box("ext_launch_frame", (0, 0, BELLY + HULL_H + 0.1), (L * 0.6, W * 0.5, 0.2), M["steel"], bevel=0.02)
    add_box("ext_launch_glow", (0, 0, BELLY + HULL_H + 0.21), (L * 0.55, W * 0.45, 0.02), M["gloworange"], bevel=0)
    export(f"ship_hangar_{W}x{L}", "ship", W, L, W, L, "ship", L)


def ship_turret(W, L):
    clear()
    hull_body(W, L)
    z = BELLY + HULL_H
    add_cyl("ext_barbette", (0, 0, z + 0.3), W * 0.32, 0.6, M["armor"], 24)
    add_box("ext_turret", (0, 0, z + 1.15), (W * 0.55, W * 0.5, 1.1), M["armor"], bevel=0.05)
    add_box("ext_turret_top", (-0.2, 0, z + 1.85), (W * 0.35, W * 0.35, 0.3), M["paintdark"], bevel=0.03)
    for y in (-W * 0.12, 0, W * 0.12):
        add_cyl(f"ext_gun{int(y*100)}", (W * 0.27 + 1.3, y, z + 1.2), 0.11, 2.6, M["steel"], 12, axis="X", bevel=0)
        add_cyl(f"ext_gun_muzzle{int(y*100)}", (W * 0.27 + 2.6, y, z + 1.2), 0.14, 0.2, M["rust"], 12, axis="X", bevel=0)
    add_box("ext_turret_glow", (0, 0, z + 1.72), (W * 0.4, 0.04, 0.04), M["glowred"], bevel=0)
    export(f"ship_turret_{W}x{L}", "ship", W, L, W, L, "ship", L)


def ship_bridge(W, L):
    clear()
    hull_body(W, L)
    z = BELLY + HULL_H
    add_box("ext_tower1", (0, 0, z + 0.9), (L * 0.6, W * 0.5, 1.8), M["armor"], bevel=0.04)
    add_box("ext_tower2", (0.2, 0, z + 2.5), (L * 0.5, W * 0.4, 1.4), M["armor"], bevel=0.04)
    add_box("ext_tower3", (0.5, 0, z + 3.6), (L * 0.55, W * 0.55, 0.8), M["paintdark"], bevel=0.03)
    add_box("ext_tower_glass", (L * 0.275 + 0.5, 0, z + 3.6), (0.04, W * 0.5, 0.5), M["wincool"], bevel=0)
    add_boxes("ext_tower_wings", [((0.5, sy * (W * 0.275 + 0.4), z + 3.5), (L * 0.3, 0.8, 0.12)) for sy in (-1, 1)], M["steel"], bevel=0.02)
    add_boxes("ext_tower_ports", [((L * 0.3 + 0.01, sy * 0.5, z + 2.5), (0.02, 0.4, 0.3)) for sy in (-1, 1)], M["wincool"], bevel=0)
    add_cyl("ext_tower_mast", (-0.6, 0, z + 5.2), 0.04, 2.4, M["steel"], 8, bevel=0)
    add_cyl("ext_tower_beacon", (-0.6, 0, z + 6.45), 0.09, 0.14, M["glowred"], 8, bevel=0)
    add_cyl("ext_radar", (0.4, 0.8, z + 4.3), 0.5, 0.06, M["steel"], 16, bevel=0)
    add_box("ext_radar_arm", (0.4, 0.8, z + 4.15), (0.08, 0.08, 0.3), M["steel"], bevel=0)
    export(f"ship_bridge_{W}x{L}", "ship", W, L, W, L, "ship", L)


if __name__ == "__main__":
    # 床面積: 3x3, 2x2, 5x5, 4x6
    for (w, d) in ((3, 3), (2, 2), (5, 5), (4, 6)):
        for fam in ("office", "concrete"):
            mod_base(w, d, fam)
            mod_mech(w, d, fam)
        mod_standard(w, d, "office")
        mod_standard(w, d, "office", cool=True)
        mod_standard(w, d, "res")
        mod_standard(w, d, "concrete")
        mod_standard(w, d, "deco")
        mod_standard(w, d, "exo")
        mod_standard(w, d, "svc")
        mod_roof(w, d, "mech")
        mod_roof(w, d, "heli")
        mod_roof(w, d, "spire")
        mod_roof(w, d, "boiler", "concrete")
        mod_roof(w, d, "mech", "concrete")
        mod_hangar(w, d)
    mod_transfer(3, 3, 2, 2)
    mod_transfer(5, 5, 3, 3)
    mod_transfer(4, 6, 3, 3)
    mod_transfer(5, 5, 4, 6)
    mod_transfer(3, 3, 2, 2, "concrete")
    mod_transfer(5, 5, 3, 3, "concrete")
    for L in range(2, 9):
        mod_bridge(L)
        mod_bridge(L, wide=True)
    mod_sky_node()
    for (W, L) in ((5, 3), (7, 4)):
        ship_mid(W, L)
        ship_lift(W, L)
        ship_bow(W, L + 3)
        ship_stern(W, L)
        ship_hangar(W, L)
        ship_turret(W, L)
        ship_bridge(W, L)
    JSON_OUT.parent.mkdir(parents=True, exist_ok=True)
    JSON_OUT.write_text(json.dumps({"floorHeight": FH, "modules": ENTRIES}, ensure_ascii=False, indent=1), encoding="utf-8")
    print("modules:", len(ENTRIES))
