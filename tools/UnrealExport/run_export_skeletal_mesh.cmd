@echo off
setlocal
rem Usage: run_export_skeletal_mesh.cmd <ProjectName> <AssetPath> <OutputGlb>
rem Example: run_export_skeletal_mesh.cmd TopDown /Game/Characters/Mannequins/Meshes/SKM_Manny_Simple Templates\CSharpGameEngine.Templates.TopDown\Assets\Characters\Mannequins\SKM_Manny_Simple.glb
rem Opens G:\UE\5.8\<ProjectName> without rendering and exports the skeletal mesh as a skinned glb. Nothing is saved back to Unreal.
set "UE_EDITOR_CMD=C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe"
set "SCRIPT_DIR=%~dp0"
for %%I in ("%SCRIPT_DIR%..\..") do set "REPO_ROOT=%%~fI"
if "%~3"=="" (
  echo Usage: run_export_skeletal_mesh.cmd ProjectName AssetPath OutputGlb 1>&2
  exit /b 2
)
rem Set CGE_SK_STATIC=1 before calling to export a static mesh (no skin) instead of a skeletal one.
set "CGE_SK_ASSET=%~2"
set "CGE_SK_OUT=%~f3"
if exist "%CGE_SK_OUT%" del /q "%CGE_SK_OUT%"

"%UE_EDITOR_CMD%" "G:\UE\5.8\%~1\%~1.uproject" ^
  -ExecutePythonScript="%SCRIPT_DIR%export_skeletal_mesh.py" ^
  -EnablePlugins=PythonScriptPlugin,GLTFExporter ^
  -stdout -unattended -NoSplash -nosound -nullrhi

if errorlevel 1 exit /b %errorlevel%
if not exist "%CGE_SK_OUT%" (
  echo The export wrote no file: %CGE_SK_OUT% 1>&2
  exit /b 1
)
echo %CGE_SK_OUT%
