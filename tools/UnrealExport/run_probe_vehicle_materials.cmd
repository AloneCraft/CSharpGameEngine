@echo off
setlocal
rem Usage: run_probe_vehicle_materials.cmd
rem Prints (to stdout) the materials of the Vehicle template meshes and sports car; see probe_vehicle_materials.py. Nothing is saved.
set "UE_EDITOR_CMD=C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe"
set "SCRIPT_DIR=%~dp0"
"%UE_EDITOR_CMD%" "G:\UE\5.8\Vehicle\Vehicle.uproject" ^
  -ExecutePythonScript="%SCRIPT_DIR%probe_vehicle_materials.py" ^
  -EnablePlugins=PythonScriptPlugin ^
  -stdout -unattended -NoSplash -nosound -nullrhi
