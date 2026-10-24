@echo off
setlocal
rem Usage: run_probe_sky_and_clouds.cmd <Project.uproject> <LevelAssetPath> <OutputFile>
rem Writes the level's VolumetricCloud (component and cloud material), sky sphere (SM_SkySphere, M_SimpleSkyDome), SkyAtmosphere, SkyLight
rem and fog values to the output file; see probe_sky_and_clouds.py. Nothing is saved.
rem Example: run_probe_sky_and_clouds.cmd G:\UE\5.8\Vehicle\Vehicle.uproject /Game/VehicleTemplate/Maps/Lvl_VehicleBasic out.txt
set "UE_EDITOR_CMD=C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe"
set "SCRIPT_DIR=%~dp0"
set "CGE_PROBE_MAP=%~2"
set "CGE_PROBE_OUT=%~f3"
"%UE_EDITOR_CMD%" "%~f1" ^
  -ExecutePythonScript="%SCRIPT_DIR%probe_sky_and_clouds.py" ^
  -EnablePlugins=PythonScriptPlugin ^
  -stdout -unattended -NoSplash -nosound -nullrhi
