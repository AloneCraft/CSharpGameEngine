@echo off
setlocal
rem Usage: run_capture_vehicle_reference.cmd
rem Captures UE reference images of the Vehicle template level with the sports car (chase camera and a front three-quarter view) into
rem .artifacts\UnrealExport\Lvl_VehicleBasic; see capture_vehicle_reference.py. Runs the GUI editor (Lumen needs rendered frames) and quits itself.
set "UE_EDITOR=C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor.exe"
set "SCRIPT_DIR=%~dp0"
for %%I in ("%SCRIPT_DIR%..\..") do set "CSGE_REPO_ROOT=%%~fI"
"%UE_EDITOR%" "G:\UE\5.8\Vehicle\Vehicle.uproject" ^
  -ExecCmds="py %SCRIPT_DIR%capture_vehicle_reference.py" ^
  -EnablePlugins=PythonScriptPlugin -NoSplash -nosound
