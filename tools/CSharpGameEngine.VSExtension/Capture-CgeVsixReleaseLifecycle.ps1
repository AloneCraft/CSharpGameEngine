[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration='Release',
    [string]$OutputRoot,
    [switch]$SkipBuild
)

$ErrorActionPreference='Stop'
$Utf8NoBom=[Text.UTF8Encoding]::new($false)
[Console]::InputEncoding=$Utf8NoBom
[Console]::OutputEncoding=$Utf8NoBom
$OutputEncoding=$Utf8NoBom
$ExtensionId='CSharpGameEngine.VSExtension.aec17804-e2e4-46e7-b373-de11af816203'
$RootSuffix='CgeRelease'
$ScriptDirectory=Split-Path -Parent $PSCommandPath
$RepositoryRoot=[IO.Path]::GetFullPath((Join-Path $ScriptDirectory '..\..'))
$ProjectPath=Join-Path $ScriptDirectory 'CSharpGameEngine.VSExtension.csproj'
$PackagePath=Join-Path $ScriptDirectory "bin\$Configuration\net472\CSharpGameEngine.VSExtension.vsix"
$BuildDllPath=Join-Path $ScriptDirectory "bin\$Configuration\net472\CSharpGameEngine.VSExtension.dll"
$ReleaseInputPath=Join-Path $RepositoryRoot 'Docs\Verification\VSIX-Release-Input.json'
if($Configuration -eq 'Release'){
    if(!(Test-Path -LiteralPath $ReleaseInputPath)){ throw "Frozen VSIX release input was not found: $ReleaseInputPath" }
    $ReleaseInput=Get-Content -LiteralPath $ReleaseInputPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if($ReleaseInput.schemaVersion -ne 'csharp-game-engine-vsix-release-input-v1' -or
        $ReleaseInput.configuration -ne 'Release'){
        throw 'Frozen VSIX release input has an invalid schema or configuration.'
    }
    $PackagePath=[IO.Path]::GetFullPath((Join-Path $RepositoryRoot ([string]$ReleaseInput.packagePath)))
    $BuildDllPath=[IO.Path]::GetFullPath((Join-Path $RepositoryRoot ([string]$ReleaseInput.buildDllPath)))
    $RepositoryPrefix=$RepositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
    if(!$PackagePath.StartsWith($RepositoryPrefix,[StringComparison]::OrdinalIgnoreCase) -or
        !$BuildDllPath.StartsWith($RepositoryPrefix,[StringComparison]::OrdinalIgnoreCase)){
        throw 'Frozen VSIX release input escaped the repository.'
    }
}
$ReinstallScript=Join-Path $ScriptDirectory 'Reinstall-CgeFixedVsix.ps1'
$StartedUtc=[DateTimeOffset]::UtcNow
if([string]::IsNullOrWhiteSpace($OutputRoot)){
    $OutputRoot=Join-Path $RepositoryRoot '.artifacts\VsixReleaseLifecycle'
}
$RunRoot=Join-Path ([IO.Path]::GetFullPath($OutputRoot)) ($StartedUtc.ToString('yyyyMMddTHHmmssfffZ')+'-'+[Guid]::NewGuid().ToString('N'))
$InstallLog=Join-Path $RunRoot 'install.log'
$UninstallLog=Join-Path $RunRoot 'uninstall.log'
$EvidencePath=Join-Path $RunRoot 'vsix-lifecycle.json'

function Get-VsixInstallerPath{
    $VsWhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if(!(Test-Path -LiteralPath $VsWhere)){ throw 'vswhere.exe was not found.' }
    $InstallPaths=@(& $VsWhere -products '*' -requires Microsoft.VisualStudio.Component.CoreEditor -property installationPath)
    foreach($InstallPath in $InstallPaths){
        $Candidate=Join-Path $InstallPath 'Common7\IDE\VSIXInstaller.exe'
        if(Test-Path -LiteralPath $Candidate){ return $Candidate }
    }
    throw 'VSIXInstaller.exe was not found for a Visual Studio Core Editor installation.'
}

