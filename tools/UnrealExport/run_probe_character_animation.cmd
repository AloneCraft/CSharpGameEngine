@echo off
setlocal
rem Usage: run_probe_character_animation.cmd <ProjectName> [RigAssetPath]
rem Opens G:\UE\5.8\<ProjectName> in UnrealEditor-Cmd (no rendering), loads its startup map and writes the world's KillZ, its PlayerStarts
rem and (with RigAssetPath, for example /Game/FirstPerson/Anims/CtrlRig_FPWarp) that control rig's controls to
rem .artifacts\UnrealExport\probe_character_<ProjectName>.json. Nothing is saved.
set "UE_ROOT=C:\Program Files\Epic Games\UE_5.8"
set "UE_EDITOR_CMD=%UE_ROOT%\Engine\Binaries\Win64\UnrealEditor-Cmd.exe"
set "SCRIPT_DIR=%~dp0"
for %%I in ("%SCRIPT_DIR%..\..") do set "REPO_ROOT=%%~fI"
set "PROJECT=%~1"
if "%PROJECT%"=="" (
  echo Usage: run_probe_character_animation.cmd ProjectName [RigAssetPath] 1>&2
  exit /b 2
)
if not "%~2"=="" set "CGE_PROBE_RIG=%~2"
if not exist "%REPO_ROOT%\.artifacts\UnrealExport" mkdir "%REPO_ROOT%\.artifacts\UnrealExport"
set "CGE_PROBE_OUT=%REPO_ROOT%\.artifacts\UnrealExport\probe_character_%PROJECT%.json"
if exist "%CGE_PROBE_OUT%" del /q "%CGE_PROBE_OUT%"

"%UE_EDITOR_CMD%" "G:\UE\5.8\%PROJECT%\%PROJECT%.uproject" ^
  -ExecutePythonScript="%SCRIPT_DIR%probe_character_animation.py" ^
  -EnablePlugins=PythonScriptPlugin,ControlRig ^
  -stdout -unattended -NoSplash -nosound -nullrhi

if errorlevel 1 exit /b %errorlevel%
if not exist "%CGE_PROBE_OUT%" (
  echo The probe wrote no result: %CGE_PROBE_OUT% 1>&2
  exit /b 1
)
type "%CGE_PROBE_OUT%"
