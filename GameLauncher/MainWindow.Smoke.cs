using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameLauncher.models;
using GameLauncher.Service;
using EventLog = GameLauncher.Service.EventLog;

namespace GameLauncher;

public partial class MainWindow
{
    private void InitializeSmoke()
    {
        if (!Environment.GetCommandLineArgs().Contains("--verify-startup")) return;
        string root = gameService.Paths.ArchivePath;
        if (!Environment.GetCommandLineArgs().Contains("--data-root") || !PathSafety.Within(root, Path.GetTempPath()))
            throw new IOException("Startprüfung erfordert einen isolierten temporären Datenordner.");
        bool started = false;
        ContentRendered += async (_, _) =>
        {
            if (started) return; started = true;
            var checks = new List<string>(); var metrics = new Dictionary<string, object>();
            try
            {
                await Task.Delay(350);
                void Check(bool success, string message) { if (!success) throw new IOException(message); checks.Add(message); }
                void Shot(string name)
                {
                    UpdateLayout();
                    var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(this);
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                    using var output = File.Create(Path.Combine(root, name + ".png")); png.Save(output);
                }
                Check(IsLoaded && ActualWidth > 0, "Native WPF window rendered");
                foreach (var route in new (string Name, UIElement Page, Action Open)[]
                {
                    ("home", HomeView, () => HomeNavigationButton_Click(this, new RoutedEventArgs())),
                    ("library", LibraryView, () => LibraryNavigationButton_Click(this, new RoutedEventArgs())),
                    ("settings", SettingsView, () => SettingsNavigationButton_Click(this, new RoutedEventArgs())),
                    ("backups", BackupsPageHost, () => BackupsNavigation_Click(this, new RoutedEventArgs())),
                    ("downloads", DownloadsPageHost, () => DownloadsNavigation_Click(this, new RoutedEventArgs())),
                    ("add", AddGameView, () => OpenAddGameButton_Click(this, new RoutedEventArgs()))
                })
                { route.Open(); await Task.Delay(200); Check(route.Page.IsVisible, "Navigation: " + route.Name); Shot(route.Name); }
                HomeNavigationButton_Click(this, new RoutedEventArgs());
                OpenDiagnosticsWindow(); await Task.Delay(150);
                var diagnostics=System.Windows.Application.Current.Windows.Cast<Window>().Single(w=>w.Owner==this&&w.Title=="Diagnoseprotokolle");diagnostics.Close();
                Check(IsVisible&&!Dispatcher.HasShutdownStarted,"Closing diagnostics keeps published launcher alive");
                var mediaFixture=new Game {Id="media-smoke",Name="Galerieprüfung",CoverPath=Path.Combine(root,"home.png"),GalleryPaths=new(){Path.Combine(root,"library.png")}};
                ShowGameDetails(mediaFixture);OpenGallery(mediaFixture.CoverPath);await Task.Delay(350);
                var gallery=GalleryOverlayHost.Child as Views.GalleryOverlay;
                Check(gallery?.IsVisible==true&&System.Windows.Application.Current.Windows.Count==1,"Published gallery opens inline without another window");CloseGallery();
                Check(IsVisible&&!Dispatcher.HasShutdownStarted,"Closing gallery keeps published launcher alive");
                HomeNavigationButton_Click(this,new RoutedEventArgs());
                metrics["startupIdle"] = await SampleProcess();
                await Task.Delay(5000);
                metrics["settledIdle"] = await SampleProcess();
                string[] args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "--test-game");
                if (index >= 0 && index + 1 < args.Length)
                {
                    string executable = Path.GetFullPath(args[index + 1]);
                    if (Path.GetFileName(executable) != "GameLauncher.TestGame.exe") throw new IOException("Nur die kontrollierte Testspiel-Fixture ist erlaubt.");
                    var fixture = new Game { Id = "smoke-process", Name = "Kontrolliertes Testspiel", GamePath = Path.GetDirectoryName(executable)!, ExecutablePath = executable, IsInstalled = true };
                    gameService.AddGame(fixture); PlayGame(fixture);
                    await Task.Delay(3500);
                    Check(launchService.IsRunning(fixture.Id), "Game child tracked after parent exits");
                    Check(quietMode && !IsVisible, "Gaming quiet mode hides UI and pauses background work");
                    metrics["gamingQuiet"] = await SampleProcess();
                    var timeout = Stopwatch.StartNew();
                    while (launchService.HasRunningGames && timeout.Elapsed < TimeSpan.FromSeconds(25)) await Task.Delay(250);
                    await Task.Delay(300);
                    Check(!launchService.HasRunningGames && !quietMode && IsVisible, "Game end restores launcher");
                    Check(fixture.Playtime >= TimeSpan.FromSeconds(12) && fixture.Playtime < TimeSpan.FromSeconds(22), "Game time accounted once through child session");
                    metrics["playtimeSeconds"] = fixture.Playtime.TotalSeconds;
                }
                AtomicJsonFile.Write(Path.Combine(root, "smoke-result.json"), new { passed = true, checks, metrics });
                forceExit = true; Close(); System.Windows.Application.Current.Shutdown(0);
            }
            catch (Exception ex)
            {
                EventLog.Write("smoke", "Native smoke failed", ex);
                AtomicJsonFile.Write(Path.Combine(root, "smoke-result.json"), new { passed = false, checks, metrics, error = ex.ToString() });
                forceExit = true; Close(); System.Windows.Application.Current.Shutdown(1);
            }
        };
    }
    private static async Task<object> SampleProcess()
    {
        using var process = Process.GetCurrentProcess(); process.Refresh();
        var cpu = process.TotalProcessorTime; var watch = Stopwatch.StartNew(); await Task.Delay(3000); process.Refresh();
        return new { elapsedSeconds = watch.Elapsed.TotalSeconds, cpuPercentOfOneCore = 100 * (process.TotalProcessorTime - cpu).TotalSeconds / watch.Elapsed.TotalSeconds,
            workingSetBytes = process.WorkingSet64, privateBytes = process.PrivateMemorySize64, logicalProcessors = Environment.ProcessorCount };
    }
}
