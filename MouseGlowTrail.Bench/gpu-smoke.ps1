# Runs a published exe once with GPU acceleration switched on: checks that the DirectComposition
# overlay is used, that the --demo flourish appears (captured over a plain backdrop, so no desktop
# content is recorded), memory, and a clean exit. The user's settings.json is restored byte for byte.
param([string]$Exe, [string]$Out = (Join-Path $PSScriptRoot 'out'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class Backdrop : System.Windows.Forms.Form {
  public Backdrop(int x, int y, int w, int h) {
    FormBorderStyle = System.Windows.Forms.FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
    StartPosition = System.Windows.Forms.FormStartPosition.Manual; Bounds = new System.Drawing.Rectangle(x, y, w, h);
    BackColor = System.Drawing.Color.FromArgb(30, 31, 38);
  }
  protected override bool ShowWithoutActivation { get { return true; } }
  protected override System.Windows.Forms.CreateParams CreateParams {
    get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x80; return p; }
  }
}
public static class G {
  [StructLayout(LayoutKind.Sequential)] public struct P { public int X, Y; }
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Rt, B; }
  [StructLayout(LayoutKind.Sequential)] public struct MI { public int Size; public R Monitor; public R Work; public uint Flags; }
  [DllImport("user32.dll")] public static extern IntPtr SetProcessDpiAwarenessContext(IntPtr c);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out P p);
  [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(P p, uint f);
  [DllImport("user32.dll")] public static extern bool GetMonitorInfoW(IntPtr m, ref MI i);
  [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr m, int t, out uint x, out uint y);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowW(string cls, string title);
  [DllImport("user32.dll")] public static extern IntPtr GetWindowLongPtrW(IntPtr h, int i);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
  [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
  [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
  [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
  [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
  [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, int rop);
  [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
  [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
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
'@ -ReferencedAssemblies System.Drawing, System.Windows.Forms
[G]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

$settings = Join-Path $env:APPDATA 'MouseGlowTrail\settings.json'
$log = Join-Path $env:LOCALAPPDATA 'MouseGlowTrail\error.log'
$hadLog = Test-Path $log
$original = if (Test-Path $settings) { [IO.File]::ReadAllBytes($settings) } else { $null }
try {
  $json = if ($original) { [Text.Encoding]::UTF8.GetString($original) | ConvertFrom-Json } else { [pscustomobject]@{ WelcomeShown = $true } }
  $json | Add-Member -NotePropertyName GpuAcceleration -NotePropertyValue $true -Force
  New-Item -ItemType Directory -Force -Path (Split-Path $settings) | Out-Null
  [IO.File]::WriteAllText($settings, ($json | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))

  # Where the engine puts the flourish: up and to the left of the pointer, kept inside the work area.
  $p = New-Object G+P; [G]::GetCursorPos([ref]$p) | Out-Null
  $monitor = [G]::MonitorFromPoint($p, 2)
  $info = New-Object G+MI; $info.Size = [Runtime.InteropServices.Marshal]::SizeOf($info); [G]::GetMonitorInfoW($monitor, [ref]$info) | Out-Null
  $dpiX = [uint32]96; $dpiY = [uint32]96; [G]::GetDpiForMonitor($monitor, 0, [ref]$dpiX, [ref]$dpiY) | Out-Null
  $s = $dpiX / 96.0; $margin = 190 * $s
  $fx = [Math]::Min([Math]::Max($p.X - 170 * $s, $info.Work.L + $margin), [Math]::Max($info.Work.L + $margin, $info.Work.Rt - $margin))
  $fy = [Math]::Min([Math]::Max($p.Y - 150 * $s, $info.Work.T + $margin * 0.6), [Math]::Max($info.Work.T + $margin * 0.6, $info.Work.B - $margin * 0.6))
  $w = [int](360 * $s); $h = [int](200 * $s)
  $bx = [int]($fx - $w / 2); $by = [int]($fy - $h / 2)
  $backdrop = New-Object Backdrop($bx, $by, $w, $h)
  $backdrop.Show(); [System.Windows.Forms.Application]::DoEvents()

  $proc = Start-Process -FilePath $Exe -ArgumentList '--demo' -PassThru
  $until = [DateTime]::UtcNow.AddMilliseconds(700)
  while ([DateTime]::UtcNow -lt $until) { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 15 }
  $shot = [G]::Grab($bx, $by, $w, $h); $file = Join-Path $Out 'gpu-demo.png'; $shot.Save($file); $shot.Dispose()
  $overlay = [G]::FindWindowW('MouseGlowTrail.Overlay', [NullString]::Value)
  $ex = [int64][G]::GetWindowLongPtrW($overlay, -20)
  "overlay uses DirectComposition (WS_EX_NOREDIRECTIONBITMAP): $((($ex -band 0x00200000) -ne 0))   captured $file"
  $backdrop.Close()
  Start-Sleep -Seconds 2
  $proc.Refresh()
  "memory with GPU acceleration: working set {0:0.0} MB, private {1:0.0} MB" -f ($proc.WorkingSet64/1MB), ($proc.PrivateMemorySize64/1MB)
  $tray = [G]::FindWindowW('MouseGlowTrail.Tray', [NullString]::Value)
  [G]::PostMessageW($tray, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
  "closed gracefully: $($proc.WaitForExit(4000))"
  if (Test-Path $log) { "--- error.log ---"; Get-Content $log -Tail 20 } else { "error.log: none" }
}
finally {
  if ($original) { [IO.File]::WriteAllBytes($settings, $original) } elseif (Test-Path $settings) { Remove-Item $settings }
  "settings.json restored: $(if ($original) { [Linq.Enumerable]::SequenceEqual([byte[]][IO.File]::ReadAllBytes($settings), [byte[]]$original) } else { -not (Test-Path $settings) })"
}
