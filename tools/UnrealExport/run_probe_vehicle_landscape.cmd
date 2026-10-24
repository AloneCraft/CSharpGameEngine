@echo off
setlocal
rem Usage: run_probe_vehicle_landscape.cmd [probe_script.py]   (CGE_PROBE_OUT = the result file; CGE_PROBE_RHI=1 keeps the render device)
rem Reads the Vehicle template level's Landscape (components, material, splines, collision heights); see probe_vehicle_landscape.py. Nothing is saved.
set "UE_EDITOR_CMD=C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe"
set "SCRIPT_DIR=%~dp0"
set "PROBE=%~1"
if "%PROBE%"=="" set "PROBE=%SCRIPT_DIR%probe_vehicle_landscape.py"
set "NULLRHI=-nullrhi"
if "%CGE_PROBE_RHI%"=="1" set "NULLRHI="
"%UE_EDITOR_CMD%" "G:\UE\5.8\Vehicle\Vehicle.uproject" ^
  -ExecutePythonScript="%PROBE%" ^
  -EnablePlugins=PythonScriptPlugin ^
  -stdout -unattended -NoSplash -nosound %NULLRHI%
