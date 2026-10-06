# Compile-check all WeldingBot scripts in memory (no domain reload) before letting Unity import them.
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$env:Path += ";C:\Users\proon\AppData\Local\Unity\bin"
Set-Location D:\UnityAIProjs\DigitalTwin\WeldingBot
$files = Get-ChildItem Assets\WeldingBot -Recurse -Filter *.cs
$usings = @(); $bodies = @()
foreach ($f in $files) {
    $lines = Get-Content -Encoding UTF8 $f.FullName
    $usings += $lines | Where-Object { $_ -match '^\s*using [\w\.]+;\s*$' } | ForEach-Object { $_.Trim() }
    $bodies += ($lines | Where-Object { $_ -notmatch '^\s*using [\w\.]+;\s*$' }) -join "`n"
}
$all = (($usings | Sort-Object -Unique) -join "`n") + "`n" + ($bodies -join "`n") + "`npublic static class __DryRunEntry { public static void Run(){} }"
Set-Content -Encoding utf8 AgentTools\_combined.cs $all
$r = unity command run_script --file AgentTools/_combined.cs --entry __DryRunEntry.Run --dry_run true --no-banner --json 2>&1 | Out-String | ConvertFrom-Json
$r.data.result.diagnostics | Where-Object { $_.id -ne "CS0436" } | ForEach-Object { "$($_.severity) $($_.id) L$($_.line): $($_.message) :: $($_.source)" }
"dryrun success=$($r.data.result.success)"
