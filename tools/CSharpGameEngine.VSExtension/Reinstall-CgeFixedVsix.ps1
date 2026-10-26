[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration='Debug',
    [ValidatePattern('^[A-Za-z][A-Za-z0-9]{0,31}$')]
    [string]$RootSuffix='CgeFixed',
    [string]$VsixPath,
    [switch]$SkipBuild,
    [string]$LogPath
)

$ErrorActionPreference='Stop'
$Utf8NoBom=[Text.UTF8Encoding]::new($false)
[Console]::InputEncoding=$Utf8NoBom
[Console]::OutputEncoding=$Utf8NoBom
$OutputEncoding=$Utf8NoBom
$PSDefaultParameterValues['Out-File:Encoding']='utf8'
$PSDefaultParameterValues['Set-Content:Encoding']='utf8'
$PSDefaultParameterValues['Add-Content:Encoding']='utf8'
$ExtensionId='CSharpGameEngine.VSExtension.aec17804-e2e4-46e7-b373-de11af816203'
$ScriptDirectory=Split-Path -Parent $PSCommandPath
$ProjectPath=Join-Path $ScriptDirectory 'CSharpGameEngine.VSExtension.csproj'
$Timestamp=Get-Date -Format 'yyyyMMdd-HHmmss'

if([string]::IsNullOrWhiteSpace($LogPath)){
    $LogDirectory=Join-Path $env:LOCALAPPDATA 'CSharpGameEngine\Logs'
    New-Item -ItemType Directory -Path $LogDirectory -Force | Out-Null
    $LogPath=Join-Path $LogDirectory "CgeFixed-VsixMaintenance-$Timestamp.log"
}
else {
    $LogDirectory=Split-Path -Parent $LogPath
    if(![string]::IsNullOrWhiteSpace($LogDirectory)){
        New-Item -ItemType Directory -Path $LogDirectory -Force | Out-Null
    }
}

function Write-Log([string]$Message){
    $Line="$(Get-Date -Format 'O') $Message"
    Write-Host $Line
    Add-Content -LiteralPath $LogPath -Value $Line
}

function Get-VsixManifestIdentity([string]$ManifestPath){
    [xml]$Manifest=[IO.File]::ReadAllText($ManifestPath,$Utf8NoBom)
    $Namespace=New-Object System.Xml.XmlNamespaceManager($Manifest.NameTable)
    $Namespace.AddNamespace('vsix','http://schemas.microsoft.com/developer/vsx-schema/2011')
    $Identity=$Manifest.SelectSingleNode('/vsix:PackageManifest/vsix:Metadata/vsix:Identity',$Namespace)
    if($null -eq $Identity){ throw "VSIX manifest Identity was not found: $ManifestPath" }
    return [pscustomobject]@{ Id=[string]$Identity.Id; Version=[string]$Identity.Version }
}

function Get-EmbeddedWpfBamlNames([string]$AssemblyPath){
    $Assembly=[Reflection.Assembly]::LoadFrom($AssemblyPath)
    $ResourceName=$Assembly.GetManifestResourceNames() |
        Where-Object { $_ -like '*.g.resources' } |
        Select-Object -First 1
    if([string]::IsNullOrWhiteSpace($ResourceName)){
        throw "VSIX assembly has no g.resources resource: $AssemblyPath"
    }

    $Stream=$Assembly.GetManifestResourceStream($ResourceName)
    if($null -eq $Stream){ throw "Unable to open g.resources resource '$ResourceName' in $AssemblyPath" }
    $Reader=New-Object System.Resources.ResourceReader($Stream)
    try {
        $Names=foreach($Entry in $Reader){
            $Name=[string]$Entry.Key
            if($Name -like '*.baml'){ $Name.ToLowerInvariant() }
        }
        return @($Names)
    }
    finally {
        $Reader.Close()
        $Stream.Close()
    }
}

