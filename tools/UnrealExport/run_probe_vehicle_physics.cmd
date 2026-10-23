@echo off
setlocal
rem Usage: run_probe_vehicle_physics.cmd <OutputFile>
rem Writes the Chaos vehicle values the logic export does not carry (physical materials, wheel bones, physics asset, spring arm lag,
rem brake light material); see probe_vehicle_physics.py. Nothing is saved.
set "UE_EDITOR_CMD=C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe"
set "SCRIPT_DIR=%~dp0"
set "CGE_PROBE_OUT=%~f1"
"%UE_EDITOR_CMD%" "G:\UE\5.8\Vehicle\Vehicle.uproject" ^
  -ExecutePythonScript="%SCRIPT_DIR%probe_vehicle_physics.py" ^
  -EnablePlugins=PythonScriptPlugin ^
  -stdout -unattended -NoSplash -nosound -nullrhi
