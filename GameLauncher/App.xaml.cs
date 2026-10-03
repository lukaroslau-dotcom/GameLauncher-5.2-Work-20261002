using System.Windows;

namespace GameLauncher
{
    public partial class App : System.Windows.Application
    {
        private Mutex? instanceMutex;
        private EventWaitHandle? showRequest;
        private RegisteredWaitHandle? showWait;
        private bool ownsMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            if (e.Args.Contains("--verify-startup") && (!e.Args.Contains("--data-root") || !Service.PathSafety.Within(Service.AppPaths.Default().ArchivePath, System.IO.Path.GetTempPath())))
                throw new System.IO.IOException("Startprüfung erfordert einen isolierten temporären Datenordner.");
            DispatcherUnhandledException += (_, failure) => { Service.EventLog.Write("dispatcher", "Unhandled UI exception", failure.Exception); };
            AppDomain.CurrentDomain.UnhandledException += (_, failure) => Service.EventLog.Write("process", "Unhandled process exception", failure.ExceptionObject as Exception);
            string identity = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(new Service.SettingsService().LoadAndResolvePaths().ArchivePath.ToUpperInvariant())))[..20];
            instanceMutex = new Mutex(true, "Local\\GameLauncher-" + identity, out ownsMutex);
            showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\GameLauncher-Show-" + identity);
            if (!ownsMutex)
            {
                showRequest.Set();
                Shutdown();
                return;
            }
            showWait = ThreadPool.RegisterWaitForSingleObject(showRequest, (_, _) =>
            {
                if (!Dispatcher.HasShutdownStarted)
                    Dispatcher.BeginInvoke(new Action(() => (MainWindow as GameLauncher.MainWindow)?.BringToFront()));
            }, null, Timeout.Infinite, false);
            base.OnStartup(e);
        }
        protected override void OnExit(ExitEventArgs e)
        {
            showWait?.Unregister(null);
            showRequest?.Dispose();
            if (ownsMutex) instanceMutex?.ReleaseMutex();
            instanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}
