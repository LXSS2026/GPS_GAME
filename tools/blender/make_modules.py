"""階モジュール(建物の 1 階分の外装部品)を Blender で生成し GLB に出力する。
実行: "D:\\software\\Blender_latest\\blender.exe" -b -P tools/blender/make_modules.py -- <出力フォルダ>
1 階 = 高さ 2.0(= 2 セル)。床面積は W×W セル(1 セル = 1.0)。原点は床の中心(底面)。Blender は Z が上、GLB 出力で Y 上に変換される。
物体名の接頭辞: ext_ = 外装(切り取り表示で消す)、int_ = 内部(床板・柱。常に表示)。材質名は Godot 側で PBR 材質に差し替える。
"""
import bpy
import math
import sys
from pathlib import Path

OUT = Path(sys.argv[sys.argv.index("--") + 1]) if "--" in sys.argv else Path("out")
OUT.mkdir(parents=True, exist_ok=True)
FH = 2.0  # 階高


# ---------- 材質(名前だけが重要。Godot で差し替える) ----------
def material(name, color, metallic=0.0, rough=0.8, emit=None, strength=0.0):
    m = bpy.data.materials.get(name)
    if m:
        return m
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
    }
    

M = {}


# ---------- 幾何の部品 ----------
def box_mesh_data(boxes):
    """複数の箱(center, size)を 1 つのメッシュにする。"""
    verts, faces = [], []
    for (cx, cy, cz), (sx, sy, sz) in boxes:
        b = len(verts)
        for dx in (-1, 1):
            for dy in (-1, 1):
                for dz in (-1, 1):
                    verts.append((cx + dx * sx / 2, cy + dy * sy / 2, cz + dz * sz / 2))
        # 頂点の順: (dx,dy,dz) = (-,-,-),(-,-,+),(-,+,-),(-,+,+),(+,-,-),(+,-,+),(+,+,-),(+,+,+)
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
    """軸に平行な配管とフランジ、ボルト。"""
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
    M = build_materials()   # 材質はシーン初期化で消えるので毎回作り直す


def export(path):
    for o in bpy.context.scene.objects:
        o.select_set(not o.get("cutter", False))
    bpy.ops.export_scene.gltf(filepath=str(path), export_format="GLB", use_selection=True, export_apply=True, export_yup=True)
    print("exported", path)


# ---------- 共通の部品群 ----------
def floor_slab(w, thick=0.12, extra=0.2, mat=None):
    add_box("int_slab", (0, 0, thick / 2), (w + extra, w + extra, thick), mat or M["concrete"], bevel=0.03)
    add_box("int_slab_edge", (0, 0, thick + 0.02), (w + extra + 0.06, w + extra + 0.06, 0.04), M["steel"], bevel=0.01)


def columns(w, h, z0=0.0, size=0.2, mat=None):
    boxes = [((sx * w / 2, sy * w / 2, z0 + h / 2), (size, size, h)) for sx in (-1, 1) for sy in (-1, 1)]
    add_boxes("int_columns", boxes, mat or M["steel"], bevel=0.015)


