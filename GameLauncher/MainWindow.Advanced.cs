using System.IO;
using System.Windows;
using System.Windows.Controls;
using GameLauncher.models;
using GameLauncher.Service;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;

namespace GameLauncher;

public partial class MainWindow
{
    private void EditAdvancedMetadata_Click(object sender, RoutedEventArgs e)
    {
        Game? game = selectedGame; if (game == null) return;
        var panel = new StackPanel { Margin = new Thickness(24) };
        var dialog = new Window { Owner = this, Title = "Erweiterte Angaben · " + game.Name, Width = 580, Height = 650, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Background, Foreground = Foreground, Content = panel, Resources = new ResourceDictionary { Source = new Uri("/GameLauncher;component/Theme.xaml", UriKind.Relative) } };
        TextBox Field(string title, string value)
        { panel.Children.Add(new TextBlock { Text = title, Margin = new Thickness(0, 8, 0, 6) }); var field = new TextBox { Text = value }; panel.Children.Add(field); return field; }
        var developer = Field("Ersteller / Entwickler", game.F95Developer);
        var arguments = Field("Startargumente", game.LaunchArguments);
        var aliases = Field("Aliase / Abkürzungen (mit Komma trennen)", string.Join(", ", game.Aliases));
        var tags = Field("Tags (mit Komma trennen)", string.Join(", ", game.Tags));
        var save = new Button { Content = "Speichern", Margin = new Thickness(0, 24, 0, 0), HorizontalAlignment = System.Windows.HorizontalAlignment.Right, IsDefault = true };
        panel.Children.Add(save);
        save.Click += (_, _) =>
        {
            if (arguments.Text.Length > 4096 || aliases.Text.Length > 4096 || tags.Text.Length > 4096) { MessageBox.Show("Bitte maximal 4096 Zeichen pro Feld verwenden."); return; }
            var old = (game.LaunchArguments, game.Aliases, game.Tags);
            var oldDeveloper = game.F95Developer; var oldEdits = new HashSet<string>(game.UserEditedFields);
            game.F95Developer = developer.Text.Trim(); game.UserEditedFields.Add(nameof(Game.F95Developer)); game.UserEditedFields.Add(nameof(Game.Tags));
            game.LaunchArguments = arguments.Text;
            game.Aliases = aliases.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).Take(50).ToList();
            game.Tags = tags.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).Take(50).ToList();
            if (!TrySaveGames()) { (game.LaunchArguments, game.Aliases, game.Tags) = old; game.F95Developer = oldDeveloper; game.UserEditedFields = oldEdits; return; }
            dialog.DialogResult = true; RefreshGameList();
        };
        dialog.ShowDialog();
    }
    private async void RollbackInstallation_Click(object sender, RoutedEventArgs e)
    {
        Game? game = selectedGame;
        if (game == null || launchService.IsRunning(game.Id) || fileOperationActive) return;
        var previous = game.InstallationHistory.LastOrDefault(v => Directory.Exists(v.GamePath) && File.Exists(v.ExecutablePath) && PathSafety.Within(v.ExecutablePath, v.GamePath));
        if (previous == null) { ShowToast("Keine erreichbare vorherige Installation. Entfernte Ordner können unter Backups wiederhergestellt werden."); return; }
        if (MessageBox.Show("Zur vorherigen Installation wechseln?\n\n" + previous.GamePath + "\nVersion: " + previous.Version + "\n\nAktuelle Dateien bleiben erhalten. Spielstände werden nicht automatisch zurückgesetzt.", "Version zurücksetzen", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        if (Directory.Exists(game.SavePath) && !await BackupGameAsync(game)) { ShowToast("Aktueller Spielstand konnte nicht gesichert werden."); return; }
        var current = new InstallationVersion(game.GamePath, game.ExecutablePath, game.SavePath, game.GameVersion, DateTime.UtcNow);
        var oldHistory = game.InstallationHistory.ToList();
        game.InstallationHistory.Remove(previous);
        gameService.ApplyReinstall(game, previous.GamePath, previous.ExecutablePath, previous.SavePath); game.GameVersion = previous.Version;
        if (!TrySaveGames())
        { game.GamePath = current.GamePath; game.ExecutablePath = current.ExecutablePath; game.SavePath = current.SavePath; game.GameVersion = current.Version; game.InstallationHistory = oldHistory; return; }
        ShowGameDetails(game); RefreshGameList(); ShowToast("Vorherige Installation aktiviert; aktuelle Version bleibt erhalten.");
    }
}
