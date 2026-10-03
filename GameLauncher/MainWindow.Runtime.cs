using GameLauncher.Constants;
using GameLauncher.models;
using GameLauncher.Service;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace GameLauncher;

public partial class MainWindow
{
    private readonly DiscoveryService discovery = new();
    private GlobalKeyboardShortcut? keyboardShortcut;
    private readonly DispatcherTimer playtimeTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private CancellationTokenSource? scanCancellation;
    private readonly List<ScannedExecutable> detectedGames = new();
    private readonly HashSet<string> knownArchivePaths = new(StringComparer.OrdinalIgnoreCase);
    private bool scanning, hasScanned, quietMode, runtimeClosed, capturingShortcut;
    private int pendingShortcutKey = 0x48;
    private uint pendingShortcutModifiers = 3;
    private Game? featuredGame;
    private bool fileOperationActive;
    private CancellationTokenSource? fileOperationCancellation;
    internal void BringToFront() => RestoreFromTray();
    private Task<bool> RunFileOperationAsync(Func<bool> operation, string title) => RunFileOperationAsync(operation, title, false);
    private async Task<bool> RunFileOperationAsync(Func<bool> operation, string title, bool canCancel)
    {
        if (fileOperationActive || f95UpdateRunning) { ShowToast("Bitte laufende Verarbeitung abwarten."); return false; }
        fileOperationActive = true;
        fileOperationCancellation = new CancellationTokenSource();
        BusyCancelButton.IsEnabled = true;
        BusyCancelButton.Visibility = canCancel ? Visibility.Visible : Visibility.Collapsed;
        discovery.Pause(true);
        scanCancellation?.Cancel();
        BusyText.Text = title;
        BusyOverlay.Visibility = Visibility.Visible;
        try { return await Task.Run(operation); }
        catch (Exception ex) { Debug.WriteLine(ex); ShowToast(ex.Message); return false; }
        finally
        {
            fileOperationCancellation?.Dispose(); fileOperationCancellation = null;
            fileOperationActive = false;
            BusyOverlay.Visibility = Visibility.Collapsed;
            discovery.Pause(quietMode);
        }
    }

