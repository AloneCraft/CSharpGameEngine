"""Captures a reference image of Lvl_ThirdPerson from the ThirdPerson spawn camera pose.

Runs in the GUI editor (UnrealEditor.exe, not -Cmd) so Lumen and the sky have rendered frames
to converge. A SceneCapture2D is placed at the template character's initial camera pose
(PlayerStart + OrbitCamera 400 cm, FOV 90) and captured after warm-up ticks. The image is written
to .artifacts and is only a visual comparison reference; it is never shipped.
"""

import os

import unreal

REPO_ROOT = os.environ.get("CSGE_REPO_ROOT", "G:/GitHub/CSharpGameEngine").replace("\\", "/")
OUTPUT_DIR = f"{REPO_ROOT}/.artifacts/UnrealExport/Lvl_ThirdPerson"
OUTPUT_NAME = "ue_reference_spawn_camera"
WARMUP_TICKS = 240
state = {"ticks": 0, "handle": None, "capture": None, "target": None}


def begin() -> None:
    unreal.get_editor_subsystem(unreal.LevelEditorSubsystem).load_level("/Game/ThirdPerson/Lvl_ThirdPerson")
    actors = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    start = [actor for actor in actors.get_all_level_actors() if isinstance(actor, unreal.PlayerStart)][0]
    location = start.get_actor_location()
    camera_location = unreal.Vector(location.x - 400.0, location.y, location.z + 8.492264)
    capture = actors.spawn_actor_from_class(unreal.SceneCapture2D, camera_location, unreal.Rotator(0.0, 0.0, 0.0))
    component = capture.get_editor_property("capture_component2d")
    target = unreal.RenderingLibrary.create_render_target2d(capture, 1280, 720, unreal.TextureRenderTargetFormat.RTF_RGBA8_SRGB)
    component.set_editor_property("texture_target", target)
    component.set_editor_property("fov_angle", 90.0)
    component.set_editor_property("capture_source", unreal.SceneCaptureSource.SCS_FINAL_TONE_CURVE_HDR)
    component.set_editor_property("capture_every_frame", True)
    state["capture"] = component
    state["target"] = target
    state["handle"] = unreal.register_slate_post_tick_callback(tick)


def tick(delta_seconds: float) -> None:
    state["ticks"] += 1
    if state["ticks"] < WARMUP_TICKS:
        return
    unreal.unregister_slate_post_tick_callback(state["handle"])
    state["capture"].capture_scene()
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    unreal.RenderingLibrary.export_render_target(state["capture"], state["target"], OUTPUT_DIR, OUTPUT_NAME)
    unreal.log(f"[UnrealExport] Wrote reference capture {OUTPUT_DIR}/{OUTPUT_NAME}")
    unreal.SystemLibrary.quit_editor()


begin()
