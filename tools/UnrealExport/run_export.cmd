@echo off
setlocal

set "UE_EDITOR_CMD=C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe"
set "UPROJECT=G:\UE\5.8\WFC\WFC.uproject"
set "SCRIPT_DIR=%~dp0"
set "PYTHON_SCRIPT=%SCRIPT_DIR%export_lvl_firstperson.py"

"%UE_EDITOR_CMD%" "%UPROJECT%" ^
  -ExecutePythonScript="%PYTHON_SCRIPT%" ^
  -EnablePlugins=PythonScriptPlugin,GLTFExporter ^
  -stdout -unattended -NoSplash -nosound

endlocal
