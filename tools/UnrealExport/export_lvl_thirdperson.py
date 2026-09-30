"""UE 5.8 ThirdPerson template -> C# Game Engine ThirdPerson template import staging data.

Runs inside the staging project built by prepare_thirdperson_project.ps1 (a copy of
Templates/TP_ThirdPersonBP plus its shared content packs). Nothing is saved back to Unreal.
Outputs:
- templates/CSharpGameEngine.Templates.ThirdPerson/Assets/Stage/Lvl_ThirdPerson/Meshes/*.glb
- templates/CSharpGameEngine.Templates.ThirdPerson/Assets/Characters/Mannequins/*.glb
- .artifacts/UnrealExport/Lvl_ThirdPerson/manifest.json (level, character, input, blend space)
"""

import json
import os
import sys

import unreal

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from unreal_export_common import (  # noqa: E402 - UE runs this file as a script, not a package
    all_level_actors,
    build_environment,
    export_gltf,
    export_level_static_meshes,
    load_level,
    log,
    log_error,
)

LEVEL_ASSET_PATH = "/Game/ThirdPerson/Lvl_ThirdPerson"
CHARACTER_CLASS = "/Game/ThirdPerson/Blueprints/BP_ThirdPersonCharacter.BP_ThirdPersonCharacter_C"
GAME_MODE_CLASS = "/Game/ThirdPerson/Blueprints/BP_ThirdPersonGameMode.BP_ThirdPersonGameMode_C"
BLEND_SPACE = "/Game/Characters/Mannequins/Anims/Unarmed/BS_Idle_Walk_Run"
INPUT_CONTEXTS = ["/Game/Input/IMC_Default", "/Game/Input/IMC_MouseLook"]
ANIMATIONS = {
    "Idle": "/Game/Characters/Mannequins/Anims/Unarmed/MM_Idle",
    "WalkForward": "/Game/Characters/Mannequins/Anims/Unarmed/Walk/MF_Unarmed_Walk_Fwd",
    "JogForward": "/Game/Characters/Mannequins/Anims/Unarmed/Jog/MF_Unarmed_Jog_Fwd",
    "Jump": "/Game/Characters/Mannequins/Anims/Unarmed/Jump/MM_Jump",
    "FallLoop": "/Game/Characters/Mannequins/Anims/Unarmed/Jump/MM_Fall_Loop",
    "Land": "/Game/Characters/Mannequins/Anims/Unarmed/Jump/MM_Land",
}
REPO_ROOT = os.environ.get("CSGE_REPO_ROOT", "G:/GitHub/CSharpGameEngine").replace("\\", "/")
TEMPLATE_DIR = f"{REPO_ROOT}/templates/CSharpGameEngine.Templates.ThirdPerson"
STAGE_ASSET_PREFIX = "Assets/Stage/Lvl_ThirdPerson/Meshes"
CHARACTER_ASSET_PREFIX = "Assets/Characters/Mannequins"
MESH_OUTPUT_DIR = f"{TEMPLATE_DIR}/{STAGE_ASSET_PREFIX}"
CHARACTER_OUTPUT_DIR = f"{TEMPLATE_DIR}/{CHARACTER_ASSET_PREFIX}"
STAGING_DIR = f"{REPO_ROOT}/.artifacts/UnrealExport/Lvl_ThirdPerson"
STAGING_GLB_PATH = f"{STAGING_DIR}/Lvl_ThirdPerson.staging.glb"
MANIFEST_PATH = f"{STAGING_DIR}/manifest.json"


def require_force_for_existing_output() -> None:
    if not os.path.exists(MESH_OUTPUT_DIR) and not os.path.exists(CHARACTER_OUTPUT_DIR):
        return
    if os.environ.get("CSGE_UNREAL_EXPORT_FORCE") != "1":
        raise RuntimeError(
            "Output already exists. Re-run tools/UnrealExport/run_export_thirdperson.cmd --force "
            "to replace generated import data."
        )


def vector(value) -> list:
    return [float(value.x), float(value.y), float(value.z)]


def rotator(value) -> dict:
    return {"pitch": float(value.pitch), "yaw": float(value.yaw), "roll": float(value.roll)}


def blueprint_components(blueprint_path: str) -> list:
    blueprint = unreal.load_asset(blueprint_path)
    subsystem = unreal.get_engine_subsystem(unreal.SubobjectDataSubsystem)
    result = []
    for handle in subsystem.k2_gather_subobject_data_for_blueprint(blueprint):
        data = unreal.SubobjectDataBlueprintFunctionLibrary.get_data(handle)
        component = unreal.SubobjectDataBlueprintFunctionLibrary.get_associated_object(data)
        if isinstance(component, unreal.SceneComponent):
            result.append(component)
    return result


