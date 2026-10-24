@echo off
setlocal
rem Usage: run_capture_vehicle_ground_color.cmd
rem Captures the Vehicle level Landscape's base colour from above and two lit reference views into .artifacts\UnrealExport\Lvl_VehicleBasic;
rem see capture_vehicle_ground_color.py. Runs the GUI editor (it needs rendered frames) and quits itself. Nothing is saved.
set "UE_EDITOR=C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor.exe"
set "SCRIPT_DIR=%~dp0"
for %%I in ("%SCRIPT_DIR%..\..") do set "CSGE_REPO_ROOT=%%~fI"
"%UE_EDITOR%" "G:\UE\5.8\Vehicle\Vehicle.uproject" ^
  -ExecCmds="py %SCRIPT_DIR%capture_vehicle_ground_color.py" ^
  -EnablePlugins=PythonScriptPlugin -NoSplash -nosound -unattended
