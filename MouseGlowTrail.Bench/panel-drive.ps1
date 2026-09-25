# Drives the control panel with window messages (the real pointer is never moved) and takes shots.
# Steps, separated by ';' (coordinates in DIPs of the panel's client area):
#   move X Y | click X Y | down X Y | up X Y | drag X1 Y1 X2 Y2 | wheel X Y NOTCHES | key VK | char TEXT
#   wait MS | shot FILE | grab FILE | front | size WIDTH HEIGHT | rec FOLDER FPS | stop
# grab and rec use PrintWindow: only the panel's own pixels (no Mica backdrop; run with MGT_NO_MICA=1).
param(
    [Parameter(Mandatory)][string]$Steps,
    [string]$Class = 'MouseGlowTrail.Panel'
)
$ErrorActionPreference = 'Stop'
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
public static class PanelDrive
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool PostMessageW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT rect, int size);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    public static IntPtr Window;
    static float Scale = 1f;

    public static bool Find(string className)
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        EnumWindows((hwnd, _) =>
        {
            var text = new StringBuilder(256);
            GetClassNameW(hwnd, text, 256);
            if (IsWindowVisible(hwnd) && text.ToString() == className) { Window = hwnd; return false; }
            return true;
        }, IntPtr.Zero);
        if (Window != IntPtr.Zero) { Scale = GetDpiForWindow(Window) / 96f; }
        return Window != IntPtr.Zero;
    }

    static IntPtr Point(float x, float y)
    {
        int px = (int)Math.Round(x * Scale), py = (int)Math.Round(y * Scale);
        return new IntPtr((py << 16) | (px & 0xFFFF));
    }

    public static void Move(float x, float y) { PostMessageW(Window, 0x0200, IntPtr.Zero, Point(x, y)); }
    public static void Down(float x, float y) { PostMessageW(Window, 0x0201, new IntPtr(1), Point(x, y)); }
    public static void Up(float x, float y) { PostMessageW(Window, 0x0202, IntPtr.Zero, Point(x, y)); }

    public static void Wheel(float x, float y, int notches)
    {
        var point = new POINT { X = (int)Math.Round(x * Scale), Y = (int)Math.Round(y * Scale) };
        ClientToScreen(Window, ref point);
        PostMessageW(Window, 0x020A, new IntPtr((notches * 120) << 16), new IntPtr((point.Y << 16) | (point.X & 0xFFFF)));
    }

    public static void Key(int vk) { PostMessageW(Window, 0x0100, new IntPtr(vk), IntPtr.Zero); }
    public static void Char(char c) { PostMessageW(Window, 0x0102, new IntPtr(c), IntPtr.Zero); }

    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    /// <summary>Resizes the window to a size in DIPs (the frame is included; Windows enforces the minimum).</summary>
    public static void Size(float width, float height)
    {
        SetWindowPos(Window, IntPtr.Zero, 0, 0, (int)(width * Scale), (int)(height * Scale), 0x0002 | 0x0004 | 0x0010);
    }

    static System.Threading.Thread s_recorder;
    static volatile bool s_recording;
    public static int Recorded, Skipped;

    /// <summary>Captures the panel into numbered PNGs until StopRecording, skipping any frame where it is covered.</summary>
    public static void StartRecording(string folder, int fps)
    {
        System.IO.Directory.CreateDirectory(folder);
        SetWindowPos(Window, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        Front();
        System.Threading.Thread.Sleep(200);
        Recorded = Skipped = 0;
        s_recording = true;
        s_recorder = new System.Threading.Thread(() =>
        {
            var interval = 1000.0 / fps;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var next = 0.0;
            while (s_recording)
            {
                // The window's own content only (PrintWindow): other windows, including the running
                // trail overlays, can never end up in a frame.
                using (var bitmap = Content())
                {
                    if (bitmap != null)
                    {
                        bitmap.Save(System.IO.Path.Combine(folder, "f" + Recorded.ToString("D4") + ".png"), ImageFormat.Png);
                        Recorded++;
                    }
                    else
                    {
                        Skipped++;
                    }
                }

                next += interval;
                var wait = next - clock.Elapsed.TotalMilliseconds;
                if (wait > 0) { System.Threading.Thread.Sleep((int)wait); }
            }
        });
        s_recorder.Start();
    }

    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

    /// <summary>The client area as the window itself renders it (PW_CLIENTONLY | PW_RENDERFULLCONTENT).</summary>
    public static Bitmap Content()
    {
        RECT client;
        GetClientRect(Window, out client);
        if (client.Right <= 0 || client.Bottom <= 0) { return null; }
        var bitmap = new Bitmap(client.Right, client.Bottom, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            var hdc = g.GetHdc();
            var ok = PrintWindow(Window, hdc, 0x1 | 0x2);
            g.ReleaseHdc(hdc);
            if (!ok) { bitmap.Dispose(); return null; }
        }

        return bitmap;
    }

    public static string StopRecording()
    {
        s_recording = false;
        s_recorder.Join();
        SetWindowPos(Window, new IntPtr(-2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        return Recorded + " frames, " + Skipped + " skipped (covered)";
    }

    static bool Visible(RECT r)
    {
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        for (int i = 0; i <= 8; i++)
        {
            for (int j = 0; j <= 8; j++)
            {
                var point = new POINT { X = r.Left + 2 + (w - 4) * i / 8, Y = r.Top + 2 + (h - 4) * j / 8 };
                if (GetAncestor(WindowFromPoint(point), 2) != Window) { return false; }
            }
        }

        return true;
    }

    public static void Front()
    {
        BringWindowToTop(Window);
        SetForegroundWindow(Window);
    }

    /// <summary>
    /// Captures the panel only: it is made topmost for the moment of the capture, and nothing is
    /// saved unless every sampled point of its frame really shows the panel (no other window's
    /// content can end up in the file).
    /// </summary>
    public static string Shot(string path)
    {
        const uint NoMoveSize = 0x0001 | 0x0002 | 0x0010;
        SetWindowPos(Window, new IntPtr(-1), 0, 0, 0, 0, NoMoveSize);
        try
        {
            Front();
            System.Threading.Thread.Sleep(250);
            RECT r;
            DwmGetWindowAttribute(Window, 9, out r, Marshal.SizeOf(typeof(RECT)));
            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            for (int i = 0; i <= 8; i++)
            {
                for (int j = 0; j <= 8; j++)
                {
                    var point = new POINT { X = r.Left + 2 + (w - 4) * i / 8, Y = r.Top + 2 + (h - 4) * j / 8 };
                    if (GetAncestor(WindowFromPoint(point), 2) != Window) { return "occluded, not captured"; }
                }
            }

            using (var bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(w, h));
                bitmap.Save(path, ImageFormat.Png);
            }

            return path;
        }
        finally
        {
            SetWindowPos(Window, new IntPtr(-2), 0, 0, 0, 0, NoMoveSize);
        }
    }
}
'@
if (-not [PanelDrive]::Find($Class)) { throw 'panel window not found' }
foreach ($step in $Steps.Split(';')) {
    $parts = $step.Trim().Split(' ', [StringSplitOptions]::RemoveEmptyEntries)
    if ($parts.Count -eq 0) { continue }
    $a = $parts[1..($parts.Count - 1)]
    switch ($parts[0]) {
        'move' { [PanelDrive]::Move([float]$a[0], [float]$a[1]) }
        'down' { [PanelDrive]::Move([float]$a[0], [float]$a[1]); [PanelDrive]::Down([float]$a[0], [float]$a[1]) }
        'up' { [PanelDrive]::Up([float]$a[0], [float]$a[1]) }
        'click' {
            [PanelDrive]::Move([float]$a[0], [float]$a[1]); [PanelDrive]::Down([float]$a[0], [float]$a[1])
            [PanelDrive]::Up([float]$a[0], [float]$a[1])
        }
        'drag' {
            $x1 = [float]$a[0]; $y1 = [float]$a[1]; $x2 = [float]$a[2]; $y2 = [float]$a[3]
            [PanelDrive]::Move($x1, $y1); [PanelDrive]::Down($x1, $y1)
            for ($i = 1; $i -le 10; $i++) { [PanelDrive]::Move($x1 + ($x2 - $x1) * $i / 10, $y1 + ($y2 - $y1) * $i / 10); Start-Sleep -Milliseconds 16 }
            [PanelDrive]::Up($x2, $y2)
        }
        'wheel' { [PanelDrive]::Wheel([float]$a[0], [float]$a[1], [int]$a[2]) }
        'key' { [PanelDrive]::Key([int]$a[0]) }
        'char' { foreach ($c in $a[0].ToCharArray()) { [PanelDrive]::Char($c) } }
        'wait' { Start-Sleep -Milliseconds ([int]$a[0]) }
        'shot' { "shot: " + [PanelDrive]::Shot($a[0]) }
        'grab' { $b = [PanelDrive]::Content(); $b.Save([IO.Path]::GetFullPath($a[0])); $b.Dispose(); "grab: $($a[0])" }
        'front' { [PanelDrive]::Front() }
        'size' { [PanelDrive]::Size([float]$a[0], [float]$a[1]) }
        'rec' { [PanelDrive]::StartRecording($a[0], [int]$a[1]) }
        'stop' { "recorded: " + [PanelDrive]::StopRecording() }
    }
    Start-Sleep -Milliseconds 60
}
