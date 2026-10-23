@echo off
setlocal
rem Usage: run_probe_vehicle_emissive.cmd <OutputFile>
rem Follows the sports car body material emissive input and exports the texture it samples; see probe_vehicle_emissive.py.
set "UE_EDITOR_CMD=C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe"
set "SCRIPT_DIR=%~dp0"
set "CGE_PROBE_OUT=%~f1"
"%UE_EDITOR_CMD%" "G:\UE\5.8\Vehicle\Vehicle.uproject" ^
  -ExecutePythonScript="%SCRIPT_DIR%probe_vehicle_emissive.py" ^
  -EnablePlugins=PythonScriptPlugin ^
  -stdout -unattended -NoSplash -nosound -nullrhi
