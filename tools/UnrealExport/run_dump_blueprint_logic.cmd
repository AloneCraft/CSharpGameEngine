@echo off
setlocal
rem Usage: run_dump_blueprint_logic.cmd <ProjectDirectory> <OutputDirectory> <AssetPath> [<AssetPath> ...]
rem Example: run_dump_blueprint_logic.cmd G:\UE\5.8\FirstPerson .artifacts\UnrealExport\logic\FirstPerson /Game/FirstPerson/Blueprints/BP_FirstPersonCharacter
rem Opens the project without rendering and writes the T3D text, the graph JSON and the decompiled pseudo code of each asset
rem (dump_blueprint_logic.py). Unreal's own log (<Project>\Saved\Logs) carries the [UnrealExport] lines. Nothing is saved.
set "UE_EDITOR_CMD=C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe"
set "SCRIPT_DIR=%~dp0"
if "%~3"=="" (
  echo Usage: run_dump_blueprint_logic.cmd ProjectDirectory OutputDirectory AssetPath... 1>&2
  exit /b 2
)
set "PROJECT_DIR=%~f1"
set "CGE_LOGIC_OUT=%~f2"
shift
shift
set "CGE_LOGIC_ASSETS=["
set "SEPARATOR="
:collect
if "%~1"=="" goto run
set "CGE_LOGIC_ASSETS=%CGE_LOGIC_ASSETS%%SEPARATOR%"%~1""
set "SEPARATOR=,"
shift
goto collect
:run
set "CGE_LOGIC_ASSETS=%CGE_LOGIC_ASSETS%]"
for %%P in ("%PROJECT_DIR%\*.uproject") do set "UPROJECT=%%~fP"
"%UE_EDITOR_CMD%" "%UPROJECT%" ^
  -ExecutePythonScript="%SCRIPT_DIR%dump_blueprint_logic.py" ^
  -EnablePlugins=PythonScriptPlugin,ToolsetRegistry,EditorToolset ^
  -stdout -unattended -NoSplash -nosound -nullrhi
if errorlevel 1 exit /b %errorlevel%
if not exist "%CGE_LOGIC_OUT%\_summary.json" (
  echo The dump wrote no summary: %CGE_LOGIC_OUT% 1>&2
  exit /b 1
)
echo %CGE_LOGIC_OUT%
