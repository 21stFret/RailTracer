"""Rail Tracer player model (Update 20.6): Alex's RedLineMainChar_v2_resized on the NYC1950 pack skeleton.

  Characters/RedLineMainChar_v2_resized.fbx  one static mesh, no bones (a CH04 from the NYC1950 series), already in
      metres in CH02's A-pose and place (bounds within 2 cm of SK_CH02_FullClothes).
  -> Characters/SK_Inspector.fbx  the same mesh on CH02's unchanged 67-bone skeleton, its weights copied from CH02's
      skinned mesh (nearest surface, interpolated), in our materials so it dresses like the guards:
      jacket and trousers Chr_Suit (the game tints it the inspector's beige), shirt Chr_Shirt, tie Chr_Velvet, shoes
      (the low loose parts of the trouser material) Chr_Shoes, head and hands Chr_Skin, hair and brows Chr_Hair, eyes
      and their wet film Chr_Eye, mouth Chr_Mouth.
  Editor/CharacterPackSetup.cs imports it Humanoid and makes Resources/Anim/Inspector.prefab (ragdoll and all).

Run:  blender -b --factory-startup --python railtracer_player.py [-- --render <dir>]
"""
import bpy, bmesh, math, os, sys
from mathutils import Vector

# >>> CONFIG
ASSETS = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
SRC = os.path.join(ASSETS, "RailTracer", "Characters", "RedLineMainChar_v2_resized.fbx")
RIG = os.path.join(ASSETS, "NYC1950_CH02", "Meshes", "SK_CH02_FullClothes.fbx")
OUT = os.path.join(ASSETS, "RailTracer", "Characters", "SK_Inspector.fbx")
SHOE_TOP = 0.16    # metres: loose parts of the trouser material that stay below this are shoes
MATS = [("lacrimal", "Chr_Eye"), ("Eye", "Chr_Eye"), ("Mouth", "Chr_Mouth"), ("Hair", "Chr_Hair"), ("Head", "Chr_Skin"),
        ("Body", "Chr_Skin"), ("tie", "Chr_Velvet"), ("Clothes03", "Chr_Shirt"), ("Clothes01", "Chr_Suit"), ("Clothes02", "Chr_Suit")]
# <<< CONFIG

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
RENDER = argv[argv.index("--render") + 1] if "--render" in argv else None


def ours(name):
    return next(n for k, n in MATS if k in name)


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=RIG)
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    ref = next(o for o in bpy.data.objects if o.type == 'MESH')
    bpy.ops.import_scene.fbx(filepath=SRC)
    me = next(o for o in bpy.data.objects if o.type == 'MESH' and o != ref)
    me.name = me.data.name = "SK_Inspector"

    # Our materials, one slot each; shoes split off the trousers by loose part.
    names = [ours(m.name) for m in me.data.materials]
    slots = list(dict.fromkeys(names + ["Chr_Shoes"]))
    old = [p.material_index for p in me.data.polygons]
    me.data.materials.clear()
    for n in slots:
        me.data.materials.append(bpy.data.materials.get(n) or bpy.data.materials.new(n))
    for p, i in zip(me.data.polygons, old):
        p.material_index = slots.index(names[i])
    bm = bmesh.new(); bm.from_mesh(me.data); bm.faces.ensure_lookup_table()
    suit = slots.index("Chr_Suit"); shoes = slots.index("Chr_Shoes")
    seen = set(); nshoe = 0
    for f in bm.faces:
        if f.index in seen or f.material_index != suit:
            continue
        part, stack = [], [f]; seen.add(f.index)
        while stack:
            g = stack.pop(); part.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen and h.material_index == suit:
                        seen.add(h.index); stack.append(h)
        if max(v.co.z for g in part for v in g.verts) < SHOE_TOP:
            for g in part:
                g.material_index = shoes
            nshoe += len(part)
    bm.to_mesh(me.data); bm.free()

    # CH02's skin weights, nearest surface.
    for g in ref.vertex_groups:
        me.vertex_groups.new(name=g.name)
    dt = me.modifiers.new("Weights", 'DATA_TRANSFER')
    dt.object = ref
    dt.use_vert_data = True
    dt.data_types_verts = {'VGROUP_WEIGHTS'}
    dt.vert_mapping = 'POLYINTERP_NEAREST'
    dt.layers_vgroup_select_src = 'ALL'
    dt.layers_vgroup_select_dst = 'NAME'
    bpy.context.view_layer.objects.active = me
    bpy.ops.object.modifier_apply(modifier=dt.name)
    bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL', limit=4)
    bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL', lock_active=False)
    unweighted = sum(1 for v in me.data.vertices if not v.groups)

    bpy.data.objects.remove(ref)
    me.parent = arm
    me.matrix_parent_inverse = arm.matrix_world.inverted()
    me.modifiers.new("Armature", 'ARMATURE').object = arm
    me.data.validate(); me.data.update()
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True); me.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={'ARMATURE', 'MESH'},
                             add_leaf_bones=False, bake_anim=False, mesh_smooth_type='FACE')
    tris = sum(len(p.vertices) - 2 for p in me.data.polygons)
    print("EXPORTED", OUT, "tris", tris, "materials", slots, "shoe faces", nshoe, "unweighted verts", unweighted)
    if RENDER:
        render(me, arm, os.path.join(RENDER, "SK_Inspector"))