def window_wall(w, z0, h, cols, win_w, win_h, sill, depth=0.18, lit_mat=None, lit_pattern=None, prefix="ext_wall"):
    """4 面の壁に窓の凹みを切り、奥にガラス、縁に方立と腰壁を付ける。"""
    shell = w
    cut_boxes = []
    panes = []
    frames = []
    for face in range(4):
        ang = face * math.pi / 2
        nx, ny = math.cos(ang), math.sin(ang)
        tx, ty = -math.sin(ang), math.cos(ang)
        for c in range(cols):
            t = ((c + 0.5) / cols - 0.5) * (shell - 0.4)
            cx, cy = nx * shell / 2 + tx * t, ny * shell / 2 + ty * t
            zc = z0 + sill + win_h / 2
            # 凹み: 壁面から depth だけ内側へ
            sz = (win_w if face % 2 else depth * 2, depth * 2 if face % 2 else win_w, win_h)
            cut_boxes.append(((cx, cy, zc), sz))
            lit = lit_pattern(face, c) if lit_pattern else (c % 2 == 0)
            pane_sz = (win_w - 0.06 if face % 2 else 0.02, 0.02 if face % 2 else win_w - 0.06, win_h - 0.06)
            pcx, pcy = cx - nx * (depth - 0.02), cy - ny * (depth - 0.02)
            panes.append((((pcx, pcy, zc), pane_sz), lit))
            # 庇(窓の下)
            frames.append(((cx + nx * 0.06, cy + ny * 0.06, z0 + sill - 0.02), (win_w + 0.1 if face % 2 else 0.16, 0.16 if face % 2 else win_w + 0.1, 0.05)))
        # 方立(窓と窓の間)
        for c in range(cols + 1):
            t = (c / cols - 0.5) * (shell - 0.4)
            cx, cy = nx * (shell / 2 + 0.03) + tx * t, ny * (shell / 2 + 0.03) + ty * t
            frames.append(((cx, cy, z0 + h / 2), (0.06 if face % 2 else 0.08, 0.08 if face % 2 else 0.06, h - 0.1)))
    cut = cutter("cutter_windows", cut_boxes)
    add_box(prefix, (0, 0, z0 + h / 2), (shell, shell, h), M["paintdark"], bevel=0.02, cutters=[cut])
    lit_boxes = [b for b, lit in panes if lit]
    dark_boxes = [b for b, lit in panes if not lit]
    if lit_boxes:
        add_boxes("ext_panes_lit", lit_boxes, lit_mat or M["winlit"], bevel=0)
    if dark_boxes:
        add_boxes("ext_panes_dark", dark_boxes, M["glass"], bevel=0)
    add_boxes("ext_frames", frames, M["steel"], bevel=0.01)


def ledge(w, z, extra=0.3, thick=0.08, mat=None):
    add_box(f"ext_ledge_{int(z*100)}", (0, 0, z), (w + extra, w + extra, thick), mat or M["concrete"], bevel=0.02)


def glow_strip(name, w, z, mat, inset=0.0, thick=0.05):
    boxes = [((0, (w / 2 + inset), z), (w, 0.04, thick)), ((0, -(w / 2 + inset), z), (w, 0.04, thick)),
             (((w / 2 + inset), 0, z), (0.04, w, thick)), ((-(w / 2 + inset), 0, z), (0.04, w, thick))]
    add_boxes(name, boxes, mat, bevel=0)


def pipes_back(w, z0, h, count=2):
    for i in range(count):
        y = -w / 2 - 0.12
        x = -w / 2 + 0.3 + i * 0.35
        pipe(f"ext_pipe{i}", (x, y, z0 + 0.15), (x, y, z0 + h - 0.15), 0.05, M["rust"] if i % 2 else M["steel"])


# ---------- モジュール ----------
def mod_base(w):
    """基壇階: 列柱・引っ込んだロビー・入口・庇・危険帯・発光帯。"""
    clear()
    floor_slab(w, 0.15, 0.8, M["concrete"])
    glow_strip("ext_base_glow", w + 0.8, 0.16, M["glowcyan"])
    add_box("ext_lobby", (0, 0, 0.15 + (FH - 0.3) / 2), (w - 0.7, w - 0.7, FH - 0.3), M["wincool"], bevel=0.02)
    cols = []
    for sx in (-1, 1):
        for sy in (-1, 1):
            cols.append(((sx * w / 2, sy * w / 2, FH / 2), (0.24, 0.24, FH)))
        for t in (-w / 6, w / 6):
            cols.append(((sx * w / 2, t, FH / 2), (0.16, 0.16, FH)))
            cols.append(((t, sx * w / 2, FH / 2), (0.16, 0.16, FH)))
    add_boxes("int_columns", cols, M["steel"], bevel=0.015)
    add_box("ext_entrance", (0, (w - 0.7) / 2 + 0.02, 0.15 + 0.75), (0.9, 0.06, 1.5), M["rubber"], bevel=0.01)
    add_box("ext_entrance_glow", (0, (w - 0.7) / 2 + 0.05, 0.15 + 1.52), (0.9, 0.03, 0.05), M["glowcyan"], bevel=0)
    add_box("ext_canopy", (0, w / 2 + 0.3, FH - 0.35), (w * 0.6, 0.8, 0.08), M["steel"], bevel=0.015)
    add_box("ext_hazard", (0, w / 2 + 0.45, 0.17), (w * 0.5, 0.4, 0.03), M["hazard"], bevel=0)
    add_box("int_ceiling", (0, 0, FH - 0.06), (w, w, 0.12), M["concrete"], bevel=0.02)
    ledge(w, FH - 0.02, 0.3, 0.1, M["steel"])
    export(OUT / f"base{w}.glb")


