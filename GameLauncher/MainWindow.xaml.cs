using GameLauncher.Constants;
using GameLauncher.models;
using GameLauncher.Service;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Control = System.Windows.Controls.Control;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace GameLauncher
{
    public partial class MainWindow : Window
    {
        private string GameFolderPath = "";
        private string GameExecutablePath = "";
        private string GameSaveFolderPath = "";
        private string GameCoverPath = "";

        private string installGamePath = "";
        private string installExecutablePath = "";
        private string installSavePath = "";

        private SettingsService settingsService = new SettingsService();
        private GameService gameService;
        private readonly GameLaunchService launchService = new GameLaunchService();
        private readonly ImportService importService = new ImportService();
        private GlobalMouseShortcut? mouseShortcut;

        private Game? editingGame;
        private Game? pendingDuplicateGame;
        private Game? selectedGame;
        private Game? reinstallGame;
        private bool isEditingGame;
        private bool fullDeleteRequested;
        private UIElement? lastListView;
        private GameListFilter libraryFilter = GameListFilter.All;
        private bool libraryListView;
        private Forms.NotifyIcon? trayIcon;
        private bool forceExit;
        private readonly DispatcherTimer toastTimer = new DispatcherTimer();

        public MainWindow()
        {
            InitializeComponent();
            AppPaths paths = settingsService.LoadAndResolvePaths();
            gameService = new GameService(paths);
            InitializeRemoteHost();
            _ = new WindowSizing(this);
            AddHandler(Controls.GameCard.OpenRequestedEvent,new RoutedEventHandler((sender,e) => { if(e.OriginalSource is FrameworkElement {DataContext:Game game}) ShowGameDetails(game); }));
            AddHandler(Controls.GameCard.LaunchRequestedEvent,new RoutedEventHandler((sender,e) => { if(e.OriginalSource is FrameworkElement {DataContext:Game game}) { if(game.IsInstalled) PlayGame(game); else OpenReinstall(game); } }));
            AddHandler(Controls.GameCard.FavoriteRequestedEvent,new RoutedEventHandler((sender,e) => { if(e.OriginalSource is FrameworkElement {DataContext:Game game}) { game.IsFavorite=!game.IsFavorite; TrySaveGames(); RefreshGameList(); } }));
            AddHandler(Controls.GameCard.OpenFolderRequestedEvent, new RoutedEventHandler((sender, e) => { if (e.OriginalSource is FrameworkElement { DataContext: Game game }) OpenFolderIfExists(game.GamePath); }));
            AddHandler(Controls.GameCard.OpenF95RequestedEvent, new RoutedEventHandler((sender, e) => { if (e.OriginalSource is FrameworkElement { DataContext: Game game }) OpenF95Thread(game); }));
            AddHandler(Controls.GameCard.EditRequestedEvent, new RoutedEventHandler((sender, e) => { if (e.OriginalSource is FrameworkElement { DataContext: Game game }) { selectedGame = game; EditGameButton_Click(sender, e); } }));
            AddHandler(Controls.GameCard.DeleteRequestedEvent, new RoutedEventHandler((sender, e) => { if (e.OriginalSource is FrameworkElement { DataContext: Game game }) { selectedGame = game; OpenDeleteDialog(game); } }));
            AddHandler(Controls.GameCard.StatusChangeRequestedEvent, new RoutedEventHandler((sender, e) => { if (e is Controls.StatusChangeRoutedEventArgs args && e.OriginalSource is FrameworkElement { DataContext: Game game }) { selectedGame = game; gameService.UpdateStatus(game.Id, args.NewStatus); TrySaveGames(); RefreshGameList(); ShowToast($"Status zu \"{args.NewStatus}\" geändert."); } }));

            InitializeF95Features();
            InitializeBackups();
            InitializeDownloads();

            lastListView = HomeView;
            toastTimer.Interval = TimeSpan.FromSeconds(4);
            toastTimer.Tick += (_, _) =>
            {
                ToastHost.Visibility = Visibility.Collapsed;
                toastTimer.Stop();
            };

            launchService.SessionEnded += OnSessionEnded;
            launchService.LaunchFailed += message => Dispatcher.Invoke(() => ShowToast(message));

            AttachValidationResetHandlers();

            mouseShortcut = new GlobalMouseShortcut(Dispatcher);
            mouseShortcut.Triggered += OnGlobalMouseShortcut;

            ApplyRuntimeSettings();
            RefreshGameList();
            HighlightNav(NavHomeButton);

            SetupWizard.SetupCompleted += OnSetupWizardCompleted;
            SetupWizard.SetupSkipped += OnSetupWizardSkipped;
            if (settingsService.Settings.IsFirstRun && gameService.GetGames().Count == 0)
            {
                ShowView(SetupWizardHost);
                SetupWizard.Reset();
            }
            searchDelay.Tick += (_,_)=>{searchDelay.Stop();ApplySearch();};
            InitializeSmoke();
            Controls.SmoothScroll.Attach(this);
            Loaded += MainWindow_Loaded;
            SizeChanged += (_, _) => UpdateCompactLayout();

            if (!string.IsNullOrWhiteSpace(gameService.LoadError))
                MessageBox.Show(gameService.LoadError, "Hinweis", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void ApplyRuntimeSettings()
        {
            models.AppSettings settings = settingsService.Settings;

            if (settings.GlobalMouseShortcutEnabled)
                mouseShortcut?.Start();
            else
                mouseShortcut?.Stop();

            ApplyPresentation();
            ApplyTraySetting(settings.MinimizeToTray);
            ConfigureRuntimeFeatures();
            ApplyRemoteHostSettings();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (fileOperationActive) { e.Cancel = true; return; }
            if ((settingsService.Settings.MinimizeToTray || launchService.HasRunningGames) && !forceExit)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            CheckpointPlaytime();
            runtimeClosed = true;
            playtimeTimer.Stop();
            searchDelay.Stop();
            scanCancellation?.Cancel();
            f95UpdateCancellation?.Cancel();
            f95UpdateTimer.Stop();
            CancelF95MetadataLoad();
            discovery.Dispose();
            keyboardShortcut?.Dispose();
            trayIcon?.Dispose();
            mouseShortcut?.Dispose();
            ClosePrivacyCurtain();
            remoteHost.Dispose();
            launchService.SessionEnded -= OnSessionEnded;
            launchService.Dispose();
            f95UpdateService.Dispose();
            downloads?.Dispose();
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);

            if (WindowState == WindowState.Minimized && settingsService.Settings.MinimizeToTray)
                HideToTray();
        }

        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (capturingShortcut) return;
            Key pressedKey = e.Key == Key.System ? e.SystemKey : e.Key;
            bool navigationBlocked = fileOperationActive || DeleteGameDialog.Visibility == Visibility.Visible ||
                AbsoluteDeleteDialog.Visibility == Visibility.Visible || SaveConflictDialog.Visibility == Visibility.Visible ||
                ReinstallChoiceDialog.Visibility == Visibility.Visible || SaveFinderDialog.Visibility == Visibility.Visible ||
                DuplicateGameDialog.Visibility == Visibility.Visible;
            if (!navigationBlocked && Keyboard.Modifiers == ModifierKeys.Alt)
            {
                if (pressedKey == Key.D1) HomeNavigationButton_Click(NavHomeButton, new RoutedEventArgs());
                else if (pressedKey == Key.D2) LibraryNavigationButton_Click(NavLibraryButton, new RoutedEventArgs());
                else if (pressedKey == Key.D3) AvailableUpdatesNavigationButton_Click(NavUpdatesButton, new RoutedEventArgs());
                else if (pressedKey == Key.D4) OpenAddGameButton_Click(NavAddGameButton, new RoutedEventArgs());
                else if (pressedKey == Key.D5) SettingsNavigationButton_Click(NavSettingsButton, new RoutedEventArgs());
                else goto ContinueKeyHandling;
                e.Handled = true;
                return;
            }
        ContinueKeyHandling:
            if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control && !fileOperationActive && DeleteGameDialog.Visibility != Visibility.Visible && AbsoluteDeleteDialog.Visibility != Visibility.Visible && SaveConflictDialog.Visibility != Visibility.Visible)
            { LibraryNavigationButton_Click(NavLibraryButton,new RoutedEventArgs()); SearchGamesTextBox.Focus(); SearchGamesTextBox.SelectAll(); e.Handled = true; return; }
            if (e.Key == Key.Escape)
            {
                if(ReinstallChoiceDialog.Visibility==Visibility.Visible){ReinstallChoiceDialog.Visibility=Visibility.Collapsed;e.Handled=true;return;}
                if(SaveFinderDialog.Visibility==Visibility.Visible){SaveFinderDialog.Visibility=Visibility.Collapsed;e.Handled=true;return;}
                if (SaveConflictDialog.Visibility == Visibility.Visible)
                {
                    CloseSaveConflict(SaveRestoreMode.Cancel);
                    e.Handled = true;
                    return;
                }

                if (AbsoluteDeleteDialog.Visibility == Visibility.Visible)
                {
                    AbsoluteDeleteDialog.Visibility = Visibility.Collapsed;
                    e.Handled = true;
                    return;
                }

                if (DeleteGameDialog.Visibility == Visibility.Visible)
                {
                    DeleteGameDialog.Visibility = Visibility.Collapsed;
                    e.Handled = true;
                    return;
                }

                if (DuplicateGameDialog.Visibility == Visibility.Visible)
                {
                    DuplicateGameDialog.Visibility = Visibility.Collapsed;
                    pendingDuplicateGame = null;
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.Enter && AddGameView.Visibility == Visibility.Visible && e.OriginalSource is System.Windows.Controls.TextBox { AcceptsReturn: false })
            {
                AddGameButton_Click(SaveGameButton, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private void SelectGameFolderButton_Click(object sender, RoutedEventArgs e)
        {
            string? folder = PickFolder(settingsService.Settings.DefaultGameFolder);
            if (folder == null)
                return;

            GameFolderPath = folder;
            GamePathTextBox.Text = folder;
            ClearValidation(GamePathTextBox);
        }

        private void SelectExecutableButton_Click(object sender, RoutedEventArgs e)
        {
            string? file = PickExe(GameFolderPath);
            if (file == null)
                return;

            GameExecutablePath = file;
            ExecutableTextBox.Text = file;
            ClearValidation(ExecutableTextBox);
            PrefillFromExecutable(file);
        }

        private void SelectSaveFolderButton_Click(object sender, RoutedEventArgs e)
        {
            string initial=Directory.Exists(GameFolderPath)?GameFolderPath:settingsService.Settings.DefaultSaveFolder;
            string? folder = PickFolder(initial);
            if (folder == null)
                return;

            GameSaveFolderPath = folder;
            SavePathTextBox.Text = folder;
            ClearValidation(SavePathTextBox);
        }

        private void SelectCoverButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "Bilddateien (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|Alle Dateien (*.*)|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            };

            if (dialog.ShowDialog() != true)
                return;

            BitmapImage? bitmap = TryLoadBitmap(dialog.FileName);
            if (bitmap == null)
            {
                ShowToast("Cover konnte nicht geladen werden.");
                return;
            }

            GameCoverImage.ImagePath = dialog.FileName;
            GameCoverImage.FocusX=GameCoverImage.FocusY=.5;
            GameCoverPath = dialog.FileName;
            GameCoverBorder.ClearValue(Border.BorderBrushProperty);
            GameCoverBorder.ClearValue(Border.BorderThicknessProperty);
        }

        private async void AddGameButton_Click(object sender, RoutedEventArgs e)
        {
            await EnsureF95MetadataForSaveAsync();
            bool requirePaths = !isEditingGame || (editingGame?.IsInstalled ?? true);

            if (!ValidateGameForm(requirePaths))
            {
                ShowToast("Bitte alle Pflichtfelder ausfüllen.");
                return;
            }

            if (isEditingGame)
                await SaveEditedGame();
            else
                await AddNewGame();
        }

        private async Task AddNewGame()
        {
            if (gameService.FindExecutable(GameExecutablePath) is Game known)
            {
                ShowToast("Diese Startdatei gehoert bereits zu " + known.Name + ".");
                return;
            }
            if (pendingF95Metadata != null &&
                F95UpdateService.TryExtractThreadId(InstallSourceTextBox.Text.Trim(), out string newThreadId))
            {
                if (gameService.FindGameByF95ThreadId(newThreadId) is Game existing)
                {
                    pendingDuplicateGame = existing;
                    DuplicateGameText.Text = $"\"{existing.Name}\" (v{existing.GameVersion}) ist bereits in der Bibliothek.";
                    DuplicateAddVersionButton.IsEnabled = !string.IsNullOrWhiteSpace(GameFolderPath);
                    DuplicateGameDialog.Visibility = Visibility.Visible;
                    return;
                }
            }
            Game game = new Game
            {
                Id = Guid.NewGuid().ToString(),
                Name = GameNameTextBox.Text.Trim(),
                GameVersion = GameVersionTextBox.Text.Trim(),
                Notes = GameNotesTextBox.Text,
                VersionInfo = GameVersionInfoTextBox.Text,
                InstallSource = InstallSourceTextBox.Text.Trim(),
                ModUrl = GameModUrlTextBox.Text.Trim(),
                GalleryPaths = pendingGalleryPaths.ToList(),
                CoverPath = GameCoverPath,
                CoverFocusX=GameCoverImage.FocusX,CoverFocusY=GameCoverImage.FocusY,
                GamePath = GameFolderPath,
                ExecutablePath = GameExecutablePath,
                SavePath = GameSaveFolderPath,
                IsInstalled = true,
                IsFavorite = GameFavoriteCheckBox.IsChecked == true,
                Status = GameStatus.Installed,
                AddedDate = DateTime.Now
            };
            if (pendingF95Metadata != null &&
                F95UpdateService.TryExtractThreadId(game.InstallSource, out string f95ThreadId) &&
                pendingF95Metadata.ThreadId == f95ThreadId)
            {
                game.F95ThreadId = pendingF95Metadata.ThreadId;
                game.LatestKnownVersion = pendingF95Metadata.Version;
                MetadataEnrichment.Apply(game, pendingF95Metadata);
                game.F95Status = pendingF95Metadata.Status;
                game.UpdateAvailable = F95UpdateService.IsUpdateAvailable(game.GameVersion, game.LatestKnownVersion, game.IgnoredUpdateVersion);
                TrackUserEdits(game, pendingF95Metadata);
            }

            if (!await MoveGameToManagedLibraryAsync(game, ManageLocalGameCheckBox.IsChecked == true)) return;

            try { game.CoverPath = gameService.CacheCover(game.CoverPath, game.Id); }
            catch (Exception ex) { ShowToast("Cover konnte nicht gespeichert werden: " + ex.Message); return; }
            gameService.AddGame(game);
            if (!TrySaveGames()) { gameService.RemoveGame(game); return; }
            RefreshGameList();
            ResetForm();
            ShowView(HomeView);
            ShowToast("Spiel hinzugefügt.");
        }

        private void OpenAddGameButton_Click(object sender, RoutedEventArgs e)
        {
            ResetForm();
            ShowView(AddGameView);
            HighlightNav(NavAddGameButton);
        }

        private async void WishlistButton_Click(object sender, RoutedEventArgs e)
        {
            await EnsureF95MetadataForSaveAsync();
            if (string.IsNullOrWhiteSpace(GameNameTextBox.Text)) { MarkInvalid(GameNameTextBox); ShowToast("Bitte einen Namen für die Wunschliste eingeben."); return; }
            var duplicateWish = gameService.GetGames().FirstOrDefault(g => (F95UpdateService.TryExtractThreadId(InstallSourceTextBox.Text, out var id) && g.F95ThreadId == id) || (g.Status == GameStatus.Wishlist && g.Name.Equals(GameNameTextBox.Text.Trim(), StringComparison.OrdinalIgnoreCase)));
            if (duplicateWish != null) { ShowGameDetails(duplicateWish); ShowToast("Dieses Spiel ist bereits gemerkt."); return; }
            Game game = new Game
            {
                Id = Guid.NewGuid().ToString(),
                Name = GameNameTextBox.Text.Trim(),
                GameVersion = GameVersionTextBox.Text.Trim(),
                Notes = GameNotesTextBox.Text,
                VersionInfo = GameVersionInfoTextBox.Text,
                InstallSource = InstallSourceTextBox.Text.Trim(),
                ModUrl = GameModUrlTextBox.Text.Trim(),
                GalleryPaths = pendingGalleryPaths.ToList(),
                CoverPath = GameCoverPath,
                CoverFocusX = GameCoverImage.FocusX, CoverFocusY = GameCoverImage.FocusY,
                GamePath = "", ExecutablePath = "", SavePath = "",
                IsInstalled = false, IsFavorite = GameFavoriteCheckBox.IsChecked == true,
                Status = GameStatus.Wishlist, AddedDate = DateTime.Now, SearchIntentUtc = DateTime.UtcNow
            };
            if (pendingF95Metadata != null &&
                F95UpdateService.TryExtractThreadId(game.InstallSource, out string f95ThreadId) &&
                pendingF95Metadata.ThreadId == f95ThreadId)
            {
                game.F95ThreadId = pendingF95Metadata.ThreadId;
                game.LatestKnownVersion = pendingF95Metadata.Version;
                MetadataEnrichment.Apply(game, pendingF95Metadata);
                game.F95Status = pendingF95Metadata.Status;
                game.UpdateAvailable = F95UpdateService.IsUpdateAvailable(game.GameVersion, game.LatestKnownVersion, game.IgnoredUpdateVersion);
                TrackUserEdits(game, pendingF95Metadata);
            }
            try { game.CoverPath = gameService.CacheCover(game.CoverPath, game.Id); }
            catch (Exception ex) { ShowToast("Cover konnte nicht gespeichert werden: " + ex.Message); return; }
            gameService.AddGame(game);
            if (!TrySaveGames()) { gameService.RemoveGame(game); return; }
            RefreshGameList();
            ResetForm();
            ShowView(HomeView);
            ShowToast("Spiel zur Wunschliste hinzugefügt.");
        }

        private void HomeNavigationButton_Click(object sender, RoutedEventArgs e)
        {
            ShowView(HomeView);
            HighlightNav(NavHomeButton);
        }

        private void LibraryNavigationButton_Click(object sender, RoutedEventArgs e)
        {
            ShowView(LibraryView);
            HighlightNav(NavLibraryButton);
        }

        private void BackToHomeButton_Click(object sender, RoutedEventArgs e)
        {
            ResetForm();
            ShowView(lastListView ?? HomeView);
        }

        private void PlayGameButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not Button button || button.DataContext is not Game game)
                return;

            PlayGame(game);
        }

        private void InstallGameButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not Button button || button.DataContext is not Game game)
                return;

            OpenReinstall(game);
        }

        private void DeleteGameButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not Button button || button.DataContext is not Game game)
                return;

            selectedGame = game;
            OpenDeleteDialog(game);
        }

        private void WaitingForUpdatesButton_Click(object sender, RoutedEventArgs e)
        {
            ArchiveAndMarkGame(GameStatus.WaitingForUpdates, "Zu Waiting for Updates verschoben.");
        }

        private void WaitingForUpdatesButtonNav_Click(object sender, RoutedEventArgs e)
        {
            ShowView(WaitingForUpdatesView);
        }

        private void FinishedGameButton_Click(object sender, RoutedEventArgs e)
        {
            ArchiveAndMarkGame(GameStatus.Finished, "Als Finished markiert.");
        }

        private void FinishedButtonNav_Click(object sender, RoutedEventArgs e)
        {
            ShowView(FinishedView);
        }

        private void DeletedButtonNav_Click(object sender, RoutedEventArgs e)
        {
            ShowView(DeletedView);
        }

        private void ArchiveAndMarkGame(string newStatus, string toast)
        {
            Game? game = selectedGame;
            if (game == null) return;
            string previous = game.Status;
            gameService.UpdateStatus(game.Id, newStatus);
            if (!TrySaveGames()) { game.Status = previous; return; }
            RefreshGameList();
            DeleteGameDialog.Visibility = Visibility.Collapsed;
            ShowView(lastListView ?? HomeView);
            ShowToast(toast + " Installation und Spielstände bleiben erhalten.");
        }
        private void CloseDeleteDialogButton_Click(object sender, RoutedEventArgs e)
        {
            DeleteGameDialog.Visibility = Visibility.Collapsed;
        }

        private void DuplicateOpenExisting_Click(object sender, RoutedEventArgs e)
        {
            DuplicateGameDialog.Visibility = Visibility.Collapsed;
            if (pendingDuplicateGame != null) ShowGameDetails(pendingDuplicateGame);
            pendingDuplicateGame = null;
        }

        private async void DuplicateAddVersion_Click(object sender, RoutedEventArgs e)
        {
            DuplicateGameDialog.Visibility = Visibility.Collapsed;
            var existing = pendingDuplicateGame;
            pendingDuplicateGame = null;
            await AddNewGameAsVersion(existing);
        }

        private void DuplicateCancel_Click(object sender, RoutedEventArgs e)
        {
            DuplicateGameDialog.Visibility = Visibility.Collapsed;
            pendingDuplicateGame = null;
        }

        private async Task AddNewGameAsVersion(Game? existing)
        {
            Game game = new Game
            {
                Id = Guid.NewGuid().ToString(),
                Name = GameNameTextBox.Text.Trim(),
                GameVersion = GameVersionTextBox.Text.Trim(),
                Notes = GameNotesTextBox.Text,
                VersionInfo = GameVersionInfoTextBox.Text,
                InstallSource = InstallSourceTextBox.Text.Trim(),
                ModUrl = GameModUrlTextBox.Text.Trim(),
                GalleryPaths = pendingGalleryPaths.ToList(),
                CoverPath = GameCoverPath,
                CoverFocusX = GameCoverImage.FocusX, CoverFocusY = GameCoverImage.FocusY,
                GamePath = GameFolderPath,
                ExecutablePath = GameExecutablePath,
                SavePath = GameSaveFolderPath,
                IsInstalled = true,
                IsFavorite = existing?.IsFavorite ?? GameFavoriteCheckBox.IsChecked == true,
                Status = GameStatus.Installed,
                AddedDate = DateTime.Now
            };
            if (pendingF95Metadata != null &&
                F95UpdateService.TryExtractThreadId(game.InstallSource, out string newThreadId) &&
                pendingF95Metadata.ThreadId == newThreadId)
            {
                game.F95ThreadId = pendingF95Metadata.ThreadId;
                game.LatestKnownVersion = pendingF95Metadata.Version;
                MetadataEnrichment.Apply(game, pendingF95Metadata);
                game.F95Status = pendingF95Metadata.Status;
                game.UpdateAvailable = F95UpdateService.IsUpdateAvailable(game.GameVersion, game.LatestKnownVersion, game.IgnoredUpdateVersion);
                TrackUserEdits(game, pendingF95Metadata);
            }
            if (!await MoveGameToManagedLibraryAsync(game, ManageLocalGameCheckBox.IsChecked == true)) return;
            try { game.CoverPath = gameService.CacheCover(game.CoverPath, game.Id); }
            catch (Exception ex) { ShowToast("Cover konnte nicht gespeichert werden: " + ex.Message); return; }
            gameService.AddGame(game);
            if (!TrySaveGames()) { gameService.RemoveGame(game); return; }
            RefreshGameList();
            ResetForm();
            ShowView(HomeView);
            ShowToast("Spiel als neue Version hinzugefügt.");
        }

        private void AbsoluteDeleteOption_Click(object sender, RoutedEventArgs e)
        {
            DeleteGameDialog.Visibility = Visibility.Collapsed;
            if (selectedGame != null) OpenAbsoluteDeleteDialog(selectedGame);
        }

        private void OpenAbsoluteDeleteDialog(Game game, bool deleteFully = false)
        {
            fullDeleteRequested = deleteFully;
            bool alreadyDeleted = game.Status == GameStatus.Deleted;
            AbsoluteDeleteTitle.Text = deleteFully ? "Spiel vollständig löschen" : alreadyDeleted ? "Endgültig aus Liste entfernen" : "In Gelöscht verschieben";
            AbsoluteDeleteDescription.Text = deleteFully
                ? "Installation, gespeichertes Save-Archiv und der Eintrag mit Cover, Quelle und Spielzeit werden endgültig entfernt. Ein externer Save-Ordner bleibt aus Sicherheitsgründen erhalten."
                : alreadyDeleted
                ? "Der Spieleintrag wird endgültig aus dem Launcher entfernt. Die bereits gelöschten lokalen Dateien können nicht wiederhergestellt werden."
                : "Installation und Save-Archiv werden gelöscht. Der Spieleintrag mit Quelle, Cover und Spielzeit wandert in die Bibliothek Gelöscht.";
            ConfirmAbsoluteDeleteButton.Content = deleteFully ? "Endgültig löschen" : alreadyDeleted ? "Aus Liste entfernen" : "In Gelöscht verschieben";
            AbsoluteDeleteDialog.Visibility = Visibility.Visible;
        }

        private void CloseAbsoluteDeleteDialog_Click(object sender, RoutedEventArgs e)
        {
            AbsoluteDeleteDialog.Visibility = Visibility.Collapsed;
        }

        private async void ConfirmAbsoluteDelete_Click(object sender, RoutedEventArgs e)
        {
            Game? game = selectedGame;
            if (game == null)
                return;

            if (fullDeleteRequested)
            {
                if (launchService.IsRunning(game.Id)) { ShowToast("Bitte das Spiel zuerst beenden."); return; }
                string fullInstallError = "";
                if (!await RunFileOperationAsync(() => gameService.TryDeleteInstallation(game, out fullInstallError), "Installation wird endgültig entfernt …"))
                { MessageBox.Show(fullInstallError, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error); return; }
                string fullArchiveError = "";
                if (!await RunFileOperationAsync(() => gameService.TryDeleteArchivedSave(game.Id, out fullArchiveError), "Save-Archiv wird endgültig entfernt …"))
                {
                    gameService.ClearLocalPathsKeepHistory(game); TrySaveGames(); RefreshGameList();
                    MessageBox.Show(fullArchiveError + "\n\nDer Eintrag bleibt deshalb unter Gelöscht erhalten.", "Teilweise gelöscht", MessageBoxButton.OK, MessageBoxImage.Warning);
                    AbsoluteDeleteDialog.Visibility=Visibility.Collapsed; ShowView(DeletedView); return;
                }
                gameService.RemoveGame(game);
                if (!TrySaveGames()) { gameService.AddGame(game); return; }
                RefreshGameList(); AbsoluteDeleteDialog.Visibility=Visibility.Collapsed;
                selectedGame=null; ShowView(DeletedView);
                ShowToast("Spiel vollständig gelöscht.");
                return;
            }

            if (game.Status == GameStatus.Deleted)
            {
                gameService.RemoveGame(game);
                if (!TrySaveGames()) { gameService.AddGame(game); return; }
                RefreshGameList();
                AbsoluteDeleteDialog.Visibility = Visibility.Collapsed;
                selectedGame = null;
                ShowView(DeletedView);
                ShowToast("Spiel endgültig aus der Liste entfernt.");
                return;
            }

            if (launchService.IsRunning(game.Id)) { ShowToast("Bitte das Spiel zuerst beenden."); return; }
            if (!TrySaveGames()) return;

            string installError = "";
            if (!await RunFileOperationAsync(() => gameService.TryDeleteInstallation(game, out installError), "Installation wird entfernt ..."))
            {
                MessageBox.Show(installError, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string archiveError = "";
            if (!await RunFileOperationAsync(() => gameService.TryDeleteArchivedSave(game.Id, out archiveError), "Save-Archiv wird entfernt ..."))
            {
                MessageBox.Show(archiveError, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            gameService.ClearLocalPathsKeepHistory(game);
            TrySaveGames();
            RefreshGameList();
            AbsoluteDeleteDialog.Visibility = Visibility.Collapsed;
            selectedGame = null;

            if (GameDetailView.Visibility == Visibility.Visible)
                ShowView(DeletedView);

            ShowToast("Lokale Dateien entfernt. Das Spiel wurde nach Gelöscht verschoben.");
        }

        private void GameDetailAbsoluteDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedGame == null)
                return;

            OpenAbsoluteDeleteDialog(selectedGame);
        }

        private void GameDetailFullDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedGame != null) OpenAbsoluteDeleteDialog(selectedGame, true);
        }

        private void GameDetailDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedGame == null)
                return;

            OpenDeleteDialog(selectedGame);
        }

        private void EditGameButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not Button button || button.DataContext is not Game game)
                return;

            OpenGameEditor(game);
        }

        private void OpenGameEditor(Game game)
        {
            ResetValidation();
            Game? existing = gameService.ShowGame(game.Id);
            if (existing == null)
            {
                ShowToast("Das Spiel konnte nicht geladen werden.");
                return;
            }

            editingGame = existing;
            isEditingGame = true;
            GameFormTitleTextBlock.Text = "Spiel bearbeiten";
            SaveGameButton.Content = "Aenderungen speichern";

            GameNotesTextBox.Text = existing.Notes;
            GameVersionInfoTextBox.Text = existing.VersionInfo;
            GameFavoriteCheckBox.IsChecked = existing.IsFavorite;
            GameNameTextBox.Text = existing.Name;
            GameVersionTextBox.Text = existing.GameVersion;
            InstallSourceTextBox.Text = existing.InstallSource;
            GameModUrlTextBox.Text = existing.ModUrl;
            GameFolderPath = existing.GamePath;
            GamePathTextBox.Text = existing.GamePath;
            GameExecutablePath = existing.ExecutablePath;
            ExecutableTextBox.Text = existing.ExecutablePath;
            GameSaveFolderPath = existing.SavePath;
            SavePathTextBox.Text = existing.SavePath;
            GameCoverPath = existing.CoverPath;
            GameCoverImage.ImagePath=existing.CoverPath;
            GameCoverImage.FocusX=existing.CoverFocusX;GameCoverImage.FocusY=existing.CoverFocusY;
            ManageLocalGameCheckBox.IsChecked = settingsService.Settings.ManageLocalGames && !GameStorageService.IsPlatformManaged(existing);
            pendingF95Metadata = F95UpdateService.TryExtractThreadId(existing.InstallSource, out string existingThreadId)
                ? new F95Metadata(string.IsNullOrWhiteSpace(existing.F95ThreadId) ? existingThreadId : existing.F95ThreadId,
                    existing.Name, existing.LatestKnownVersion, existing.F95Developer, existing.F95Status, "",Array.Empty<string>())
                : null;
            pendingGalleryPaths=existing.GalleryPaths.ToList();
            lastMetadataSource = existing.InstallSource;

            ShowView(AddGameView);
            HighlightNav(NavAddGameButton);
        }

        private async Task SaveEditedGame()
        {
            if (editingGame == null)
                return;

            Game updatedGame = new Game
            {
                Id = editingGame.Id,
                Name = GameNameTextBox.Text.Trim(),
                GameVersion = GameVersionTextBox.Text.Trim(),
                Notes = GameNotesTextBox.Text,
                VersionInfo = GameVersionInfoTextBox.Text,
                InstallSource = InstallSourceTextBox.Text.Trim(),
                ModUrl = GameModUrlTextBox.Text.Trim(),
                GalleryPaths = pendingGalleryPaths.Count>0?pendingGalleryPaths.ToList():editingGame.GalleryPaths.ToList(),
                CoverPath = GameCoverPath,
                CoverFocusX=GameCoverImage.FocusX,CoverFocusY=GameCoverImage.FocusY,
                GamePath = GameFolderPath,
                ExecutablePath = GameExecutablePath,
                SavePath = GameSaveFolderPath,
                IsInstalled = editingGame.IsInstalled,
                Status = editingGame.Status,
                IsFavorite = GameFavoriteCheckBox.IsChecked == true,
                LastPlayed = editingGame.LastPlayed,
                Playtime = editingGame.Playtime,
                AddedDate = editingGame.AddedDate
            };

            if (!await MoveGameToManagedLibraryAsync(updatedGame, ManageLocalGameCheckBox.IsChecked == true)) return;

            try { updatedGame.CoverPath = gameService.CacheCover(updatedGame.CoverPath, editingGame.Id); }
            catch (Exception ex) { ShowToast("Cover konnte nicht gespeichert werden: " + ex.Message); return; }
            gameService.EditGame(editingGame.Id, updatedGame);
            editingGame.PathWarning = GameService.GetPathWarning(editingGame);
            bool hasEditedF95Source = F95UpdateService.TryExtractThreadId(editingGame.InstallSource, out string editedThreadId);
            if (pendingF95Metadata != null && hasEditedF95Source && pendingF95Metadata.ThreadId == editedThreadId)
            {
                editingGame.F95ThreadId = pendingF95Metadata.ThreadId;
                editingGame.LatestKnownVersion = pendingF95Metadata.Version;
                MetadataEnrichment.Apply(editingGame, pendingF95Metadata);
                editingGame.F95Status = pendingF95Metadata.Status;
                editingGame.UpdateAvailable = F95UpdateService.IsUpdateAvailable(editingGame.GameVersion, editingGame.LatestKnownVersion, editingGame.IgnoredUpdateVersion);
                TrackUserEdits(editingGame, pendingF95Metadata);
            }
            else
            {
                editingGame.F95ThreadId = hasEditedF95Source ? editedThreadId : "";
                editingGame.LatestKnownVersion = "";
                editingGame.IgnoredUpdateVersion = "";
                editingGame.F95Developer = "";
                editingGame.F95Status = "";
                editingGame.F95LastChanged = "";
                editingGame.UpdateCheckError = "";
                editingGame.UpdateAvailable = false;
            }
            if (!TrySaveGames()) return;
            RefreshGameList();
            ResetForm();
            ShowView(lastListView ?? HomeView);
            ShowToast("Änderungen gespeichert.");
        }

        private void GameCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (e.Handled)
                return;

            if (sender is not Border border || border.DataContext is not Game game)
                return;

            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                ToggleGameSelection(game);
                return;
            }

            selectedGame = game;
            ShowGameDetails(game);
        }

        private void ToggleGameSelection(Game game)
        {
            if (selectedGameIds.Contains(game.Id))
                selectedGameIds.Remove(game.Id);
            else
                selectedGameIds.Add(game.Id);
            UpdateBulkActionBar();
        }

        private void UpdateBulkActionBar()
        {
            BulkActionText.Text = $"{selectedGameIds.Count} Spiele ausgewählt";
            BulkActionBar.Visibility = selectedGameIds.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BulkClearSelection_Click(object sender, RoutedEventArgs e)
        {
            selectedGameIds.Clear();
            UpdateBulkActionBar();
        }

        private void BulkSetFavorite_Click(object sender, RoutedEventArgs e)
        {
            foreach (var id in selectedGameIds)
            {
                var game = gameService.GetGames().FirstOrDefault(g => g.Id == id);
                if (game != null) game.IsFavorite = true;
            }
            TrySaveGames();
            RefreshGameList();
            selectedGameIds.Clear();
            UpdateBulkActionBar();
            ShowToast("Als Favoriten markiert.");
        }

        private void ShowGameDetails(Game game)
        {
            selectedGame = game;
            bool hasCover = !string.IsNullOrWhiteSpace(game.CoverPath) && File.Exists(game.CoverPath);
            GameDetailHero.Visibility = hasCover ? Visibility.Visible : Visibility.Collapsed;
            GameDetailCoverImage.ImagePath=game.CoverPath;
            GameDetailCoverImage.FocusX=game.CoverFocusX;GameDetailCoverImage.FocusY=game.CoverFocusY;
            GameDetailHeroBackdrop.ImagePath=game.CoverPath;
            GameDetailHeroBackdrop.FocusX=game.CoverFocusX;GameDetailHeroBackdrop.FocusY=game.CoverFocusY;
            var gallery=game.GalleryPaths.Where(File.Exists).ToList();
            GameDetailGalleryItems.ItemsSource=gallery;
            GameDetailGalleryCount.Text=gallery.Count==1?"1 Bild":gallery.Count+" Bilder";
            GameDetailGallerySection.Visibility=gallery.Count>0?Visibility.Visible:Visibility.Collapsed;
            GameDetailNotesText.Text = string.Join("\n\n", new[] { game.Description, game.Notes }.Where(s => !string.IsNullOrWhiteSpace(s)));
            GameDetailVersionInfoText.Text = string.IsNullOrWhiteSpace(game.VersionInfo) ? "Keine zusätzlichen Angaben." : game.VersionInfo;
            UpdateMoreTextButton(GameDetailNotesText, GameDetailNotesMoreButton);
            UpdateMoreTextButton(GameDetailVersionInfoText, GameDetailVersionInfoMoreButton);
            GameDetailArchiveText.Text = Directory.Exists(gameService.Paths.GetSaveArchiveForGame(game.Id)) ? "Spielstand archiviert · bereit zur Wiederherstellung" : "Kein archivierter Spielstand";
            GameDetailPlayButton.Content = launchService.IsRunning(game.Id) ? "Zum Spiel" : "Spielen";
            GameDetailNameTextBlock.Text = game.Name;
            GameDetailStatusTextBlock.Text = Converters.StatusLabelConverter.Label(game.Status);
            GameDetailStatusTextBlock.Foreground = StatusBrush(game.Status);
            GameDetailVersionTextBlock.Text = string.IsNullOrWhiteSpace(game.GameVersion) ? "-" : game.GameVersion;
            GameDetailSourceTextBlock.Text = string.IsNullOrWhiteSpace(game.InstallSource) ? "-" : game.InstallSource;
            string f95Meta = string.Join(" · ", new[]
            {
                string.IsNullOrWhiteSpace(game.F95Developer) ? "" : "Entwickler: " + game.F95Developer,
                string.IsNullOrWhiteSpace(game.F95Status) ? "" : "F95-Status: " + game.F95Status,
                game.Tags.Count == 0 ? "" : "Tags: " + string.Join(", ", game.Tags),
                string.IsNullOrWhiteSpace(game.Engine) ? "" : "Engine: " + game.Engine,
                string.IsNullOrWhiteSpace(game.LatestKnownVersion) ? "" : "Online: " + game.LatestKnownVersion
            }.Where(x => x.Length > 0));
            GameDetailF95MetaTextBlock.Text = f95Meta;
            GameDetailF95MetaTextBlock.Visibility = f95Meta.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            GameDetailGamePathTextBlock.Text = string.IsNullOrWhiteSpace(game.GamePath) ? "-" : game.GamePath;
            GameDetailSavePathTextBlock.Text = string.IsNullOrWhiteSpace(game.SavePath) ? "-" : game.SavePath;
            GameDetailExecutableTextBlock.Text = string.IsNullOrWhiteSpace(game.ExecutablePath) ? "-" : game.ExecutablePath;
            GameDetailPlaytimeTextBlock.Text = FormatHelper.FormatPlaytime(game.Playtime);
            GameDetailLastPlayedTextBlock.Text = FormatHelper.FormatLastPlayed(game.LastPlayed);
            GameDetailUpdatePanel.Visibility = game.UpdateAvailable &&
                (game.Status is GameStatus.Installed or GameStatus.WaitingForUpdates) ? Visibility.Visible : Visibility.Collapsed;
            GameDetailUpdateText.Text = $"Installiert: {(string.IsNullOrWhiteSpace(game.GameVersion) ? "nicht angegeben" : game.GameVersion)} · Online: {game.LatestKnownVersion}";
            GameDetailUpdateErrorPanel.Visibility = string.IsNullOrWhiteSpace(game.UpdateCheckError)
                ? Visibility.Collapsed : Visibility.Visible;
            GameDetailUpdateErrorText.Text = game.UpdateCheckError;
            GameDetailPathWarningPanel.Visibility = game.PathWarning.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            GameDetailPathWarningText.Text = game.PathWarning;
            GameDetailFavoriteButton.Content = game.IsFavorite ? "★ Favorit" : "☆ Favorit";

            bool installed = game.IsInstalled && game.Status == GameStatus.Installed;
            GameDetailPlayButton.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
            GameDetailInstallButton.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
            GameDetailDeleteButton.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
            GameDetailAbsoluteDeleteButton.Visibility = !installed && game.Status != GameStatus.Deleted ? Visibility.Visible : Visibility.Collapsed;
            GameDetailAbsoluteDeleteButton.Content = game.Status == GameStatus.Deleted
                ? "Aus Liste entfernen"
                : "Lokale Dateien entfernen";
            GameDetailOpenGameFolderButton.Visibility =
                Directory.Exists(game.GamePath) ? Visibility.Visible : Visibility.Collapsed;
            bool hasSaveFolder=Directory.Exists(game.SavePath);
            GameDetailOpenSaveFolderButton.Visibility =
                hasSaveFolder || Directory.Exists(game.GamePath) ? Visibility.Visible : Visibility.Collapsed;
            GameDetailOpenSaveFolderButton.Content=hasSaveFolder?"Spielstände":"Spielordner · Save noch unbekannt";
            bool hasMod = Uri.TryCreate(game.ModUrl, UriKind.Absolute, out var modUri) && modUri.Scheme is "http" or "https";
            GameDetailOpenModButton.Visibility = hasMod ? Visibility.Visible : Visibility.Collapsed;
            GameDetailInstallModButton.Visibility = hasMod && Directory.Exists(game.GamePath) ? Visibility.Visible : Visibility.Collapsed;

            GameDetailOpenSourceButton.IsEnabled = Uri.TryCreate(game.InstallSource, UriKind.Absolute, out var sourceUri) &&
                sourceUri.Scheme is "http" or "https";

            ShowView(GameDetailView);
        }

        private void GameDetailCoverImage_CropChanged(object? sender, EventArgs e)
        {
            if (selectedGame == null) return;
            selectedGame.CoverFocusX = GameDetailCoverImage.FocusX;
            selectedGame.CoverFocusY = GameDetailCoverImage.FocusY;
            if (TrySaveGames()) ShowToast("Bildausschnitt gespeichert.");
        }

        private void BackFromGameDetailButton_Click(object sender, RoutedEventArgs e)
        {
            selectedGame = null;
            ShowView(lastListView ?? HomeView);
        }

        private async void PlayGame(Game? game)
        {
            if (game == null)
                return;

            if (launchService.IsRunning(game.Id))
            {
                launchService.ActivateGame(game.Id);
                return;
            }

            if (fileOperationActive) return;
            if (settingsService.Settings.BackupBeforePlay && Directory.Exists(game.SavePath) && !await BackupGameAsync(game))
            { ShowToast("Sicherung vor Spielstart fehlgeschlagen. Spiel wurde nicht gestartet."); return; }
            if (!launchService.TryLaunch(game))
                return;

            game.LastPlayed = DateTime.Now;
            TrySaveGames();
            RefreshDashboard();
            StatusLineText.Text = $"Läuft: {game.Name}";
            if (settingsService.Settings.LowEnergyWhilePlaying) EnterQuietMode();

            if (selectedGame?.Id == game.Id)
            GameDetailLastPlayedTextBlock.Text = FormatHelper.FormatLastPlayed(game.LastPlayed);
            if (!string.IsNullOrWhiteSpace(game.F95ThreadId))
            {
                var allVersions = gameService.GetGames().Where(g => g.F95ThreadId == game.F95ThreadId && g.Id != game.Id).ToList();
                if (allVersions.Count > 0)
                {
                    allVersions.Insert(0, game);
                    GameDetailVersionsList.ItemsSource = allVersions;
                    GameDetailVersionsSection.Visibility = Visibility.Visible;
                }
                else GameDetailVersionsSection.Visibility = Visibility.Collapsed;
            }
            else GameDetailVersionsSection.Visibility = Visibility.Collapsed;
        }

        private void OnSessionEnded(Game game, TimeSpan duration)
        {
            if (runtimeClosed || Dispatcher.HasShutdownStarted) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (saveProbeSessions.Remove(game.Id, out var probe))
                {
                    _ = FinishSaveProbeAsync(probe);
                    return;
                }
                if (settingsService.Settings.BackupAfterPlay && Directory.Exists(game.SavePath)) _ = BackupAfterSessionAsync(game);
                game.Playtime += duration;
                TrySaveGames();
                RefreshGameList();
                StatusLineText.Text = "Bereit";

                if (selectedGame?.Id == game.Id && GameDetailView.Visibility == Visibility.Visible)
                    ShowGameDetails(game);
                if (!launchService.HasRunningGames) ExitQuietMode();
            }));
        }

        private static void UpdateMoreTextButton(TextBlock textBlock, Button moreButton)
        {
            if (string.IsNullOrWhiteSpace(textBlock.Text) || textBlock.Text == "Keine Notizen." || textBlock.Text == "Keine zusätzlichen Angaben.")
            {
                moreButton.Visibility = Visibility.Collapsed;
                textBlock.MaxHeight = double.PositiveInfinity;
                return;
            }
            textBlock.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            bool isTruncated = textBlock.DesiredSize.Height > 65;
            moreButton.Visibility = isTruncated ? Visibility.Visible : Visibility.Collapsed;
            textBlock.MaxHeight = 60;
        }

        private void DetailMoreText_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string tag) return;
            TextBlock? textBlock = tag switch
            {
                "Notes" => GameDetailNotesText,
                "VersionInfo" => GameDetailVersionInfoText,
                _ => null
            };
            if (textBlock == null) return;
            bool expanded = textBlock.MaxHeight < 100;
            textBlock.MaxHeight = expanded ? double.PositiveInfinity : 60;
            button.Content = expanded ? "Weniger anzeigen ▴" : "Mehr anzeigen ▾";
        }

        private void VersionSetDefault_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: Game version } && selectedGame != null)
            {
                selectedGame.GameVersion = version.GameVersion;
                selectedGame.GamePath = version.GamePath;
                selectedGame.ExecutablePath = version.ExecutablePath;
                TrySaveGames();
                ShowGameDetails(selectedGame);
                ShowToast($"Standard-Version auf {version.GameVersion} gesetzt.");
            }
        }

        private void GameDetailPlayButton_Click(object sender, RoutedEventArgs e)
        {
            PlayGame(selectedGame);
        }

        private async void GameDetailInstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedGame == null) return;
            if (!string.IsNullOrWhiteSpace(selectedGame.SavePath) && Directory.Exists(selectedGame.SavePath))
            {
                try
                {
                    if (!gameService.ArchiveSave(selectedGame)) { ShowToast("Spielstand konnte nicht gesichert werden; Update nicht gestartet."); return; }
                    ShowToast("Spielstand wurde vor dem Update gesichert.");
                }
                catch { }
            }
            OpenReinstall(selectedGame);
        }

        private void GameDetailFavoriteButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedGame == null)
                return;

            selectedGame.IsFavorite = !selectedGame.IsFavorite;
            TrySaveGames();
            GameDetailFavoriteButton.Content =
                selectedGame.IsFavorite ? "★ Favorite" : "☆ Favorite";
            RefreshGameList();
        }

        private void GameDetailEditButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedGame == null)
                return;

            OpenGameEditor(selectedGame);
        }

        private void GameDetailOpenSourceButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedGame == null)
                return;

            if (!Uri.TryCreate(selectedGame.InstallSource, UriKind.Absolute, out Uri? uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                ShowToast("Für dieses Spiel ist keine gültige Website gespeichert.");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = uri.ToString(),
                UseShellExecute = true
            });
        }

        private void GameDetailOpenGameFolderButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFolderIfExists(selectedGame?.GamePath);
        }

        private void MarkOnlineVersionInstalled_Click(object sender, RoutedEventArgs e)
        {
            if (selectedGame == null || F95UpdateService.NormalizeVersion(selectedGame.LatestKnownVersion) == null) return;
            selectedGame.GameVersion = selectedGame.LatestKnownVersion.Trim();
            selectedGame.IgnoredUpdateVersion = "";
            selectedGame.UpdateAvailable = false;
            if (!TrySaveGames()) return;
            RefreshGameList();
            ShowGameDetails(selectedGame);
            ShowToast("Online-Version als installiert gespeichert.");
        }

        private void RelinkMissingGame_Click(object sender, RoutedEventArgs e)
        {
            if (selectedGame != null) OpenReinstall(selectedGame);
        }

        private void IgnoreOnlineVersion_Click(object sender, RoutedEventArgs e)
        {
            if (selectedGame == null || F95UpdateService.NormalizeVersion(selectedGame.LatestKnownVersion) == null) return;
            selectedGame.IgnoredUpdateVersion = selectedGame.LatestKnownVersion.Trim();
            selectedGame.UpdateAvailable = false;
            if (!TrySaveGames()) return;
            RefreshGameList();
            ShowGameDetails(selectedGame);
            ShowToast("Diese Online-Version wird ignoriert. Die nächste Version erscheint wieder.");
        }

        private void GameDetailOpenSaveFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if(selectedGame==null)return;
            bool saveExists=Directory.Exists(selectedGame.SavePath);
            OpenFolderIfExists(saveExists?selectedGame.SavePath:selectedGame.GamePath);
            if(!saveExists)ShowToast("Save-Ordner noch nicht gefunden – deshalb wurde der Spielordner geöffnet.");
        }

        private void GameDetailOpenModButton_Click(object sender,RoutedEventArgs e)
        {
            if(selectedGame==null||!Uri.TryCreate(selectedGame.ModUrl,UriKind.Absolute,out var uri)||uri.Scheme is not ("http" or "https"))return;
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true});
        }

        private void GameDetailInstallModButton_Click(object sender,RoutedEventArgs e)=>OpenFolderIfExists(selectedGame?.GamePath);

        private readonly DispatcherTimer searchDelay=new() {Interval=TimeSpan.FromMilliseconds(160)};
        private void SearchGamesTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if(gameService==null)return;
            searchDelay.Stop();searchDelay.Start();
        }
        private void ApplySearch()
        {
            if (syncingSearch || LibrarySearchTextBox == null || gameService == null) return;
            syncingSearch = true;
            LibrarySearchTextBox.Text = SearchGamesTextBox.Text;
            syncingSearch = false;
            RefreshLibraryList();
            if (SearchGamesTextBox.IsKeyboardFocusWithin && SearchGamesTextBox.Text.Length > 0 && AddGameView.Visibility != Visibility.Visible) ShowView(LibraryView);
            RefreshHomeGameList();
        }

        private void LibrarySearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshLibraryList();
        }

        private async void LibraryFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string tag)
                return;

            libraryFilter = tag switch
            {
                "Wishlist" => GameListFilter.Wishlist,
                "Installed" => GameListFilter.Installed,
                "Waiting" => GameListFilter.WaitingForUpdates,
                "Finished" => GameListFilter.Finished,
                "Favorites" => GameListFilter.Favorites,
                "Problems" => GameListFilter.NeedsAttention,
                "Updates" => GameListFilter.UpdatesAvailable,
                "Unplayed" => GameListFilter.NeverPlayed,
                _ => GameListFilter.All
            };

            if (libraryFilter == GameListFilter.NeedsAttention) await RefreshPathWarningsAsync(true);
            RefreshLibraryList();
            foreach (var filterButton in new[] { FilterAllButton, FilterInstalledButton, FilterUpdatesButton, FilterUnplayedButton, FilterWaitingButton, FilterFinishedButton, FilterFavoritesButton, FilterProblemsButton })
            {
                filterButton.Background = (Brush)FindResource(filterButton == button ? "AccentSoft" : "SurfaceSecondary");
                filterButton.Foreground = (Brush)FindResource(filterButton == button ? "AccentColor" : "TextPrimary");
            }
            UpdateLibraryFilterSummary();
        }

        private void LibraryFilterToggle_Click(object sender,RoutedEventArgs e)
        {
            LibraryFilterPanel.Visibility=LibraryFilterPanel.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;
            UpdateLibraryFilterSummary();
        }

        private void LibraryViewToggle_Click(object sender, RoutedEventArgs e)
        {
            libraryListView = !libraryListView;
            LibraryViewToggleIcon.Text = libraryListView ? "\uE8A9" : "\uE80A";
            LibraryViewToggle.ToolTip = libraryListView ? "Kachelansicht" : "Listenansicht";
            RefreshLibraryList();
        }

        private void UpdateLibraryFilterSummary()
        {
            string filter=libraryFilter switch
            {
                GameListFilter.Installed=>"Installiert",GameListFilter.UpdatesAvailable=>"Updates verfügbar",
                GameListFilter.NeverPlayed=>"Noch nie gespielt",GameListFilter.WaitingForUpdates=>"Wartend",
                GameListFilter.Finished=>"Abgeschlossen",GameListFilter.Favorites=>"Favoriten",
                GameListFilter.NeedsAttention=>"Probleme",_=>"Alle"
            };
            string sort=librarySort switch{"Recent"=>"Zuletzt gespielt","Playtime"=>"Meiste Spielzeit","Updates"=>"Updates zuerst","Added"=>"Hinzugefügt",_=>"Name"};
            string arrow=LibraryFilterPanel.Visibility==Visibility.Visible?"▴":"▾";
            LibraryFilterToggleButton.Content=$"Filter & Sortierung · {filter} · {sort}  {arrow}";
        }

        private void ShowReinstallForm(Game game)
        {
            reinstallGame = gameService.ShowGame(game.Id) ?? game;
            installGamePath = "";
            installExecutablePath = "";
            installSavePath = "";
            InstallGamePathTextBox.Text = "";
            InstallExecutableTextBox.Text = "";
            InstallSavePathTextBox.Text = "";
            InstallVersionTextBox.Text = string.IsNullOrWhiteSpace(reinstallGame.GameVersion)
                ? "Unknown"
                : reinstallGame.GameVersion;
            InstallKnownNameTextBlock.Text = reinstallGame.Name;
            InstallGameIdTextBlock.Text = "ID  " + reinstallGame.Id;
            InstallVersionTextBlock.Text = "Version  " + reinstallGame.GameVersion;
            InstallSourceTextBlock.Text = "Source  " + reinstallGame.InstallSource;
            InstallCoverImage.ImagePath=reinstallGame.CoverPath;
            InstallCoverImage.FocusX=reinstallGame.CoverFocusX;InstallCoverImage.FocusY=reinstallGame.CoverFocusY;

            bool hasArchive = gameService.HasArchivedSave(reinstallGame.Id);
            InstallArchiveStatusTextBlock.Text = hasArchive
                ? "Archivierter Spielstand vorhanden – möchtest du ihn übernehmen?"
                : "Kein archivierter Spielstand vorhanden.";
            RestoreSaveCheckBox.IsEnabled = hasArchive;
            RestoreSaveCheckBox.IsChecked = false;
            TransferCurrentSaveCheckBox.IsEnabled = Directory.Exists(reinstallGame.SavePath);
            TransferCurrentSaveCheckBox.IsChecked = false;
            KeepOldInstallationCheckBox.IsChecked = true;

            ShowView(InstallGameView);
        }

        private void BackFromInstallButton_Click(object sender, RoutedEventArgs e)
        {
            reinstallGame = null;
            ShowView(lastListView ?? HomeView);
        }

        private void SelectInstallGameFolder_Click(object sender, RoutedEventArgs e)
        {
            string? folder = PickFolder(settingsService.Settings.DefaultGameFolder);
            if (folder == null)
                return;

            installGamePath = folder;
            InstallGamePathTextBox.Text = folder;
        }

        private void SelectInstallExecutable_Click(object sender, RoutedEventArgs e)
        {
            string? file = PickExe(installGamePath);
            if (file == null)
                return;

            PrefillReinstall(file);
        }

        private void SelectInstallSaveFolder_Click(object sender, RoutedEventArgs e)
        {
            string initial=Directory.Exists(installGamePath)?installGamePath:settingsService.Settings.DefaultSaveFolder;
            string? folder = PickFolder(initial);
            if (folder == null)
                return;

            installSavePath = folder;
            InstallSavePathTextBox.Text = folder;
        }

        private async void ConfirmInstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (reinstallGame == null)
                return;
            installSavePath=SaveLocationService.Expand(InstallSavePathTextBox.Text);

            bool transferCurrent = TransferCurrentSaveCheckBox.IsChecked == true && !string.IsNullOrWhiteSpace(installSavePath) && Directory.Exists(reinstallGame.SavePath) &&
                !PathSafety.Same(reinstallGame.SavePath,installSavePath);
            bool restore = transferCurrent || (RestoreSaveCheckBox.IsChecked == true && gameService.HasArchivedSave(reinstallGame.Id));

            if (string.IsNullOrWhiteSpace(installGamePath) || !Directory.Exists(installGamePath) ||
                string.IsNullOrWhiteSpace(installExecutablePath) || !File.Exists(installExecutablePath) ||
                (!restore && !string.IsNullOrWhiteSpace(installSavePath) && !Directory.Exists(installSavePath)) ||
                string.IsNullOrWhiteSpace(InstallVersionTextBox.Text))
            {
                ShowToast("Bitte Version, Spielordner und Startdatei prüfen. Der Spielstandordner ist optional.");
                return;
            }

            if (!PathSafety.Within(installExecutablePath, installGamePath) ||
                (restore && (string.IsNullOrWhiteSpace(installSavePath) || PathSafety.Within(installGamePath, installSavePath))))
            {
                ShowToast("Bitte einen passenden Spielordner und einen getrennten Spielstandordner waehlen.");
                return;
            }
            if(transferCurrent)
            {
                if(MessageBox.Show("Der aktuelle Spielstand wird jetzt gesichert und anschließend in die neue Version übertragen. Fortfahren?",
                    "Spielstand übertragen",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
                if(MessageBox.Show("Letzte Bestätigung: Die neue Save-Struktur kann je nach Spielversion inkompatibel sein. Der neue Zielordner wird vor dem Kopieren als Rollback-Sicherung behalten.",
                    "Übertragung wirklich starten?",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
                if(!await RunFileOperationAsync(()=>gameService.ArchiveSave(reinstallGame),"Alter Spielstand wird sicher archiviert …"))
                {ShowToast("Der alte Spielstand konnte nicht gesichert werden. Das Update wurde abgebrochen.");return;}
            }
            if (restore && gameService.DestinationHasFiles(installSavePath))
            {
                SaveConflictDialog.Visibility = Visibility.Visible;
                return;
            }

            CompleteReinstall(restore ? SaveRestoreMode.Replace : SaveRestoreMode.Cancel, restore);
        }

        private void SaveConflictReplace_Click(object sender, RoutedEventArgs e)
        {
            CloseSaveConflict(SaveRestoreMode.Replace);
        }

        private void SaveConflictMerge_Click(object sender, RoutedEventArgs e)
        {
            CloseSaveConflict(SaveRestoreMode.Merge);
        }

        private void SaveConflictCancel_Click(object sender, RoutedEventArgs e)
        {
            CloseSaveConflict(SaveRestoreMode.Cancel);
        }

        private void CloseSaveConflict(SaveRestoreMode mode)
        {
            SaveConflictDialog.Visibility = Visibility.Collapsed;

            if (mode == SaveRestoreMode.Cancel)
                return;

            CompleteReinstall(mode, true);
        }

        private async void CompleteReinstall(SaveRestoreMode restoreMode, bool restore)
        {
            if (reinstallGame == null)
                return;

            string oldGamePath=reinstallGame.GamePath;
            string oldExecutablePath=reinstallGame.ExecutablePath;
            string oldSavePath=reinstallGame.SavePath;

            var linkedInstallation = new Game
            {
                Name = reinstallGame.Name,
                InstallSource = reinstallGame.InstallSource,
                GamePath = installGamePath,
                ExecutablePath = installExecutablePath,
                SavePath = installSavePath,
                CoverPath = reinstallGame.CoverPath
            };
            if (!await MoveGameToManagedLibraryAsync(linkedInstallation, settingsService.Settings.ManageLocalGames)) return;
            installGamePath = linkedInstallation.GamePath;
            installExecutablePath = linkedInstallation.ExecutablePath;
            installSavePath = linkedInstallation.SavePath;

            if (restore)
            {
                if (!await RunFileOperationAsync(() => gameService.RestoreSave(reinstallGame.Id, installSavePath, restoreMode),
                    "Spielstand wird wiederhergestellt ..."))
                {
                    MessageBox.Show(
                        "Der Save konnte nicht wiederhergestellt werden. Die Installation wurde nicht abgeschlossen.",
                        "Fehler",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }

            gameService.ApplyReinstall(
                reinstallGame,
                installGamePath,
                installExecutablePath,
                installSavePath);
            reinstallGame.PathWarning = GameService.GetPathWarning(reinstallGame);
            reinstallGame.GameVersion = InstallVersionTextBox.Text.Trim();
            reinstallGame.UpdateAvailable = F95UpdateService.IsUpdateAvailable(reinstallGame.GameVersion, reinstallGame.LatestKnownVersion, reinstallGame.IgnoredUpdateVersion);

            if (!TrySaveGames()) return;
            if(KeepOldInstallationCheckBox.IsChecked!=true && Directory.Exists(oldGamePath) && !PathSafety.Same(oldGamePath,installGamePath))
            {
                var oldInstallation=new Game{Id="old-"+reinstallGame.Id,Name=reinstallGame.Name,GamePath=oldGamePath,
                    ExecutablePath=oldExecutablePath,SavePath=oldSavePath,IsInstalled=true,Status=GameStatus.Installed};
                string deleteError="";
                if(!await RunFileOperationAsync(()=>gameService.TryDeleteInstallation(oldInstallation,out deleteError),"Alte Spielversion wird entfernt …"))
                    ShowToast("Update gespeichert; die alte Version blieb zur Sicherheit erhalten: "+deleteError);
            }
            RefreshGameList();
            ShowView(HomeView);
            HighlightNav(NavHomeButton);
            ShowToast("Spiel eingebunden und gespeichert.");
            reinstallGame = null;
        }

        private void ImportJsonButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "JSON (*.json)|*.json"
            };

            if (dialog.ShowDialog() != true)
                return;

            ImportService.ImportPreview preview;
            try { preview = importService.Preview(dialog.FileName, gameService); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Importvorschau"); return; }
            if (MessageBox.Show($"{preview.Games} Spiele · {preview.ExistingIds} bereits bekannte IDs · {preview.MissingInstallations} fehlende Installationsordner.\n\nDas Original wird kopiert. Bestehende Einträge werden nicht überschrieben. Importieren?", "Importvorschau", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var (added, skipped, error) = importService.ImportGamesJson(dialog.FileName, gameService, preview.Sha256);
            if (error != null)
            {
                MessageBox.Show(error, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            RefreshGameList();
            ShowToast($"{added} importiert, {skipped} übersprungen.");
        }

        private async void ScanFolderButton_Click(object sender, RoutedEventArgs e)
        {
            string? folder = PickFolder(settingsService.Settings.DefaultGameFolder);
            if (folder == null)
                return;

            await ScanDetectedGamesAsync(folder);
        }

        private async void ImportScannedGame_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not ScannedExecutable scanned)
                return;

            if (scanned.IsArchive) await ExtractDetectedArchiveAsync(scanned);
            else await AddDetectedGame(scanned);
        }

        private void SettingsNavigationButton_Click(object sender, RoutedEventArgs e)
        {
            models.AppSettings settings = settingsService.Settings;
            SettingsArchivePathTextBox.Text = gameService.Paths.ArchivePath;
            SettingsManagedGameFolderTextBox.Text = settings.ManagedGameFolder;
            SettingsManageGamesCheckBox.IsChecked = settings.ManageLocalGames;
            SettingsDefaultGameFolderTextBox.Text = settings.DefaultGameFolder;
            SettingsDefaultSaveFolderTextBox.Text = settings.DefaultSaveFolder;
            SettingsShortcutCheckBox.IsChecked = settings.GlobalMouseShortcutEnabled;
            SettingsAutostartCheckBox.IsChecked = settings.StartWithWindows;
            SettingsTrayCheckBox.IsChecked = settings.MinimizeToTray;
            SettingsF95UpdatesCheckBox.IsChecked = settings.F95UpdateChecksEnabled;
            SettingsF95NotificationsCheckBox.IsChecked = settings.F95UpdateNotificationsEnabled;
            SettingsF95IntervalSlider.Value = Math.Clamp(settings.F95UpdateCheckIntervalHours, 6, 24);
            SettingsHoverDelaySlider.Value = Math.Clamp(settings.HoverPreviewDelaySeconds, 1, 15);
            SettingsF95GalleryCheckBox.IsChecked = settings.AutoDownloadF95Gallery;
            SettingsRemoteAutoStartCheckBox.IsChecked = settings.RemoteHostAutoStart;
            SettingsRemotePortTextBox.Text = settings.RemoteHostPort.ToString();
            SettingsRemoteFpsSlider.Value = Math.Clamp(settings.RemoteHostFps, 15, 60);
            SettingsRemoteQualitySlider.Value = Math.Clamp(settings.RemoteHostQuality, 35, 92);
            SettingsRemoteWidthSlider.Value = Math.Clamp(settings.RemoteHostWidth, 1280, 1920);
            SettingsRemotePrivacyCheckBox.IsChecked = settings.RemotePrivacyCurtain;
            SettingsRemotePerformanceCheckBox.IsChecked = settings.RemotePerformanceMode;
            RefreshRemoteHostPanel();
            LoadRuntimeSettingsForm();
            LoadPresentationSettings();
            ShowView(SettingsView);
            HighlightNav(NavSettingsButton);
        }

        private void NavHome_Click(object sender, RoutedEventArgs e) => HomeNavigationButton_Click(sender, e);
        private void NavLibrary_Click(object sender, RoutedEventArgs e) => LibraryNavigationButton_Click(sender, e);
        private void NavAddGame_Click(object sender, RoutedEventArgs e) => OpenAddGameButton_Click(sender, e);
        private void NavUpdates_Click(object sender, RoutedEventArgs e) => AvailableUpdatesNavigationButton_Click(sender, e);
        private void NavSettings_Click(object sender, RoutedEventArgs e) => SettingsNavigationButton_Click(sender, e);

        private void ImportsButton_Click(object sender, RoutedEventArgs e)
        {
            ShowView(ImportsView);
        }

        private void SelectArchivePath_Click(object sender, RoutedEventArgs e)
        {
            string? folder = PickFolder(gameService.Paths.ArchivePath);
            if (folder == null)
                return;

            SettingsArchivePathTextBox.Text = folder;
        }

        private void SelectManagedGameFolder_Click(object sender, RoutedEventArgs e)
        {
            string? folder = PickFolder(settingsService.Settings.ManagedGameFolder);
            if (folder != null) SettingsManagedGameFolderTextBox.Text = folder;
        }

        private void SelectDefaultGameFolder_Click(object sender, RoutedEventArgs e)
        {
            string? folder = PickFolder(settingsService.Settings.DefaultGameFolder);
            if (folder == null)
                return;

            SettingsDefaultGameFolderTextBox.Text = folder;
        }

        private void SelectDefaultSaveFolder_Click(object sender, RoutedEventArgs e)
        {
            string? folder = PickFolder(settingsService.Settings.DefaultSaveFolder);
            if (folder == null)
                return;

            SettingsDefaultSaveFolderTextBox.Text = folder;
        }

        private async void RefreshAllMetadata_Click(object sender, RoutedEventArgs e)
        {
            var games = gameService.GetGames().Where(g => F95UpdateService.TryExtractThreadId(g.InstallSource, out _)).ToList();
            if (games.Count == 0) { ShowToast("Keine Spiele mit F95-Quellen gefunden."); return; }
            var result = MessageBox.Show($"Metadaten für {games.Count} Spiele aktualisieren?\n\nDies kann einige Minuten dauern.", "Metadaten aktualisieren", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
            int updated = 0;
            foreach (var game in games)
            {
                try
                {
                    var metadata = await f95UpdateService.FetchMetadataAsync(game.InstallSource, CancellationToken.None);
                    if (metadata == null) continue;
                    game.LatestKnownVersion = metadata.Version;
                    MetadataEnrichment.Apply(game, metadata);
                    game.F95Status = metadata.Status;
                    game.UpdateAvailable = F95UpdateService.IsUpdateAvailable(game.GameVersion, game.LatestKnownVersion, game.IgnoredUpdateVersion);
                    updated++;
                }
                catch { }
            }
            TrySaveGames();
            RefreshGameList();
            ShowToast($"{updated} von {games.Count} Spielen aktualisiert.");
        }

        private async void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (launchService.HasRunningGames || downloads?.IsActive == true || fileOperationActive || f95UpdateRunning)
            {
                ShowToast("Einstellungen nach Spielende speichern.");
                return;
            }
            var oldSettings = System.Text.Json.JsonSerializer.Serialize(settingsService.Settings);
            try
            {
                string archivePath = SettingsArchivePathTextBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(archivePath)) { ShowToast("Archivordner fehlt."); return; }
                if (capturingShortcut) { ShowToast("Bitte die Tastenkombination zuerst abschliessen."); return; }
                var settings = settingsService.Settings;
                if (keyboardShortcut != null && !keyboardShortcut.Configure(SettingsKeyboardCheckBox.IsChecked == true, pendingShortcutModifiers, pendingShortcutKey))
                {
                    ConfigureRuntimeFeatures();
                    ShowToast("Diese Tastenkombination ist bereits belegt.");
                    return;
                }
                AppPaths newPaths = new AppPaths(archivePath);
                string error = "";
                if (!await RunFileOperationAsync(() => gameService.TryCopyDataTo(newPaths, out error), "Archiv wird kopiert ..."))
                {
                    ConfigureRuntimeFeatures();
                    ShowToast(error);
                    return;
                }
                settings.ArchivePath = archivePath;
                string managedFolder = SettingsManagedGameFolderTextBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(managedFolder)) { ShowToast("GameLibrary-Ordner fehlt."); return; }
                Directory.CreateDirectory(managedFolder);
                settings.ManagedGameFolder = Path.GetFullPath(managedFolder);
                settings.ManageLocalGames = SettingsManageGamesCheckBox.IsChecked == true;
                settings.DefaultGameFolder = SettingsDefaultGameFolderTextBox.Text.Trim();
                settings.DefaultSaveFolder = SettingsDefaultSaveFolderTextBox.Text.Trim();
                settings.GlobalMouseShortcutEnabled = SettingsShortcutCheckBox.IsChecked == true;
                settings.StartWithWindows = SettingsAutostartCheckBox.IsChecked == true;
                settings.MinimizeToTray = SettingsTrayCheckBox.IsChecked == true;
                settings.KeyboardShortcutEnabled = SettingsKeyboardCheckBox.IsChecked == true;
                settings.ShortcutKey = pendingShortcutKey;
                settings.ShortcutModifiers = pendingShortcutModifiers;
                settings.MinimizeGameOnShortcut = SettingsMinimizeGameCheckBox.IsChecked == true;
                settings.LowEnergyWhilePlaying = SettingsEnergyCheckBox.IsChecked == true;
                settings.RestoreAfterGame = SettingsRestoreCheckBox.IsChecked == true;
                settings.AutoDiscoveryEnabled = SettingsDiscoveryCheckBox.IsChecked == true;
                settings.AutoAddDetectedGames = SettingsAutoAddCheckBox.IsChecked == true;
                settings.F95UpdateChecksEnabled = SettingsF95UpdatesCheckBox.IsChecked == true;
                settings.F95UpdateNotificationsEnabled = SettingsF95NotificationsCheckBox.IsChecked == true;
                settings.F95UpdateCheckIntervalHours = (int)SettingsF95IntervalSlider.Value;
                settings.HoverPreviewDelaySeconds = (int)SettingsHoverDelaySlider.Value;
                settings.AutoDownloadF95Gallery = SettingsF95GalleryCheckBox.IsChecked == true;
                settings.RemoteHostAutoStart = SettingsRemoteAutoStartCheckBox.IsChecked == true;
                settings.RemoteHostPort = int.TryParse(SettingsRemotePortTextBox.Text, out int remotePort) ? Math.Clamp(remotePort, 1025, 65535) : 17840;
                settings.RemoteHostFps = (int)SettingsRemoteFpsSlider.Value;
                settings.RemoteHostQuality = (int)SettingsRemoteQualitySlider.Value;
                settings.RemoteHostWidth = (int)SettingsRemoteWidthSlider.Value;
                settings.RemotePrivacyCurtain = SettingsRemotePrivacyCheckBox.IsChecked == true;
                settings.RemotePerformanceMode = SettingsRemotePerformanceCheckBox.IsChecked == true;
                settings.RemoteStreamingV51Migrated = true;
                SavePresentationSettings(newPaths);
                settingsService.Save(newPaths);
                if (!PathSafety.Same(gameService.Paths.ArchivePath, newPaths.ArchivePath))
                {
                    gameService.UsePaths(newPaths);
                    InitializeDownloads();
                    selectedGame = null;
                    reinstallGame = null;
                }
                settingsService.ApplyStartWithWindows(settings.StartWithWindows);
                ApplyRuntimeSettings();
                RefreshGameList();
                ShowToast("Einstellungen gespeichert.");
            }
            catch (Exception ex)
            {
                settingsService.RestoreSettings(oldSettings);
                ConfigureRuntimeFeatures();
                ShowToast("Einstellungen konnten nicht gespeichert werden: " + ex.Message);
            }
        }

        private void OnSetupWizardCompleted(List<Views.SetupScanResult> selectedGames)
        {
            settingsService.Settings.IsFirstRun = false;
            settingsService.Save(gameService.Paths);

            int added = 0;
            foreach (var result in selectedGames)
            {
                var game = new Game
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = result.Name,
                    GameVersion = result.Version,
                    InstallSource = result.Source,
                    GamePath = result.DisplayPath,
                    ExecutablePath = result.ExecutablePath,
                    SavePath = result.SavePath,
                    CoverPath = result.CoverPath,
                    IsInstalled = true,
                    Status = GameStatus.Installed,
                    AddedDate = DateTime.Now
                };

                try
                {
                    game.CoverPath = gameService.CacheCover(game.CoverPath, game.Id);
                    gameService.AddGame(game);
                    if (TrySaveGames()) added++;
                    else gameService.RemoveGame(game);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Setup import failed for {game.Name}: {ex.Message}");
                }
            }

            RefreshGameList();
            ShowView(HomeView);
            HighlightNav(NavHomeButton);
            ShowToast(added > 0 ? $"{added} Spiele importiert." : "Setup abgeschlossen.");
        }

        private void OnSetupWizardSkipped()
        {
            settingsService.Settings.IsFirstRun = false;
            settingsService.Save(gameService.Paths);
            ShowView(HomeView);
            HighlightNav(NavHomeButton);
        }

        private void OnGlobalMouseShortcut()
        {
            if (capturingShortcut || fileOperationActive) return;
            try
            {
                if (settingsService.Settings.MinimizeGameOnShortcut && launchService.HasRunningGames)
                {
                    launchService.MinimizeGames();
                    HideToTray();
                }
                else if (IsVisible && WindowState != WindowState.Minimized && IsActive) HideToTray();
                else RestoreFromTray();
            }
            catch (Exception ex) { Debug.WriteLine(ex); ShowToast("Shortcut konnte nicht ausgefuehrt werden."); }
        }

        private void ResetForm()
        {
            if (!isEditingGame) gameService.DeleteTemporaryCover(GameCoverPath);
            GameNameTextBox.Text = "";
            GameVersionTextBox.Text = "";
            InstallSourceTextBox.Text = "";
            GameFolderPath = "";
            GamePathTextBox.Text = "";
            GameExecutablePath = "";
            ExecutableTextBox.Text = "";
            GameSaveFolderPath = "";
            SavePathTextBox.Text = "";
            GameCoverPath = "";
            GameModUrlTextBox.Text = "";
            GameCoverImage.ImagePath="";GameCoverImage.FocusX=GameCoverImage.FocusY=.5;
            ManageLocalGameCheckBox.IsChecked = settingsService.Settings.ManageLocalGames;
            GameFormTitleTextBlock.Text = "Spiel hinzufügen";
            SaveGameButton.Content = "Hinzufügen";
            editingGame = null;
            isEditingGame = false;
            ResetF95FormState();
            ResetValidation();
        }

        private void RefreshGameList()
        {
            if (gameService == null) return;
            RefreshHomeGameList();
            RefreshLibraryList();
            RefreshDashboard();
            RefreshAvailableUpdates();

            if (GameItemsControlWaiting != null)
            {
                var waiting = gameService.GetWaitingForUpdatesGames();
                GameItemsControlWaiting.ItemsSource = waiting;
                WaitingEmptyState.Visibility = waiting.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }

            if (GameItemsControlFinished != null)
            {
                var finished = gameService.GetFinishedGames();
                GameItemsControlFinished.ItemsSource = finished;
                FinishedEmptyState.Visibility = finished.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }

            if (GameItemsControlDeleted != null)
            {
                var deleted = gameService.GetDeletedGames();
                GameItemsControlDeleted.ItemsSource = deleted;
                DeletedEmptyState.Visibility = deleted.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void RefreshHomeGameList()
        {
            if (GameItemsControl == null || gameService == null)
                return;

            var games = gameService.QueryGames(GameListFilter.Installed, SearchGamesTextBox?.Text);
            GameItemsControl.ItemsSource = games.Take(8).ToList();
            HomeEmptyState.Visibility =
                games.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private void RefreshLibraryList()
        {
            if (LibraryItemsControl == null || gameService == null)
                return;

            var games = gameService.QueryGames(libraryFilter, LibrarySearchTextBox?.Text);
            var sorted = librarySort switch
            {
                "Recent" => games.OrderByDescending(g => g.LastPlayed).ThenBy(g => g.Name).ToList(),
                "Playtime" => games.OrderByDescending(g => g.Playtime).ThenBy(g => g.Name).ToList(),
                "Updates" => games.OrderByDescending(g => g.UpdateAvailable).ThenByDescending(g => g.LastUpdateCheckUtc).ThenBy(g => g.Name).ToList(),
                "Added" => games.OrderByDescending(g => g.AddedDate).ThenBy(g => g.Name).ToList(),
                _ => games.OrderBy(g => g.Name,StringComparer.CurrentCultureIgnoreCase).ToList()
            };
            LibraryItemsControl.ItemsSource = sorted;
            LibraryListControl.ItemsSource = sorted;
            LibraryItemsControl.Visibility = libraryListView ? Visibility.Collapsed : Visibility.Visible;
            LibraryListControl.Visibility = libraryListView ? Visibility.Visible : Visibility.Collapsed;
            foreach (var sort in new[] {SortNameButton, SortRecentButton, SortPlaytimeButton, SortUpdateButton, SortAddedButton})
                sort.Background = (System.Windows.Media.Brush)FindResource((string)sort.Tag == librarySort ? "AccentSoft" : "SurfaceSecondary");
            LibraryEmptyState.Visibility = games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowView(UIElement view)
        {
            if (view != AddGameView && AddGameView.Visibility == Visibility.Visible) ResetForm();
            DownloadsPageHost.Visibility = Visibility.Collapsed;
            BackupsPageHost.Visibility = Visibility.Collapsed;
            HomeView.Visibility = Visibility.Collapsed;
            LibraryView.Visibility = Visibility.Collapsed;
            AvailableUpdatesView.Visibility = Visibility.Collapsed;
            AddGameView.Visibility = Visibility.Collapsed;
            GameDetailView.Visibility = Visibility.Collapsed;
            WaitingForUpdatesView.Visibility = Visibility.Collapsed;
            FinishedView.Visibility = Visibility.Collapsed;
            DeletedView.Visibility = Visibility.Collapsed;
            InstallGameView.Visibility = Visibility.Collapsed;
            SettingsView.Visibility = Visibility.Collapsed;
            ImportsView.Visibility = Visibility.Collapsed;
            SetupWizardHost.Visibility = Visibility.Collapsed;
            DeleteGameDialog.Visibility = Visibility.Collapsed;
            AbsoluteDeleteDialog.Visibility = Visibility.Collapsed;
            SaveConflictDialog.Visibility = Visibility.Collapsed;

            view.Visibility = Visibility.Visible;
            // A focused control in the previous page can otherwise keep a stale
            // scroll offset when WPF brings the next page into view.
            Keyboard.ClearFocus();
            ResetPageScroll(view);
            AnimatePage(view);

            if (view == HomeView || view == LibraryView ||
                view == AvailableUpdatesView || view == WaitingForUpdatesView || view == FinishedView || view == DeletedView)
            {
                lastListView = view;
            }
            if (view == HomeView) HighlightNav(NavHomeButton);
            else if (view == LibraryView) HighlightNav(NavLibraryButton);
            else if (view == AvailableUpdatesView) HighlightNav(NavUpdatesButton);
            else if (view == WaitingForUpdatesView) HighlightNav(NavUpdatesButton);
            else if (view == FinishedView) HighlightNav(NavLibraryButton);
            else if (view == DeletedView) HighlightNav(NavLibraryButton);
        }

        private static void ResetPageScroll(UIElement view)
        {
            if (view is not DependencyObject root) return;
            foreach (var scroll in FindVisualChildren<ScrollViewer>(root)) scroll.ScrollToTop();
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
        {
            for (int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            {
                var child=VisualTreeHelper.GetChild(root,i);
                if(child is T match) yield return match;
                foreach(var descendant in FindVisualChildren<T>(child)) yield return descendant;
            }
        }

        private bool ValidateGameForm(bool requirePaths)
        {
            bool valid = true;
            GameSaveFolderPath=SaveLocationService.Expand(SavePathTextBox.Text);
            ResetValidation();

            if (string.IsNullOrWhiteSpace(GameNameTextBox.Text))
            {
                MarkInvalid(GameNameTextBox);
                valid = false;
            }

            if (F95UpdateService.NormalizeVersion(GameVersionTextBox.Text) == null)
            {
                MarkInvalid(GameVersionTextBox);
                valid = false;
            }

            if (string.IsNullOrWhiteSpace(InstallSourceTextBox.Text))
            {
                MarkInvalid(InstallSourceTextBox);
                valid = false;
            }

            if (requirePaths)
            {
                if (string.IsNullOrWhiteSpace(GameFolderPath) || !Directory.Exists(GameFolderPath))
                {
                    MarkInvalid(GamePathTextBox);
                    valid = false;
                }

                if (string.IsNullOrWhiteSpace(GameExecutablePath) || !File.Exists(GameExecutablePath))
                {
                    MarkInvalid(ExecutableTextBox);
                    valid = false;
                }
                else if (!string.IsNullOrWhiteSpace(GameFolderPath) && !PathSafety.Within(GameExecutablePath, GameFolderPath))
                {
                    MarkInvalid(ExecutableTextBox);
                    ShowToast("Die Startdatei muss im gewaehlten Spielordner liegen.");
                    valid = false;
                }

                if (!string.IsNullOrWhiteSpace(GameSaveFolderPath) && !Directory.Exists(GameSaveFolderPath))
                {
                    MarkInvalid(SavePathTextBox);
                    valid = false;
                }
            }

            if (!string.IsNullOrWhiteSpace(GameCoverPath) && !File.Exists(GameCoverPath))
            {
                MarkInvalid(GameCoverBorder);
                valid = false;
            }

            return valid;
        }

        private void MarkInvalid(System.Windows.Controls.Control control)
        {
            control.BorderBrush = new SolidColorBrush(Color.FromRgb(220, 60, 70));
            control.BorderThickness = new Thickness(2);
            if (FindName(control.Name + "Error") is TextBlock error)
            { error.Text = control.Name.Contains("Path") || control.Name == "ExecutableTextBox" ? "Bitte einen gültigen, passenden Pfad wählen." : "Bitte dieses Feld ausfüllen."; error.Visibility = Visibility.Visible; }
        }

        private void MarkInvalid(Border border)
        {
            border.BorderBrush = new SolidColorBrush(Color.FromRgb(220, 60, 70));
            border.BorderThickness = new Thickness(2);
        }

        private void ResetValidation()
        {
            ClearValidation(GameNameTextBox);
            ClearValidation(GameVersionTextBox);
            ClearValidation(InstallSourceTextBox);
            ClearValidation(GamePathTextBox);
            ClearValidation(ExecutableTextBox);
            ClearValidation(SavePathTextBox);
            GameCoverBorder.ClearValue(Border.BorderBrushProperty);
            GameCoverBorder.ClearValue(Border.BorderThicknessProperty);
        }

        private void AttachValidationResetHandlers()
        {
            GameNameTextBox.TextChanged += (_, _) => ClearValidation(GameNameTextBox);
            GameVersionTextBox.TextChanged += (_, _) => ClearValidation(GameVersionTextBox);
            InstallSourceTextBox.TextChanged += (_, _) => ClearValidation(InstallSourceTextBox);
        }

        private void ClearValidation(System.Windows.Controls.Control control)
        {
            if (FindName(control.Name + "Error") is TextBlock error) error.Visibility = Visibility.Collapsed;
            control.ClearValue(Control.BorderBrushProperty);
            control.ClearValue(Control.BorderThicknessProperty);
        }

        private bool TrySaveGames()
        {
            try
            {
                gameService.SaveGames();
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                MessageBox.Show("Die Spiele konnten nicht gespeichert werden.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void ShowToast(string message)
        {
            ToastText.Text = message;
            ToastHost.Visibility = Visibility.Visible;
            toastTimer.Stop();
            toastTimer.Start();
        }

        private void ApplyTraySetting(bool enabled)
        {
            EnsureTrayIcon();
        }

        private void EnsureTrayIcon()
        {
            if (trayIcon != null)
                return;

            Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
            menu.Items.Add("GameLauncher oeffnen", null, (_, _) => Dispatcher.Invoke(RestoreFromTray));
            menu.Items.Add("Zum Spiel", null, (_, _) => Dispatcher.Invoke(() => launchService.RestoreGames()));
            menu.Items.Add("Beenden", null, (_, _) => Dispatcher.Invoke(ExitApplication));

            trayIcon = new Forms.NotifyIcon
            {
                Icon = Drawing.SystemIcons.Application,
                Text = "GameLauncher",
                ContextMenuStrip = menu,
                Visible = true
            };
            trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreFromTray);
        }

        private void HideToTray()
        {
            EnsureTrayIcon();

            Hide();
            StatusLineText.Text = "Im Infobereich";
        }

        private void RestoreFromTray()
        {
            SetLauncherPriority(false);
            Show();

            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;

            Activate();
            StatusLineText.Text = launchService.HasRunningGames ? "Läuft: " + launchService.RunningTitle : "Bereit";
        }

        private void ExitApplication()
        {
            forceExit = true;
            Close();
        }

        private void HighlightNav(Control active)
        {
            Control[] buttons =
            {
                NavHomeButton, NavLibraryButton, NavUpdatesButton,
                NavAddGameButton, NavSettingsButton
            };

            foreach (Control button in buttons)
            {
                button.Foreground = (Brush)FindResource("TextSecondary");
                button.Background = Brushes.Transparent;
            }

            active.Foreground = (Brush)FindResource("TextPrimary");
            active.Background = (Brush)FindResource("SurfaceHover");
        }

        private string? PickFolder(string? initial)
        {
            OpenFolderDialog dialog = new OpenFolderDialog();
            if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial))
                dialog.InitialDirectory = initial;

            return dialog.ShowDialog() == true ? dialog.FolderName : null;
        }

        private static string? PickExe(string? initialFolder)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "Executable files (*.exe)|*.exe"
            };

            if (!string.IsNullOrWhiteSpace(initialFolder) && Directory.Exists(initialFolder))
                dialog.InitialDirectory = initialFolder;

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        private static void OpenFolderIfExists(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }

        private static void OpenF95Thread(Game game)
        {
            if (string.IsNullOrWhiteSpace(game.F95ThreadId)) return;
            Process.Start(new ProcessStartInfo
            {
                FileName = $"https://f95zone.to/thread/{game.F95ThreadId}/",
                UseShellExecute = true
            });
        }

        private static System.Windows.Media.Brush StatusBrush(string status)
        {
            return status switch
            {
                GameStatus.Installed => new SolidColorBrush(Color.FromRgb(77, 183, 131)),
                GameStatus.WaitingForUpdates => new SolidColorBrush(Color.FromRgb(215, 168, 75)),
                GameStatus.Finished => new SolidColorBrush(Color.FromRgb(124, 111, 255)),
                GameStatus.Deleted => new SolidColorBrush(Color.FromRgb(218, 105, 117)),
                _ => new SolidColorBrush(Color.FromRgb(156, 162, 174))
            };
        }

        private static BitmapImage? TryLoadBitmap(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;

            try
            {
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path);
                bitmap.DecodePixelWidth = 1000;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        private static void TrackUserEdits(Game game, F95Metadata metadata)
        {
            if (metadata == null) return;

            if (game.Name != metadata.Name && !string.IsNullOrWhiteSpace(metadata.Name))
                game.UserEditedFields.Add("Name");
            if (F95UpdateService.NormalizeVersion(game.GameVersion) != F95UpdateService.NormalizeVersion(metadata.Version)
                && !string.IsNullOrWhiteSpace(metadata.Version))
                game.UserEditedFields.Add("GameVersion");
            if (!string.IsNullOrWhiteSpace(game.Notes) && game.Notes.Length > 0)
                game.UserEditedFields.Add("Notes");
        }
    }
}
