@echo off
setlocal
rem Usage: run_probe_vehicle_brake_lights.cmd <OutputFile>
rem Writes whether the brake light material is drawn and the physics asset bodies; see probe_vehicle_brake_lights.py.
set "UE_EDITOR_CMD=C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe"
set "SCRIPT_DIR=%~dp0"
set "CGE_PROBE_OUT=%~f1"
"%UE_EDITOR_CMD%" "G:\UE\5.8\Vehicle\Vehicle.uproject" ^
  -ExecutePythonScript="%SCRIPT_DIR%probe_vehicle_brake_lights.py" ^
  -EnablePlugins=PythonScriptPlugin ^
  -stdout -unattended -NoSplash -nosound -nullrhi
