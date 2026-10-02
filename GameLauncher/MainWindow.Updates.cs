using GameLauncher.models;
using GameLauncher.Service;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace GameLauncher;

public partial class MainWindow
{
    private readonly F95UpdateService f95UpdateService = new();
    private readonly DispatcherTimer f95UpdateTimer = new() { Interval = TimeSpan.FromMinutes(15) };
    private CancellationTokenSource? f95UpdateCancellation;
    private bool f95UpdateRunning;
    private Task? f95MetadataTask;
    private CancellationTokenSource? f95MetadataCancellation;
    private long f95MetadataGeneration;
    private string f95MetadataRequestSource = "";
    private string lastMetadataSource = "";
    private F95Metadata? pendingF95Metadata;
    private List<string> pendingGalleryPaths = new();

    private void InitializeF95Features()
    {
        f95UpdateTimer.Tick += async (_, _) => await RunAutomaticUpdateCheckIfDueAsync();
        RefreshAvailableUpdates();
    }

    private void ConfigureF95UpdateTimer()
    {
        f95UpdateTimer.Stop();
        if (settingsService.Settings.F95UpdateChecksEnabled && !runtimeClosed && !remotePerformanceActive)
            f95UpdateTimer.Start();
    }

    private async Task RunAutomaticUpdateCheckIfDueAsync()
    {
        if (!settingsService.Settings.F95UpdateChecksEnabled || quietMode || remotePerformanceActive || launchService.HasRunningGames || f95UpdateRunning) return;
        var eligible = gameService.GetGames().Where(g =>
            (g.Status is Constants.GameStatus.Installed or Constants.GameStatus.WaitingForUpdates) &&
            F95UpdateService.TryExtractThreadId(g.InstallSource, out _)).ToList();
        if (eligible.Count == 0) return;
        int hours = Math.Clamp(settingsService.Settings.F95UpdateCheckIntervalHours, 6, 24);
        DateTime oldest = eligible.Where(g => g.LastUpdateCheckUtc.HasValue).Select(g => g.LastUpdateCheckUtc!.Value)
            .DefaultIfEmpty(DateTime.MinValue).Min();
        bool neverChecked = eligible.Any(g => !g.LastUpdateCheckUtc.HasValue);
        bool retryError = eligible.Any(g => g.UpdateCheckError.Length > 0 &&
            (!g.LastUpdateCheckUtc.HasValue || DateTime.UtcNow - g.LastUpdateCheckUtc.Value >= TimeSpan.FromHours(1)));
        if (!neverChecked && !retryError && DateTime.UtcNow - oldest < TimeSpan.FromHours(hours)) return;
        await CheckF95UpdatesAsync(false);
    }

    private void AvailableUpdatesNavigationButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshAvailableUpdates();
        ShowView(AvailableUpdatesView);
        HighlightNav(NavUpdatesButton);
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e) => await CheckF95UpdatesAsync(true);

    private async Task CheckF95UpdatesAsync(bool manual)
    {
        if (f95UpdateRunning) return;
        if (quietMode && !manual) return;
        if (launchService.HasRunningGames && manual)
        {
            ShowToast("Die Update-Prüfung wartet bis das Spiel beendet ist.");
            return;
        }

        int eligible = gameService.GetGames().Count(g =>
            (g.Status is Constants.GameStatus.Installed or Constants.GameStatus.WaitingForUpdates) &&
            F95UpdateService.TryExtractThreadId(g.InstallSource, out _));
        if (eligible == 0)
        {
            UpdateCheckStatusText.Text = "Kein Spiel besitzt einen gültigen F95Zone-Link.";
            if (manual) ShowToast("Keine gültigen F95-Quellen gefunden.");
            return;
        }

        f95UpdateRunning = true;
        f95UpdateCancellation?.Cancel();
        f95UpdateCancellation?.Dispose();
        f95UpdateCancellation = new CancellationTokenSource();
        CheckUpdatesButton.IsEnabled = false;
        CheckUpdatesButton.Content = "Prüfung läuft …";
        UpdateCheckStatusText.Text = $"{eligible} F95-Quellen werden geprüft …";
        StatusLineText.Text = "F95-Updates werden geprüft";
        try
        {
            F95UpdateReport report = await f95UpdateService.CheckAsync(gameService.GetGames(), f95UpdateCancellation.Token);
            int galleries=0;
            if(settingsService.Settings.AutoDownloadF95Gallery)
                galleries=await DownloadMissingGalleriesAsync(f95UpdateCancellation.Token);
            TrySaveGames();
            RefreshAvailableUpdates();
            string suffix = report.MissingInstalledVersion > 0
                ? $" · {report.MissingInstalledVersion} ohne sichere installierte Version"
                : "";
            if (report.Failed > 0) suffix += $" · {report.Failed} Fehler";
            if(galleries>0)suffix+=$" · Galerien für {galleries} Spiele geladen";
            UpdateCheckStatusText.Text = $"Zuletzt geprüft: {DateTime.Now:g} · {report.Checked} geprüft · {report.Updates} Updates{suffix}";
            if (report.NewUpdates.Count > 0 && settingsService.Settings.F95UpdateNotificationsEnabled)
                ShowUpdateNotification(report.NewUpdates);
            if (manual)
                ShowToast(report.Updates == 0 ? "Keine neuen F95-Versionen gefunden." : $"{report.Updates} Updates verfügbar.");
        }
        catch (OperationCanceledException) { UpdateCheckStatusText.Text = "Update-Prüfung abgebrochen."; }
        catch (Exception ex)
        {
            UpdateCheckStatusText.Text = "Update-Prüfung fehlgeschlagen: " + ex.Message;
            if (manual) ShowToast("Update-Prüfung fehlgeschlagen.");
        }
        finally
        {
            f95UpdateRunning = false;
            CheckUpdatesButton.IsEnabled = true;
            CheckUpdatesButton.Content = "Jetzt prüfen";
            StatusLineText.Text = launchService.HasRunningGames ? "Läuft: " + launchService.RunningTitle : "Bereit";
        }
    }

    private async Task<int> DownloadMissingGalleriesAsync(CancellationToken cancellationToken)
    {
        int completed=0;
        var games=gameService.GetGames().Where(game=>game.Status!=Constants.GameStatus.Deleted &&
            F95UpdateService.TryExtractThreadId(game.InstallSource,out _) &&
            !game.GalleryPaths.Any(File.Exists)).ToList();
        foreach(var game in games)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                UpdateCheckStatusText.Text=$"Galeriebilder für {game.Name} werden nachgeladen …";
                F95Metadata metadata=await f95UpdateService.FetchMetadataAsync(game.InstallSource,cancellationToken);
                string folder=Path.Combine(gameService.Paths.GamesDataPath,"F95Galleries",metadata.ThreadId);
                var paths=await f95UpdateService.DownloadGalleryAsync(metadata.PreviewUrls,folder,metadata.ThreadId,cancellationToken);
                if(paths.Count>0){game.GalleryPaths=paths;completed++;}
            }
            catch(Exception ex) when(ex is not OperationCanceledException){System.Diagnostics.Debug.WriteLine("Galerie konnte nicht geladen werden: "+ex.Message);}
        }
        return completed;
    }

    private void RefreshAvailableUpdates()
    {
        if (AvailableUpdatesItemsControl == null || gameService == null) return;
        var updates = gameService.GetGames().Where(g => g.UpdateAvailable &&
            (g.Status is Constants.GameStatus.Installed or Constants.GameStatus.WaitingForUpdates))
            .OrderByDescending(g => g.LastUpdateCheckUtc).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        AvailableUpdatesItemsControl.ItemsSource = updates;
        var errors = gameService.GetGames().Where(g => g.UpdateCheckError.Length > 0 &&
            (g.Status is Constants.GameStatus.Installed or Constants.GameStatus.WaitingForUpdates) &&
            F95UpdateService.TryExtractThreadId(g.InstallSource, out _))
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        UpdateErrorsItemsControl.ItemsSource = errors;
        UpdateErrorsPanel.Visibility = errors.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        AvailableUpdatesEmptyState.Visibility = updates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NavUpdatesBadge.Visibility = updates.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        NavUpdatesBadgeText.Text = updates.Count.ToString();
        var recentlyUpdated = gameService.GetGames().Where(g => g.LastUpdateCheckUtc != null && g.LastUpdateCheckUtc > DateTime.MinValue &&
            (g.Status is Constants.GameStatus.Installed or Constants.GameStatus.WaitingForUpdates) && !g.UpdateAvailable)
            .OrderByDescending(g => g.LastUpdateCheckUtc).Take(6).ToList();
        RecentlyUpdatedItemsControl.ItemsSource = recentlyUpdated;
        RecentlyUpdatedLabel.Visibility = recentlyUpdated.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentlyUpdatedItemsControl.Visibility = recentlyUpdated.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenUpdateErrorSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { DataContext: Game game } ||
            !Uri.TryCreate(game.InstallSource, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https")) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
    }

    private void ShowUpdateNotification(IReadOnlyList<Game> updates)
    {
        EnsureTrayIcon();
        string names = string.Join(", ", updates.Take(3).Select(g => g.Name));
        if (updates.Count > 3) names += $" und {updates.Count - 3} weitere";
        trayIcon?.ShowBalloonTip(7000, "GameLauncher · Updates verfügbar", names,
            System.Windows.Forms.ToolTipIcon.Info);
        ShowToast(updates.Count == 1 ? $"Update für {updates[0].Name} verfügbar." : $"{updates.Count} neue Updates verfügbar.");
    }

    private async void LoadF95MetadataButton_Click(object sender, RoutedEventArgs e) => await LoadF95MetadataAsync(true, true);

    private async void InstallSourceTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        string source = InstallSourceTextBox.Text.Trim();
        if (source == lastMetadataSource || !F95UpdateService.TryExtractThreadId(source, out _)) return;
        await LoadF95MetadataAsync(false, false);
    }

    private async Task EnsureF95MetadataForSaveAsync()
    {
        string source = InstallSourceTextBox.Text.Trim();
        if (F95UpdateService.TryExtractThreadId(source, out _) && source != lastMetadataSource)
            await LoadF95MetadataAsync(false, false);
    }

    private async Task LoadF95MetadataAsync(bool showErrors, bool overwriteText)
    {
        string source = InstallSourceTextBox.Text.Trim();
        if (!F95UpdateService.TryExtractThreadId(source, out _))
        {
            if (showErrors) ShowToast("Bitte einen gültigen F95Zone-Thread-Link eingeben.");
            return;
        }

        if (f95MetadataTask is { IsCompleted: false } && source == f95MetadataRequestSource)
        {
            await f95MetadataTask;
            return;
        }

        CancelF95MetadataLoad();
        pendingF95Metadata = null;
        long generation = ++f95MetadataGeneration;
        f95MetadataRequestSource = source;
        f95MetadataCancellation = new CancellationTokenSource();
        f95MetadataTask = LoadF95MetadataCoreAsync(source, generation, showErrors, overwriteText, f95MetadataCancellation.Token);
        await f95MetadataTask;
    }

    private async Task LoadF95MetadataCoreAsync(string source, long generation, bool showErrors,
        bool overwriteText, CancellationToken cancellationToken)
    {
        bool IsCurrent() => generation == f95MetadataGeneration &&
            source == InstallSourceTextBox.Text.Trim() && !cancellationToken.IsCancellationRequested;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(settingsService.Settings.AutoDownloadF95Gallery?TimeSpan.FromMinutes(4):TimeSpan.FromSeconds(30));

        LoadF95MetadataButton.IsEnabled = false;
        LoadF95MetadataButton.Content = "F95-Daten werden geladen …";
        F95MetadataStatusText.Text = "Metadaten werden sicher vom F95Checker-Cache geladen …";
        try
        {
            F95Metadata metadata = await f95UpdateService.FetchMetadataAsync(source, timeout.Token);
            if (!IsCurrent()) return;
            pendingF95Metadata = metadata;
            lastMetadataSource = source;
            if ((!isEditingGame && overwriteText) || string.IsNullOrWhiteSpace(GameNameTextBox.Text))
                if (metadata.Name.Length > 0 && !(isEditingGame && editingGame?.UserEditedFields.Contains("Name") == true))
                    GameNameTextBox.Text = metadata.Name;
            if (!isEditingGame && (overwriteText || F95UpdateService.NormalizeVersion(GameVersionTextBox.Text) == null))
                if (!(isEditingGame && editingGame?.UserEditedFields.Contains("GameVersion") == true))
                    GameVersionTextBox.Text = metadata.Version;

            bool imageLoaded = false;
            if (string.IsNullOrWhiteSpace(GameCoverPath) && metadata.ImageUrl.Length > 0)
            {
                string previewFolder = Path.Combine(gameService.Paths.GamesDataPath, "F95Preview");
                string? image = await f95UpdateService.DownloadImageAsync(metadata.ImageUrl, previewFolder, metadata.ThreadId, timeout.Token);
                if (!IsCurrent()) return;
                if (image != null && TryLoadBitmap(image) != null)
                {
                    GameCoverPath = image;
                    GameCoverImage.ImagePath = image;
                    GameCoverImage.FocusX = GameCoverImage.FocusY = .5;
                    imageLoaded = true;
                }
            }
            pendingGalleryPaths.Clear();
            if(settingsService.Settings.AutoDownloadF95Gallery && metadata.PreviewUrls.Count>0)
            {
                F95MetadataStatusText.Text=$"{metadata.PreviewUrls.Count} Galeriebilder werden geladen …";
                string galleryFolder=Path.Combine(gameService.Paths.GamesDataPath,"F95Galleries",metadata.ThreadId);
                pendingGalleryPaths=await f95UpdateService.DownloadGalleryAsync(metadata.PreviewUrls,galleryFolder,metadata.ThreadId,timeout.Token);
                if(!IsCurrent())return;
            }
            if (!IsCurrent()) return;
            string details = string.Join(" · ", new[] { metadata.Developer, metadata.Status }.Where(x => !string.IsNullOrWhiteSpace(x)));
            F95MetadataStatusText.Text = $"Geladen: {metadata.Version}" + (details.Length > 0 ? " · " + details : "") +
                (imageLoaded ? " · Titelbild übernommen" : GameCoverPath.Length > 0 ? " · vorhandenes Cover beibehalten" : "")+
                (pendingGalleryPaths.Count>0?$" · {pendingGalleryPaths.Count} Bilder gespeichert":"");
            if (showErrors) ShowToast("F95-Daten geladen.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (OperationCanceledException)
        {
            if (!IsCurrent()) return;
            F95MetadataStatusText.Text = "Zeitüberschreitung beim Laden der F95-Daten.";
            if (showErrors) ShowToast("F95-Daten konnten nicht rechtzeitig geladen werden.");
        }
        catch (Exception ex)
        {
            if (!IsCurrent()) return;
            F95MetadataStatusText.Text = "F95-Daten konnten nicht geladen werden: " + ex.Message;
            if (showErrors) ShowToast("F95-Daten konnten nicht geladen werden.");
        }
        finally
        {
            if (generation == f95MetadataGeneration)
            {
                LoadF95MetadataButton.IsEnabled = true;
                LoadF95MetadataButton.Content = "F95-Daten laden";
            }
        }
    }

    private void CancelF95MetadataLoad()
    {
        f95MetadataGeneration++;
        f95MetadataCancellation?.Cancel();
        f95MetadataCancellation?.Dispose();
        f95MetadataCancellation = null;
        f95MetadataTask = null;
        f95MetadataRequestSource = "";
    }

    private void ResetF95FormState()
    {
        CancelF95MetadataLoad();
        pendingF95Metadata = null;
        pendingGalleryPaths.Clear();
        lastMetadataSource = "";
        if (F95MetadataStatusText != null)
            F95MetadataStatusText.Text = "Name, Version, Entwickler und Titelbild werden aus einem gültigen F95-Link übernommen.";
    }
}
