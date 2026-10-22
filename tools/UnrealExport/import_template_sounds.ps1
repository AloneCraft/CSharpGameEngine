# Imports the footstep, jump and landing sounds of the game templates from Epic's own content in the user's UE5 projects (G:\UE\5.0).
# The three source SoundWave assets are Epic learning-course content (Northwood: GameplayDesigner footsteps, ShooterGame foley);
# they are copied to /Game/CgeImportedSounds of a reference project (G:\UE\5.8\FirstPerson), exported as WAV through UnrealEditor-Cmd,
# and the staged copy is removed again. The same WAV names are written to every template that plays them, so Assets.Sounds.* is unchanged.
# Usage: powershell -File tools\UnrealExport\import_template_sounds.ps1
$ErrorActionPreference = 'Stop'
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$Editor = 'C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
$Project = 'G:\UE\5.8\FirstPerson\FirstPerson.uproject'
$Stage = 'G:\UE\5.8\FirstPerson\Content\CgeImportedSounds'

# source asset file -> WAV name. Found by file name so that this script holds no localized path.
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
        # The package must keep its own name: the object inside is named after the file.
        Copy-Item $Found.FullName (Join-Path $Stage $File) -Force
        $Exports += @{ asset = "/Game/CgeImportedSounds/" + [IO.Path]::GetFileNameWithoutExtension($File); out = (Join-Path $Temp "$Name.wav") }
        Write-Host "staged $Name from $($Found.FullName.Substring(0, [Math]::Min(80, $Found.FullName.Length)))"
    }
    # Windows PowerShell 5.1 has no ConvertTo-Json -AsArray, and a one-element array would not stay an array.
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