def single(components: list, component_class):
    matches = {component.get_name(): component for component in components if isinstance(component, component_class)}
    if len(matches) != 1:
        raise RuntimeError(f"Expected one {component_class.__name__}, found {sorted(matches)}")
    return next(iter(matches.values()))


def build_character() -> dict:
    generated = unreal.load_object(None, CHARACTER_CLASS)
    default = unreal.get_default_object(generated)
    components = blueprint_components(CHARACTER_CLASS.split(".")[0])
    capsule = default.get_editor_property("capsule_component")
    movement = default.get_editor_property("character_movement")
    mesh = default.get_editor_property("mesh")
    arm = single(components, unreal.SpringArmComponent)
    camera = single(components, unreal.CameraComponent)
    skeletal_mesh = mesh.get_editor_property("skeletal_mesh_asset")
    return {
        "blueprint": CHARACTER_CLASS,
        "useControllerRotationYaw": bool(default.get_editor_property("use_controller_rotation_yaw")),
        "capsuleRadiusCm": float(capsule.get_unscaled_capsule_radius()),
        "capsuleHalfHeightCm": float(capsule.get_unscaled_capsule_half_height()),
        "movement": {
            "orientRotationToMovement": bool(movement.get_editor_property("orient_rotation_to_movement")),
            "rotationRateYawDegrees": float(movement.get_editor_property("rotation_rate").yaw),
            "jumpZVelocityCm": float(movement.get_editor_property("jump_z_velocity")),
            "airControl": float(movement.get_editor_property("air_control")),
            "airControlBoostMultiplier": float(movement.get_editor_property("air_control_boost_multiplier")),
            "airControlBoostVelocityThresholdCm": float(movement.get_editor_property("air_control_boost_velocity_threshold")),
            "maxWalkSpeedCm": float(movement.get_editor_property("max_walk_speed")),
            "minAnalogWalkSpeedCm": float(movement.get_editor_property("min_analog_walk_speed")),
            "maxAccelerationCm": float(movement.get_editor_property("max_acceleration")),
            "brakingDecelerationWalkingCm": float(movement.get_editor_property("braking_deceleration_walking")),
            "brakingDecelerationFallingCm": float(movement.get_editor_property("braking_deceleration_falling")),
            "groundFriction": float(movement.get_editor_property("ground_friction")),
            "brakingFrictionFactor": float(movement.get_editor_property("braking_friction_factor")),
            "brakingFriction": float(movement.get_editor_property("braking_friction")),
            "useSeparateBrakingFriction": bool(movement.get_editor_property("use_separate_braking_friction")),
            "fallingLateralFriction": float(movement.get_editor_property("falling_lateral_friction")),
            "gravityScale": float(movement.get_editor_property("gravity_scale")),
            "maxStepHeightCm": float(movement.get_editor_property("max_step_height")),
            "walkableFloorAngleDegrees": float(movement.get_editor_property("walkable_floor_angle")),
        },
        "mesh": {
            "source": skeletal_mesh.get_path_name(),
            "assetPath": f"{CHARACTER_ASSET_PREFIX}/{skeletal_mesh.get_name()}.glb",
            "relativeLocationCm": vector(mesh.get_editor_property("relative_location")),
            "relativeRotation": rotator(mesh.get_editor_property("relative_rotation")),
            "animClass": mesh.get_editor_property("anim_class").get_path_name(),
        },
        "springArm": {
            "relativeLocationCm": vector(arm.get_editor_property("relative_location")),
            "targetArmLengthCm": float(arm.get_editor_property("target_arm_length")),
            "usePawnControlRotation": bool(arm.get_editor_property("use_pawn_control_rotation")),
            "doCollisionTest": bool(arm.get_editor_property("do_collision_test")),
            "probeSizeCm": float(arm.get_editor_property("probe_size")),
            "socketOffsetCm": vector(arm.get_editor_property("socket_offset")),
            "targetOffsetCm": vector(arm.get_editor_property("target_offset")),
            "enableCameraLag": bool(arm.get_editor_property("enable_camera_lag")),
        },
        "camera": {
            "fieldOfViewDegrees": float(camera.get_editor_property("field_of_view")),
            "usePawnControlRotation": bool(camera.get_editor_property("use_pawn_control_rotation")),
        },
    }


