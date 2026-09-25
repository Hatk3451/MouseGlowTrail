# Starts a published exe once on the real desktop, checks start-up, idle cost, single instance and a
# graceful exit, then removes any settings.json it created. -Shots saves captures around the pointer
# (they show whatever is on screen there, so they are off by default).
param([string]$Exe, [string]$Out, [switch]$Shots)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class W {
  [StructLayout(LayoutKind.Sequential)] public struct P { public int X, Y; }
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out P p);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowW(string cls, string title);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("kernel32.dll")] public static extern bool QueryProcessCycleTime(IntPtr process, out ulong cycles);
  [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
  [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
  [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
  [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
  [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
  [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, int rop);
  [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
  [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
  // SRCCOPY | CAPTUREBLT so layered windows are included.
  public static System.Drawing.Bitmap Grab(int x, int y, int w, int h) {
    IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), bmp = CreateCompatibleBitmap(screen, w, h);
    IntPtr old = SelectObject(mem, bmp);
    BitBlt(mem, 0, 0, w, h, screen, x, y, 0x00CC0020 | 0x40000000);
    SelectObject(mem, old);
    var result = System.Drawing.Image.FromHbitmap(bmp);
    DeleteObject(bmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen);
    return result;
  }
}
'@ -ReferencedAssemblies System.Drawing

$settings = Join-Path $env:APPDATA 'MouseGlowTrail\settings.json'
$hadSettings = Test-Path $settings
$log = Join-Path $env:LOCALAPPDATA 'MouseGlowTrail\error.log'
$hadLog = Test-Path $log

$p = New-Object W+P
[W]::GetCursorPos([ref]$p) | Out-Null
$proc = Start-Process -FilePath $Exe -ArgumentList '--demo' -PassThru
$sw = [Diagnostics.Stopwatch]::StartNew()
$shotFiles = @()
foreach ($at in 0.45, 0.75, 1.25) {
  while ($sw.Elapsed.TotalSeconds -lt $at) { Start-Sleep -Milliseconds 5 }
  if ($Shots) {
    $x = [Math]::Max(0, $p.X - 520); $y = [Math]::Max(0, $p.Y - 380)
    $bmp = [W]::Grab($x, $y, 700, 460)
    $file = Join-Path $Out ("smoke-{0:0.00}s.png" -f $at)
    $bmp.Save($file); $bmp.Dispose()
    $shotFiles += $file
  }
}
$overlay = [W]::FindWindowW('MouseGlowTrail.Overlay', [NullString]::Value)
$tray = [W]::FindWindowW('MouseGlowTrail.Tray', [NullString]::Value)
"startup: overlay window=$($overlay -ne [IntPtr]::Zero) tray window=$($tray -ne [IntPtr]::Zero)"

Start-Sleep -Seconds 2
$proc.Refresh()
"overlay visible after the demo faded: $([W]::IsWindowVisible($overlay))"
$c0 = [uint64]0; $c1 = [uint64]0
[W]::QueryProcessCycleTime($proc.Handle, [ref]$c0) | Out-Null
$t0 = [DateTime]::UtcNow
Start-Sleep -Seconds 10
[W]::QueryProcessCycleTime($proc.Handle, [ref]$c1) | Out-Null
$wall = ([DateTime]::UtcNow - $t0).TotalSeconds
"idle over 10 s: {0:0.00} Mcycles/s (~{1:0.000}% of one core at 4.2 GHz; includes any real mouse movement meanwhile)" -f (($c1 - $c0) / 1e6 / $wall), (($c1 - $c0) / 1e6 / $wall / 42)
$proc.Refresh()
"working set: {0:0.0} MB   private: {1:0.0} MB   threads: {2}   handles: {3}" -f ($proc.WorkingSet64/1MB), ($proc.PrivateMemorySize64/1MB), $proc.Threads.Count, $proc.HandleCount

$second = Start-Process -FilePath $Exe -PassThru
$exited = $second.WaitForExit(3000)
"second instance exited on its own: $exited (code $($second.ExitCode))"
Start-Sleep -Milliseconds 500
"first instance still running: $(-not $proc.HasExited)"

[W]::PostMessageW($tray, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null  # WM_CLOSE
$closed = $proc.WaitForExit(4000)
"closed gracefully via WM_CLOSE: $closed (code $($proc.ExitCode))"
if (-not $closed) { $proc.Kill() }

if (Test-Path $log) { "--- error.log ---"; Get-Content $log -Tail 40 } else { "error.log: none" }
if (-not $hadSettings -and (Test-Path $settings)) { "settings.json written: $(Get-Content $settings -Raw)"; Remove-Item $settings }
$shotFiles
