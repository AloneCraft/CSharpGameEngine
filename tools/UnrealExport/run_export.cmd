@echo off
setlocal

set "UE_EDITOR_CMD=C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe"
set "UPROJECT=G:\UE\5.8\WFC\WFC.uproject"
set "REPO_ROOT=G:\GitHub\CSharpGameEngine"
set "SCRIPT_DIR=%~dp0"
set "PYTHON_SCRIPT=%SCRIPT_DIR%export_lvl_firstperson.py"
set "MANIFEST=%REPO_ROOT%\.artifacts\UnrealExport\Lvl_FirstPerson\manifest.json"

if /I "%~1"=="--force" set "CSGE_UNREAL_EXPORT_FORCE=1"
if exist "%MANIFEST%" del /q "%MANIFEST%"

"%UE_EDITOR_CMD%" "%UPROJECT%" ^
  -ExecutePythonScript="%PYTHON_SCRIPT%" ^
  -EnablePlugins=PythonScriptPlugin,GLTFExporter ^
  -stdout -unattended -NoSplash -nosound

if errorlevel 1 exit /b %errorlevel%
if not exist "%MANIFEST%" (
  echo Unreal export did not produce manifest.json. 1>&2
  exit /b 1
)

dotnet run --project "%SCRIPT_DIR%..\CSharpGameEngine.Unreal.SceneImporter\CSharpGameEngine.Unreal.SceneImporter.csproj" -- ^
  "%MANIFEST%" ^
  "%REPO_ROOT%\UserEcsProject\Scenes\FirstPersonStageScene.cs" ^
  "%REPO_ROOT%\UserEcsProject\Scenes\FirstPersonStageAssets.cs"

endlocal
