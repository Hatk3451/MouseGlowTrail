# Captures the live benchmark's overlay on the real desktop (CPU and GPU renderer) and checks that the
# overlay never becomes the window under the trail, i.e. that clicks fall through it.
param([string]$Out = (Join-Path $PSScriptRoot 'out'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
// A plain dark panel behind the trail so captures are comparable; it never takes focus.
public class Backdrop : System.Windows.Forms.Form {
  public Backdrop(int x, int y, int w, int h) {
    FormBorderStyle = System.Windows.Forms.FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
    StartPosition = System.Windows.Forms.FormStartPosition.Manual; Bounds = new System.Drawing.Rectangle(x, y, w, h);
    BackColor = System.Drawing.Color.FromArgb(30, 31, 38);
  }
  protected override bool ShowWithoutActivation { get { return true; } }
  protected override System.Windows.Forms.CreateParams CreateParams {
    get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x80; return p; } // NOACTIVATE | TOOLWINDOW
  }
}
public static class Screen {
  [StructLayout(LayoutKind.Sequential)] public struct P { public int X, Y; }
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(P p);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, System.Text.StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
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
  public static string ClassAt(int x, int y) {
    var p = new P { X = x, Y = y };
    var sb = new System.Text.StringBuilder(128);
    GetClassNameW(WindowFromPoint(p), sb, 128);
    return sb.ToString();
  }
}
'@ -ReferencedAssemblies System.Drawing, System.Windows.Forms

$bench = Join-Path $PSScriptRoot 'bin\Release\net10.0-windows\MouseGlowTrail.Bench.dll'
$cx = [int]([Screen]::GetSystemMetrics(0) / 2 - 200); $cy = [int]([Screen]::GetSystemMetrics(1) / 2)
$left = $cx - 420; $top = $cy - 250
$backdrop = New-Object Backdrop($left, $top, 840, 500)
$backdrop.Show()
[System.Windows.Forms.Application]::DoEvents()
foreach ($renderer in 'cpu', 'gpu') {
  $proc = Start-Process -FilePath 'dotnet' -ArgumentList "`"$bench`" live $renderer" -PassThru -WindowStyle Hidden
  $until = [DateTime]::UtcNow.AddMilliseconds(2600)
  while ([DateTime]::UtcNow -lt $until) { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 20 }
  $overlayHits = 0
  for ($i = 0; $i -lt 40; $i++) {
    # Sample points across the gesture area; the overlay must never be the window under them.
    $x = $cx - 300 + ($i * 37) % 600; $y = $cy - 150 + ($i * 53) % 300
    if ([Screen]::ClassAt($x, $y) -eq 'MouseGlowTrail.Overlay') { $overlayHits++ }
  }
  $shot = [Screen]::Grab($left, $top, 840, 500)
  $file = Join-Path $Out "screen-$renderer.png"
  $shot.Save($file); $shot.Dispose()
  "$renderer : overlay was the window under the pointer at $overlayHits of 40 points; saved $file"
  $proc.WaitForExit(60000) | Out-Null
}
$backdrop.Close()
