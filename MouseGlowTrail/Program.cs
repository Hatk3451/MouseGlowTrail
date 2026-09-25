namespace MouseGlowTrail;

internal static class Program
{
    private const string InstanceName = @"Local\MouseGlowTrail.Instance";
    private const string ActivationName = @"Local\MouseGlowTrail.Activate";

    [STAThread]
    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            AppLog.Write(e.ExceptionObject.ToString() ?? "Unhandled exception");

        // --isolated: a development copy that runs beside the installed one with its own settings.
        var isolated = args.Contains("--isolated");
        var suffix = isolated ? ".Dev" : "";
        if (isolated)
        {
            SettingsStore.Folder = Path.Combine(Path.GetTempPath(), "MouseGlowTrail-dev");
            StartupRegistration.Simulated = true;
        }

        using var instance = new Mutex(true, InstanceName + suffix, out var isFirst);
        if (!isFirst)
        {
            // Already running: poke the first instance (it resumes and says hello) and leave.
            if (EventWaitHandle.TryOpenExisting(ActivationName + suffix, out var activation))
            {
                // This process was started by the user, so it may bring a window to the front; let the
                // running copy use that right for its control panel.
                Native.AllowSetForegroundWindow(uint.MaxValue);
                activation.Set();
                activation.Dispose();
            }

            return 0;
        }

        using var activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationName + suffix);
        return new TrayApp(activationEvent, demo: args.Contains("--demo"), openPanel: args.Contains("--panel")).Run();
    }
}