    private void CancelFileOperation_Click(object sender, RoutedEventArgs e) { fileOperationCancellation?.Cancel(); BusyCancelButton.IsEnabled = false; }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        keyboardShortcut = new GlobalKeyboardShortcut(this);
        keyboardShortcut.Triggered += OnGlobalMouseShortcut;
        int dark = 1;
        DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int));
        discovery.Changed += () =>
        {
            if (!runtimeClosed) Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!quietMode && !scanning) _ = ScanDetectedGamesAsync();
            }));
        };
        playtimeTimer.Tick += (_, _) => CheckpointPlaytime();
        playtimeTimer.Start();
        ConfigureRuntimeFeatures();
        if (settingsService.Settings.AutoDiscoveryEnabled) _ = ScanDetectedGamesAsync();
        _ = RunAutomaticUpdateCheckIfDueAsync();
        _ = RefreshPathWarningsAsync();
    }

    private async Task RefreshPathWarningsAsync(bool showFeedback = false)
    {
        if (runtimeClosed) return;
        var games = gameService.GetGames();
        var warnings = await Task.Run(() => games.Select(game => (game, warning: GameService.GetPathWarning(game))).ToList());
        if (runtimeClosed) return;
        foreach (var item in warnings) item.game.PathWarning = item.warning;
        RefreshLibraryList();
        if (selectedGame != null && GameDetailView.Visibility == Visibility.Visible) ShowGameDetails(selectedGame);
        if (showFeedback)
        {
            int count = warnings.Count(item => item.warning.Length > 0);
            ShowToast(count == 0 ? "Alle Spielpfade sind erreichbar." : $"{count} Spiele benötigen eine neue Verknüpfung.");
        }
    }

    private void ConfigureRuntimeFeatures()
    {
        ConfigureF95UpdateTimer();
        if (keyboardShortcut == null) return;
        var settings = settingsService.Settings;
        if (!keyboardShortcut.Configure(settings.KeyboardShortcutEnabled, settings.ShortcutModifiers, settings.ShortcutKey))
            ShowToast("Die Tastenkombination ist bereits belegt. Bitte eine andere Kombination wählen.");
        discovery.Watch(settings.AutoDiscoveryEnabled
            ? DiscoveryService.WatchRoots(settings.DefaultGameFolder) : Array.Empty<string>());
        discovery.Pause(quietMode || remotePerformanceActive);
    }

    private void LoadRuntimeSettingsForm()
    {
        var s = settingsService.Settings;
        SettingsEnergyCheckBox.IsChecked = s.LowEnergyWhilePlaying;
        SettingsRestoreCheckBox.IsChecked = s.RestoreAfterGame;
        SettingsMinimizeGameCheckBox.IsChecked = s.MinimizeGameOnShortcut;
        SettingsKeyboardCheckBox.IsChecked = s.KeyboardShortcutEnabled;
        SettingsDiscoveryCheckBox.IsChecked = s.AutoDiscoveryEnabled;
        SettingsAutoAddCheckBox.IsChecked = s.AutoAddDetectedGames;
        pendingShortcutKey = s.ShortcutKey;
        pendingShortcutModifiers = s.ShortcutModifiers;
        SettingsHotkeyTextBox.Text = FormatShortcut(pendingShortcutModifiers, pendingShortcutKey);
    }

    private static string FormatShortcut(uint modifiers, int key)
    {
        string result = "";
        if ((modifiers & 2) != 0) result += "Ctrl + ";
        if ((modifiers & 1) != 0) result += "Alt + ";
        if ((modifiers & 4) != 0) result += "Shift + ";
        if ((modifiers & 8) != 0) result += "Win + ";
        return result + KeyInterop.KeyFromVirtualKey(key);
    }
    private void CaptureHotkey_Click(object sender, RoutedEventArgs e)
    {
        capturingShortcut = true;
        keyboardShortcut?.Configure(false, 0, 0);
        SettingsHotkeyTextBox.Text = "Tastenkombination drücken ...";
        SettingsHotkeyTextBox.Focus();
    }
    private void CaptureHotkey_KeyDown(object sender, KeyEventArgs e)
    {
        if (!capturingShortcut) return;
        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            capturingShortcut = false;
            SettingsHotkeyTextBox.Text = FormatShortcut(pendingShortcutModifiers, pendingShortcutKey);
            ConfigureRuntimeFeatures();
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None) { ShowToast("Mindestens Ctrl, Alt oder Shift mit einer weiteren Taste kombinieren."); return; }
        pendingShortcutModifiers = (uint)modifiers;
        pendingShortcutKey = KeyInterop.VirtualKeyFromKey(key);
        SettingsHotkeyTextBox.Text = FormatShortcut(pendingShortcutModifiers, pendingShortcutKey);
        capturingShortcut = false;
        ConfigureRuntimeFeatures();
    }

    private void EnterQuietMode()
    {
        quietMode = true;
        downloads?.PauseAll();
        discovery.Pause(true);
        scanCancellation?.Cancel();
        toastTimer.Stop();
        ToastHost.Visibility = Visibility.Collapsed;
        HideToTray();
        SetLauncherPriority(true);
        StatusLineText.Text = "Ruhemodus · " + launchService.RunningTitle;
    }
    private void ExitQuietMode()
    {
        bool wasQuiet = quietMode;
        quietMode = false;
        SetLauncherPriority(false);
        discovery.Pause(false);
        if (wasQuiet && settingsService.Settings.RestoreAfterGame) RestoreFromTray();
        StatusLineText.Text = "Bereit";
    }
    private void SetLauncherPriority(bool low)
    {
        try { using var process = Process.GetCurrentProcess(); process.PriorityClass = remotePerformanceActive ? ProcessPriorityClass.AboveNormal : low ? ProcessPriorityClass.BelowNormal : ProcessPriorityClass.Normal; }
        catch (Exception ex) { Debug.WriteLine(ex.Message); }
        launchService.SetPerformanceMode(remotePerformanceActive);
    }
    private void CheckpointPlaytime()
    {
        if (!launchService.HasRunningGames) return;
        foreach (var session in launchService.Checkpoint()) session.Game.Playtime += session.Duration;
        TrySaveGames();
    }
    private void RefreshDashboard()
    {
        var all = gameService.QueryGames(GameListFilter.All,null);
        var installed = all.Where(g => g.IsInstalled && g.Status != GameStatus.Deleted && File.Exists(g.ExecutablePath)).ToList();
        HomeDashboard.Model.Summary = all.Count + " Spiele · lokal auf deinem PC";
        HomeDashboard.Model.TotalTime = FormatHelper.FormatPlaytime(TimeSpan.FromTicks(all.Sum(g => g.Playtime.Ticks)));
        HomeDashboard.Model.Favorites = all.Count(g => g.IsFavorite).ToString();
        HomeDashboard.Model.Waiting = all.Count(g => g.Status == GameStatus.WaitingForUpdates).ToString();
        var recent = installed.Where(g => g.LastPlayed != null).OrderByDescending(g => g.LastPlayed).Take(5).ToList();
        HomeDashboard.RecentItems.ItemsSource = recent;
        HomeDashboard.RecentSection.Visibility = recent.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        var recentlyAdded = installed.Where(g => g.AddedDate != DateTime.MinValue && g.AddedDate != default)
            .OrderByDescending(g => g.AddedDate).Take(5).ToList();
        HomeDashboard.RecentlyAddedItems.ItemsSource = recentlyAdded;
        HomeDashboard.RecentlyAddedSection.Visibility = recentlyAdded.Count <= 1 ? Visibility.Collapsed : Visibility.Visible;
        var recentlyUpdated = installed.Where(g => g.UpdateAvailable && g.Status is GameStatus.Installed or GameStatus.WaitingForUpdates)
            .OrderByDescending(g => g.AddedDate).Take(5).ToList();
        HomeDashboard.RecentlyUpdatedItems.ItemsSource = recentlyUpdated;
        HomeDashboard.RecentlyUpdatedSection.Visibility = recentlyUpdated.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        var favorites = all.Where(g => g.IsFavorite).Take(5).ToList();
        HomeDashboard.FavoritesItems.ItemsSource = favorites;
        HomeDashboard.FavoritesSection.Visibility = favorites.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var mix = installed.OrderByDescending(g => g.IsFavorite).ThenBy(g => g.LastPlayed ?? DateTime.MinValue).ThenBy(g => g.Name).Take(5).ToList();
        HomeDashboard.SmartMixItems.ItemsSource = mix;
        HomeDashboard.SmartMixSection.Visibility = mix.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SidebarStatsText.Text = all.Count + " Spiele · " + (int)all.Sum(g => g.Playtime.TotalHours) + " Stunden";
        CollectionSummaryText.Text = installed.Count + " bereit";
        featuredGame = installed.OrderByDescending(g => g.LastPlayed).FirstOrDefault();
        FeaturedHost.Visibility = featuredGame == null ? Visibility.Collapsed : Visibility.Visible;
        if (featuredGame == null) return;
        HomeDashboard.FeaturedPlayButton.Content = launchService.IsRunning(featuredGame.Id) ? "Zum Spiel" : "▶  Spielen";
        FeaturedTitle.Text = featuredGame.Name;
        FeaturedImage.ImagePath = featuredGame.CoverPath;
        FeaturedImage.FocusX=featuredGame.CoverFocusX;FeaturedImage.FocusY=featuredGame.CoverFocusY;
        FeaturedMeta.Text = FormatHelper.FormatPlaytime(featuredGame.Playtime) + "  ·  " + FormatHelper.FormatLastPlayed(featuredGame.LastPlayed);
    }
    private void FeaturedPlay_Click(object sender, RoutedEventArgs e) => PlayGame(featuredGame);
    private void CardFavorite_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { DataContext: Game game })
        {
            game.IsFavorite = !game.IsFavorite;
            TrySaveGames();
            RefreshGameList();
        }
    }
    private void OpenDeleteDialog(Game game)
    {
        if (launchService.IsRunning(game.Id)) { ShowToast("Bitte das Spiel zuerst beenden."); return; }
        selectedGame = game;
        DeleteTargetText.Text = game.Name + "\nInstallation: " + game.GamePath + "\nSpielstand: " + (string.IsNullOrWhiteSpace(game.SavePath) ? "Nicht angegeben" : game.SavePath) + "\nArchiv: " + gameService.Paths.GetSaveArchiveForGame(game.Id);
        NoSaveConfirmCheckBox.Visibility = string.IsNullOrWhiteSpace(game.SavePath) ? Visibility.Visible : Visibility.Collapsed;
        NoSaveConfirmCheckBox.IsChecked = false;
        if (game.IsInstalled) DeleteGameDialog.Visibility = Visibility.Visible;
        else AbsoluteDeleteDialog.Visibility = Visibility.Visible;
    }
    private void PrefillFromExecutable(string file)
    {
        var candidate = DiscoveryService.Describe(file);
        if (candidate == null) return;
        if (GameNameTextBox.Text.Length == 0) GameNameTextBox.Text = candidate.Name;
        if (GameVersionTextBox.Text.Length == 0) GameVersionTextBox.Text = candidate.Version;
        if (InstallSourceTextBox.Text.Length == 0) InstallSourceTextBox.Text = "Lokal";
        if (GameFolderPath.Length == 0) { GameFolderPath = candidate.GamePath; GamePathTextBox.Text = candidate.GamePath; }
        if (GameSaveFolderPath.Length == 0 && candidate.SavePath.Length > 0)
        { GameSaveFolderPath = candidate.SavePath; SavePathTextBox.Text = candidate.SavePath; }
    }
    private async void DiscoverNow_Click(object sender, RoutedEventArgs e) => await ScanDetectedGamesAsync();
    private async Task ScanDetectedGamesAsync(string? folder = null)
    {
        if (scanning || runtimeClosed || fileOperationActive) return;
        if (quietMode) { ShowToast("Die Spielerkennung pausiert waehrend des Spielens."); return; }
        scanning = true;
        scanCancellation?.Dispose();
        scanCancellation = new CancellationTokenSource();
        var token = scanCancellation.Token;
        DiscoverNowButton.IsEnabled = false;
        CancelScanButton.IsEnabled = true;
        DiscoveryStatusText.Text = "Suche nach Spielen ...";
        try
        {
            var roots = DiscoveryService.DefaultRoots(settingsService.Settings.DefaultGameFolder);
            var found = await Task.Run(() => folder == null ? DiscoveryService.Scan(roots, token) : DiscoveryService.ScanFolder(folder, token), token);
            if (token.IsCancellationRequested || runtimeClosed) return;
            var archivePaths=found.Where(item=>item.IsArchive).Select(item=>PathSafety.Full(item.ArchivePath)).ToList();
            var newArchives=hasScanned?archivePaths.Where(path=>!knownArchivePaths.Contains(path)).ToList():new List<string>();
            knownArchivePaths.UnionWith(archivePaths);
            detectedGames.Clear();
            detectedGames.AddRange(found.Where(g => g.IsArchive || gameService.FindExecutable(g.ExecutablePath)?.IsInstalled != true));
            int added = AutoAssociateDetected(found);
            detectedGames.RemoveAll(g => !g.IsArchive && gameService.FindExecutable(g.ExecutablePath)?.IsInstalled == true);
            if (settingsService.Settings.AutoAddDetectedGames)
            {
                foreach (var game in detectedGames.Where(g => !g.IsArchive && g.Confidence >= 90).ToList())
                    if (await AddDetectedGame(game, false)) added++;
            }
            hasScanned = true;
            ImportScanItemsControl.ItemsSource = detectedGames.ToList();
            DiscoveryStatusText.Text = detectedGames.Count + " neue Funde" + (added > 0 ? " · " + added + " automatisch hinzugefuegt" : "") +
                " · Letzter Scan " + DateTime.Now.ToString("HH:mm");
            if(newArchives.Count>0 && System.Windows.MessageBox.Show($"Neue Spieldatei erkannt:\n{Path.GetFileName(newArchives[0])}\n\nJetzt automatisch entpacken und einrichten?",
                "Neues Spiel erkannt",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes)
            {
                OpenAddGameButton_Click(NavAddGameButton,new RoutedEventArgs());
                await PrepareArchiveForAddAsync(newArchives[0]);
            }
        }
        catch (OperationCanceledException) { DiscoveryStatusText.Text = "Scan pausiert."; }
        catch (Exception ex) { Debug.WriteLine(ex); DiscoveryStatusText.Text = "Scan nicht moeglich: " + ex.Message; }
        finally { scanning = false; DiscoverNowButton.IsEnabled = true; CancelScanButton.IsEnabled = false; }
    }
    private async Task<bool> AddDetectedGame(ScannedExecutable scanned, bool feedback = true)
    {
        if (gameService.FindExecutable(scanned.ExecutablePath) is Game known)
        {
            if (known.IsInstalled) { ShowToast("Dieses Spiel ist bereits vorhanden."); return false; }
            if (feedback)
            {
                // Discovery has already found a local installation, so skip the
                // install-source question and take the user straight to linking it.
                ShowReinstallForm(known);
                PrefillReinstall(scanned.ExecutablePath);
                if (scanned.SavePath.Length > 0)
                {
                    installSavePath = scanned.SavePath;
                    InstallSavePathTextBox.Text = scanned.SavePath;
                }
                InstallVersionTextBox.Text = scanned.Version;
            }
            return false;
        }
        var ranked = SmartAssociation.Rank(gameService.GetGames(), scanned);
        if (ranked.Count > 0)
        {
            if (!feedback) return false;
            var match = ranked[0];
            if (System.Windows.MessageBox.Show(match.Reason + "\n\nMit " + match.Game.Name + " verbinden?", "Zuordnung prüfen", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                if (match.Game.IsInstalled) { ShowReinstallForm(match.Game); PrefillReinstall(scanned.ExecutablePath); return false; }
                if (SmartAssociation.Link(gameService, match, scanned)) { RefreshGameList(); return true; }
            }
        }
        var game = new Game
        {
            Id = Guid.NewGuid().ToString(), Name = scanned.Name, GamePath = scanned.GamePath,
            ExecutablePath = scanned.ExecutablePath, SavePath = scanned.SavePath, CoverPath = scanned.CoverPath,
            GameVersion = scanned.Version, InstallSource = scanned.InstallSource.Length > 0 ? scanned.InstallSource : scanned.Source,
            IsInstalled = true, Status = GameStatus.Installed, AddedDate = DateTime.Now
        };
        try
        {
            if (!await MoveGameToManagedLibraryAsync(game, settingsService.Settings.ManageLocalGames)) return false;
            game.CoverPath = gameService.CacheCover(game.CoverPath, game.Id);
            gameService.AddGame(game);
            if (!TrySaveGames()) { gameService.RemoveGame(game); return false; }
            detectedGames.Remove(scanned);
            ImportScanItemsControl.ItemsSource = detectedGames.ToList();
            RefreshGameList();
            if (feedback) ShowToast(game.Name + " hinzugefuegt.");
            return true;
        }
        catch (Exception ex) { ShowToast(ex.Message); return false; }
    }

    private async Task ExtractDetectedArchiveAsync(ScannedExecutable archiveCandidate)
    {
        string archive = archiveCandidate.ArchivePath;
        var extracted = await ExtractGameArchiveAsync(archive);
        if (extracted.Result?.Success != true) return;

        detectedGames.Remove(archiveCandidate);
        ImportScanItemsControl.ItemsSource = detectedGames.ToList();
        if (extracted.Game == null)
        {
            ShowToast(extracted.Result.SourceDeleted
                ? "Archiv entpackt und gelöscht. Bitte die Startdatei im entpackten Ordner später auswählen."
                : "Entpackt und hinzugefügt; das Originalarchiv bleibt erhalten.");
            return;
        }

        bool added = await AddDetectedGame(extracted.Game, true);
        if (added)
            ShowToast(extracted.Result.SourceDeleted
                ? $"Mit {extracted.Result.Tool} entpackt, Archiv gelöscht und Spiel hinzugefügt."
                : "Entpackt und hinzugefügt; das Originalarchiv bleibt erhalten.");
    }
    private void ExportJson_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "JSON|*.json", FileName = "games-export.json" };
        if (dialog.ShowDialog() != true) return;
        try { gameService.ExportGames(dialog.FileName); ShowToast("Bibliothek exportiert."); }
        catch (Exception ex) { ShowToast(ex.Message); }
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
