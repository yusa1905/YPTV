using System.Windows;

namespace Yptv;

public partial class App : Application
{
    // A playlist file / URL or a stream given on the command line (the legacy "--playlist x" form works too).
    public static string? StartArgument { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        StartArgument = e.Args.Length > 1 && e.Args[0] == "--playlist" ? e.Args[1] : e.Args.FirstOrDefault();
        // The second launch passes its argument to the window that is already running and quits before building any UI.
        if (!SingleInstance.TryBecomePrimary() && SingleInstance.Forward(StartArgument ?? ""))
        {
            Shutdown();
            return;
        }
        Log.Info($"start {Diagnostics.Version}, {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error($"unhandled exception{(args.IsTerminating ? " (terminating)" : "")}: {(args.ExceptionObject as Exception)?.ToString() ?? "?"}");
        DispatcherUnhandledException += (_, args) => Log.Exception("ui exception", args.Exception);
        TaskScheduler.UnobservedTaskException += (_, args) => Log.Exception("task exception", args.Exception);
        Themes.Apply(Settings.Current.Theme);
        // The UI is flat gradients and the video has its own window, so the GPU path buys nothing; software rendering
        // keeps the D3D device and driver allocations (~80 MB private) out of the process.
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        base.OnStartup(e);
    }
}
