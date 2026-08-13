param([switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'tools\CSharpGameEngine.VSExtension\CSharpGameEngine.VSExtension.csproj'
$solution = Join-Path $repoRoot 'UserEcsProject.slnx'
if (-not $NoBuild) { dotnet build $project -c Debug; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) { throw 'vswhere.exe was not found.' }
$installPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.CoreEditor -property installationPath
$devenv = Join-Path $installPath 'Common7\IDE\devenv.exe'
$vsixInstaller = Join-Path $installPath 'Common7\IDE\VSIXInstaller.exe'
$vsix = Join-Path $repoRoot 'tools\CSharpGameEngine.VSExtension\bin\Debug\CSharpGameEngine.VSExtension.vsix'
if (-not (Test-Path $devenv)) { throw "devenv.exe が見つかりません: $devenv" }
if (-not (Test-Path $vsix)) { throw "VSIX が見つかりません: $vsix" }
if (-not (Test-Path $solution)) { throw "UserEcsProject.slnx が見つかりません: $solution" }
if (Test-Path $vsixInstaller) { & $vsixInstaller /quiet /rootSuffix:Exp $vsix; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } }
Start-Process -FilePath $devenv -ArgumentList '/RootSuffix', 'Exp', ('"{0}"' -f $solution)
