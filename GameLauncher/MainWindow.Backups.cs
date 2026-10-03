using System.IO;
using System.Windows;
using System.Windows.Threading;
using GameLauncher.models;

namespace GameLauncher;

public partial class MainWindow
{
    private readonly DispatcherTimer backupTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private DateTime lastScheduledBackup = DateTime.UtcNow;
    private void InitializeBackups()
    {
        BackupPage.Configure(gameService, settingsService, BackupGameAsync, RunFileOperationAsync);
        backupTimer.Tick += async (_, _) =>
        {
            int minutes = settingsService.Settings.BackupIntervalMinutes;
            if (minutes <= 0 || runtimeClosed || quietMode || fileOperationActive || launchService.HasRunningGames || DateTime.UtcNow - lastScheduledBackup < TimeSpan.FromMinutes(minutes)) return;
            lastScheduledBackup = DateTime.UtcNow;
            foreach (var game in gameService.GetGames().Where(g => Directory.Exists(g.SavePath)))
            {
                if (runtimeClosed || launchService.HasRunningGames) break;
                if (!await BackupGameAsync(game)) ShowToast("Geplante Sicherung fehlgeschlagen: " + game.Name);
            }
        };
        backupTimer.Start();
        Closed += (_, _) => backupTimer.Stop();
    }
    private async Task BackupAfterSessionAsync(Game game)
    { if (!await BackupGameAsync(game)) ShowToast("Sicherung nach Spielende fehlgeschlagen: " + game.Name); }
    private Task<bool> BackupGameAsync(Game game)
    {
        if (launchService.IsRunning(game.Id) || !Directory.Exists(game.SavePath)) return Task.FromResult(false);
        return RunFileOperationAsync(() => gameService.ArchiveSave(game), "Spielstand wird mit SHA-256 gesichert …");
    }
    private void BackupsNavigation_Click(object sender, RoutedEventArgs e)
    { BackupPage.Refresh(); ShowView(BackupsPageHost); }
}
