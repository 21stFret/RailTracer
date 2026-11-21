"""Rail Tracer: the player's model (the conductor), from the Renderpeople Eric download.

rp_eric_rigged_001_u3d.fbx comes in centimetres under a 0.01 armature scale (Unity's Ragdoll Builder makes millimetre
colliders in that space) with no material and a stray cube, camera and light. This re-exports it in metres at scale 1,
the armature and the one skinned mesh only, the mesh wearing Chr_Conductor (made by Editor/CharacterPackSetup.cs from
Eric's own textures). Bone names, rest pose and weights are unchanged.

  in:  Assets/RailTracer/Characters/37-rp_eric_rigged_001_fbx/rp_eric_rigged_001_u3d.fbx
  out: Assets/RailTracer/Characters/37-rp_eric_rigged_001_fbx/SK_Conductor.fbx (next to its source; gitignored with it)

Run:  blender -b --factory-startup --python railtracer_conductor.py
"""
import bpy, os

DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "RailTracer", "Characters", "37-rp_eric_rigged_001_fbx"))
SRC = os.path.join(DIR, "rp_eric_rigged_001_u3d.fbx")
OUT = os.path.join(DIR, "SK_Conductor.fbx")

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
me = next(o for o in bpy.data.objects if o.type == 'MESH' and o.parent == arm)
for o in list(bpy.data.objects):
    if o not in (arm, me):
        bpy.data.objects.remove(o)
height0 = max((me.matrix_world @ v.co).z for v in me.data.vertices)

# metres at scale 1: apply the armature's 0.01 to its bones and the mesh
bpy.ops.object.select_all(action='DESELECT')
arm.select_set(True); me.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

me.data.materials.clear()
me.data.materials.append(bpy.data.materials.new("Chr_Conductor"))
for p in me.data.polygons:
    p.material_index = 0

height = max((me.matrix_world @ v.co).z for v in me.data.vertices)
print("CONDUCTOR scale", tuple(round(x, 3) for x in arm.scale), "height %.3f (was %.3f)" % (height, height0),
      "bones", len(arm.data.bones), "faces", len(me.data.polygons))
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={'ARMATURE', 'MESH'},
                         add_leaf_bones=False, bake_anim=False, mesh_smooth_type='FACE')

# check: re-import, same height, scale 1, every vertex weighted
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=OUT)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
me = next(o for o in bpy.data.objects if o.type == 'MESH')
print("CHECK SK_Conductor scale", tuple(round(x, 3) for x in arm.scale), "height %.3f" % max((me.matrix_world @ v.co).z for v in me.data.vertices),
      "unweighted", sum(1 for v in me.data.vertices if not v.groups), "materials", [m.name for m in me.data.materials],
      "eye bones", [b for b in ("eye_l", "eye_r") if b in arm.data.bones])
