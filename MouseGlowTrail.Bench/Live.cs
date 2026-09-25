using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MouseGlowTrail.Bench;

/// <summary>
/// Drives the real overlay engine (layered window, DwmFlush pacing) with a scripted pointer, so frame
/// costs and the resume-after-a-long-pause path can be measured on the desktop without moving the
/// user's cursor. Timeline: move, rest long enough for the surface to be released, move again.
/// </summary>
internal static class Live
{
    private const double MoveEnd = 6.0;
    private const double RestLength = 4.5;
    private const double ResumeLength = 2.5;
    private const double ReleaseDelay = 1.0;
    private const double End = MoveEnd + RestLength + ResumeLength + 1.4;

    public static bool Run(string renderers)
    {
        OverlayWindow.ReleaseDelay = ReleaseDelay;
        var ok = true;
        var heavy = renderers.EndsWith("-heavy", StringComparison.Ordinal);
        renderers = renderers.Replace("-heavy", "");
        foreach (var gpu in renderers switch { "cpu" => [false], "gpu" => [true], _ => new[] { false, true } })
        {
            if (heavy)
            {
                ok &= Scenario(gpu, "重载: 霓虹 醒目 长 100% 星尘 大幅", Scene.MakeConfig(TrailStyleId.Neon, sparkles: true,
                    width: TrailWidth.Bold, length: TrailLength.Long), 2.0);
                continue;
            }

            ok &= Scenario(gpu, "默认: 极光 标准 60%", Scene.MakeConfig(TrailStyleId.Aurora, opacity: 60));
            ok &= Scenario(gpu, "用户: 幻彩 短 纤细 星尘 45%", Scene.MakeConfig(TrailStyleId.Prism, sparkles: true,
                width: TrailWidth.Thin, length: TrailLength.Short, opacity: 45));
        }

        return ok;
    }

    private static POINT Path(double t, int cx, int cy, double scale)
    {
        var travel = t < MoveEnd ? t : t < MoveEnd + RestLength ? MoveEnd :
            t < MoveEnd + RestLength + ResumeLength ? t - RestLength : MoveEnd + ResumeLength;
        var p = Gesture.At(travel, scale, cx, cy);
        return new POINT(p.X, p.Y);
    }

    private static bool Scenario(bool allowGpu, string name, RenderConfig config, double scale = 1.0)
    {
        var cx = GetSystemMetrics(0) / 2 - (scale > 1.5 ? 0 : 200);
        var cy = GetSystemMetrics(1) / 2;
        var probe = new FrameProbe();
        var engine = new TrailEngine(config with { GpuAcceleration = allowGpu }, t => Path(t, cx, cy, scale), probe, true);
        var process = Process.GetCurrentProcess();
        var dwm = Process.GetProcessesByName("dwm").FirstOrDefault();

        engine.Start();
        var clock = Stopwatch.StartNew();
        WaitUntil(clock, 0.6);
        var cpu0 = Snapshot(process, dwm);
        var threads0 = ThreadCycles(process);
        WaitUntil(clock, MoveEnd);
        var cpu1 = Snapshot(process, dwm);
        var threads1 = ThreadCycles(process);
        var renderer = engine.RendererName ?? "?";
        process.Refresh();
        var memory = $"private {process.PrivateMemorySize64 / 1048576.0:0.0} MB, working set {process.WorkingSet64 / 1048576.0:0.0} MB";
        // Baseline: the trail has faded and the overlay is hidden for the rest of the pause.
        WaitUntil(clock, MoveEnd + 1.6);
        var rest0 = Snapshot(process, dwm);
        var restThreads0 = ThreadCycles(process);
        WaitUntil(clock, MoveEnd + RestLength - 0.1);
        var rest1 = Snapshot(process, dwm);
        var restThreads1 = ThreadCycles(process);
        WaitUntil(clock, End);
        engine.Stop();

        var frames = Enumerable.Range(0, probe.Count).Select(i => probe[i]).ToArray();
        var moving = frames.Where(f => f.Time is >= 0.6 and < MoveEnd).ToArray();
        var seconds = cpu1.Wall - cpu0.Wall;
        Console.WriteLine($"── [{renderer}] {name}   ({memory} while moving)");
        Summarise("  moving", moving, seconds, cpu0, cpu1);
        var restSeconds = rest1.Wall - rest0.Wall;
        var renderRest = (restThreads1.GetValueOrDefault(probe.RenderThreadId) - restThreads0.GetValueOrDefault(probe.RenderThreadId)) / 1e6 / restSeconds;
        Console.WriteLine($"  hidden (rest): process {(rest1.Cycles - rest0.Cycles) / 1e6 / restSeconds:0.0} Mcycles/s, render thread {renderRest:0.00} Mcycles/s;  dwm.exe {(rest1.DwmCycles - rest0.DwmCycles) / 1e6 / restSeconds:0} Mcycles/s");
        var busiest = threads1.Select(t => (t.Key, Mcps: (t.Value - threads0.GetValueOrDefault(t.Key)) / 1e6 / seconds))
            .Where(t => t.Mcps > 1).OrderByDescending(t => t.Mcps).Take(6)
            .Select(t => $"{(t.Key == probe.RenderThreadId ? "render" : t.Key == (int)GetCurrentThreadId() ? "bench" : $"tid {t.Key}")} {t.Mcps:0}");
        Console.WriteLine($"  threads (Mcycles/s): {string.Join(", ", busiest)}");

        // After the rest the surface has been released; the resumed stroke must never drop a frame.
        var resumeStart = MoveEnd + RestLength;
        var resume = frames.Where(f => f.Time >= resumeStart && f.Time < resumeStart + ResumeLength).ToArray();
        var firstShown = Array.FindIndex(resume, f => f.Presented);
        var gaps = 0;
        var failures = 0;
        for (var i = Math.Max(0, firstShown); i < resume.Length && firstShown >= 0; i++)
        {
            if (resume[i].Failed)
            {
                failures++;
            }

            if (!resume[i].Presented)
            {
                gaps++;
            }
        }

        var releasedDuringRest = frames.Any(f => f.Time > MoveEnd + 1.2 && f.Time < resumeStart && f.Presented);
        Console.WriteLine($"  resume after a {RestLength:0.0} s rest: first frame {(firstShown >= 0 ? $"{(resume[firstShown].Time - resumeStart) * 1000:0} ms" : "never")}" +
                          $" after motion, dropped frames {gaps}, failed presents {failures}" +
                          (releasedDuringRest ? "  (warning: overlay still visible during the rest)" : ""));
        return gaps == 0 && failures == 0 && firstShown >= 0;
    }

