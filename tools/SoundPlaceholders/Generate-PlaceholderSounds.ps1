# ADR-0077：ThirdPerson template の仮サウンド効果音3種類（Jump、Land、Footstep）を生成します。
# UE ThirdPerson template には音声が含まれないため、これは Unreal のコンテンツではなく決定論的な音です。.wav ファイルを
# 同じ名前の実際の効果音に置き換えてください。ゲームは見つかった PCM .wav（8/16/24-bit または float、mono または stereo）を再生します。
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\Templates\CSharpGameEngine.Templates.ThirdPerson\Assets\Sounds')
)
$ErrorActionPreference = 'Stop'
$Rate = 22050
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

function Write-Wav([string]$Path, [int16[]]$Samples) {
    $Stream = [System.IO.File]::Create($Path)
    $Writer = New-Object System.IO.BinaryWriter($Stream)
    try {
        $Writer.Write([System.Text.Encoding]::ASCII.GetBytes('RIFF'))
        $Writer.Write([int](36 + $Samples.Length * 2))
        $Writer.Write([System.Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
        $Writer.Write([int]16)
        $Writer.Write([int16]1)
        $Writer.Write([int16]1)
        $Writer.Write([int]$Rate)
        $Writer.Write([int]($Rate * 2))
        $Writer.Write([int16]2)
        $Writer.Write([int16]16)
        $Writer.Write([System.Text.Encoding]::ASCII.GetBytes('data'))
        $Writer.Write([int]($Samples.Length * 2))
        foreach ($Sample in $Samples) { $Writer.Write([int16]$Sample) }
    }
    finally { $Writer.Dispose(); $Stream.Dispose() }
}

function New-Samples([double]$Seconds, [scriptblock]$Shape) {
    $Count = [int]($Seconds * $Rate)
    $Samples = New-Object 'int16[]' $Count
    for ($Index = 0; $Index -lt $Count; $Index++) {
        $Time = $Index / $Rate
        $Value = & $Shape $Time ($Index / [double]$Count)
        $Samples[$Index] = [int16][Math]::Round([Math]::Max(-1.0, [Math]::Min(1.0, $Value)) * 32000)
    }
    return $Samples
}

# Jump：短く上昇する chirp。
$Phase = 0.0
Write-Wav (Join-Path $OutputDirectory 'Jump.wav') (New-Samples 0.2 {
    param($Time, $Progress)
    $Frequency = 260 + 260 * $Progress
    $script:Phase += 2 * [Math]::PI * $Frequency / $Rate
    0.5 * [Math]::Exp(-5 * $Progress) * [Math]::Sin($script:Phase)
})

# Land：低い thump と noise の burst。
$Random = New-Object System.Random 1
$Phase = 0.0
Write-Wav (Join-Path $OutputDirectory 'Land.wav') (New-Samples 0.18 {
    param($Time, $Progress)
    $script:Phase += 2 * [Math]::PI * (90 - 40 * $Progress) / $Rate
    $Noise = ($Random.NextDouble() * 2 - 1)
    (0.7 * [Math]::Sin($script:Phase) + 0.25 * $Noise) * [Math]::Exp(-7 * $Progress)
})

# Footstep：短く柔らかな noise burst。
$Random = New-Object System.Random 2
$Low = 0.0
Write-Wav (Join-Path $OutputDirectory 'Footstep.wav') (New-Samples 0.07 {
    param($Time, $Progress)
    $script:Low += 0.35 * (($Random.NextDouble() * 2 - 1) - $script:Low)
    0.6 * $script:Low * [Math]::Exp(-9 * $Progress)
})

Get-ChildItem $OutputDirectory -Filter *.wav | Select-Object Name, Length