def mod_office(w, cool=False):
    """標準階(事務所): 窓の凹み、方立、庇、腰壁、背面の配管、通気口。"""
    clear()
    floor_slab(w)
    columns(w, FH - 0.12, 0.12)
    window_wall(w, 0.12, FH - 0.24, max(2, w), 0.5, 0.95, 0.45, lit_mat=M["wincool"] if cool else M["winlit"],
                lit_pattern=lambda f, c: (f * 3 + c) % 5 != 1)
    add_box("ext_spandrel", (0, 0, 0.12 + 0.2), (w + 0.04, w + 0.04, 0.3), M["paint"], bevel=0.015)
    pipes_back(w, 0.12, FH - 0.24)
    # 通気口(側面)
    slats = [((w / 2 + 0.03, 0.9, 0.12 + 1.2 + i * 0.07), (0.06, 0.35, 0.03)) for i in range(5)]
    add_boxes("ext_vent", slats, M["rubber"], bevel=0)
    add_box("int_ceiling", (0, 0, FH - 0.06), (w, w, 0.12), M["concrete"], bevel=0.02)
    export(OUT / f"office{w}{'c' if cool else ''}.glb")


def mod_residential(w):
    """標準階(住居): 窓とベランダ(床板・手すり・鉢植え)、暖色の光。"""
    clear()
    floor_slab(w)
    columns(w, FH - 0.12, 0.12)
    window_wall(w, 0.12, FH - 0.24, max(2, w), 0.45, 0.8, 0.5, lit_mat=M["winlit"], lit_pattern=lambda f, c: (f + c) % 3 != 0)
    for face in range(4):
        ang = face * math.pi / 2
        nx, ny = math.cos(ang), math.sin(ang)
        tx, ty = -math.sin(ang), math.cos(ang)
        d = w / 2 + 0.3
        add_box(f"ext_balcony{face}", (nx * d, ny * d, 0.12 + 0.03), (0.6 if face % 2 == 0 else w * 0.7, w * 0.7 if face % 2 == 0 else 0.6, 0.06), M["concrete"], bevel=0.015)
        rail = [((nx * (d + 0.27), ny * (d + 0.27), 0.12 + 0.55), (0.03 if face % 2 == 0 else w * 0.7, w * 0.7 if face % 2 == 0 else 0.03, 0.03))]
        for k in range(-2, 3):
            rail.append(((nx * (d + 0.27) + tx * k * w * 0.17, ny * (d + 0.27) + ty * k * w * 0.17, 0.12 + 0.3), (0.03, 0.03, 0.5)))
        add_boxes(f"ext_rail{face}", rail, M["rust"], bevel=0)
        add_box(f"ext_plant{face}", (nx * (d + 0.1) + tx * w * 0.25, ny * (d + 0.1) + ty * w * 0.25, 0.12 + 0.25), (0.3, 0.3, 0.3), M["foliage"], bevel=0.03)
    add_box("int_ceiling", (0, 0, FH - 0.06), (w, w, 0.12), M["concrete"], bevel=0.02)
    export(OUT / f"res{w}.glb")


def mod_mech(w):
    """機械階: ルーバーの壁、太い配管、タンク、発光帯。窓なし。"""
    clear()
    floor_slab(w, 0.12, 0.3, M["steel"])
    columns(w, FH - 0.12, 0.12, 0.24)
    add_box("ext_core", (0, 0, 0.12 + (FH - 0.24) / 2), (w - 0.3, w - 0.3, FH - 0.24), M["paintdark"], bevel=0.02)
    for face in range(4):
        ang = face * math.pi / 2
        nx, ny = math.cos(ang), math.sin(ang)
        slats = []
        for i in range(10):
            z = 0.12 + 0.3 + i * 0.14
            slats.append(((nx * (w / 2 - 0.02), ny * (w / 2 - 0.02), z), (0.08 if face % 2 == 0 else w - 0.6, w - 0.6 if face % 2 == 0 else 0.08, 0.05)))
        add_boxes(f"ext_louver{face}", slats, M["steel"], bevel=0)
    pipe("ext_bigpipe", (-w / 2 - 0.25, -w / 2 + 0.4, 0.5), (-w / 2 - 0.25, w / 2 - 0.4, 0.5), 0.12, M["rust"])
    pipe("ext_bigpipe2", (-w / 2 - 0.25, w / 2 - 0.4, 0.5), (-w / 2 - 0.25, w / 2 - 0.4, FH - 0.3), 0.12, M["rust"])
    add_cyl("ext_tank", (w / 2 + 0.35, -w / 2 + 0.5, 0.12 + 0.6), 0.32, 1.2, M["rust"], 24)
    add_cyl("ext_tank_cap", (w / 2 + 0.35, -w / 2 + 0.5, 0.12 + 1.26), 0.3, 0.12, M["steel"], 24, r2=0.18)
    glow_strip("ext_mech_glow", w - 0.3, 0.12 + 0.18, M["gloworange"], inset=0.03, thick=0.04)
    add_box("int_ceiling", (0, 0, FH - 0.06), (w, w, 0.12), M["concrete"], bevel=0.02)
    export(OUT / f"mech{w}.glb")


