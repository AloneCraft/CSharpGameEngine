[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$RepositoryRoot=Split-Path -Parent $PSScriptRoot
$ProjectPath=Join-Path $RepositoryRoot 'tools\CSharpGameEngine.VSExtension\CSharpGameEngine.VSExtension.csproj'
$SolutionPath=Join-Path $RepositoryRoot 'UserEcsProject.slnx'
$VsWherePath=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if(!(Test-Path -LiteralPath $VsWherePath)){
    throw "Visual Studio の検出ツールが見つかりません: $VsWherePath"
}
$InstallationPath=& $VsWherePath -latest -products Microsoft.VisualStudio.Product.Community -requires Microsoft.Component.MSBuild -property installationPath
if([string]::IsNullOrWhiteSpace($InstallationPath)){
    throw 'MSBuild を含む Visual Studio Community が見つかりません。'
}
$MsBuildPath=Join-Path $InstallationPath 'MSBuild\Current\Bin\MSBuild.exe'
$VsixInstallerPath=Join-Path $InstallationPath 'Common7\IDE\VSIXInstaller.exe'
$DevEnvPath=Join-Path $InstallationPath 'Common7\IDE\devenv.exe'
foreach($RequiredPath in @($ProjectPath,$SolutionPath,$MsBuildPath,$VsixInstallerPath,$DevEnvPath)){
    if(!(Test-Path -LiteralPath $RequiredPath)){
        throw "必要なファイルが見つかりません: $RequiredPath"
    }
}
& $MsBuildPath $ProjectPath '/t:Rebuild' '/p:Configuration=Debug' '/p:Platform=x64' '/m'
if($LASTEXITCODE -ne 0){
    throw "VSIX の再ビルドに失敗しました。終了コード: $LASTEXITCODE"
}
$VsixDirectory=Join-Path $RepositoryRoot 'tools\CSharpGameEngine.VSExtension\bin\x64\Debug\net472'
$Vsix=Get-ChildItem -LiteralPath $VsixDirectory -Filter '*.vsix' -File | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
if($null -eq $Vsix){
    throw "再ビルド後の VSIX が見つかりません: $VsixDirectory"
}
& $VsixInstallerPath '/rootSuffix:CgeFixed' $Vsix.FullName
if($LASTEXITCODE -ne 0){
    throw "VSIX のインストールに失敗しました。終了コード: $LASTEXITCODE"
}
Start-Process -FilePath $DevEnvPath -ArgumentList @('/rootsuffix','CgeFixed',$SolutionPath)
