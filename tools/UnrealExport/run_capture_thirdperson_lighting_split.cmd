@echo off
setlocal
rem Usage: run_capture_thirdperson_lighting_split.cmd
rem Captures the ThirdPerson spawn view with default flags and with SkyLighting / GlobalIllumination / AmbientOcclusion off into
rem .artifacts\UnrealExport\Lvl_ThirdPerson; see capture_thirdperson_lighting_split.py. Runs the GUI editor on the staged TP_ThirdPerson project. Nothing is saved.
set "UE_EDITOR=C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor.exe"
set "SCRIPT_DIR=%~dp0"
for %%I in ("%SCRIPT_DIR%..\..") do set "CSGE_REPO_ROOT=%%~fI"
"%UE_EDITOR%" "%CSGE_REPO_ROOT%\.artifacts\UnrealExport\TP_ThirdPerson\TP_ThirdPerson.uproject" ^
  -ExecCmds="py %SCRIPT_DIR%capture_thirdperson_lighting_split.py" ^
  -EnablePlugins=PythonScriptPlugin -NoSplash -nosound -unattended
