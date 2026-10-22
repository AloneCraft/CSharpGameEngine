# UEのゲーム系テンプレート(Blueprint版)から、UE5.8で開けるプロジェクトを作る。
# UEの「新規プロジェクト」と同じく、テンプレートのConfig/ContentにTemplateDefs.iniのSharedContentPacksを
# Content/<MountName>/へ展開し、<ProjectName>.uprojectを書く。UE5.8のインストールにはTemplatesが無い(確認: 2026-10-22)ため、
# 既定ではUE5.7のTemplatesを元にする。5.7のアセットは5.8のエディターで開くと読み込み時に5.8へ上げられる(保存するまで元は変わらない)。
# Program Files配下へは一切書き込まない。
param(
    [Parameter(Mandatory=$true)][ValidateSet("FirstPerson","TopDown","Vehicle")][string]$Name,
    [string]$TemplatesRoot="C:\Program Files\Epic Games\UE_5.7\Templates",
    [string]$OutputRoot="G:\UE\5.8",
    [string]$EngineAssociation="5.8",
    [switch]$Force
)
$ErrorActionPreference="Stop"
# UEの表示名(UEの新規プロジェクトの一覧の名前)と同じ名前をプロジェクト名にする。元のテンプレートとその共有パックはTemplateDefs.iniの記述どおり。
$Definitions=@{
    FirstPerson=@{ Template="TP_FirstPersonBP";  Packs=@(@("Characters","High"),@("LevelPrototyping","High"),@("Input","High")) }
    TopDown=@{     Template="TP_TopDownBP";      Packs=@(@("LevelPrototyping","High"),@("Characters","High"),@("Cursor","Standard")) }
    Vehicle=@{     Template="TP_VehicleAdvBP";   Packs=@(@("Vehicles","Standard"),@("Input","High")) }
}
$Definition=$Definitions[$Name]
$Template=Join-Path $TemplatesRoot $Definition.Template
if(-not (Test-Path (Join-Path $Template "$($Definition.Template).uproject"))){
    throw "The UE template was not found: $Template"
}
$Project=Join-Path $OutputRoot $Name
if((Test-Path $Project) -and -not $Force){
    throw "$Project already exists. Pass -Force to rebuild it."
}
New-Item -ItemType Directory -Force $Project | Out-Null
function Copy-Folder([string]$From,[string]$To,[string[]]$ExcludeFiles=@()){
    $Arguments=@($From,$To,"/MIR","/NFL","/NDL","/NJH","/NJS","/NP")
    if($ExcludeFiles.Count -gt 0){ $Arguments+=@("/XF")+$ExcludeFiles }
    robocopy @Arguments | Out-Null
    if($LASTEXITCODE -ge 8){ throw "robocopy failed from $From with exit code $LASTEXITCODE" }
}
# TemplateDefs.ini の FilesToIgnore: Config/TemplateDefs.ini と Config/config.ini はプロジェクトへ入れない。
Copy-Folder (Join-Path $Template "Config") (Join-Path $Project "Config") @("TemplateDefs.ini","config.ini")
Copy-Folder (Join-Path $Template "Content") (Join-Path $Project "Content")
foreach($Pack in $Definition.Packs){
    $Source=Join-Path $TemplatesRoot "TemplateResources\$($Pack[1])\$($Pack[0])\Content"
    if(-not (Test-Path $Source)){ throw "The shared content pack was not found: $Source" }
    Copy-Folder $Source (Join-Path $Project "Content\$($Pack[0])") @("manifest.json")
}
# uprojectはテンプレートのものを写し、EngineAssociationだけ書き換える(テンプレートでは空)。
$Uproject=Get-Content (Join-Path $Template "$($Definition.Template).uproject") -Raw | ConvertFrom-Json
$Uproject.EngineAssociation=$EngineAssociation
$Json=$Uproject | ConvertTo-Json -Depth 10
[System.IO.File]::WriteAllText((Join-Path $Project "$Name.uproject"),$Json,(New-Object System.Text.UTF8Encoding($false)))
$global:LASTEXITCODE=0
$Files=Get-ChildItem $Project -Recurse -File
"{0}: {1} files, {2:N1} MB" -f (Join-Path $Project "$Name.uproject"),$Files.Count,(($Files | Measure-Object Length -Sum).Sum/1MB)
