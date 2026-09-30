@echo off
setlocal

rem Imports the UE5.8 ThirdPerson (BP) template into templates\CSharpGameEngine.Templates.ThirdPerson.
rem export_lvl_thirdperson.py refuses to replace existing generated assets unless --force is given.
set "UE_ROOT=C:\Program Files\Epic Games\UE_5.8"
set "UE_EDITOR_CMD=%UE_ROOT%\Engine\Binaries\Win64\UnrealEditor-Cmd.exe"
set "SCRIPT_DIR=%~dp0"
for %%I in ("%SCRIPT_DIR%..\..") do set "REPO_ROOT=%%~fI"
set "CSGE_REPO_ROOT=%REPO_ROOT%"
set "PYTHON_SCRIPT=%SCRIPT_DIR%export_lvl_thirdperson.py"
set "MANIFEST=%REPO_ROOT%\.artifacts\UnrealExport\Lvl_ThirdPerson\manifest.json"
set "TEMPLATE_DIR=%REPO_ROOT%\templates\CSharpGameEngine.Templates.ThirdPerson"

if /I "%~1"=="--force" set "CSGE_UNREAL_EXPORT_FORCE=1"

set "UPROJECT="
for /f "usebackq delims=" %%P in (`powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%prepare_thirdperson_project.ps1" -EngineRoot "%UE_ROOT%"`) do set "UPROJECT=%%P"
if not defined UPROJECT (
  echo prepare_thirdperson_project.ps1 did not produce a staging project. 1>&2
  exit /b 1
)
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
  thirdperson "%MANIFEST%" "%TEMPLATE_DIR%"

endlocal