def modifier_entry(modifier) -> dict:
    name = modifier.get_class().get_name()
    if name == "InputModifierSwizzleAxis":
        return {"kind": "SwizzleAxis", "order": str(modifier.get_editor_property("order")).split(".")[-1].split(":")[0]}
    if name == "InputModifierNegate":
        return {"kind": "Negate", "x": bool(modifier.get_editor_property("x")), "y": bool(modifier.get_editor_property("y")), "z": bool(modifier.get_editor_property("z"))}
    if name == "InputModifierDeadZone":
        return {
            "kind": "DeadZone",
            "lower": float(modifier.get_editor_property("lower_threshold")),
            "upper": float(modifier.get_editor_property("upper_threshold")),
            "type": str(modifier.get_editor_property("type")).split(".")[-1].split(":")[0],
        }
    if name == "InputModifierScalar":
        return {"kind": "Scalar", "scalar": vector(modifier.get_editor_property("scalar"))}
    raise RuntimeError(f"Unsupported input modifier {name}")


def linear_color(value) -> list:
    return [float(value.r), float(value.g), float(value.b)]


def srgb_color_to_linear(value) -> list:
    def channel(byte_value: int) -> float:
        c = byte_value / 255.0
        return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
    return [channel(value.r), channel(value.g), channel(value.b)]


def single_component(actor, component_class):
    components = actor.get_components_by_class(component_class)
    if len(components) != 1:
        raise RuntimeError(f"'{actor.get_actor_label()}' has {len(components)} {component_class.__name__}")
    return components[0]


def post_process_value(settings, name: str, default):
    """Returns the volume's value only when its override flag is set, otherwise Unreal's default."""
    if bool(settings.get_editor_property(f"override_{name}")):
        return settings.get_editor_property(name)
    return default


