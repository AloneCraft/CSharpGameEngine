"""Captures the ThirdPerson spawn view through the editor viewport's own high-resolution screenshot, with lighting parts switched off (M0112).

A SceneCapture2D does not run Lumen's global illumination (turning the GI method off changes its image by 0.1 of 255; the viewport's drops by 10 % in the
darks), so the viewport screenshot is the reference that contains it. Runs in the GUI editor through run_capture_thirdperson_viewport.cmd (the staged
TP_ThirdPerson project). UE writes the images to <project>/Saved/Screenshots/WindowsEditor/thirdperson_viewport_<variant>.png. Variants, 500 ticks apart (Lumen's indirect light keeps brightening for about a thousand ticks): three default shots to see it settle; then the
ambient occlusion show flag off; Lumen's short range AO off too; no global illumination.
Nothing is saved.
"""

import unreal

# (name, console commands applied before the shot; they stay applied for the next variants unless undone by the next entry)
STEPS = [
    ("converge_1", []),
    ("converge_2", []),
    ("converge_3", []),
    ("no_ao", ["ShowFlag.AmbientOcclusion 0"]),
    ("no_ao_no_shortrange", ["r.Lumen.ScreenProbeGather.ShortRangeAO 0"]),
    ("no_gi", ["ShowFlag.AmbientOcclusion 1", "r.Lumen.ScreenProbeGather.ShortRangeAO 1", "r.DynamicGlobalIlluminationMethod 0"]),
]
STEP_TICKS = 500
FIRST_SHOT_TICK = 1200
state = {"ticks": 0, "handle": None, "world": None}


def begin() -> None:
    unreal.get_editor_subsystem(unreal.LevelEditorSubsystem).load_level("/Game/ThirdPerson/Lvl_ThirdPerson")
    state["world"] = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()
    actors = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    start = [actor for actor in actors.get_all_level_actors() if isinstance(actor, unreal.PlayerStart)][0]
    location = start.get_actor_location()
    # The spawn camera of capture_thirdperson_reference.py: 400 cm behind the PlayerStart, 8.49 cm up, looking along +X.
    unreal.EditorLevelLibrary.set_level_viewport_camera_info(unreal.Vector(location.x - 400.0, location.y, location.z + 8.492264), unreal.Rotator(0.0, 0.0, 0.0))
    state["handle"] = unreal.register_slate_post_tick_callback(tick)


def tick(delta_seconds: float) -> None:
    state["ticks"] += 1
    ticks = state["ticks"]
    if ticks < FIRST_SHOT_TICK - STEP_TICKS:
        return
    relative = ticks - (FIRST_SHOT_TICK - STEP_TICKS)
    index, phase = divmod(relative, STEP_TICKS)
    if index > len(STEPS):
        unreal.unregister_slate_post_tick_callback(state["handle"])
        unreal.SystemLibrary.quit_editor()
        return
    if phase == 0 and index < len(STEPS):
        for command in STEPS[index][1]:
            unreal.SystemLibrary.execute_console_command(state["world"], command)
    if phase == STEP_TICKS - 1 and index < len(STEPS):
        unreal.AutomationLibrary.take_high_res_screenshot(1280, 720, f"thirdperson_viewport_{STEPS[index][0]}.png")


begin()