function Get-ManifestIdentity([string]$ManifestPath){
    [xml]$Manifest=[IO.File]::ReadAllText($ManifestPath,$Utf8NoBom)
    $Namespace=New-Object System.Xml.XmlNamespaceManager($Manifest.NameTable)
    $Namespace.AddNamespace('vsix','http://schemas.microsoft.com/developer/vsx-schema/2011')
    $Identity=$Manifest.SelectSingleNode('/vsix:PackageManifest/vsix:Metadata/vsix:Identity',$Namespace)
    if($null -eq $Identity){ throw "VSIX manifest Identity was not found: $ManifestPath" }
    [pscustomobject]@{ Id=[string]$Identity.Id;Version=[string]$Identity.Version }
}

function Get-PackageDllHash([string]$PathValue){
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $Archive=[IO.Compression.ZipFile]::OpenRead($PathValue)
    try {
        $Entries=@($Archive.Entries | Where-Object { $_.FullName -like '*CSharpGameEngine.VSExtension.dll' })
        if($Entries.Count -ne 1){ throw "Expected one extension DLL in $PathValue, found $($Entries.Count)." }
        $Stream=$Entries[0].Open()
        try { (Get-FileHash -InputStream $Stream -Algorithm SHA256).Hash.ToLowerInvariant() }
        finally { $Stream.Dispose() }
    }
    finally { $Archive.Dispose() }
}

function Get-ReleaseInstallations{
    $VisualStudioRoot=Join-Path $env:LOCALAPPDATA 'Microsoft\VisualStudio'
    if(!(Test-Path -LiteralPath $VisualStudioRoot)){ return @() }
    $Results=foreach($Hive in @(Get-ChildItem -LiteralPath $VisualStudioRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like "*$RootSuffix" })){
        $ExtensionsDirectory=Join-Path $Hive.FullName 'Extensions'
        if(!(Test-Path -LiteralPath $ExtensionsDirectory)){ continue }
        foreach($Manifest in @(Get-ChildItem -LiteralPath $ExtensionsDirectory -Filter '*.vsixmanifest' -File -Recurse -ErrorAction SilentlyContinue)){
            try { $Identity=Get-ManifestIdentity $Manifest.FullName } catch { continue }
            if($Identity.Id -ne $ExtensionId){ continue }
            $Directory=Split-Path -Parent $Manifest.FullName
            $Assembly=Get-ChildItem -LiteralPath $Directory -Filter 'CSharpGameEngine.VSExtension.dll' -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
            [pscustomobject]@{
                Hive=$Hive.FullName
                Manifest=$Manifest.FullName
                Assembly=if($null -eq $Assembly){$null}else{$Assembly.FullName}
            }
        }
    }
    @($Results)
}

function Assert-ReleaseHiveNotRunning{
    $Escaped=[regex]::Escape($RootSuffix)
    $Running=@(Get-CimInstance Win32_Process -Filter "Name='devenv.exe'" |
        Where-Object { $_.CommandLine -match "(?i)(?:/|-)(?:rootSuffix|rootsuffix)(?:\s+|:)$Escaped(?:\s|$)" })
    if($Running.Count -ne 0){
        throw "Visual Studio /RootSuffix $RootSuffix is running: $($Running.ProcessId -join ',')."
    }
}

function Invoke-VsixInstaller([string]$Installer,[string[]]$Arguments,[string]$LogPath){
    $Started=[DateTimeOffset]::UtcNow
    $Process=Start-Process -FilePath $Installer -ArgumentList $Arguments -WindowStyle Hidden -Wait -PassThru
    $Line="$($Started.ToString('O')) exitCode=$($Process.ExitCode) arguments=$($Arguments -join ' ')"
    [IO.File]::AppendAllText($LogPath,$Line+[Environment]::NewLine,$Utf8NoBom)
    $Process.ExitCode
}

