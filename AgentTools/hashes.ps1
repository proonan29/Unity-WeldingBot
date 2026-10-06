# Print short MD5 of every WeldingBot source file (to verify uploads).
Set-Location D:\UnityAIProjs\DigitalTwin\WeldingBot
Get-ChildItem Assets\WeldingBot -Recurse -Include *.cs,*.shader | Sort-Object FullName | ForEach-Object {
    (Get-FileHash -Algorithm MD5 $_.FullName).Hash.Substring(0, 8).ToLower() + " " + $_.Name
}
