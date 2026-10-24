@echo off
setlocal
rem Usage: run_capture_lumen_reference_check.cmd
rem Captures one Vehicle view three ways (scene capture, persisted scene capture, viewport screenshot) into .artifacts\UnrealExport\Lvl_VehicleBasic;
rem see capture_lumen_reference_check.py. Runs the GUI editor (it needs rendered frames) and quits itself. Nothing is saved.
set "UE_EDITOR=C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor.exe"
set "SCRIPT_DIR=%~dp0"
for %%I in ("%SCRIPT_DIR%..\..") do set "CSGE_REPO_ROOT=%%~fI"
"%UE_EDITOR%" "G:\UE\5.8\Vehicle\Vehicle.uproject" ^
  -ExecCmds="py %SCRIPT_DIR%capture_lumen_reference_check.py" ^
  -EnablePlugins=PythonScriptPlugin -NoSplash -nosound -unattended