function Assert-WpfBamlResources([string]$AssemblyPath){
    $Expected=@(Get-ChildItem -LiteralPath $ScriptDirectory -Filter '*.xaml' -File |
        ForEach-Object { $_.BaseName.ToLowerInvariant()+'.baml' })
    $Actual=@(Get-EmbeddedWpfBamlNames $AssemblyPath)
    $Missing=@($Expected | Where-Object { $Actual -notcontains $_ })
    if($Missing.Count -ne 0){
        throw "VSIX assembly is missing WPF BAML resources for XAML: $($Missing -join ', ')"
    }
}

function Get-VsixAssemblyHash([string]$PackagePath){
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $Archive=[IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $Entry=$Archive.Entries |
            Where-Object { $_.FullName -like '*CSharpGameEngine.VSExtension.dll' } |
            Select-Object -First 1
        if($null -eq $Entry){ throw "VSIX package does not contain CSharpGameEngine.VSExtension.dll: $PackagePath" }
        $Stream=$Entry.Open()
        try { return (Get-FileHash -InputStream $Stream -Algorithm SHA256).Hash }
        finally { $Stream.Dispose() }
    }
    finally { $Archive.Dispose() }
}

function Get-VsixInstallerPath{
    $VsWhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if(Test-Path -LiteralPath $VsWhere){
        $InstallPaths=@(& $VsWhere -products '*' -requires Microsoft.VisualStudio.Component.CoreEditor -property installationPath)
        foreach($InstallPath in $InstallPaths){
            $Candidate=Join-Path $InstallPath 'Common7\IDE\VSIXInstaller.exe'
            if(Test-Path -LiteralPath $Candidate){ return $Candidate }
        }
    }

    $Candidates=Get-ChildItem -Path (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio') -Filter VSIXInstaller.exe -Recurse -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty FullName
    if($Candidates.Count -eq 1){ return $Candidates[0] }
    if($Candidates.Count -gt 1){ throw 'Multiple VSIXInstaller.exe files were found. Install vswhere or select one installation.' }
    throw 'VSIXInstaller.exe was not found. Install Visual Studio first.'
}

function Assert-CgeFixedIsNotRunning{
    $EscapedRootSuffix=[regex]::Escape($RootSuffix)
    $Running=Get-CimInstance Win32_Process -Filter "Name='devenv.exe'" |
        Where-Object { $_.CommandLine -match "(?i)(?:/|-)(?:rootSuffix|rootsuffix)(?:\s+|:)$EscapedRootSuffix(?:\s|$)" }
    if($Running){
        $Processes=($Running | ForEach-Object { "PID $($_.ProcessId): $($_.CommandLine)" }) -join [Environment]::NewLine
        throw "Visual Studio /rootSuffix $RootSuffix is running. Close it before updating.`n$Processes"
    }
}

function Get-CgeFixedHives{
    $VisualStudioRoot=Join-Path $env:LOCALAPPDATA 'Microsoft\VisualStudio'
    if(!(Test-Path -LiteralPath $VisualStudioRoot)){ return @() }
    return @(Get-ChildItem -LiteralPath $VisualStudioRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like "*$RootSuffix" -and (Test-Path -LiteralPath (Join-Path $_.FullName 'Extensions')) })
}

