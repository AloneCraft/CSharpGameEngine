"""Captures reference images of Lvl_VehicleBasic with the sports car, from the Vehicle template's chase camera pose.

Runs in the GUI editor (UnrealEditor.exe, not -Cmd) so Lumen and the sky have rendered frames to converge. The car blueprint is spawned
at the PlayerStart (the editor world does not run the game mode), and SceneCapture2D actors are placed at the engine template's start camera
pose (car at R 44.5 U 1.02 F 0, yaw half a turn, pitch -8 degrees, the camera 6.5 m behind the pivot 1.5 m above the car, FOV 90) and at a front
three-quarter view for the lights and glass. Images go to .artifacts and are only a visual comparison reference; they are never shipped.
Nothing in the UE project is saved.
"""

import os

import unreal

REPO_ROOT = os.environ.get("CSGE_REPO_ROOT", "G:/GitHub/CSharpGameEngine").replace("\\", "/")
OUTPUT_DIR = f"{REPO_ROOT}/.artifacts/UnrealExport/Lvl_VehicleBasic"
WARMUP_TICKS = 240
CAR_BLUEPRINT = "/Game/VehicleTemplate/Blueprints/SportsCar/BP_VehicleAdvSportsCar"
state = {"ticks": 0, "handle": None, "captures": []}


def engine_to_ue(r: float, u: float, f: float) -> unreal.Vector:
    """Engine RUF metres to UE centimetres (ue_to_engine_translation inverted: R = x, U = z, F = -y)."""
    return unreal.Vector(r * 100.0, -f * 100.0, u * 100.0)


def add_capture(actors, name: str, location: unreal.Vector, rotation: unreal.Rotator) -> None:
    capture = actors.spawn_actor_from_class(unreal.SceneCapture2D, location, rotation)
    component = capture.get_editor_property("capture_component2d")
    target = unreal.RenderingLibrary.create_render_target2d(capture, 1280, 720, unreal.TextureRenderTargetFormat.RTF_RGBA8_SRGB)
    component.set_editor_property("texture_target", target)
    component.set_editor_property("fov_angle", 90.0)
    component.set_editor_property("capture_source", unreal.SceneCaptureSource.SCS_FINAL_TONE_CURVE_HDR)
    component.set_editor_property("capture_every_frame", True)
    state["captures"].append((name, component, target))


def begin() -> None:
    unreal.get_editor_subsystem(unreal.LevelEditorSubsystem).load_level("/Game/VehicleTemplate/Maps/Lvl_VehicleBasic")
    actors = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    start = [actor for actor in actors.get_all_level_actors() if isinstance(actor, unreal.PlayerStart)][0]
    start_location = start.get_actor_location()
    unreal.log(f"[UnrealExport] PlayerStart {start_location} {start.get_actor_rotation()}")
    car_class = unreal.EditorAssetLibrary.load_blueprint_class(CAR_BLUEPRINT)
    car = actors.spawn_actor_from_class(car_class, start_location, start.get_actor_rotation())
    unreal.log(f"[UnrealExport] Car {car.get_actor_location()} {car.get_actor_rotation()}")
    # The engine's chase camera at the start: 6.5 m behind the car on the camera's own back axis, looking along the car (-F = +y in UE), pitched down 8 degrees.
    add_capture(actors, "ue_reference_chase", engine_to_ue(44.5, 2.6846242, 5.6282177), unreal.Rotator(roll=0.0, pitch=-8.0, yaw=90.0))
    # A front three-quarter view for the headlights, glass and paint.
    add_capture(actors, "ue_reference_front_three_quarter", unreal.Vector(start_location.x + 450.0, start_location.y + 450.0, start_location.z + 150.0), unreal.Rotator(roll=0.0, pitch=-10.0, yaw=-135.0))
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
        unreal.log(f"[UnrealExport] Wrote reference capture {OUTPUT_DIR}/{name}")
    unreal.SystemLibrary.quit_editor()


begin()
