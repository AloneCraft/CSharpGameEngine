@echo off
setlocal
rem Usage: run_dump_blueprint.cmd <ProjectName> <GeneratedClassPath> <OutputName>
rem Example: run_dump_blueprint.cmd FirstPerson /Game/FirstPerson/Blueprints/BP_FirstPersonCharacter.BP_FirstPersonCharacter_C FirstPersonCharacter
rem Opens G:\UE\5.8\<ProjectName> without rendering and writes .artifacts\UnrealExport\blueprint_<OutputName>.json. Nothing is saved.
set "UE_EDITOR_CMD=C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe"
set "SCRIPT_DIR=%~dp0"
for %%I in ("%SCRIPT_DIR%..\..") do set "REPO_ROOT=%%~fI"
if "%~3"=="" (
  echo Usage: run_dump_blueprint.cmd ProjectName GeneratedClassPath OutputName 1>&2
  exit /b 2
)
if not exist "%REPO_ROOT%\.artifacts\UnrealExport" mkdir "%REPO_ROOT%\.artifacts\UnrealExport"
set "CGE_BP_CLASS=%~2"
set "CGE_BP_OUT=%REPO_ROOT%\.artifacts\UnrealExport\blueprint_%~3.json"
if exist "%CGE_BP_OUT%" del /q "%CGE_BP_OUT%"

"%UE_EDITOR_CMD%" "G:\UE\5.8\%~1\%~1.uproject" ^
  -ExecutePythonScript="%SCRIPT_DIR%dump_blueprint.py" ^
  -EnablePlugins=PythonScriptPlugin ^
  -stdout -unattended -NoSplash -nosound -nullrhi

if errorlevel 1 exit /b %errorlevel%
if not exist "%CGE_BP_OUT%" (
  echo The dump wrote no result: %CGE_BP_OUT% 1>&2
  exit /b 1
)
echo %CGE_BP_OUT%
