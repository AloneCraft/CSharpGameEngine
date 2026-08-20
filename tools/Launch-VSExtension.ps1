param(
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'

# This launcher deliberately targets the repaired development hive only.  Its package
# CodeBase is a compatibility directory without a VSIX manifest, so it must be kept in
# sync with the current VSIX deployment before Visual Studio starts.
$rootSuffix = 'CgeFixed'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'tools\CSharpGameEngine.VSExtension\CSharpGameEngine.VSExtension.csproj'
$solution = Join-Path $repoRoot 'UserEcsProject.slnx'

$rootSuffixArgumentPattern = '(?i)/rootsuffix(?:\s+|:)"?{0}"?(?:\s|$)' -f [regex]::Escape($rootSuffix)
$runningRootSuffixInstances = @(
    Get-CimInstance -ClassName Win32_Process -Filter "Name = 'devenv.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -match $rootSuffixArgumentPattern }
)
if ($runningRootSuffixInstances.Count -gt 0) {
    throw "Close Visual Studio instances using root suffix '$rootSuffix' before updating its extension files."
}

if (-not $NoBuild) {
    dotnet build $project -c Debug
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) { throw 'vswhere.exe was not found.' }
$installPath = & $vswhere -latest -products Microsoft.VisualStudio.Product.Community -requires Microsoft.VisualStudio.Component.CoreEditor -property installationPath
$devenv = Join-Path $installPath 'Common7\IDE\devenv.exe'
$vsixInstaller = Join-Path $installPath 'Common7\IDE\VSIXInstaller.exe'
$vsix = Join-Path $repoRoot 'tools\CSharpGameEngine.VSExtension\bin\Debug\net472\CSharpGameEngine.VSExtension.vsix'
$extensionAssembly = Join-Path $repoRoot 'tools\CSharpGameEngine.VSExtension\bin\Debug\net472\CSharpGameEngine.VSExtension.dll'
if (-not (Test-Path $devenv)) { throw "devenv.exe was not found: $devenv" }
if (-not (Test-Path $vsix)) { throw "VSIX was not found: $vsix" }
if (-not (Test-Path $extensionAssembly)) { throw "VSIX assembly was not found: $extensionAssembly" }
if (-not (Test-Path $solution)) { throw "UserEcsProject.slnx was not found: $solution" }
if (-not (Test-Path $vsixInstaller)) { throw "VSIXInstaller.exe was not found: $vsixInstaller" }

& $vsixInstaller /quiet "/rootSuffix:$rootSuffix" $vsix
$installerExitCode = $LASTEXITCODE
if ($installerExitCode -ne 0 -and $installerExitCode -ne 1) {
    throw "VSIX installation failed for root suffix '$rootSuffix' (exit code $installerExitCode)."
}

$expectedHash = (Get-FileHash -LiteralPath $extensionAssembly -Algorithm SHA256).Hash
$visualStudioProfiles = Join-Path $env:LOCALAPPDATA 'Microsoft\VisualStudio'
$profileDirectories = @(
    Get-ChildItem -LiteralPath $visualStudioProfiles -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name.EndsWith($rootSuffix, [System.StringComparison]::OrdinalIgnoreCase) }
)
$deployedAssemblies = @(
    $profileDirectories |
        ForEach-Object {
            Get-ChildItem -LiteralPath (Join-Path $_.FullName 'Extensions') -Filter 'CSharpGameEngine.VSExtension.dll' -File -Recurse -ErrorAction SilentlyContinue
        } |
        ForEach-Object {
            [PSCustomObject]@{
                Assembly = $_
                Directory = $_.Directory
                HasManifest = Test-Path -LiteralPath (Join-Path $_.Directory.FullName 'extension.vsixmanifest')
                Hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            }
        }
)
$currentDeploymentDirectories = @(
    $deployedAssemblies |
        Where-Object { $_.HasManifest -and $_.Hash -eq $expectedHash } |
        ForEach-Object { $_.Directory.FullName } |
        Sort-Object -Unique
)
if ($currentDeploymentDirectories.Count -eq 0) {
    throw "VSIX installation did not deploy the current extension assembly to root suffix '$rootSuffix'."
}
if ($currentDeploymentDirectories.Count -ne 1) {
    throw "VSIX installation found multiple current deployment directories for root suffix '$rootSuffix'; refusing to choose one."
}

$currentDeploymentDirectory = Get-Item -LiteralPath $currentDeploymentDirectories[0]
$currentDeploymentDlls = @(
    Get-ChildItem -LiteralPath $currentDeploymentDirectory.FullName -Filter '*.dll' -File -ErrorAction Stop
)
if ($currentDeploymentDlls.Count -eq 0) {
    throw "The current VSIX deployment directory contains no DLL files: $($currentDeploymentDirectory.FullName)"
}

# Directories containing this package DLL but no VSIX manifest are the old CodeBase
# compatibility targets.  Synchronize all current deployment DLLs into those targets,
# never create new targets, and verify every copied file before starting Visual Studio.
$compatibilityDirectories = @(
    $deployedAssemblies |
        Where-Object { -not $_.HasManifest } |
        ForEach-Object { $_.Directory.FullName } |
        Sort-Object -Unique
)
foreach ($compatibilityDirectoryPath in $compatibilityDirectories) {
    $compatibilityDirectory = Get-Item -LiteralPath $compatibilityDirectoryPath
    foreach ($sourceDll in $currentDeploymentDlls) {
        $targetDll = Join-Path $compatibilityDirectory.FullName $sourceDll.Name
        Copy-Item -LiteralPath $sourceDll.FullName -Destination $targetDll -Force
        $sourceHash = (Get-FileHash -LiteralPath $sourceDll.FullName -Algorithm SHA256).Hash
        $targetHash = (Get-FileHash -LiteralPath $targetDll -Algorithm SHA256).Hash
        if ($targetHash -ne $sourceHash) {
            throw "Compatibility DLL verification failed: $targetDll"
        }
    }

    Write-Host "Synchronized current VSIX DLLs to compatibility CodeBase: $($compatibilityDirectory.FullName)"
}

if ($installerExitCode -eq 1) {
    Write-Warning "VSIXInstaller returned exit code 1, but the current extension assembly was verified in root suffix '$rootSuffix'. Continuing."
}

# Do not run devenv /setup here. The CgeFixed profile already has the compatibility
# CodeBase registration required for this extension; /setup can block on unrelated
# system extensions and is not needed for the normal development launch.
Start-Process -FilePath $devenv -ArgumentList '/RootSuffix', $rootSuffix, ('"{0}"' -f $solution)
