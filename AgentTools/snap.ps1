# Capture the game view from the Main Camera into Screenshots/<name>.png
# Usage: .\AgentTools\snap.ps1 <name> [width] [height]
param([string]$name = "shot", [int]$w = 1600, [int]$h = 900, [string]$src = "camera")
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$env:Path += ";C:\Users\proon\AppData\Local\Unity\bin"
Set-Location D:\UnityAIProjs\DigitalTwin\WeldingBot
$r = unity command capture_game_view --source $src --save_path "Screenshots/$name.png" --width $w --height $h --no-banner 2>&1 | Out-String
New-Item -ItemType Directory Screenshots -Force | Out-Null
if (Test-Path "Assets\Screenshots\$name.png") {
    Move-Item -Force "Assets\Screenshots\$name.png" "Screenshots\$name.png"
    Remove-Item -Recurse -Force "Assets\Screenshots", "Assets\Screenshots.meta" -ErrorAction SilentlyContinue
    "saved Screenshots\$name.png"
} else { $r }
