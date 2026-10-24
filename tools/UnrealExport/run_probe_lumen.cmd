@echo off
setlocal
rem Usage: run_probe_lumen.cmd <Project.uproject> <LevelAssetPath> <OutputFile>
rem Writes the level's PostProcessVolume Lumen settings (Global Illumination and Reflection methods, Lumen quality values and their override
rem flags), the project's renderer settings and the r.Lumen.* console variables to the output file; see probe_lumen.py. Nothing is saved.
rem Example: run_probe_lumen.cmd G:\UE\5.8\Vehicle\Vehicle.uproject /Game/VehicleTemplate/Maps/Lvl_VehicleBasic out.txt
set "UE_EDITOR_CMD=C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe"
set "SCRIPT_DIR=%~dp0"
set "CGE_PROBE_MAP=%~2"
set "CGE_PROBE_OUT=%~f3"
"%UE_EDITOR_CMD%" "%~f1" ^
  -ExecutePythonScript="%SCRIPT_DIR%probe_lumen.py" ^
  -EnablePlugins=PythonScriptPlugin ^
  -stdout -unattended -NoSplash -nosound -nullrhi
