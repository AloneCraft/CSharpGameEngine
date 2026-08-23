[CmdletBinding()]
param(
    [switch]$NoBuild,
    [switch]$CloseCgeFixed,
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
function Get-CgeFixedProcesses{
    return @(Get-CimInstance -ClassName Win32_Process -Filter "Name = 'devenv.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -match $RootSuffixArgumentPattern })
}

# Never terminate devenv. With -CloseCgeFixed the launcher only asks each visible instance to
# close, leaving Visual Studio's normal save/cancel dialog in control; without it we fail before
# touching the extension hive.
function Assert-CgeFixedCanBeUpdated{
    $Running=Get-CgeFixedProcesses
    if($Running.Count -eq 0){ return }
    if(!$CloseCgeFixed){
        throw "Visual Studio /rootSuffix $RootSuffix is running. Close it, or rerun with -CloseCgeFixed to request a normal Visual Studio close."
    }

    foreach($Entry in $Running){
        $Process=Get-Process -Id $Entry.ProcessId -ErrorAction Stop
        if(!$Process.CloseMainWindow()){
            throw "Could not request a normal close for CgeFixed PID $($Entry.ProcessId). Close it manually; no process was terminated."
        }
    }

    $Deadline=[DateTime]::UtcNow.AddSeconds(60)
    do {
        Start-Sleep -Milliseconds 500
        $Running=Get-CgeFixedProcesses
    } while($Running.Count -ne 0 -and [DateTime]::UtcNow -lt $Deadline)

    if($Running.Count -ne 0){
        throw "CgeFixed is still running, probably waiting for a save/cancel dialog. Close it manually; no extension files were changed."
    }
}

Assert-CgeFixedCanBeUpdated

if(!$NoBuild){
    & dotnet build $Project --configuration Debug -t:Rebuild
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

function Get-VisualStudioInstallPath{
    $VsWhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if(!(Test-Path -LiteralPath $VsWhere)){ throw 'vswhere.exe was not found.' }
    $Candidates=@(& $VsWhere -products '*' -requires Microsoft.VisualStudio.Component.CoreEditor -property installationPath)
    foreach($Candidate in $Candidates){
        if(Test-Path -LiteralPath (Join-Path $Candidate 'Common7\IDE\devenv.exe')){
            return $Candidate
        }
    }
    throw "No runnable Visual Studio installation was found. Candidates: $($Candidates -join ', ')"
}

$InstallPath=Get-VisualStudioInstallPath
$Devenv=Join-Path $InstallPath 'Common7\IDE\devenv.exe'

Write-Host "Starting Visual Studio /rootSuffix $RootSuffix. ActivityLog: $LogPath"
Start-Process -FilePath $Devenv -ArgumentList '/RootSuffix',$RootSuffix,'/Log',('"{0}"' -f $LogPath),('"{0}"' -f $Solution)
