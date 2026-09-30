# UE5.8のThirdPerson(BP)テンプレートから、エクスポート専用のステージングUEプロジェクトを組み立てる。
# UEの「新規プロジェクト」と同じく、TemplateDefs.iniのSharedContentPacks(LevelPrototyping/Characters/Input)を
# Content/<MountName>/へ展開する。Program Files配下へは一切書き込まない。
param(
    [string]$EngineRoot="C:\Program Files\Epic Games\UE_5.8",
    [string]$StagingRoot=(Join-Path $PSScriptRoot "..\..\.artifacts\UnrealExport\TP_ThirdPerson")
)
$ErrorActionPreference="Stop"
$Templates=Join-Path $EngineRoot "Templates"
$Template=Join-Path $Templates "TP_ThirdPersonBP"
$Packs=Join-Path $Templates "TemplateResources\High"
if(-not (Test-Path (Join-Path $Template "TP_ThirdPersonBP.uproject"))){
    throw "UE5.8 ThirdPerson BP template was not found: $Template"
}
$StagingRoot=[System.IO.Path]::GetFullPath($StagingRoot)
New-Item -ItemType Directory -Force $StagingRoot | Out-Null
foreach($Folder in @("Config","Content")){
    robocopy (Join-Path $Template $Folder) (Join-Path $StagingRoot $Folder) /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    if($LASTEXITCODE -ge 8){ throw "robocopy failed for $Folder with exit code $LASTEXITCODE" }
}
foreach($Pack in @("LevelPrototyping","Characters","Input")){
    $Source=Join-Path $Packs "$Pack\Content"
    $Destination=Join-Path $StagingRoot "Content\$Pack"
    robocopy $Source $Destination /MIR /XF manifest.json /NFL /NDL /NJH /NJS /NP | Out-Null
    if($LASTEXITCODE -ge 8){ throw "robocopy failed for $Pack with exit code $LASTEXITCODE" }
}
Copy-Item (Join-Path $Template "TP_ThirdPersonBP.uproject") (Join-Path $StagingRoot "TP_ThirdPerson.uproject") -Force
$global:LASTEXITCODE=0
Write-Output (Join-Path $StagingRoot "TP_ThirdPerson.uproject")
