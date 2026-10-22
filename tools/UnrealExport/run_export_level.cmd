@echo off
setlocal

rem Usage: run_export_level.cmd <FirstPerson|TopDown|Vehicle> [--force]
rem Note: the FirstPerson template now uses a copy of the ThirdPerson level (same layout, and this export loses the green material), so do not re-export it.
rem Imports the level of a UE5.8 game template project (G:\UE\5.8\<Template>, made by create_template_project.ps1) into
rem Templates\CSharpGameEngine.Templates.<Template>: static meshes as glb, the level as scene JSON (ADR-0070), the world grid.
rem The character is authored by hand from dump_blueprint.py's output; this script does not touch it.
set "TEMPLATE=%~1"
if "%TEMPLATE%"=="" (
  echo Usage: run_export_level.cmd FirstPerson^|TopDown [--force] 1>&2
  exit /b 2
)
if /I "%TEMPLATE%"=="FirstPerson" set "LEVEL=Lvl_FirstPerson"
if /I "%TEMPLATE%"=="TopDown" set "LEVEL=Lvl_TopDown"
if /I "%TEMPLATE%"=="Vehicle" set "LEVEL=Lvl_VehicleBasic"
if not defined LEVEL (
  echo Unsupported template: %TEMPLATE% 1>&2
  exit /b 2
)
set "UE_EDITOR_CMD=C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe"
set "SCRIPT_DIR=%~dp0"
for %%I in ("%SCRIPT_DIR%..\..") do set "REPO_ROOT=%%~fI"
set "CSGE_REPO_ROOT=%REPO_ROOT%"
set "CSGE_LEVEL_TEMPLATE=%TEMPLATE%"
set "MANIFEST=%REPO_ROOT%\.artifacts\UnrealExport\%LEVEL%\manifest.json"
set "TEMPLATE_DIR=%REPO_ROOT%\Templates\CSharpGameEngine.Templates.%TEMPLATE%"
set "IMPORTER=%SCRIPT_DIR%..\CSharpGameEngine.Unreal.SceneImporter\CSharpGameEngine.Unreal.SceneImporter.csproj"

if /I "%~2"=="--force" set "CSGE_UNREAL_EXPORT_FORCE=1"
if exist "%MANIFEST%" del /q "%MANIFEST%"

"%UE_EDITOR_CMD%" "G:\UE\5.8\%TEMPLATE%\%TEMPLATE%.uproject" ^
  -ExecutePythonScript="%SCRIPT_DIR%export_level.py" ^
  -EnablePlugins=PythonScriptPlugin,GLTFExporter ^
  -stdout -unattended -NoSplash -nosound -nullrhi
if errorlevel 1 exit /b %errorlevel%
if not exist "%MANIFEST%" (
  echo Unreal export did not produce manifest.json. 1>&2
  exit /b 1
)

rem The importer writes the level as a C# class; the converter turns it into the scene JSON (ADR-0070).
if exist "%TEMPLATE_DIR%\Scenes\%LEVEL%.json" del /q "%TEMPLATE_DIR%\Scenes\%LEVEL%.json"
dotnet run --project "%IMPORTER%" -- level "%MANIFEST%" "%TEMPLATE_DIR%" %TEMPLATE% %LEVEL%
if errorlevel 1 exit /b %errorlevel%
dotnet run --project "%SCRIPT_DIR%..\CSharpGameEngine.SceneJson.Cli\CSharpGameEngine.SceneJson.Cli.csproj" -- ^
  convert "%TEMPLATE_DIR%\CSharpGameEngine.Templates.%TEMPLATE%.csproj" --write --delete-source
if errorlevel 1 exit /b %errorlevel%

rem The asset catalog the converter needed is an intermediate; the typed Assets.* replaces it (ADR-0077).
if exist "%TEMPLATE_DIR%\Scenes\%TEMPLATE%Assets.cs" del /q "%TEMPLATE_DIR%\Scenes\%TEMPLATE%Assets.cs"

rem The prototype grid materials are drawn procedurally in world space, not from the baked textures (ADR-0075).
rem The Vehicle arena uses no prototype grid material (its manifest has no materialGrids), so there is nothing to convert.
if /I "%TEMPLATE%"=="Vehicle" goto :done
dotnet run --project "%IMPORTER%" -- worldgrid "%MANIFEST%" "%TEMPLATE_DIR%"
if errorlevel 1 exit /b %errorlevel%

:done
endlocal
