@echo off
setlocal
rem Usage: run_probe_topdown_camera.cmd
rem Writes the TopDown character camera settings to the file named by CGE_PROBE_OUT; see probe_topdown_camera.py. Nothing is saved.
set "UE_EDITOR_CMD=C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe"
set "SCRIPT_DIR=%~dp0"
"%UE_EDITOR_CMD%" "G:\UE\5.8\TopDown\TopDown.uproject" ^
  -ExecutePythonScript="%SCRIPT_DIR%probe_topdown_camera.py" ^
  -EnablePlugins=PythonScriptPlugin ^
  -stdout -unattended -NoSplash -nosound -nullrhi
