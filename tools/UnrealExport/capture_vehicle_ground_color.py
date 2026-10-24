"""Captures the Vehicle template Landscape's base colour from above (M0112), plus lit top-down and oblique reference views.

Runs in the GUI editor (UnrealEditor.exe) through run_capture_vehicle_ground_color.cmd. Why: the Landscape material blends the Road layer
(painted along the spline with a falloff) and the slope-tinted Ground layer, and samples a runtime virtual texture; a base-colour capture
with only the Landscape visible reads the finished albedo without reverse-engineering the layer weights. The result is only the albedo
(no lighting), so the engine still lights it itself. Nothing in the UE project is saved.

Outputs in .artifacts/UnrealExport/Lvl_VehicleBasic/: ground_color (PNG, sRGB, 2048 px over the 126 m Landscape; image right = UE +Y, image up = UE +X),
ue_reference_topdown (lit, whole level), ue_reference_hill (a lit view over the hill with the tunnel).
"""

import os

import unreal

REPO_ROOT = os.environ.get("CSGE_REPO_ROOT", "G:/GitHub/CSharpGameEngine").replace("\\", "/")
OUTPUT_DIR = f"{REPO_ROOT}/.artifacts/UnrealExport/Lvl_VehicleBasic"
WARMUP_TICKS = 300
LANDSCAPE_SIDE_CM = 12600.0
GROUND_PIXELS = 2048
state = {"ticks": 0, "handle": None, "captures": []}


def add_capture(actors, name, location, rotation, width, height, source, fov=90.0, ortho_width=None, show_only=None, srgb=True):
    capture = actors.spawn_actor_from_class(unreal.SceneCapture2D, location, rotation)
    component = capture.get_editor_property("capture_component2d")
    target_format = unreal.TextureRenderTargetFormat.RTF_RGBA8_SRGB if srgb else unreal.TextureRenderTargetFormat.RTF_RGBA8
    target = unreal.RenderingLibrary.create_render_target2d(capture, width, height, target_format)
    component.set_editor_property("texture_target", target)
    component.set_editor_property("capture_source", source)
    component.set_editor_property("capture_every_frame", True)
    if ortho_width is not None:
        component.set_editor_property("projection_type", unreal.CameraProjectionMode.ORTHOGRAPHIC)
        component.set_editor_property("ortho_width", ortho_width)
    else:
        component.set_editor_property("fov_angle", fov)
    if show_only is not None:
        component.set_editor_property("primitive_render_mode", unreal.SceneCapturePrimitiveRenderMode.PRM_USE_SHOW_ONLY_LIST)
        # ShowOnlyActors is instance-only and cannot be set through set_editor_property; the function adds the actor's components.
        for shown in show_only:
            component.show_only_actor_components(shown, True)
    state["captures"].append((name, component, target))
    return capture


def begin() -> None:
    unreal.get_editor_subsystem(unreal.LevelEditorSubsystem).load_level("/Game/VehicleTemplate/Maps/Lvl_VehicleBasic")
    actors = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    landscapes = [actor for actor in actors.get_all_level_actors() if isinstance(actor, unreal.LandscapeProxy)]
    unreal.log(f"[UnrealExport] landscape proxies {len(landscapes)}")
    center = unreal.Vector(0.0, 0.0, 20000.0)
    add_capture(actors, "ground_color", center, unreal.Rotator(roll=0.0, pitch=-90.0, yaw=0.0), GROUND_PIXELS, GROUND_PIXELS,
                unreal.SceneCaptureSource.SCS_BASE_COLOR, ortho_width=LANDSCAPE_SIDE_CM, show_only=landscapes)
    add_capture(actors, "ue_reference_topdown", center, unreal.Rotator(roll=0.0, pitch=-90.0, yaw=0.0), 1600, 1600,
                unreal.SceneCaptureSource.SCS_FINAL_TONE_CURVE_HDR, ortho_width=LANDSCAPE_SIDE_CM)
    # Over the hill looking toward the tunnel and the ring track, from the south-west corner of the hill.
    add_capture(actors, "ue_reference_hill", unreal.Vector(-1800.0, -3200.0, 900.0), unreal.Rotator(roll=0.0, pitch=-18.0, yaw=50.0), 1280, 720,
                unreal.SceneCaptureSource.SCS_FINAL_TONE_CURVE_HDR, fov=90.0)
    state["handle"] = unreal.register_slate_post_tick_callback(tick)


def tick(delta_seconds: float) -> None:
    state["ticks"] += 1
    if state["ticks"] < WARMUP_TICKS:
        return
    unreal.unregister_slate_post_tick_callback(state["handle"])
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    for name, component, target in state["captures"]:
        component.capture_scene()
        unreal.RenderingLibrary.export_render_target(component, target, OUTPUT_DIR, name)
        unreal.log(f"[UnrealExport] Wrote capture {OUTPUT_DIR}/{name}")
    unreal.SystemLibrary.quit_editor()


begin()
