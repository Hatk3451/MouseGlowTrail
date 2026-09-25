using System.Diagnostics;

namespace MouseGlowTrail.Bench;

/// <summary>Where the GPU renderer's memory goes: device, composition, first frames, release.</summary>
internal static class GpuMemory
{
    public static void Run()
    {
        var process = Process.GetCurrentProcess();
        void Report(string step)
        {
            GC.Collect();
            process.Refresh();
            Console.WriteLine($"{step,-34} private {process.PrivateMemorySize64 / 1048576.0,6:0.0} MB   working set {process.WorkingSet64 / 1048576.0,6:0.0} MB");
        }

        _ = TrailStyles.All;
        _ = Profiles.Particle;
        Report("baseline");
        var canvas = GpuCanvas.TryCreate(out var error);
        Report($"D3D11 device + pipeline ({(canvas is null ? error : "ok")})");
        canvas?.Dispose();
        Report("device released");
        var presenter = CompositionPresenter.TryCreate("Static", new PixelBounds(0, 0, 2560, 1440), out error);
        Report($"+ DirectComposition ({(presenter is null ? error : "ok")})");
        if (presenter is null)
        {
            return;
        }

        var scene = Scene.Run(Scene.MakeConfig(TrailStyleId.Aurora), t => Gesture.At(t, 1.0, 1000, 700), 1.4);
        for (var frame = 0; frame < 60; frame++)
        {
            if (presenter.BeginFrame(new PixelBounds(500, 400, 1500, 1000), out var ox, out var oy) is { } sink)
            {
                FrameComposer.Draw(sink, scene.Model, scene.Particles, scene.Clicks, scene.Config, 1.4, ox, oy);
                presenter.EndFrame(frame / 60.0);
            }
        }

        presenter.Hide(1.0);
        Report("after 60 frames (hidden again)");
        presenter.Dispose();
        Report("everything released");
    }
}
