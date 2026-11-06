using System.IO;
using System.Windows;
using NetBridge.Windows.Core;

namespace NetBridge.Windows;

public partial class App : Application
{
    private static Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        // One app at a time: a windowless leftover instance would otherwise
        // hold the proxy port and break the freshly launched one.
        _singleInstance = new Mutex(true, @"Local\NetBridgePro.SingleInstance", out var first);
        if (!first)
        {
            MessageBox.Show(
                "NetBridge Pro is already running.\n" +
                "Close the other window first (Task Manager > NetBridge.Windows.exe > End task), then start it again.",
                "NetBridge Pro", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(0);
            return;
        }

        // Global exception guards: never die silently — always show the user
        // what happened and leave a crash file they can share.
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            Fatal("UI error", args.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                Fatal("Fatal error", ex);
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved();
            Logger.Error("App", "Unobserved task exception: " + args.Exception.Message);
        };

        try
        {
            SettingsStore.Load();
            Logger.Info("App", $"NetBridge Pro v1.0 starting (lang={SettingsStore.Current.Language})");
            I18N.Init(SettingsStore.Current.Language);

            base.OnStartup(e);

            // Restore saved firewall rules on boot (needs elevation; silently skips otherwise)
            Firewall.AppFirewall.ReapplySavedRules();
        }
        catch (Exception ex)
        {
            Fatal("Startup error", ex);
            Shutdown(1);
        }
    }

    /// <summary>Show the error to the user and drop a crash file next to the exe
    /// (ProgramData first, exe folder / Desktop as fallbacks).</summary>
    private static void Fatal(string title, Exception ex)
    {
        var full = $"{title}: {ex.GetType().Name}\n{ex.Message}\n\n{ex.StackTrace}";
        try { Logger.Error("App", full); } catch { }
        try { WriteCrashFile(full); } catch { }
        try
        {
            MessageBox.Show(full, $"NetBridge Pro — {title}",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { }
    }

    private static void WriteCrashFile(string text)
    {
        string[] candidates =
        {
            Path.Combine(Constants.ProgramDataDir, "crash.log"),
            Path.Combine(AppContext.BaseDirectory, "NetBridge-crash.log"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "NetBridge-crash.log"),
        };
        foreach (var p in candidates)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                File.AppendAllText(p,
                    $"==== {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===={Environment.NewLine}{text}{Environment.NewLine}{Environment.NewLine}");
                return;
            }
            catch { /* try next location */ }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { Net.ConnectionManager.Instance.Shutdown(); } catch { }
        try { Net.SystemProxySetter.RestoreIfNeeded(); } catch { }
        try { Adb.AdbServerManager.ShutdownServer(); } catch { }
        Logger.Info("App", "NetBridge Pro exited");
        base.OnExit(e);
    }
}
