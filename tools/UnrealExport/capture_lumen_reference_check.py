"""Checks whether the UE reference images (SceneCapture2D) contain Lumen's indirect light (M0112).

Runs in the GUI editor through run_capture_lumen_reference_check.cmd (UnrealEditor.exe, -unattended, the Vehicle project). The same view of the
Vehicle level is captured three ways: A, a SceneCapture2D as capture_vehicle_reference.py uses it; B, the same capture with the rendering state
persisted between frames; C, the editor viewport's own high-resolution screenshot. Then r.DynamicGlobalIlluminationMethod is set to 0 (no
global illumination) and A and C are taken again. If a capture is unchanged by that, it never contained Lumen's GI.
Images go to .artifacts/UnrealExport/Lvl_VehicleBasic/lumen_check_*. Nothing in the UE project is saved.
"""

import os

import unreal

REPO_ROOT = os.environ.get("CSGE_REPO_ROOT", "G:/GitHub/CSharpGameEngine").replace("\\", "/")
OUTPUT_DIR = f"{REPO_ROOT}/.artifacts/UnrealExport/Lvl_VehicleBasic"
LOCATION = unreal.Vector(3850.0, -1500.0, 222.0)
ROTATION = unreal.Rotator(roll=0.0, pitch=-6.0, yaw=60.0)
state = {"ticks": 0, "handle": None, "captures": [], "world": None}


def add_capture(actors, name, persist):
    capture = actors.spawn_actor_from_class(unreal.SceneCapture2D, LOCATION, ROTATION)
    component = capture.get_editor_property("capture_component2d")
    target = unreal.RenderingLibrary.create_render_target2d(capture, 1280, 720, unreal.TextureRenderTargetFormat.RTF_RGBA8_SRGB)
    component.set_editor_property("texture_target", target)
    component.set_editor_property("fov_angle", 90.0)
    component.set_editor_property("capture_source", unreal.SceneCaptureSource.SCS_FINAL_TONE_CURVE_HDR)
    component.set_editor_property("capture_every_frame", True)
    if persist:
        component.set_editor_property("always_persist_rendering_state", True)
    state["captures"].append((name, component, target))


def export(name):
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    for capture_name, component, target in state["captures"]:
        if capture_name.startswith(name):
            component.capture_scene()
            unreal.RenderingLibrary.export_render_target(component, target, OUTPUT_DIR, capture_name + ("_nogi" if state.get("off") else ""))


def begin() -> None:
    unreal.get_editor_subsystem(unreal.LevelEditorSubsystem).load_level("/Game/VehicleTemplate/Maps/Lvl_VehicleBasic")
    state["world"] = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()
    actors = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    add_capture(actors, "lumen_check_a_scenecapture", False)
    add_capture(actors, "lumen_check_b_persisted", True)
    unreal.EditorLevelLibrary.set_level_viewport_camera_info(LOCATION, ROTATION)
    for name in ("r.DynamicGlobalIlluminationMethod", "r.ReflectionMethod", "r.Lumen.Supported", "r.Lumen.DiffuseIndirect.Allow"):
        unreal.log(f"[UnrealExport] {name} = {unreal.SystemLibrary.get_console_variable_int_value(name)}")
    state["handle"] = unreal.register_slate_post_tick_callback(tick)


def tick(delta_seconds: float) -> None:
    state["ticks"] += 1
    ticks = state["ticks"]
    if ticks == 250:
        unreal.AutomationLibrary.take_high_res_screenshot(1280, 720, "lumen_check_c_viewport.png")
    elif ticks == 300:
        export("lumen_check_")
        unreal.SystemLibrary.execute_console_command(state["world"], "r.DynamicGlobalIlluminationMethod 0")
        state["off"] = True
    elif ticks == 420:
        unreal.AutomationLibrary.take_high_res_screenshot(1280, 720, "lumen_check_c_viewport_nogi.png")
    elif ticks == 470:
        export("lumen_check_")
        unreal.unregister_slate_post_tick_callback(state["handle"])
        unreal.SystemLibrary.quit_editor()


begin()