def build_lighting(actors: list) -> dict:
    """Unreal's environment lighting actors in their authored units (ADR-0069)."""
    by_class = {}
    for actor in actors:
        by_class.setdefault(actor.get_class().get_name(), []).append(actor)
    sun = single_component(by_class["DirectionalLight"][0], unreal.DirectionalLightComponent)
    sky_light = single_component(by_class["SkyLight"][0], unreal.SkyLightComponent)
    atmosphere = single_component(by_class["SkyAtmosphere"][0], unreal.SkyAtmosphereComponent)
    fog_actor = by_class["ExponentialHeightFog"][0]
    fog = single_component(fog_actor, unreal.ExponentialHeightFogComponent)
    volume = by_class["PostProcessVolume"][0]
    settings = volume.get_editor_property("settings")
    if not bool(volume.get_editor_property("unbound")):
        raise RuntimeError("Only an unbound PostProcessVolume is supported")
    rayleigh = atmosphere.get_editor_property("rayleigh_scattering")
    mie_scattering = atmosphere.get_editor_property("mie_scattering")
    mie_absorption = atmosphere.get_editor_property("mie_absorption")
    other = atmosphere.get_editor_property("other_absorption")
    tent = atmosphere.get_editor_property("other_tent_distribution")
    exposure_method = str(post_process_value(settings, "auto_exposure_method", unreal.AutoExposureMethod.AEM_HISTOGRAM)).split(".")[-1].split(":")[0]
    return {
        "sun": {
            "intensityLux": float(sun.get_editor_property("intensity")),
            "useTemperature": bool(sun.get_editor_property("use_temperature")),
            "temperatureKelvin": float(sun.get_editor_property("temperature")),
            "sourceAngleDegrees": float(sun.get_editor_property("light_source_angle")),
            "atmosphereSunLight": bool(sun.get_editor_property("atmosphere_sun_light")),
            "castShadows": bool(sun.get_editor_property("cast_shadows")),
            "dynamicShadowCascades": int(sun.get_editor_property("dynamic_shadow_cascades")),
            "dynamicShadowDistanceCm": float(sun.get_editor_property("dynamic_shadow_distance_movable_light")),
        },
        "skyLight": {
            "sourceType": str(sky_light.get_editor_property("source_type")).split(".")[-1].split(":")[0],
            "realTimeCapture": bool(sky_light.get_editor_property("real_time_capture")),
            "intensity": float(sky_light.get_editor_property("intensity")),
            "lowerHemisphereIsBlack": bool(sky_light.get_editor_property("lower_hemisphere_is_black")),
            "lowerHemisphereColor": linear_color(sky_light.get_editor_property("lower_hemisphere_color")),
        },
        "skyAtmosphere": {
            "bottomRadiusKm": float(atmosphere.get_editor_property("bottom_radius")),
            "atmosphereHeightKm": float(atmosphere.get_editor_property("atmosphere_height")),
            "groundAlbedoLinear": srgb_color_to_linear(atmosphere.get_editor_property("ground_albedo")),
            "rayleighScattering": linear_color(rayleigh),
            "rayleighScatteringScale": float(atmosphere.get_editor_property("rayleigh_scattering_scale")),
            "rayleighExponentialDistributionKm": float(atmosphere.get_editor_property("rayleigh_exponential_distribution")),
            "mieScattering": linear_color(mie_scattering),
            "mieScatteringScale": float(atmosphere.get_editor_property("mie_scattering_scale")),
            "mieAbsorption": linear_color(mie_absorption),
            "mieAbsorptionScale": float(atmosphere.get_editor_property("mie_absorption_scale")),
            "mieAnisotropy": float(atmosphere.get_editor_property("mie_anisotropy")),
            "mieExponentialDistributionKm": float(atmosphere.get_editor_property("mie_exponential_distribution")),
            "otherAbsorption": linear_color(other),
            "otherAbsorptionScale": float(atmosphere.get_editor_property("other_absorption_scale")),
            "otherTentTipAltitudeKm": float(tent.get_editor_property("tip_altitude")),
            "otherTentWidthKm": float(tent.get_editor_property("width")) * 2.0,
            "multiScatteringFactor": float(atmosphere.get_editor_property("multi_scattering_factor")),
        },
        "heightFog": {
            "fogDensity": float(fog.get_editor_property("fog_density")),
            "fogHeightFalloff": float(fog.get_editor_property("fog_height_falloff")),
            "fogHeightMeters": float(fog_actor.get_actor_location().z) * 0.01,
            "fogMaxOpacity": float(fog.get_editor_property("fog_max_opacity")),
            "inscatteringLuminance": linear_color(fog.get_editor_property("fog_inscattering_luminance")),
            "skyAtmosphereAmbientContributionScale": float(fog.get_editor_property("sky_atmosphere_ambient_contribution_color_scale").r),
            "startDistanceCm": float(fog.get_editor_property("start_distance")),
            "volumetricFog": bool(fog.get_editor_property("enable_volumetric_fog")),
        },
        "postProcess": {
            "autoExposureMethod": exposure_method,
            "minEv100": float(post_process_value(settings, "auto_exposure_min_brightness", -10.0)),
            "maxEv100": float(post_process_value(settings, "auto_exposure_max_brightness", 20.0)),
            "exposureCompensation": float(post_process_value(settings, "auto_exposure_bias", 1.0)),
            "autoExposureSpeedUp": float(post_process_value(settings, "auto_exposure_speed_up", 3.0)),
            "autoExposureSpeedDown": float(post_process_value(settings, "auto_exposure_speed_down", 1.0)),
            "bloomIntensity": float(post_process_value(settings, "bloom_intensity", 0.675)),
            "ambientOcclusionIntensity": float(post_process_value(settings, "ambient_occlusion_intensity", 0.5)),
            "ambientOcclusionRadiusCm": float(post_process_value(settings, "ambient_occlusion_radius", 200.0)),
        },
        "volumetricCloud": "VolumetricCloud" in by_class,
        "skyDomeMaterial": "/Engine/EngineSky/M_SimpleSkyDome",
    }


def build_input() -> dict:
    contexts = []
    actions = {}
    for path in INPUT_CONTEXTS:
        context = unreal.load_asset(path)
        mappings = []
        for mapping in context.get_editor_property("default_key_mappings").get_editor_property("mappings"):
            action = mapping.action
            action_name = action.get_name()
            actions[action_name] = str(action.get_editor_property("value_type")).split(".")[-1].split(":")[0]
            if mapping.triggers:
                raise RuntimeError(f"Mapping-level triggers are not supported ({action_name})")
            mappings.append({
                "action": action_name,
                "key": str(mapping.key.get_editor_property("key_name")),
                "modifiers": [modifier_entry(modifier) for modifier in (mapping.modifiers or [])],
            })
        contexts.append({"name": context.get_name(), "mappings": mappings})
    return {"contexts": contexts, "actions": actions}


