"""Writes the TopDown character's camera and spring arm settings read from a spawned BP_TopDownCharacter (projection mode, FOV, arm).

Run through run_probe_topdown_camera.cmd; the result goes to the file named by CGE_PROBE_OUT. Nothing is saved.
Why: M0107 asked whether the UE TopDown camera is orthographic; the T3D export lists only FieldOfView and OrthoWidth.
"""
import os
import unreal

lines = []
try:
    cls = unreal.EditorAssetLibrary.load_blueprint_class("/Game/TopDown/Blueprints/BP_TopDownCharacter")
    actors = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    actor = actors.spawn_actor_from_class(cls, unreal.Vector(0, 0, 0), unreal.Rotator(0, 0, 0))
    for camera in actor.get_components_by_class(unreal.CameraComponent):
        lines.append("CAMERA " + camera.get_name())
        for name in ("projection_mode", "field_of_view", "ortho_width", "aspect_ratio", "constrain_aspect_ratio"):
            lines.append(f"  {name} = {camera.get_editor_property(name)}")
    for arm in actor.get_components_by_class(unreal.SpringArmComponent):
        lines.append("ARM " + arm.get_name())
        for name in ("target_arm_length", "use_pawn_control_rotation", "inherit_pitch", "inherit_yaw", "enable_camera_lag", "camera_lag_speed", "do_collision_test"):
            lines.append(f"  {name} = {arm.get_editor_property(name)}")
        lines.append(f"  relative_rotation = {arm.get_editor_property('relative_rotation')}")
except Exception as ex:  # noqa: BLE001 - the failure goes to the output file
    lines.append(f"FAILED: {ex}")
with open(os.environ["CGE_PROBE_OUT"], "w", encoding="utf-8") as handle:
    handle.write("\n".join(lines))