function Get-CgeFixedInstallations([object[]]$Hives){
    $Matches=foreach($Hive in $Hives){
        $ExtensionsDirectory=Join-Path $Hive.FullName 'Extensions'
        # ForEach-Object 内で return を使わないでください。無関係な extension manifest に遭遇した時点で関数を終了し、
        # 有効な C# Game Engine VSIX が存在しないように見せてしまう可能性があります。
        # 通常の foreach を使い、各 manifest の検索をこの hive 内に留めます。
        foreach($Manifest in @(Get-ChildItem -LiteralPath $ExtensionsDirectory -Filter '*.vsixmanifest' -File -Recurse -ErrorAction SilentlyContinue)){
            try { $Identity=Get-VsixManifestIdentity $Manifest.FullName }
            catch { Write-Log "Ignoring unreadable extension manifest: $($Manifest.FullName)"; continue }
            if($Identity.Id -ne $ExtensionId){ continue }
            $Directory=Split-Path -Parent $Manifest.FullName
            $Assembly=Get-ChildItem -LiteralPath $Directory -Filter 'CSharpGameEngine.VSExtension.dll' -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
            [pscustomobject]@{
                Hive=$Hive.FullName
                Manifest=$Manifest.FullName
                Directory=$Directory
                Version=$Identity.Version
                Assembly=if($null -eq $Assembly){$null}else{$Assembly.FullName}
            }
        }
    }
    return @($Matches)
}

function Get-CgeFixedLegacyDirectories([object[]]$Hives){
    $Directories=foreach($Hive in $Hives){
        $ExtensionsDirectory=Join-Path $Hive.FullName 'Extensions'
        Get-ChildItem -LiteralPath $ExtensionsDirectory -Filter 'CSharpGameEngine.VSExtension.dll' -File -Recurse -ErrorAction SilentlyContinue |
            ForEach-Object {
                $Directory=$_.Directory.FullName
                if(!(Test-Path -LiteralPath (Join-Path $Directory 'extension.vsixmanifest'))){
                    [pscustomobject]@{ Hive=$Hive.FullName; Directory=$Directory }
                }
            }
    }
    return @($Directories | Sort-Object Hive,Directory -Unique)
}