def build_blend_space() -> dict:
    blend_space = unreal.load_asset(BLEND_SPACE)
    parameters = []
    for parameter in blend_space.get_editor_property("blend_parameters"):
        parameters.append({
            "name": str(parameter.get_editor_property("display_name")),
            "min": float(parameter.get_editor_property("min")),
            "max": float(parameter.get_editor_property("max")),
        })
    samples = []
    for sample in blend_space.get_editor_property("sample_data"):
        animation = sample.get_editor_property("animation")
        value = sample.get_editor_property("sample_value")
        samples.append({"animation": animation.get_path_name(), "x": float(value.x), "y": float(value.y), "rateScale": float(sample.get_editor_property("rate_scale"))})
    return {
        "source": BLEND_SPACE,
        "parameters": parameters[:2],
        "samples": samples,
        "targetWeightInterpolationSpeedPerSecond": float(blend_space.get_editor_property("target_weight_interpolation_speed_per_sec")),
    }


def export_character(character: dict) -> list:
    os.makedirs(f"{CHARACTER_OUTPUT_DIR}/Anims", exist_ok=True)
    skeletal_mesh = unreal.load_asset(character["mesh"]["source"].split(".")[0])
    export_gltf(skeletal_mesh, f"{TEMPLATE_DIR}/{character['mesh']['assetPath']}", set(), skinned=True)
    log(f"Exported skeletal mesh: {character['mesh']['assetPath']}")
    clips = []
    for name, path in ANIMATIONS.items():
        animation = unreal.load_asset(path)
        asset_path = f"{CHARACTER_ASSET_PREFIX}/Anims/{animation.get_name()}.glb"
        export_gltf(animation, f"{TEMPLATE_DIR}/{asset_path}", set(), skinned=True)
        clips.append({
            "name": name,
            "source": animation.get_path_name(),
            "assetPath": asset_path,
            "lengthSeconds": float(animation.get_play_length()),
            "frames": int(unreal.AnimationLibrary.get_num_frames(animation)),
            "rateScale": float(animation.get_editor_property("rate_scale")),
        })
        log(f"Exported animation: {asset_path}")
    return clips


def main() -> None:
    require_force_for_existing_output()
    os.makedirs(MESH_OUTPUT_DIR, exist_ok=True)
    os.makedirs(STAGING_DIR, exist_ok=True)
    world = load_level(LEVEL_ASSET_PATH)
    actors = all_level_actors()
    mesh_actors = [actor for actor in actors if isinstance(actor, unreal.StaticMeshActor)]
    labels = [actor.get_actor_label() for actor in mesh_actors]
    if len(labels) != len(set(labels)):
        raise RuntimeError("StaticMeshActor labels must be unique")
    if len(mesh_actors) != 55:
        raise RuntimeError(f"Expected 55 StaticMeshActors, found {len(mesh_actors)}")
    export_gltf(world, STAGING_GLB_PATH, set())
    log(f"Exported coordinate oracle: {STAGING_GLB_PATH}")
    actor_entries, render_variants = export_level_static_meshes(world, mesh_actors, MESH_OUTPUT_DIR, STAGE_ASSET_PREFIX)
    game_mode = unreal.get_default_object(unreal.load_object(None, GAME_MODE_CLASS))
    character = build_character()
    if game_mode.get_editor_property("default_pawn_class").get_path_name() != CHARACTER_CLASS:
        raise RuntimeError("BP_ThirdPersonGameMode no longer spawns BP_ThirdPersonCharacter")
    character["animations"] = export_character(character)
    character["blendSpace"] = build_blend_space()
    manifest = {
        "version": 3,
        "levelAssetPath": LEVEL_ASSET_PATH,
        "engineVersion": unreal.SystemLibrary.get_engine_version(),
        "stagingGlb": STAGING_GLB_PATH,
        "actors": actor_entries,
        "renderVariants": render_variants,
        "environment": build_environment(actors),
        "character": character,
        "input": build_input(),
        "lighting": build_lighting(actors),
    }
    with open(MANIFEST_PATH, "w", encoding="utf-8", newline="\n") as output:
        json.dump(manifest, output, ensure_ascii=False, indent=2)
        output.write("\n")
    log(f"Wrote manifest: {MANIFEST_PATH}")
    log(f"Exported {len(mesh_actors)} actors using {len(render_variants)} render variants, {len(character['animations'])} animations")


if __name__ == "__main__":
    try:
        main()
    except Exception as ex:  # noqa: BLE001 - top level must make commandlet failure visible
        log_error(f"FAILED: {ex}")
        import traceback

        log_error(traceback.format_exc())
        sys.exit(1)
