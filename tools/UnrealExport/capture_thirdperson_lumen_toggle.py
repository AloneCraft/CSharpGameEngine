"""Checks that Lumen runs in the UE reference captures (M0112): captures the ThirdPerson spawn view, then turns the global illumination method off
(r.DynamicGlobalIlluminationMethod 0) and captures again. If the two images are the same, the reference had no Lumen.

Runs in the GUI editor through run_capture_thirdperson_lumen_toggle.cmd. Images: .artifacts/UnrealExport/Lvl_ThirdPerson/lumen_toggle_on, lumen_toggle_off.
Nothing in the UE project is saved.
"""

import os

import unreal

REPO_ROOT = os.environ.get("CSGE_REPO_ROOT", "G:/GitHub/CSharpGameEngine").replace("\\", "/")
OUTPUT_DIR = f"{REPO_ROOT}/.artifacts/UnrealExport/Lvl_ThirdPerson"
state = {"ticks": 0, "handle": None, "component": None, "target": None, "world": None}


def begin() -> None:
    unreal.get_editor_subsystem(unreal.LevelEditorSubsystem).load_level("/Game/ThirdPerson/Lvl_ThirdPerson")
    state["world"] = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()
    actors = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    start = [actor for actor in actors.get_all_level_actors() if isinstance(actor, unreal.PlayerStart)][0]
    location = start.get_actor_location()
    capture = actors.spawn_actor_from_class(unreal.SceneCapture2D, unreal.Vector(location.x - 400.0, location.y, location.z + 8.492264), unreal.Rotator(0.0, 0.0, 0.0))
    component = capture.get_editor_property("capture_component2d")
    target = unreal.RenderingLibrary.create_render_target2d(capture, 1280, 720, unreal.TextureRenderTargetFormat.RTF_RGBA8_SRGB)
    component.set_editor_property("texture_target", target)
    component.set_editor_property("fov_angle", 90.0)
    component.set_editor_property("capture_source", unreal.SceneCaptureSource.SCS_FINAL_TONE_CURVE_HDR)
    component.set_editor_property("capture_every_frame", True)
    component.set_editor_property("always_persist_rendering_state", True)
    state["component"] = component
    state["target"] = target
    state["handle"] = unreal.register_slate_post_tick_callback(tick)


def write(name: str) -> None:
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    state["component"].capture_scene()
    unreal.RenderingLibrary.export_render_target(state["component"], state["target"], OUTPUT_DIR, name)
    unreal.log(f"[UnrealExport] Wrote capture {OUTPUT_DIR}/{name}")


def tick(delta_seconds: float) -> None:
    state["ticks"] += 1
    if state["ticks"] == 300:
        write("lumen_toggle_on")
        unreal.SystemLibrary.execute_console_command(state["world"], "r.DynamicGlobalIlluminationMethod 0")
        unreal.SystemLibrary.execute_console_command(state["world"], "r.ReflectionMethod 2")
    elif state["ticks"] == 420:
        write("lumen_toggle_off")
        unreal.unregister_slate_post_tick_callback(state["handle"])
        unreal.SystemLibrary.quit_editor()


begin()
