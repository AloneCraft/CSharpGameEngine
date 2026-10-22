@echo off
setlocal
rem Usage: run_export_vehicle_car.cmd [output folder]
rem Exports the UE5.8 Vehicle template's sports car meshes (G:\UE\5.8\Vehicle, made by create_template_project.ps1) to glTF with their materials;
rem see export_vehicle_car.py. The editor runs with a render device (no -nullrhi) because the glTF exporter cannot read materials without one.
set "UE_EDITOR_CMD=C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe"
set "SCRIPT_DIR=%~dp0"
for %%I in ("%SCRIPT_DIR%..\..") do set "REPO_ROOT=%%~fI"
set "CGE_VEHICLE_CAR_OUT_DIR=%~1"
if "%CGE_VEHICLE_CAR_OUT_DIR%"=="" set "CGE_VEHICLE_CAR_OUT_DIR=%REPO_ROOT%\Templates\CSharpGameEngine.Templates.Vehicle\Assets\Vehicles\SportsCar"
"%UE_EDITOR_CMD%" "G:\UE\5.8\Vehicle\Vehicle.uproject" ^
  -ExecutePythonScript="%SCRIPT_DIR%export_vehicle_car.py" ^
  -EnablePlugins=PythonScriptPlugin,GLTFExporter ^
  -stdout -unattended -NoSplash -nosound
