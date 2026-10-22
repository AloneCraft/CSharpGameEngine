@echo off
setlocal
rem -nullrhi on purpose: an animation file needs the clip only. With a render device the exporter bakes the preview mesh's materials into it (10 MB a file).
rem The files still hold the preview mesh (3.7 MB each); compact them with: CSharpGameEngine.Unreal.SceneImporter compact-animations <directory>
rem Usage: run_export_mannequin_animations.cmd <ProjectName> <OutputDirectory> [all]
rem Example: run_export_mannequin_animations.cmd TopDown .artifacts\m0107\anims all
rem Opens G:\UE\5.8\<ProjectName> and writes the Walk and Jog animations (and with "all" Idle, Jump, Fall Loop and Land) as skinned glb files.
set "UE_EDITOR_CMD=C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe"
set "SCRIPT_DIR=%~dp0"
if "%~2"=="" (
  echo Usage: run_export_mannequin_animations.cmd ProjectName OutputDirectory [all] 1>&2
  exit /b 2
)
set "CGE_ANIM_OUT=%~f2"
if /i "%~3"=="all" set "CGE_ANIM_ALL=1"

"%UE_EDITOR_CMD%" "G:\UE\5.8\%~1\%~1.uproject" ^
  -ExecutePythonScript="%SCRIPT_DIR%export_mannequin_animations.py" ^
  -EnablePlugins=PythonScriptPlugin,GLTFExporter ^
  -stdout -unattended -NoSplash -nosound -nullrhi

if errorlevel 1 exit /b %errorlevel%
dir /b "%CGE_ANIM_OUT%\*.glb"
