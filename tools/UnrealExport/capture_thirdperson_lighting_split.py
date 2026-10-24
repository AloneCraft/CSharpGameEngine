"""Splits UE's ThirdPerson spawn-camera image into its lighting components with show flags (M0112).

Runs in the GUI editor through run_capture_thirdperson_lighting_split.cmd (the staged TP_ThirdPerson project). The same SceneCapture2D pose as
capture_thirdperson_reference.py is captured with the default flags, with SkyLighting off, with GlobalIllumination off, and with both off; the
differences are what the sky light and the global illumination add to a surface in shadow. Images go to .artifacts/UnrealExport/Lvl_ThirdPerson/
lighting_split_*. Nothing in the UE project is saved.
"""

import os

import unreal

REPO_ROOT = os.environ.get("CSGE_REPO_ROOT", "G:/GitHub/CSharpGameEngine").replace("\\", "/")
OUTPUT_DIR = f"{REPO_ROOT}/.artifacts/UnrealExport/Lvl_ThirdPerson"
WARMUP_TICKS = 300
VARIANTS = {
    "lighting_split_default": [],
    "lighting_split_no_skylighting": ["SkyLighting"],
    "lighting_split_no_gi": ["GlobalIllumination"],
    "lighting_split_no_sky_no_gi": ["SkyLighting", "GlobalIllumination"],
    "lighting_split_no_ao": ["AmbientOcclusion"],
    # The GlobalIllumination flag changed nothing; Lumen has its own flags.
    "lighting_split_no_lumen_gi": ["LumenGlobalIllumination"],
    "lighting_split_no_lumen_gi_no_sky": ["LumenGlobalIllumination", "SkyLighting"],
    "lighting_split_no_lumen_gi_no_ao": ["LumenGlobalIllumination", "AmbientOcclusion"],
}
state = {"ticks": 0, "handle": None, "captures": []}


def begin() -> None:
    unreal.get_editor_subsystem(unreal.LevelEditorSubsystem).load_level("/Game/ThirdPerson/Lvl_ThirdPerson")
    actors = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    start = [actor for actor in actors.get_all_level_actors() if isinstance(actor, unreal.PlayerStart)][0]
    location = start.get_actor_location()
    camera_location = unreal.Vector(location.x - 400.0, location.y, location.z + 8.492264)
    for name, disabled in VARIANTS.items():
        capture = actors.spawn_actor_from_class(unreal.SceneCapture2D, camera_location, unreal.Rotator(0.0, 0.0, 0.0))
        component = capture.get_editor_property("capture_component2d")
        target = unreal.RenderingLibrary.create_render_target2d(capture, 1280, 720, unreal.TextureRenderTargetFormat.RTF_RGBA8_SRGB)
        component.set_editor_property("texture_target", target)
        component.set_editor_property("fov_angle", 90.0)
        component.set_editor_property("capture_source", unreal.SceneCaptureSource.SCS_FINAL_TONE_CURVE_HDR)
        component.set_editor_property("capture_every_frame", True)
        component.set_editor_property("always_persist_rendering_state", True)
        settings = []
        for flag in disabled:
            setting = unreal.EngineShowFlagsSetting()
            setting.set_editor_property("show_flag_name", flag)
            setting.set_editor_property("enabled", False)
            settings.append(setting)
        component.set_editor_property("show_flag_settings", settings)
        state["captures"].append((name, component, target))
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