def mod_transfer(w, w2):
    """後退階: 広い床板(庭園・手すり)の上に細い胴体。上に w2 のモジュールが積める。"""
    clear()
    floor_slab(w, 0.14, 0.5, M["steel"])
    glow_strip("ext_deck_glow", w + 0.5, 0.15, M["glowcyan"])
    rail = []
    for sx in (-1, 1):
        rail.append(((sx * (w + 0.5) / 2, 0, 0.14 + 0.45), (0.03, w + 0.5, 0.03)))
        rail.append(((0, sx * (w + 0.5) / 2, 0.14 + 0.45), (w + 0.5, 0.03, 0.03)))
        for k in range(-3, 4):
            rail.append(((sx * (w + 0.5) / 2, k * (w + 0.5) / 7, 0.14 + 0.22), (0.03, 0.03, 0.45)))
            rail.append(((k * (w + 0.5) / 7, sx * (w + 0.5) / 2, 0.14 + 0.22), (0.03, 0.03, 0.45)))
    add_boxes("ext_deck_rail", rail, M["rust"], bevel=0)
    plants = [((sx * (w / 2 + 0.05), sy * (w / 2 + 0.05), 0.14 + 0.2), (0.4, 0.4, 0.4)) for sx in (-1, 1) for sy in (-1, 1)]
    add_boxes("ext_deck_plants", plants, M["foliage"], bevel=0.04)
    columns(w2, FH - 0.14, 0.14)
    window_wall(w2, 0.14, FH - 0.28, max(2, w2), 0.5, 0.95, 0.45, lit_mat=M["wincool"], lit_pattern=lambda f, c: (f + c) % 2 == 0)
    add_box("int_ceiling", (0, 0, FH - 0.06), (w2, w2, 0.12), M["concrete"], bevel=0.02)
    export(OUT / f"transfer{w}to{w2}.glb")


