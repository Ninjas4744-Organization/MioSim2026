"""Export the packed editable game model without exporting studio objects.

blender -b Models/robot.blend --python Tools/Blender/export_model.py
"""
from pathlib import Path
import bpy

repo = Path(__file__).resolve().parents[2]
destination = repo / "Assets/Prefabs/Games/Rebuilt/Robots/Mods/Ninjas4744/Model/Turbo4744.fbx"
root = bpy.data.objects.get("Turbo4744_Game")
if root is None:
    raise RuntimeError("Open Models/robot.blend; expected root Turbo4744_Game")
bpy.ops.object.select_all(action="DESELECT")
for obj in [root, *root.children_recursive]:
    if obj.type in {"MESH", "EMPTY"}:
        obj.hide_set(False)
        obj.select_set(True)
bpy.context.view_layer.objects.active = root
destination.parent.mkdir(parents=True, exist_ok=True)
bpy.ops.export_scene.fbx(
    filepath=str(destination), use_selection=True,
    object_types={"MESH", "EMPTY"}, use_mesh_modifiers=True,
    # Preserve the original FBX's centimetre metadata and native coordinates.
    # Unity prefabs already normalize its metre-long axis markers; changing
    # this unit metadata would enlarge existing serialized meshes 100 times.
    apply_unit_scale=True, global_scale=.01, apply_scale_options="FBX_SCALE_ALL",
    axis_forward="-Z", axis_up="Y", bake_space_transform=False,
    add_leaf_bones=False, bake_anim=False, path_mode="STRIP",
    use_custom_props=True,
)
print(f"Exported {destination}; source .blend was not overwritten")
