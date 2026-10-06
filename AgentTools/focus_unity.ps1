# Bring the WeldingBot Unity Editor window to the foreground (needed for play mode to tick).
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class FG {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
}
"@
$p = Get-Process Unity | Where-Object { $_.MainWindowTitle -like "WeldingBot*" } | Select-Object -First 1
$h = $p.MainWindowHandle
if ([FG]::IsIconic($h)) { [FG]::ShowWindow($h, 9) | Out-Null }
# ALT tap lets this process pass the foreground lock
[FG]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
[FG]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
[FG]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 300
"foreground=$([FG]::GetForegroundWindow() -eq $h)"
