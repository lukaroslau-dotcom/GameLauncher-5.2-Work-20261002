using System.Diagnostics;
using System.IO;
using System.Windows;
using GameLauncher.Constants;
using GameLauncher.models;
using GameLauncher.Service;

namespace GameLauncher.Views;

public partial class BackupsView : System.Windows.Controls.UserControl
{
    private GameService? service;
    private SettingsService? settings;
    private Func<Game, Task<bool>>? backup;
    private Func<Func<bool>, string, Task<bool>>? operate;
    private bool loading;
    public sealed record Row(Game Game, string Name, string Description);
    public BackupsView() => InitializeComponent();
    public void Configure(GameService games, SettingsService configuration, Func<Game, Task<bool>> createBackup,
        Func<Func<bool>, string, Task<bool>> operation)
    { service = games; settings = configuration; backup = createBackup; operate = operation; }
    public void Refresh()
    {
        if (service == null || settings == null) return;
        loading = true;
        Before.IsChecked = settings.Settings.BackupBeforePlay; After.IsChecked = settings.Settings.BackupAfterPlay;
        Scheduled.IsChecked = settings.Settings.BackupIntervalMinutes > 0;
        loading = false;
        RecoveryRows.ItemsSource = InstallationRecovery.List(service.Paths);
        Rows.ItemsSource = service.GetGames().Select(g =>
        {
            string path = service.Paths.GetSaveArchiveForGame(g.Id);
            string description = !Directory.Exists(path) ? "Noch keine Sicherung" :
                File.Exists(Path.Combine(path, VerifiedSaveBackup.ManifestName)) ? "Prüfsummen vorhanden · Prüfung auf Anforderung" : "Ältere 5.2-Sicherung · ohne historische Prüfsummen";
            if (!Directory.Exists(g.SavePath)) description += " · Save-Ordner fehlt";
            return new Row(g, g.Name, description);
        }).ToArray();
    }
    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();
    private void OptionsChanged(object sender, RoutedEventArgs e)
    {
        if (loading || settings == null || service == null) return;
        settings.Settings.BackupBeforePlay = Before.IsChecked == true;
        settings.Settings.BackupAfterPlay = After.IsChecked == true;
        settings.Settings.BackupIntervalMinutes = Scheduled.IsChecked == true ? 60 : 0;
        try { settings.Save(service.Paths); Feedback.Text = "Backup-Einstellungen gespeichert."; }
        catch (Exception ex) { Feedback.Text = ex.Message; }
    }
    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Row row } && backup != null)
        { bool success = await backup(row.Game); Feedback.Text = success ? "Sicherung erstellt und SHA-256 geprüft." : "Sicherung nicht erstellt. Save-Pfad prüfen und laufendes Spiel beenden."; Refresh(); }
    }
    private async void Verify_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Row row } || service == null || operate == null) return;
        string message = "";
        await operate(() => { try { message = VerifiedSaveBackup.Verify(service.Paths.GetSaveArchiveForGame(row.Game.Id)) ? "SHA-256: alle Dateien stimmen überein." : "Ältere Sicherung ohne Prüfsummen; historische Integrität nicht nachweisbar."; } catch (Exception ex) { message = "Prüfung fehlgeschlagen: " + ex.Message; } return true; }, "Backup wird geprüft …");
        Feedback.Text = message;
    }
    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Row row } || service == null || operate == null) return;
        var picker = new Microsoft.Win32.OpenFolderDialog { Title = "Übergeordneten Ordner wählen; Wiederherstellung erstellt einen neuen Unterordner" };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
        string target = Path.Combine(picker.FolderName, "Save-Restore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
        if (System.Windows.MessageBox.Show("Spielstand in diesen neuen Ordner kopieren?\n\n" + target + "\n\nAktuelle Spielstände bleiben unverändert.", "Wiederherstellen", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        bool restored = await operate(() => service.RestoreSave(row.Game.Id, target, SaveRestoreMode.Replace), "Spielstand wird geprüft und wiederhergestellt …");
        Feedback.Text = restored ? "Wiederhergestellt: " + target : "Wiederherstellung abgelehnt oder fehlgeschlagen; aktuelle Spielstände bleiben erhalten.";
    }
    private void History_Click(object sender, RoutedEventArgs e)
    {
        if (service == null) return;
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        start.ArgumentList.Add(service.Paths.ArchivePath); Process.Start(start);
    }
    private async void RestoreInstallation_Click(object sender, RoutedEventArgs e)
    {
        if (service == null || operate == null || sender is not FrameworkElement { DataContext: InstallationRecovery.Receipt item }) return;
        if (System.Windows.MessageBox.Show("Installation nach SHA-256-Prüfung an diesen Ort zurücksetzen?\n\n" + item.Original,
            "Installation wiederherstellen", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        InstallationRecovery.Receipt? restored = null;
        bool success = await operate(() => { restored = InstallationRecovery.Restore(service.Paths, item.Id); return true; }, "Installation wird geprüft und wiederhergestellt …");
        if (success && restored != null)
        {
            var game = service.ShowGame(restored.GameId);
            // Never redirect a game that has already been updated to another installation.
            if (game != null && (game.GamePath.Length == 0 || PathSafety.Same(game.GamePath, restored.Original)))
            {
                game.GamePath = restored.Original; game.ExecutablePath = restored.Executable; game.SavePath = restored.Save;
                game.IsInstalled = true; game.Status = GameStatus.Installed;
                try { service.SaveGames(); } catch (Exception ex) { Feedback.Text = "Dateien wiederhergestellt; Bibliothek konnte nicht gespeichert werden: " + ex.Message; Refresh(); return; }
            }
        }
        Feedback.Text = success ? "Installation wiederhergestellt." : "Wiederherstellung fehlgeschlagen; vorhandene Dateien wurden nicht überschrieben.";
        Refresh();
    }
}
