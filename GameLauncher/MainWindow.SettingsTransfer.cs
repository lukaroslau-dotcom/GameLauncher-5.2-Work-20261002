using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using GameLauncher.Service;
using MessageBox = System.Windows.MessageBox;

namespace GameLauncher;

public partial class MainWindow
{
    private async void ExportLibraryPackage_Click(object sender, RoutedEventArgs e)
    {
        if (launchService.HasRunningGames || fileOperationActive || f95UpdateRunning) { ShowToast("Export nach Ende laufender Aufgaben starten."); return; }
        var picker = new Microsoft.Win32.SaveFileDialog { Filter = "Verifiziertes Bibliothekspaket|*.zip", FileName = "gamelauncher-library.zip" };
        if (picker.ShowDialog(this) != true) return;
        if (!TrySaveGames()) return;
        bool success = await RunFileOperationAsync(() => { LibraryPackage.Export(gameService, picker.FileName, fileOperationCancellation!.Token); return true; }, "Bibliothek, Medien und Backups werden exportiert und geprüft …", true);
        if (success) ShowToast("Bibliothekspaket mit SHA-256 geprüft. Installationen und Downloads sind nicht enthalten.");
    }
    private void ExportSettings_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.SaveFileDialog { Filter = "JSON|*.json", FileName = "gamelauncher-settings.json" };
        if (picker.ShowDialog(this) != true) return;
        try { SettingsTransfer.Export(settingsService.Settings, picker.FileName); ShowToast("Einstellungen exportiert."); }
        catch (Exception ex) { ShowToast(ex.Message); }
    }
    private void ImportSettings_Click(object sender, RoutedEventArgs e)
    {
        if (launchService.HasRunningGames || fileOperationActive || downloads?.IsActive == true) { ShowToast("Einstellungen nach Ende laufender Aufgaben importieren."); return; }
        var picker = new Microsoft.Win32.OpenFileDialog { Filter = "JSON|*.json" };
        if (picker.ShowDialog(this) != true) return;
        string baseline = JsonSerializer.Serialize(settingsService.Settings);
        try
        {
            var preview = SettingsTransfer.Inspect(picker.FileName, settingsService.Settings);
            if (MessageBox.Show("Darstellung, Backup- und Benachrichtigungsoptionen importieren?\n\nLokale Ordner, Autostart, aktive Fernsteuerung und Erststartstatus bleiben erhalten.", "Einstellungsvorschau", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var proposed = SettingsTransfer.Apply(preview, settingsService.Settings);
            settingsService.RestoreSettings(JsonSerializer.Serialize(proposed)); settingsService.Save(gameService.Paths);
            ApplyRuntimeSettings(); SettingsNavigationButton_Click(sender, e); ShowToast("Einstellungen importiert.");
        }
        catch (Exception ex) { settingsService.RestoreSettings(baseline); ShowToast(ex.Message); }
    }
    private void OpenDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        string root = Path.Combine(AppPaths.Default().ArchivePath, "Logs");
        Directory.CreateDirectory(root);
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true }; start.ArgumentList.Add(root); Process.Start(start);
    }
}
