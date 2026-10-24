"""Prints a template level's Lumen settings (M0111): every PostProcessVolume's Global Illumination / Reflection method and Lumen values (with their
override flags), the project's renderer settings (DefaultEngine.ini) and the r.Lumen.* / scalability console variables the editor resolved, so the
engine's Lumen-like GI and reflections can be fed UE's values.

Run: run_probe_lumen.cmd <Project.uproject> <LevelAssetPath> <OutputFile>. Nothing is saved in the project.
"""
import os
import sys

import unreal

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from unreal_export_common import all_level_actors, load_level, log_error  # noqa: E402 - UE runs this file as a script

LINES = []
KEYWORDS = ("lumen", "global_illumination", "reflection", "ray_tracing", "ambient_occlusion", "auto_exposure", "exposure")
CONSOLE_VARIABLES = (
    "r.DynamicGlobalIlluminationMethod", "r.ReflectionMethod", "r.GenerateMeshDistanceFields", "r.Lumen.HardwareRayTracing",
    "r.Lumen.TraceMeshSDFs", "r.Lumen.TraceMeshSDFs.TraceDistance", "r.Lumen.MaxTraceDistance",
    "r.Lumen.ScreenProbeGather.DownsampleFactor", "r.Lumen.ScreenProbeGather.TracingOctahedronResolution",
    "r.Lumen.ScreenProbeGather.ScreenTraces", "r.Lumen.ScreenProbeGather.Temporal", "r.Lumen.ScreenProbeGather.SpatialFilterNumPasses",
    "r.Lumen.ScreenProbeGather.RadianceCache", "r.Lumen.RadianceCache.ProbeResolution", "r.Lumen.RadianceCache.NumProbesToTraceBudget",
    "r.Lumen.ScreenProbeGather.RadianceCache.ProbeResolution", "r.Lumen.ScreenProbeGather.RadianceCache.NumClipmaps",
    "r.Lumen.ScreenProbeGather.RadianceCache.ClipmapWorldExtent", "r.Lumen.ScreenProbeGather.RadianceCache.ClipmapDistributionBase",
    "r.Lumen.ScreenProbeGather.RadianceCache.GridResolution", "r.Lumen.Reflections.DownsampleFactor", "r.Lumen.Reflections.MaxRoughnessToTrace",
    "r.Lumen.Reflections.ScreenTraces", "r.Lumen.Reflections.Temporal", "r.Lumen.Reflections.SpatialFilter",
    "r.LumenScene.SurfaceCache.CardTexelDensityScale", "r.LumenScene.SurfaceCache.AtlasSize", "r.LumenScene.SurfaceCache.CardMinResolution",
    "r.LumenScene.SurfaceCache.CardMaxResolution", "r.LumenScene.SurfaceCache.CardCaptureRefreshFraction",
    "r.LumenScene.DirectLighting.UpdateFactor", "r.LumenScene.Radiosity.UpdateFactor", "r.LumenScene.Radiosity",
    "r.LumenScene.GlobalSDF.Resolution", "r.LumenScene.GlobalSDF.ClipmapExtent", "r.DistanceFields.MaxPerMeshResolution",
    "r.DistanceFields.DefaultVoxelDensity", "sg.GlobalIlluminationQuality", "sg.ReflectionQuality", "r.SSR.Quality",
)


def log(message: str) -> None:
    LINES.append(message)


def prop(target, name: str):
    try:
        return target.get_editor_property(name)
    except Exception as ex:  # noqa: BLE001
        return f"<{type(ex).__name__}>"


def text_of(value) -> str:
    if hasattr(value, "get_path_name"):
        return value.get_path_name()
    return str(value)


def dump_settings(prefix: str, settings) -> None:
    for name in sorted(name for name in dir(settings) if not name.startswith("_")):
        lowered = name.lower()
        if not any(word in lowered for word in KEYWORDS):
            continue
        value = prop(settings, name)
        if callable(value) or (isinstance(value, str) and value.startswith("<")):
            continue
        log(f"{prefix}{name}={text_of(value)}")


def dump_console_variables() -> None:
    library = unreal.SystemLibrary
    for name in CONSOLE_VARIABLES:
        try:
            value = library.get_console_variable_float_value(name)
            log(f"CVAR {name}={value}")
        except Exception as ex:  # noqa: BLE001
            log(f"CVAR {name}=<{type(ex).__name__}>")


def dump_project_ini() -> None:
    directory = unreal.Paths.project_config_dir()
    for file_name in ("DefaultEngine.ini", "DefaultScalability.ini"):
        path = os.path.join(directory, file_name)
        if not os.path.exists(path):
            log(f"INI {file_name} missing")
            continue
        section = ""
        with open(path, encoding="utf-8", errors="replace") as handle:
            for line in handle:
                line = line.strip()
                if line.startswith("["):
                    section = line
                    continue
                if any(word in line.lower() for word in ("lumen", "globalillumination", "reflection", "distancefield", "raytracing", "r.ssr", "sg.")):
                    log(f"INI {file_name} {section} {line}")


def main() -> None:
    level = os.environ["CGE_PROBE_MAP"]
    load_level(level)
    log(f"LEVEL {level}")
    volumes = 0
    for actor in all_level_actors():
        class_name = actor.get_class().get_name()
        if "PostProcessVolume" not in class_name:
            continue
        volumes += 1
        log(f"ACTOR {actor.get_actor_label()} class={class_name} unbound={prop(actor, 'unbound')} priority={prop(actor, 'priority')} enabled={prop(actor, 'enabled')}")
        dump_settings("  ", prop(actor, "settings"))
    log(f"POSTPROCESSVOLUMES {volumes}")
    dump_project_ini()
    dump_console_variables()


try:
    main()
except Exception as ex:  # noqa: BLE001 - top level must make commandlet failure visible
    LINES.append(f"FAILED: {ex}")
    log_error(f"FAILED: {ex}")
    sys.exit(1)
finally:
    output = os.environ.get("CGE_PROBE_OUT")
    if output:
        with open(output, "w", encoding="utf-8") as handle:
            handle.write("\n".join(LINES))