def mod_roof(w, kind):
    """屋上: 欄干・機械室・空調機・給水塔・アンテナと赤灯。kind: mech / helipad / spire。"""
    clear()
    floor_slab(w, 0.12, 0.2, M["concrete"])
    par = [((0, w / 2 + 0.08, 0.3), (w + 0.2, 0.08, 0.36)), ((0, -w / 2 - 0.08, 0.3), (w + 0.2, 0.08, 0.36)),
           ((w / 2 + 0.08, 0, 0.3), (0.08, w + 0.2, 0.36)), ((-w / 2 - 0.08, 0, 0.3), (0.08, w + 0.2, 0.36))]
    add_boxes("ext_parapet", par, M["concrete"], bevel=0.015)
    add_box("ext_penthouse", (-w * 0.2, -w * 0.2, 0.12 + 0.45), (w * 0.4, w * 0.4, 0.9), M["paintdark"], bevel=0.02)
    add_box("ext_penthouse_door", (-w * 0.2, -w * 0.2 + w * 0.2 + 0.02, 0.12 + 0.4), (0.4, 0.04, 0.7), M["rubber"], bevel=0)
    ac = [((w * 0.25, -w * 0.3 + i * w * 0.25, 0.12 + 0.2), (0.4, 0.4, 0.4)) for i in range(3)]
    add_boxes("ext_ac", ac, M["steel"], bevel=0.02)
    add_cyl("ext_tank", (w * 0.25, w * 0.3, 0.12 + 0.9), 0.26, 0.6, M["rust"], 20)
    legs = [((w * 0.25 + sx * 0.18, w * 0.3 + sy * 0.18, 0.12 + 0.3), (0.05, 0.05, 0.6)) for sx in (-1, 1) for sy in (-1, 1)]
    add_boxes("ext_tank_legs", legs, M["steel"], bevel=0)
    if kind == "spire":
        add_cyl("ext_spire", (0, 0, 0.12 + 1.2), 0.3, 2.4, M["steel"], 12, r2=0.06)
        add_cyl("ext_mast", (0, 0, 0.12 + 3.4), 0.03, 2.0, M["steel"], 8, bevel=0)
        add_cyl("ext_beacon", (0, 0, 0.12 + 4.45), 0.09, 0.14, M["glowred"], 8, bevel=0)
    elif kind == "helipad":
        add_cyl("ext_helipad", (-w * 0.15, w * 0.15, 0.12 + 0.72), w * 0.3, 0.1, M["concrete"], 24)
        add_cyl("ext_helipad_ring", (-w * 0.15, w * 0.15, 0.12 + 0.78), w * 0.31, 0.02, M["glowcyan"], 24, bevel=0)
        add_cyl("ext_mast", (w * 0.3, -w * 0.3, 0.12 + 0.8), 0.025, 1.4, M["steel"], 8, bevel=0)
        add_cyl("ext_beacon", (w * 0.3, -w * 0.3, 0.12 + 1.55), 0.07, 0.12, M["glowred"], 8, bevel=0)
    else:
        add_cyl("ext_mast", (w * 0.3, -w * 0.3, 0.12 + 0.8), 0.025, 1.4, M["steel"], 8, bevel=0)
        add_cyl("ext_beacon", (w * 0.3, -w * 0.3, 0.12 + 1.55), 0.07, 0.12, M["glowred"], 8, bevel=0)
        for i in range(3):
            add_cyl(f"ext_ant{i}", (-w * 0.35 + i * 0.3, w * 0.35, 0.12 + 0.5), 0.015, 0.8, M["steel"], 6, bevel=0)
    export(OUT / f"roof{w}_{kind}.glb")


def mod_hangar(w):
    """特殊階(格納庫): 大扉の開口と橙の発光、内部は空。ロボットの出撃口。"""
    clear()
    floor_slab(w, 0.12, 0.3, M["steel"])
    columns(w, FH - 0.12, 0.12, 0.26)
    cut = cutter("cutter_door", [((0, w / 2, 0.12 + 0.75), (w * 0.6, 0.6, 1.4))])
    add_box("ext_wall", (0, 0, 0.12 + (FH - 0.24) / 2), (w, w, FH - 0.24), M["paintdark"], bevel=0.02, cutters=[cut])
    add_box("ext_door_glow", (0, w / 2 + 0.02, 0.12 + 1.48), (w * 0.6, 0.04, 0.05), M["gloworange"], bevel=0)
    add_box("ext_door_hazard", (0, w / 2 + 0.06, 0.16), (w * 0.6, 0.1, 0.04), M["hazard"], bevel=0)
    add_box("int_floor_grate", (0, 0, 0.13), (w - 0.5, w - 0.5, 0.02), M["grate"], bevel=0)
    for face in (1, 2, 3):
        ang = face * math.pi / 2
        nx, ny = math.cos(ang), math.sin(ang)
        add_box(f"ext_stripe{face}", (nx * (w / 2 + 0.01), ny * (w / 2 + 0.01), 0.12 + 1.6), (0.02 if face % 2 == 0 else w * 0.8, w * 0.8 if face % 2 == 0 else 0.02, 0.06), M["gloworange"], bevel=0)
    add_box("int_ceiling", (0, 0, FH - 0.06), (w, w, 0.12), M["concrete"], bevel=0.02)
    export(OUT / f"hangar{w}.glb")


if __name__ == "__main__":
    mod_base(3)
    mod_office(3)
    mod_office(3, cool=True)
    mod_residential(3)
    mod_mech(3)
    mod_transfer(3, 2)
    mod_office(2)
    mod_office(2, cool=True)
    mod_hangar(3)
    mod_roof(3, "mech")
    mod_roof(3, "helipad")
    mod_roof(2, "spire")
    mod_roof(2, "mech")
