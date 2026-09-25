param([string]$Exe, [int]$Seconds = 20)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class W {
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowW(string cls, string title);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
}
'@
$settings = Join-Path $env:APPDATA 'MouseGlowTrail\settings.json'
$hadSettings = Test-Path $settings
$proc = Start-Process -FilePath $Exe -PassThru
Start-Sleep -Seconds 3   # let startup JIT and the welcome ripple settle
$overlay = [W]::FindWindowW('MouseGlowTrail.Overlay', [NullString]::Value)
$tray = [W]::FindWindowW('MouseGlowTrail.Tray', [NullString]::Value)
$hiddenCpu = 0.0; $hiddenWall = 0.0; $visibleCpu = 0.0; $visibleWall = 0.0; $visibleSlices = 0
$proc.Refresh(); $lastCpu = $proc.TotalProcessorTime; $last = [DateTime]::UtcNow
for ($i = 0; $i -lt $Seconds * 4; $i++) {
  $visibleDuring = $false
  for ($k = 0; $k -lt 5; $k++) { Start-Sleep -Milliseconds 50; if ([W]::IsWindowVisible($overlay)) { $visibleDuring = $true } }
  $proc.Refresh(); $cpu = $proc.TotalProcessorTime; $now = [DateTime]::UtcNow
  $dc = ($cpu - $lastCpu).TotalMilliseconds; $dw = ($now - $last).TotalMilliseconds
  if ($visibleDuring) { $visibleCpu += $dc; $visibleWall += $dw; $visibleSlices++ } else { $hiddenCpu += $dc; $hiddenWall += $dw }
  $lastCpu = $cpu; $last = $now
}
if ($hiddenWall -gt 0) { "hidden (idle): {0:0.0} s observed, {1:0.000}% of one core" -f ($hiddenWall / 1000), (100 * $hiddenCpu / $hiddenWall) }
if ($visibleWall -gt 0) { "trail on screen: {0:0.0} s observed, {1:0.00}% of one core (includes pointer motion)" -f ($visibleWall / 1000), (100 * $visibleCpu / $visibleWall) }
"working set: {0:0.0} MB   private: {1:0.0} MB" -f ($proc.WorkingSet64 / 1MB), ($proc.PrivateMemorySize64 / 1MB)
[W]::PostMessageW($tray, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
"closed gracefully: $($proc.WaitForExit(4000))"
if (-not $hadSettings -and (Test-Path $settings)) { Remove-Item $settings }