function Get-PathWithin([string]$Path,[string]$Root){
    $ResolvedPath=(Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path.TrimEnd('\')
    $ResolvedRoot=(Resolve-Path -LiteralPath $Root -ErrorAction Stop).Path.TrimEnd('\')
    if(!$ResolvedPath.StartsWith($ResolvedRoot+'\',[System.StringComparison]::OrdinalIgnoreCase)){
        throw "Refusing to move a path outside the CgeFixed hive. Path=$ResolvedPath Root=$ResolvedRoot"
    }
    return $ResolvedPath
}

function Move-CgeFixedPathToBackup([string]$Path,[string]$Hive,[string]$BackupDirectory){
    if(!(Test-Path -LiteralPath $Path)){ return }
    $ExtensionsDirectory=Join-Path $Hive 'Extensions'
    $Source=Get-PathWithin $Path $ExtensionsDirectory
    $Leaf=Split-Path -Leaf $Source
    $Destination=Join-Path $BackupDirectory "Extensions-$Leaf"
    $Ordinal=1
    while(Test-Path -LiteralPath $Destination){
        $Destination=Join-Path $BackupDirectory "Extensions-$Leaf-$Ordinal"
        $Ordinal++
    }
    Move-Item -LiteralPath $Source -Destination $Destination -ErrorAction Stop
    Write-Log "Backed up extension directory: $Source -> $Destination"
}

function Move-CgeFixedHiveFileToBackup([string]$Path,[string]$Hive,[string]$BackupDirectory){
    if(!(Test-Path -LiteralPath $Path)){ return }
    $Source=Get-PathWithin $Path $Hive
    $Leaf=Split-Path -Leaf $Source
    $Destination=Join-Path $BackupDirectory $Leaf
    $Ordinal=1
    while(Test-Path -LiteralPath $Destination){
        $Destination=Join-Path $BackupDirectory "$Leaf-$Ordinal"
        $Ordinal++
    }
    Move-Item -LiteralPath $Source -Destination $Destination -ErrorAction Stop
    Write-Log "Backed up hive extension metadata: $Source -> $Destination"
}

function Backup-CgeFixedState([object[]]$Hives,[object[]]$Installations,[object[]]$LegacyDirectories){
    foreach($Hive in $Hives){
        $BackupDirectory=Join-Path $Hive.FullName "CSharpGameEngine.VSExtension.Backups\$Timestamp"
        New-Item -ItemType Directory -Path $BackupDirectory -Force | Out-Null
        $Directories=@($Installations | Where-Object { $_.Hive -eq $Hive.FullName } | ForEach-Object { $_.Directory })+
            @($LegacyDirectories | Where-Object { $_.Hive -eq $Hive.FullName } | ForEach-Object { $_.Directory }) |
            Sort-Object -Unique
        foreach($Directory in $Directories){
            Move-CgeFixedPathToBackup $Directory $Hive.FullName $BackupDirectory
        }

        $ComponentModelCache=Join-Path $Hive.FullName 'ComponentModelCache'
        if(Test-Path -LiteralPath $ComponentModelCache){
            $CacheSource=Get-PathWithin $ComponentModelCache $Hive.FullName
            $CacheBackup=Join-Path $BackupDirectory 'ComponentModelCache'
            Move-Item -LiteralPath $CacheSource -Destination $CacheBackup -ErrorAction Stop
            Write-Log "Backed up ComponentModelCache: $CacheSource -> $CacheBackup"
        }

        Get-ChildItem -LiteralPath $Hive.FullName -Filter 'ExtensionMetadata*.mpack' -File -ErrorAction SilentlyContinue |
            ForEach-Object { Move-CgeFixedHiveFileToBackup $_.FullName $Hive.FullName $BackupDirectory }
        Move-CgeFixedHiveFileToBackup (Join-Path $Hive.FullName 'extensions.configurationchanged') $Hive.FullName $BackupDirectory
    }
}

function Invoke-VsixInstaller([string]$Installer,[string[]]$Arguments,[bool]$AllowFailure=$false){
    $Process=Start-Process -FilePath $Installer -ArgumentList $Arguments -WindowStyle Hidden -Wait -PassThru
    if($Process.ExitCode -ne 0 -and !$AllowFailure){
        throw "VSIXInstaller failed (exit code $($Process.ExitCode)): $($Arguments -join ' ')"
    }
    return $Process.ExitCode
}

if([string]::IsNullOrWhiteSpace($VsixPath)){
    $VsixPath=Join-Path $ScriptDirectory "bin\$Configuration\net472\CSharpGameEngine.VSExtension.vsix"
}

Assert-CgeFixedIsNotRunning
if(!$SkipBuild){
    & dotnet build $ProjectPath --configuration $Configuration -t:Rebuild
    if($LASTEXITCODE -ne 0){ throw "VSIX build failed (exit code $LASTEXITCODE)." }
}

$VsixPath=[IO.Path]::GetFullPath($VsixPath)
if(!(Test-Path -LiteralPath $VsixPath)){ throw "VSIX package was not found: $VsixPath" }
$SourceManifest=Join-Path $ScriptDirectory 'source.extension.vsixmanifest'
$SourceIdentity=Get-VsixManifestIdentity $SourceManifest
if($SourceIdentity.Id -ne $ExtensionId){
    throw "Source manifest must use extension ID $ExtensionId, but is $($SourceIdentity.Id)."
}
$ExpectedVersion=$SourceIdentity.Version
if([string]::IsNullOrWhiteSpace($ExpectedVersion)){ throw "Source manifest has no VSIX version: $SourceManifest" }
$BuildAssembly=Join-Path (Split-Path -Parent $VsixPath) 'CSharpGameEngine.VSExtension.dll'
if(!(Test-Path -LiteralPath $BuildAssembly)){ throw "Build assembly was not found beside the VSIX: $BuildAssembly" }
Assert-WpfBamlResources $BuildAssembly
$BuildHash=(Get-FileHash -LiteralPath $BuildAssembly -Algorithm SHA256).Hash
$PackageHash=Get-VsixAssemblyHash $VsixPath
if($PackageHash -ne $BuildHash){
    throw "VSIX package DLL SHA-256 does not match build output. Package=$PackageHash Build=$BuildHash. Rebuild the VSIX before deployment."
}
# PowerShell は関数出力が1項目だけだと scalar に展開します。検出結果を常に array にし、単一項目の場合も Count=1 を保ちます。
$Hives=@(Get-CgeFixedHives)
$Installed=@(Get-CgeFixedInstallations $Hives)
$Legacy=@(Get-CgeFixedLegacyDirectories $Hives)
$Current=$Installed.Count -eq 1 -and $Installed[0].Version -eq $ExpectedVersion -and
    $Installed[0].Assembly -and (Get-FileHash -LiteralPath $Installed[0].Assembly -Algorithm SHA256).Hash -eq $BuildHash -and
    $Legacy.Count -eq 0

if($Current){
    Write-Log "VSIX deployment is current (version=$ExpectedVersion hash=$BuildHash). Caches were not changed."
    [pscustomobject]@{ DeploymentChanged=$false; LogPath=$LogPath; BuildHash=$BuildHash; InstalledAssembly=$Installed[0].Assembly }
    return
}

Write-Log "VSIX deployment changed or is inconsistent. Installed=$($Installed.Count) Legacy=$($Legacy.Count) BuildHash=$BuildHash."
Backup-CgeFixedState $Hives $Installed $Legacy
$Installer=Get-VsixInstallerPath
Write-Log "Uninstalling $ExtensionId from /rootSuffix $RootSuffix."
$UninstallExitCode=Invoke-VsixInstaller $Installer @("/uninstall:$ExtensionId", "/rootSuffix:$RootSuffix", '/quiet') $true
if($UninstallExitCode -ne 0){ Write-Log "Uninstall returned exit code $UninstallExitCode; continuing with installation." }
Write-Log "Installing $VsixPath into /rootSuffix $RootSuffix."
[void](Invoke-VsixInstaller $Installer @($VsixPath, "/rootSuffix:$RootSuffix", '/quiet'))

$Hives=@(Get-CgeFixedHives)
$Installed=@(Get-CgeFixedInstallations $Hives)
$Legacy=@(Get-CgeFixedLegacyDirectories $Hives)
if($Installed.Count -ne 1){ throw "Expected exactly one $ExtensionId installation, found $($Installed.Count)." }
if($Installed[0].Version -ne $ExpectedVersion){ throw "Installed version is $($Installed[0].Version), expected $ExpectedVersion." }
if([string]::IsNullOrWhiteSpace($Installed[0].Assembly)){ throw "Installed extension assembly was not found under $($Installed[0].Directory)." }
Assert-WpfBamlResources $Installed[0].Assembly
$InstalledHash=(Get-FileHash -LiteralPath $Installed[0].Assembly -Algorithm SHA256).Hash
if($InstalledHash -ne $BuildHash){ throw "Installed DLL SHA-256 does not match build output. Installed=$InstalledHash Build=$BuildHash" }
if($Legacy.Count -ne 0){ throw "Legacy CSharpGameEngine extension directories remain after installation: $($Legacy.Directory -join ', ')" }

$Devenv=Join-Path (Split-Path -Parent $Installer) 'devenv.exe'
if(Test-Path -LiteralPath $Devenv){
    Write-Log 'Refreshing item-template cache because the VSIX deployment changed.'
    $TemplateProcess=Start-Process -FilePath $Devenv -ArgumentList '/RootSuffix',$RootSuffix,'/InstallVSTemplates','/Log',$LogPath -WindowStyle Hidden -Wait -PassThru
    if($TemplateProcess.ExitCode -ne 0){ throw "Visual Studio item-template cache rebuild failed (exit code $($TemplateProcess.ExitCode))." }
}

Write-Log "Verified CgeFixed VSIX $ExpectedVersion at $($Installed[0].Assembly) with SHA-256 $InstalledHash."
[pscustomobject]@{ DeploymentChanged=$true; LogPath=$LogPath; BuildHash=$BuildHash; InstalledAssembly=$Installed[0].Assembly }
