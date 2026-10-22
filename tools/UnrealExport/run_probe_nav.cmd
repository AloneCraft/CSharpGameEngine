@echo off
setlocal
rem Usage: run_probe_nav.cmd <ProjectName> [Map]
rem Opens G:\UE\5.8\<ProjectName> in UnrealEditor-Cmd (no rendering), loads its startup map and writes the NavMeshBoundsVolume and
rem RecastNavMesh values to .artifacts\UnrealExport\logic\<ProjectName>\nav.json. Nothing is saved.
set "UE_ROOT=C:\Program Files\Epic Games\UE_5.8"
set "UE_EDITOR_CMD=%UE_ROOT%\Engine\Binaries\Win64\UnrealEditor-Cmd.exe"
set "SCRIPT_DIR=%~dp0"
for %%I in ("%SCRIPT_DIR%..\..") do set "REPO_ROOT=%%~fI"
set "PROJECT=%~1"
if "%PROJECT%"=="" (
  echo Usage: run_probe_nav.cmd ProjectName [Map] 1>&2
  exit /b 2
)
if not "%~2"=="" set "CGE_PROBE_MAP=%~2"
if not exist "%REPO_ROOT%\.artifacts\UnrealExport\logic\%PROJECT%" mkdir "%REPO_ROOT%\.artifacts\UnrealExport\logic\%PROJECT%"
set "CGE_NAV_OUT=%REPO_ROOT%\.artifacts\UnrealExport\logic\%PROJECT%\nav.json"
if exist "%CGE_NAV_OUT%" del /q "%CGE_NAV_OUT%"

"%UE_EDITOR_CMD%" "G:\UE\5.8\%PROJECT%\%PROJECT%.uproject" ^
  -ExecutePythonScript="%SCRIPT_DIR%probe_nav.py" ^
  -EnablePlugins=PythonScriptPlugin ^
  -stdout -unattended -NoSplash -nosound -nullrhi

if errorlevel 1 exit /b %errorlevel%
if not exist "%CGE_NAV_OUT%" (
  echo The probe wrote no result: %CGE_NAV_OUT% 1>&2
  exit /b 1
)
type "%CGE_NAV_OUT%"