def render(me, arm, base):
    scn = bpy.context.scene
    scn.render.engine = 'BLENDER_WORKBENCH'
    scn.display.shading.color_type = 'MATERIAL'
    scn.render.resolution_x, scn.render.resolution_y = 640, 900
    cols = {"Chr_Suit": (0.66, 0.56, 0.42), "Chr_Velvet": (0.2, 0.195, 0.21), "Chr_Shirt": (0.85, 0.82, 0.74),
            "Chr_Skin": (0.78, 0.58, 0.45), "Chr_Hair": (0.16, 0.1, 0.05), "Chr_Shoes": (0.03, 0.03, 0.03),
            "Chr_Eye": (0.08, 0.05, 0.03), "Chr_Mouth": (0.45, 0.18, 0.17)}
    for m in me.data.materials:
        m.diffuse_color = (*cols.get(m.name, (1, 0, 1)), 1)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scn.collection.objects.link(cam); scn.camera = cam
    cam.data.type = 'ORTHO'

    def shoot(tag):
        for name, (yaw, pitch, scale, tgt) in {"front": (0, 8, 2.1, 0.95), "side": (90, 8, 2.1, 0.95),
                                               "game": (-25, 50, 2.3, 0.9), "head": (-30, 15, 0.55, 1.7)}.items():
            cam.data.ortho_scale = scale
            y, p = math.radians(yaw), math.radians(pitch)
            d = Vector((math.sin(y) * math.cos(p), -math.cos(y) * math.cos(p), math.sin(p)))
            cam.location = Vector((0, 0, tgt)) + d * 6
            cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
            scn.render.filepath = base + tag + "_" + name + ".png"
            bpy.ops.render.render(write_still=True)

    shoot("")
    # a pose: stride, arms down and bent, head turned, fist closed: the weights should bend cleanly
    pb = arm.pose.bones
    for bn, axis, ang in (("thigh_l", 'X', -40), ("thigh_r", 'X', 30), ("calf_r", 'X', 45), ("upperarm_l", 'Y', 50),
                          ("upperarm_r", 'Y', -50), ("lowerarm_r", 'Z', 70), ("head", 'Z', 30), ("spine_02", 'X', 15)):
        pb[bn].rotation_mode = 'XYZ'
        setattr(pb[bn].rotation_euler, axis.lower(), math.radians(ang))
    for f in ("index", "middle", "ring", "pinky"):
        for k in ("01", "02", "03"):
            pb[f + "_" + k + "_r"].rotation_mode = 'XYZ'; pb[f + "_" + k + "_r"].rotation_euler.z = math.radians(60)
    bpy.context.view_layer.update()
    shoot("_posed")


main()
