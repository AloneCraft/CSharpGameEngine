[CmdletBinding()]
param(
    [switch]$NoBuild,
    [string]$LogPath
)

$ErrorActionPreference='Stop'
$RootSuffix='CgeFixed'
$RepoRoot=Split-Path -Parent $PSScriptRoot
$Project=Join-Path $RepoRoot 'tools\CSharpGameEngine.VSExtension\CSharpGameEngine.VSExtension.csproj'
$ReinstallScript=Join-Path $RepoRoot 'tools\CSharpGameEngine.VSExtension\Reinstall-CgeFixedVsix.ps1'
$Solution=Join-Path $RepoRoot 'UserEcsProject.slnx'
$Vsix=Join-Path $RepoRoot 'tools\CSharpGameEngine.VSExtension\bin\Debug\net472\CSharpGameEngine.VSExtension.vsix'
$Timestamp=Get-Date -Format 'yyyyMMdd-HHmmss'

if([string]::IsNullOrWhiteSpace($LogPath)){
    $LogDirectory=Join-Path $env:LOCALAPPDATA 'CSharpGameEngine\Logs'
    New-Item -ItemType Directory -Path $LogDirectory -Force | Out-Null
    $LogPath=Join-Path $LogDirectory "CgeFixed-ActivityLog-$Timestamp.xml"
}

$RootSuffixArgumentPattern='(?i)(?:/|-)(?:rootSuffix|rootsuffix)(?:\s+|:)"?CgeFixed"?(?:\s|$)'
$Running=@(Get-CimInstance -ClassName Win32_Process -Filter "Name = 'devenv.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -match $RootSuffixArgumentPattern })
if($Running.Count -ne 0){
    throw "Close Visual Studio instances using root suffix '$RootSuffix' before updating its extension files."
}

if(!$NoBuild){
    & dotnet build $Project --configuration Debug
    if($LASTEXITCODE -ne 0){ exit $LASTEXITCODE }
}

if(!(Test-Path -LiteralPath $ReinstallScript)){ throw "CgeFixed maintenance script was not found: $ReinstallScript" }
if(!(Test-Path -LiteralPath $Vsix)){ throw "VSIX package was not found: $Vsix" }
if(!(Test-Path -LiteralPath $Solution)){ throw "UserEcsProject.slnx was not found: $Solution" }

$MaintenanceLog=Join-Path (Split-Path -Parent $LogPath) "CgeFixed-VsixMaintenance-$Timestamp.log"
$Maintenance=@(& $ReinstallScript -SkipBuild -VsixPath $Vsix -LogPath $MaintenanceLog)
if($Maintenance.Count -ne 1){ throw "CgeFixed maintenance did not return exactly one result. See $MaintenanceLog" }
Write-Host "VSIX deployment changed: $($Maintenance[0].DeploymentChanged)"
Write-Host "VSIX maintenance log: $($Maintenance[0].LogPath)"

$VsWhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if(!(Test-Path -LiteralPath $VsWhere)){ throw 'vswhere.exe was not found.' }
$InstallPath=& $VsWhere -latest -products '*' -property installationPath | Select-Object -First 1
$Devenv=Join-Path $InstallPath 'Common7\IDE\devenv.exe'
if(!(Test-Path -LiteralPath $Devenv)){ throw "devenv.exe was not found: $Devenv" }

Write-Host "Starting Visual Studio /rootSuffix $RootSuffix. ActivityLog: $LogPath"
Start-Process -FilePath $Devenv -ArgumentList '/RootSuffix',$RootSuffix,'/Log',('"{0}"' -f $LogPath),('"{0}"' -f $Solution)