New-Item -ItemType Directory -Path $RunRoot -Force | Out-Null
$Installer=$null
$InstallExitCode=-1
$UninstallExitCode=-1
$InstalledDllHash=$null
$Failure=$null
try {
    Assert-ReleaseHiveNotRunning
    if(!$SkipBuild){
        & dotnet build $ProjectPath --configuration $Configuration --nologo
        if($LASTEXITCODE -ne 0){ throw "VSIX build failed with exit code $LASTEXITCODE." }
    }
    if(!(Test-Path -LiteralPath $PackagePath)){ throw "VSIX package was not found: $PackagePath" }
    if(!(Test-Path -LiteralPath $BuildDllPath)){ throw "VSIX build DLL was not found: $BuildDllPath" }
    if($Configuration -eq 'Release'){
        $FrozenPackageHash=(Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash.ToLowerInvariant()
        $FrozenBuildDllHash=(Get-FileHash -LiteralPath $BuildDllPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if($FrozenPackageHash -ne ([string]$ReleaseInput.packageSha256).ToLowerInvariant() -or
            $FrozenBuildDllHash -ne ([string]$ReleaseInput.buildDllSha256).ToLowerInvariant()){
            throw 'Frozen VSIX release input content does not match its declared hashes.'
        }
    }
    $Installer=Get-VsixInstallerPath
    $InstallResult=@(& $ReinstallScript -Configuration $Configuration -RootSuffix $RootSuffix -VsixPath $PackagePath -SkipBuild -LogPath $InstallLog)
    $InstallExitCode=0
    $Installations=@(Get-ReleaseInstallations)
    if($Installations.Count -ne 1){ throw "Expected one $RootSuffix installation, found $($Installations.Count)." }
    if([string]::IsNullOrWhiteSpace($Installations[0].Assembly)){ throw 'Installed extension DLL was not found.' }
    $PackageDllHash=Get-PackageDllHash $PackagePath
    $BuildDllHash=(Get-FileHash -LiteralPath $BuildDllPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $InstalledDllHash=(Get-FileHash -LiteralPath $Installations[0].Assembly -Algorithm SHA256).Hash.ToLowerInvariant()
    if($PackageDllHash -ne $BuildDllHash -or $BuildDllHash -ne $InstalledDllHash){
        throw "VSIX DLL provenance mismatch. package=$PackageDllHash build=$BuildDllHash installed=$InstalledDllHash"
    }
}
catch { $Failure=$_ }
finally {
    try {
        Assert-ReleaseHiveNotRunning
        if($null -eq $Installer){ $Installer=Get-VsixInstallerPath }
        $UninstallExitCode=Invoke-VsixInstaller $Installer @("/uninstall:$ExtensionId","/rootSuffix:$RootSuffix",'/quiet') $UninstallLog
        if($UninstallExitCode -ne 0){ throw "VSIX uninstall failed with exit code $UninstallExitCode." }
    }
    catch { if($null -eq $Failure){ $Failure=$_ } }
}

if($null -ne $Failure){ throw $Failure }
$Remaining=@(Get-ReleaseInstallations)
if($Remaining.Count -ne 0){ throw "VSIX uninstall left $($Remaining.Count) $RootSuffix installation(s)." }
$PackageHash=(Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash.ToLowerInvariant()
$PackageDllHash=Get-PackageDllHash $PackagePath
$BuildDllHash=(Get-FileHash -LiteralPath $BuildDllPath -Algorithm SHA256).Hash.ToLowerInvariant()
$FinishedUtc=[DateTimeOffset]::UtcNow
$Evidence=[ordered]@{
    schemaVersion='csharp-game-engine-vsix-lifecycle-v1'
    configuration=$Configuration
    extensionId=$ExtensionId
    rootSuffix=$RootSuffix
    startedUtc=$StartedUtc
    finishedUtc=$FinishedUtc
    packageSha256=$PackageHash
    packageDllSha256=$PackageDllHash
    buildDllSha256=$BuildDllHash
    installedDllSha256=$InstalledDllHash
    installExitCode=$InstallExitCode
    uninstallExitCode=$UninstallExitCode
    remainingInstallations=$Remaining.Count
    assertions=@(
        [ordered]@{id='package-build-provenance';passed=($PackageDllHash -eq $BuildDllHash);detail='Package DLL equals the build DLL.'}
        [ordered]@{id='installed-build-provenance';passed=($InstalledDllHash -eq $BuildDllHash);detail='Installed DLL equals the build DLL.'}
        [ordered]@{id='isolated-install';passed=($InstallExitCode -eq 0);detail='The exact package installed into CgeRelease.'}
        [ordered]@{id='isolated-uninstall';passed=($UninstallExitCode -eq 0 -and $Remaining.Count -eq 0);detail='CgeRelease contains no remaining installation.'}
    )
}
$Temporary=$EvidencePath+'.tmp'
try {
    [IO.File]::WriteAllText($Temporary,($Evidence|ConvertTo-Json -Depth 8)+[Environment]::NewLine,$Utf8NoBom)
    Move-Item -LiteralPath $Temporary -Destination $EvidencePath -Force
}
finally { if(Test-Path -LiteralPath $Temporary){ Remove-Item -LiteralPath $Temporary -Force } }
Write-Output $EvidencePath
