# ユーザーの UE5 project（G:\UE\5.0）にある Epic 自身のコンテンツから、game template の footstep、jump、landing sound を import します。
# 3つの source SoundWave asset は Epic の学習コースのコンテンツです（Northwood：GameplayDesigner の footsteps、ShooterGame の foley）。
# これらを参照用 project（G:\UE\5.8\FirstPerson）の /Game/CgeImportedSounds にコピーし、UnrealEditor-Cmd で WAV に export してから、
# stage に置いたコピーを削除します。同じ WAV 名を再生するすべての template に書き込むため、Assets.Sounds.* は変更されません。
# 使用方法：powershell -File tools\UnrealExport\import_template_sounds.ps1
$ErrorActionPreference = 'Stop'
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$Editor = 'C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
$Project = 'G:\UE\5.8\FirstPerson\FirstPerson.uproject'
$Stage = 'G:\UE\5.8\FirstPerson\Content\CgeImportedSounds'

# source asset file から WAV 名への対応。ファイル名で検索するため、localized path を script に埋め込みません。
$Sources = [ordered]@{ 'A_Footstep_Dirt01.uasset' = 'Footstep'; 'sw_Foley_Jump_01.uasset' = 'Jump'; 'sw_Foley_JumpLand_Tile_01.uasset' = 'Land' }
$Targets = @{
    'ThirdPerson' = @('Footstep', 'Jump', 'Land')
    'FirstPerson' = @('Footstep', 'Jump', 'Land')
    'TopDown'     = @('Footstep', 'Land')
}

New-Item -ItemType Directory -Force $Stage | Out-Null
$Exports = @()
$Temp = Join-Path $env:TEMP 'cge-sounds'
New-Item -ItemType Directory -Force $Temp | Out-Null
try {
    foreach ($File in $Sources.Keys) {
        $Found = Get-ChildItem 'G:\UE\5.0' -Recurse -File -Filter $File -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $Found) { throw "Source asset not found under G:\UE\5.0: $File" }
        $Name = $Sources[$File]
        # package は固有の名前を維持する必要があります。中の object 名はファイル名から決まります。
        Copy-Item $Found.FullName (Join-Path $Stage $File) -Force
        $Exports += @{ asset = "/Game/CgeImportedSounds/" + [IO.Path]::GetFileNameWithoutExtension($File); out = (Join-Path $Temp "$Name.wav") }
        Write-Host "staged $Name from $($Found.FullName.Substring(0, [Math]::Min(80, $Found.FullName.Length)))"
    }
    # Windows PowerShell 5.1 には ConvertTo-Json -AsArray がなく、1項目の配列も array のまま維持されません。
    $env:CGE_SOUND_EXPORTS = '[' + (($Exports | ForEach-Object { $_ | ConvertTo-Json -Compress }) -join ',') + ']'
    & $Editor $Project "-ExecutePythonScript=$PSScriptRoot\export_sound_waves.py" -EnablePlugins=PythonScriptPlugin -stdout -unattended -NoSplash -nosound -nullrhi | Out-Null
    foreach ($Template in $Targets.Keys) {
        $Dir = Join-Path $Repo "Templates\CSharpGameEngine.Templates.$Template\Assets\Sounds"
        New-Item -ItemType Directory -Force $Dir | Out-Null
        foreach ($Name in $Targets[$Template]) {
            $Wav = Join-Path $Temp "$Name.wav"
            if (-not (Test-Path $Wav)) { throw "The export wrote no $Name.wav; see G:\UE\5.8\FirstPerson\Saved\Logs\FirstPerson.log ([UnrealExport] lines)." }
            Copy-Item $Wav (Join-Path $Dir "$Name.wav") -Force
        }
    }
    Get-ChildItem $Temp -Filter *.wav | ForEach-Object { '{0} {1:N0} bytes' -f $_.Name, $_.Length }
}
finally {
    Remove-Item $Stage -Recurse -Force -ErrorAction SilentlyContinue
    $env:CGE_SOUND_EXPORTS = $null
}
