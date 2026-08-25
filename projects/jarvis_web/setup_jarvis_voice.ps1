$ErrorActionPreference = 'Stop'

$JarvisDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$VoiceDir = Join-Path $JarvisDir 'voice_models'
New-Item -ItemType Directory -Force -Path $VoiceDir | Out-Null

Write-Host ''
Write-Host 'JARVIS Romanian Neural Voice Setup' -ForegroundColor Cyan
Write-Host '----------------------------------' -ForegroundColor Cyan

python -m pip install --upgrade pip
python -m pip install -r (Join-Path $JarvisDir 'requirements-voice.txt')

$piper = Get-Command piper.exe -ErrorAction SilentlyContinue
if (-not $piper) { $piper = Get-Command piper -ErrorAction SilentlyContinue }
if (-not $piper) {
    throw 'Piper was not found on PATH after installation. Close/reopen the terminal and run this script again.'
}

$env:PIPER_BIN = $piper.Source
$env:PIPER_DATA_DIR = $VoiceDir
$env:JARVIS_TTS_MODEL = 'ro_RO-mihai-medium'

Write-Host "Piper: $($piper.Source)" -ForegroundColor Green
Write-Host 'Voice: ro_RO-mihai-medium' -ForegroundColor Green
Write-Host ''
Write-Host 'Downloading/initializing the Romanian voice through Piper...' -ForegroundColor Yellow

$testWav = Join-Path $VoiceDir 'voice_test.wav'
'Buna. Sunt JARVIS. Vocea mea ruleaza local.' | & $piper.Source --model 'ro_RO-mihai-medium' --data-dir $VoiceDir --output_file $testWav

if (-not (Test-Path $testWav)) {
    throw 'Piper did not generate the test WAV file.'
}
Remove-Item $testWav -Force -ErrorAction SilentlyContinue

$envFile = Join-Path $JarvisDir '.jarvis-voice.env.cmd'
Set-Content -Path $envFile -Encoding ASCII -Value @(
    '@echo off'
    "set PIPER_BIN=$($piper.Source)"
    "set PIPER_DATA_DIR=$VoiceDir"
    'set JARVIS_TTS_MODEL=ro_RO-mihai-medium'
)

Write-Host ''
Write-Host 'JARVIS Romanian neural voice is ready.' -ForegroundColor Green
Write-Host 'Restart start.bat before testing.' -ForegroundColor Cyan
