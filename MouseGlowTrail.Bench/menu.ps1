# -Attach uses the already running instance instead of launching one; -Downs picks the submenu
# (2 = 流光样式, 3 = 轨迹浓淡, 4 = 轨迹长度, 5 = 轨迹粗细).
param([string]$Exe, [string]$Out, [switch]$Attach, [int]$Downs = 2)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class W {
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowW(string cls, string title);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after, string cls, string title);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Rt, B; }
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
  // Menus live in top-level "#32768" windows; collect the visible ones.
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(uint pid);
  public static IntPtr[] Menus() {
    var list = new System.Collections.Generic.List<IntPtr>();
    IntPtr h = IntPtr.Zero;
    while ((h = FindWindowExW(IntPtr.Zero, h, "#32768", null)) != IntPtr.Zero) if (IsWindowVisible(h)) list.Add(h);
    return list.ToArray();
  }
}
'@ -ReferencedAssemblies System.Drawing

$settings = Join-Path $env:APPDATA 'MouseGlowTrail\settings.json'
$hadSettings = Test-Path $settings
if ($Attach) { $proc = $null } else { $proc = Start-Process -FilePath $Exe -PassThru; Start-Sleep -Seconds 2 }
$tray = [W]::FindWindowW('MouseGlowTrail.Tray', [NullString]::Value)
$anchorX = 1500; $anchorY = 1000
$wParam = [IntPtr](($anchorY -shl 16) -bor $anchorX)
# Like Explorer does before a tray callback, let the app take the foreground so its menu can open.
$trayPid = 0; [void][W]::GetWindowThreadProcessId($tray, [ref]$trayPid)
[void][W]::AllowSetForegroundWindow($trayPid)
# WM_APP+1 with WM_CONTEXTMENU, exactly what the notification area sends on right-click.
[W]::PostMessageW($tray, 0x8001, $wParam, [IntPtr]0x7B) | Out-Null
Start-Sleep -Milliseconds 600
$menus = [W]::Menus()
"menus open: $($menus.Count)"
function Shot($name) {
  $bmp = [W]::Grab(1160, 520, 760, 500)
  $bmp.Save((Join-Path $Out $name)); $bmp.Dispose()
}
if ($menus.Count -gt 0) {
  Shot 'menu-main.png'
  $menu = $menus[0]
  # Down x N (separators are skipped), then Right to open that submenu.
  foreach ($key in @(@(0x28) * $Downs) + 0x27) {
    [W]::PostMessageW($menu, 0x0100, [IntPtr]$key, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 250
  }
  Start-Sleep -Milliseconds 300
  Shot 'menu-styles.png'
  foreach ($m in [W]::Menus()) { [W]::PostMessageW($m, 0x0100, [IntPtr]0x1B, [IntPtr]::Zero) | Out-Null; Start-Sleep -Milliseconds 150 }
  Start-Sleep -Milliseconds 300
  foreach ($m in [W]::Menus()) { [W]::PostMessageW($m, 0x0100, [IntPtr]0x1B, [IntPtr]::Zero) | Out-Null; Start-Sleep -Milliseconds 150 }
}
Start-Sleep -Milliseconds 300
"menus still open: $([W]::Menus().Count)"
if (-not $Attach) {
  [W]::PostMessageW($tray, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
  "closed gracefully: $($proc.WaitForExit(4000))"
  if (-not $hadSettings -and (Test-Path $settings)) { Remove-Item $settings }
}
