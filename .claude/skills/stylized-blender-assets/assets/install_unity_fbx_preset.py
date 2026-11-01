"""
Run once in Blender's Scripting tab. Adds a "Kit Unity" preset to
File > Export > FBX (top of the export panel, Operator Presets dropdown).
"""
import bpy, os

preset_dir = bpy.utils.user_resource('SCRIPTS', path=os.path.join("presets", "operator", "export_scene.fbx"),
                                     create=True)
settings = {
    "use_selection": True,                 # export what you select (include child objects)
    "object_types": {'EMPTY', 'MESH'},
    "apply_unit_scale": True,
    "apply_scale_options": 'FBX_SCALE_ALL', # 1 Blender meter = 1 Unity unit, scale 1 in Unity
    "axis_forward": '-Z',
    "axis_up": 'Y',
    "bake_space_transform": True,           # no -90 X rotation on import
    "use_mesh_modifiers": True,
    "mesh_smooth_type": 'FACE',
    "use_custom_props": False,
    "add_leaf_bones": False,
    "bake_anim": False,
    "path_mode": 'AUTO',
}
lines = ["import bpy", "op = bpy.context.active_operator", ""]
lines += [f"op.{k} = {v!r}" for k, v in settings.items()]
path = os.path.join(preset_dir, "Kit_Unity.py")
with open(path, "w") as f:
    f.write("\n".join(lines) + "\n")
print("Saved FBX preset:", path)