    private static void Summarise(string label, FrameRecord[] frames, double seconds, CpuSnapshot a, CpuSnapshot b)
    {
        var shown = frames.Where(f => f.Presented).ToArray();
        if (shown.Length == 0)
        {
            Console.WriteLine($"{label}: nothing presented");
            return;
        }

        static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
        double Avg(Func<FrameRecord, long> pick) => shown.Average(f => Ms(pick(f)));
        double P95(Func<FrameRecord, long> pick) => shown.Select(f => Ms(pick(f))).Order().ElementAt((int)(shown.Length * 0.95));
        var fps = shown.Length / seconds;
        var megabytes = shown.Sum(f => (double)f.UploadArea) * 4 / 1048576 / seconds;
        Console.WriteLine($"{label}: {fps:0} fps  update {Avg(f => f.UpdateTicks):0.000} ms  draw {Avg(f => f.DrawTicks):0.000} ms (p95 {P95(f => f.DrawTicks):0.000})" +
                          $"  upload {Avg(f => f.UploadTicks):0.000} ms (p95 {P95(f => f.UploadTicks):0.000})  window {shown.Average(f => f.Width):0}x{shown.Average(f => f.Height):0}  {megabytes:0} MB/s");
        var cycles = (b.Cycles - a.Cycles) / 1e6 / seconds;
        var dwm = a.DwmCycles > 0 && b.DwmCycles > 0 ? $"{(b.DwmCycles - a.DwmCycles) / 1e6 / seconds:0} Mcycles/s" : "n/a";
        Console.WriteLine($"{label}: process {cycles:0} Mcycles/s = {cycles / shown.Length * seconds * 1000:0} kcycles/frame (~{cycles / 4200 * 100:0.00}% of one core at 4.2 GHz);  dwm.exe {dwm}");
    }

    // Cycle counters are exact; GetProcessTimes only advances in 15.6 ms scheduler ticks.
    private readonly record struct CpuSnapshot(double Wall, ulong Cycles, ulong DwmCycles);

    private static CpuSnapshot Snapshot(Process process, Process? dwm)
    {
        QueryProcessCycleTime(GetCurrentProcess(), out var cycles);
        return new CpuSnapshot(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency, cycles, DwmCycles());
    }

    private static byte[] s_systemInfo = new byte[1 << 22];

    /// <summary>
    /// dwm.exe's cycle count from the system process list (SYSTEM_PROCESS_INFORMATION.CycleTime), which
    /// needs no handle to the compositor's process.
    /// </summary>
    private static unsafe ulong DwmCycles()
    {
        fixed (byte* buffer = s_systemInfo)
        {
            if (NtQuerySystemInformation(5, buffer, s_systemInfo.Length, out _) < 0)
            {
                return 0;
            }

            var entry = buffer;
            while (true)
            {
                var nameLength = *(ushort*)(entry + 56);
                var name = *(char**)(entry + 64);
                if (name != null && new string(name, 0, nameLength / 2).Equals("dwm.exe", StringComparison.OrdinalIgnoreCase))
                {
                    return *(ulong*)(entry + 24);
                }

                var next = *(uint*)entry;
                if (next == 0)
                {
                    return 0;
                }

                entry += next;
            }
        }
    }

    [DllImport("ntdll.dll")]
    private static extern unsafe int NtQuerySystemInformation(int infoClass, byte* buffer, int length, out int returned);

    private static Dictionary<int, ulong> ThreadCycles(Process process)
    {
        process.Refresh();
        var result = new Dictionary<int, ulong>();
        foreach (ProcessThread thread in process.Threads)
        {
            var handle = OpenThread(0x0800, 0, (uint)thread.Id);
            if (handle == 0)
            {
                continue;
            }

            if (QueryThreadCycleTime(handle, out var cycles) != 0)
            {
                result[thread.Id] = cycles;
            }

            CloseHandle(handle);
        }

        return result;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll")]
    private static extern int QueryProcessCycleTime(IntPtr process, out ulong cycles);

    [DllImport("kernel32.dll")]
    private static extern int QueryThreadCycleTime(IntPtr thread, out ulong cycles);

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenThread(uint access, int inherit, uint threadId);

    [DllImport("kernel32.dll")]
    private static extern int CloseHandle(IntPtr handle);

    private static void WaitUntil(Stopwatch clock, double seconds)
    {
        var remaining = seconds - clock.Elapsed.TotalSeconds;
        if (remaining > 0)
        {
            Thread.Sleep(TimeSpan.FromSeconds(remaining));
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
