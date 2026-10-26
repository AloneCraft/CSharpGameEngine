"""UE 5.8 game template level -> C# Game Engine template import staging data (FirstPerson, TopDown).

Generalizes export_lvl_thirdperson.py for levels that have no PostProcessVolume. Run through run_export_level.cmd, which opens
G:\\UE\\5.8\\<Template> (made by create_template_project.ps1). The source level is never saved. Outputs:
- Templates/CSharpGameEngine.Templates.<Template>/Assets/Stage/<Level>/Meshes/*.glb
- .artifacts/UnrealExport/<Level>/manifest.json (version 4: level, environment, lighting; no character)
The character of a template is authored by hand from the Blueprint dump (dump_blueprint.py).
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
    prototype_grid_materials,
)
from export_lvl_thirdperson import (  # noqa: E402 - the shared light and color helpers live there
    linear_color,
    post_process_value,
    single_component,
    srgb_color_to_linear,
)

REPO_ROOT = os.environ.get("CSGE_REPO_ROOT", "G:/GitHub/CSharpGameEngine").replace("\\", "/")
TEMPLATES = {
    "FirstPerson": {"level": "/Game/FirstPerson/Lvl_FirstPerson", "levelName": "Lvl_FirstPerson"},
    "TopDown": {"level": "/Game/TopDown/Lvl_TopDown", "levelName": "Lvl_TopDown"},
    # Vehicle level の地面は glTF では export できない Landscape であり、エンジンにも terrain がありません。この template の Landscape は
    # 高さ0で周囲の壁と同じ幅の平面なので、同じ大きさの厚さ1 mの板で代用します。
    "Vehicle": {"level": "/Game/VehicleTemplate/Maps/Lvl_VehicleBasic", "levelName": "Lvl_VehicleBasic", "landscapeGroundHalfExtentMeters": 63.5},
}
TEMPLATE = os.environ["CSGE_LEVEL_TEMPLATE"]
CONFIG = TEMPLATES[TEMPLATE]
LEVEL_ASSET_PATH = CONFIG["level"]
LEVEL_NAME = CONFIG["levelName"]
TEMPLATE_DIR = f"{REPO_ROOT}/Templates/CSharpGameEngine.Templates.{TEMPLATE}"
STAGE_ASSET_PREFIX = f"Assets/Stage/{LEVEL_NAME}/Meshes"
MESH_OUTPUT_DIR = f"{TEMPLATE_DIR}/{STAGE_ASSET_PREFIX}"
STAGING_DIR = f"{REPO_ROOT}/.artifacts/UnrealExport/{LEVEL_NAME}"
STAGING_GLB_PATH = f"{STAGING_DIR}/{LEVEL_NAME}.staging.glb"
MANIFEST_PATH = f"{STAGING_DIR}/manifest.json"


def require_force_for_existing_output() -> None:
    if not os.path.exists(MESH_OUTPUT_DIR):
        return
    if os.environ.get("CSGE_UNREAL_EXPORT_FORCE") != "1":
        raise RuntimeError(
            "Output already exists. Re-run tools/UnrealExport/run_export_level.cmd with --force to replace generated import data."
        )


def build_lighting(actors: list) -> dict:
    """Unreal's environment lighting actors in their authored units (ADR-0069). A level without a PostProcessVolume uses Unreal's defaults."""
    by_class = {}
    for actor in actors:
        by_class.setdefault(actor.get_class().get_name(), []).append(actor)
    sun = single_component(by_class["DirectionalLight"][0], unreal.DirectionalLightComponent)
    sky_light = single_component(by_class["SkyLight"][0], unreal.SkyLightComponent)
    atmosphere = single_component(by_class["SkyAtmosphere"][0], unreal.SkyAtmosphereComponent)
    fog_actor = by_class["ExponentialHeightFog"][0]
    fog = single_component(fog_actor, unreal.ExponentialHeightFogComponent)
    rayleigh = atmosphere.get_editor_property("rayleigh_scattering")
    mie_scattering = atmosphere.get_editor_property("mie_scattering")
    mie_absorption = atmosphere.get_editor_property("mie_absorption")
    other = atmosphere.get_editor_property("other_absorption")
    tent = atmosphere.get_editor_property("other_tent_distribution")
    volumes = by_class.get("PostProcessVolume", [])
    if volumes:
        volume = volumes[0]
        if not bool(volume.get_editor_property("unbound")):
            raise RuntimeError("Only an unbound PostProcessVolume is supported")
        settings = volume.get_editor_property("settings")
        method = str(post_process_value(settings, "auto_exposure_method", unreal.AutoExposureMethod.AEM_HISTOGRAM)).split(".")[-1].split(":")[0]

        def value(name, default):
            return float(post_process_value(settings, name, default))
    else:
        method = "AEM_HISTOGRAM"

        def value(name, default):
            return float(default)
    return {
        "sun": {
            "intensityLux": float(sun.get_editor_property("intensity")),
            "sourceAngleDegrees": float(sun.get_editor_property("light_source_angle")),
        },
        "skyLight": {
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
        },
        "postProcess": {
            "autoExposureMethod": method,
            "minEv100": value("auto_exposure_min_brightness", -10.0),
            "maxEv100": value("auto_exposure_max_brightness", 20.0),
            "exposureCompensation": value("auto_exposure_bias", 1.0),
            "autoExposureSpeedUp": value("auto_exposure_speed_up", 3.0),
            "autoExposureSpeedDown": value("auto_exposure_speed_down", 1.0),
            "bloomIntensity": value("bloom_intensity", 0.675),
            "ambientOcclusionIntensity": value("ambient_occlusion_intensity", 0.5),
            "ambientOcclusionRadiusCm": value("ambient_occlusion_radius", 200.0),
            "fromVolume": bool(volumes),
        },
        "volumetricCloud": "VolumetricCloud" in by_class,
        "skyDomeMaterial": "/Engine/EngineSky/M_SimpleSkyDome",
    }


def add_landscape_ground(half_extent_meters: float) -> None:
    """Spawns the slab that stands in for the Landscape (in the loaded editor world only; nothing is saved).

    It copies the first cube wall's mesh and material so that it renders like the rest of the arena, with the top face at height 0.
    """
    walls = [actor for actor in all_level_actors() if isinstance(actor, unreal.StaticMeshActor) and actor.get_actor_label() == "Cube"]
    if not walls:
        raise RuntimeError("The Vehicle level has no 'Cube' wall to take the ground's mesh and material from")
    wall_component = walls[0].get_component_by_class(unreal.StaticMeshComponent)
    ground = unreal.EditorLevelLibrary.spawn_actor_from_class(unreal.StaticMeshActor, unreal.Vector(0.0, 0.0, -50.0))
    ground.set_actor_label("Ground")
    ground_component = ground.get_component_by_class(unreal.StaticMeshComponent)
    ground_component.set_static_mesh(wall_component.static_mesh)
    ground_component.set_material(0, wall_component.get_material(0))
    side = half_extent_meters * 2.0
    ground.set_actor_scale3d(unreal.Vector(side, side, 1.0))


def main() -> None:
    require_force_for_existing_output()
    os.makedirs(MESH_OUTPUT_DIR, exist_ok=True)
    os.makedirs(STAGING_DIR, exist_ok=True)
    world = load_level(LEVEL_ASSET_PATH)
    if "landscapeGroundHalfExtentMeters" in CONFIG:
        add_landscape_ground(CONFIG["landscapeGroundHalfExtentMeters"])
    actors = all_level_actors()
    mesh_actors = [actor for actor in actors if isinstance(actor, unreal.StaticMeshActor)]
    labels = [actor.get_actor_label() for actor in mesh_actors]
    if len(labels) != len(set(labels)):
        raise RuntimeError("StaticMeshActor labels must be unique")
    export_gltf(world, STAGING_GLB_PATH, set())
    log(f"Exported coordinate oracle: {STAGING_GLB_PATH}")
    actor_entries, render_variants = export_level_static_meshes(world, mesh_actors, MESH_OUTPUT_DIR, STAGE_ASSET_PREFIX)
    manifest = {
        "version": 4,
        "template": TEMPLATE,
        "levelAssetPath": LEVEL_ASSET_PATH,
        "engineVersion": unreal.SystemLibrary.get_engine_version(),
        "stagingGlb": STAGING_GLB_PATH,
        "actors": actor_entries,
        "renderVariants": render_variants,
        "materialGrids": prototype_grid_materials(mesh_actors),
        "environment": build_environment(actors),
        "lighting": build_lighting(actors),
    }
    with open(MANIFEST_PATH, "w", encoding="utf-8", newline="\n") as output:
        json.dump(manifest, output, ensure_ascii=False, indent=2)
        output.write("\n")
    log(f"Wrote manifest: {MANIFEST_PATH}")
    log(f"Exported {len(mesh_actors)} actors using {len(render_variants)} render variants")


if __name__ == "__main__":
    try:
        main()
    except Exception as ex:  # noqa: BLE001 - top level must make commandlet failure visible
        log_error(f"FAILED: {ex}")
        import traceback

        log_error(traceback.format_exc())
        sys.exit(1)
