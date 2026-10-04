using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GameLauncher;
using GameLauncher.models;
using GameLauncher.Service;

internal static class UiChecks
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { Test(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) throw new Exception("UI checks failed",failure);
    }
    private static void Test()
    {
        var paths = AppPaths.Default();
        if (!Environment.GetCommandLineArgs().Contains("--data-root") || !PathSafety.Within(paths.ArchivePath,Path.GetTempPath())) throw new Exception("UI tests require an isolated temporary data root.");
        paths.EnsureCreated();
        var settings = new AppSettings { ArchivePath = paths.ArchivePath, KeyboardShortcutEnabled = false, AutoDiscoveryEnabled = false, F95UpdateChecksEnabled = false, IsFirstRun = false, ReduceMotion = true };
        File.WriteAllText(paths.SettingsFilePath,JsonSerializer.Serialize(settings));
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new MainWindow { Title = "GameLauncher · UI-Prüfung" };
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Call(string name, params object[] arguments) => typeof(MainWindow).GetMethod(name,flags)!.Invoke(window,arguments);
        T Find<T>(string name) where T : class => window.FindName(name) as T ?? throw new Exception("Missing "+name);
        T? Descendant<T>(DependencyObject root) where T : DependencyObject
        {
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            {
                var child=VisualTreeHelper.GetChild(root,i);
                if(child is T found) return found;
                var nested=Descendant<T>(child);
                if(nested!=null) return nested;
            }
            return null;
        }
        void Pump() { window.UpdateLayout(); window.Dispatcher.Invoke(() => {},DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
        void Check(bool condition,string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS UI: " + message); }
        void Shot(string name)
        {
            Pump(); var image = new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32); image.Render(window);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image)); using var stream = File.Create(Path.Combine(paths.ArchivePath,name+".png")); png.Save(stream);
        }
        window.Show(); Pump();
        try
        {
            Check(window.ActualHeight <= SystemParameters.WorkArea.Height,"Initial window fits work area");
            foreach (double dpi in new[] {1d,1.25,1.5,2})
                foreach (var area in new[] {new Rect(0,0,1366,728),new Rect(-1920,40,1920,1040),new Rect(1920,0,1280,680)})
                {
                    var fit = WindowSizing.FitBounds(area,new Rect(-500,-500,2000,1800),dpi,true);
                    Check(area.Contains(fit),"Monitor bounds / DPI " + dpi + " " + area.Width);
                }
            foreach (var size in new[] {new Size(1000,650),new Size(1366,768)})
            {
                window.Width=size.Width; window.Height=size.Height; Pump();
                Call("OpenAddGameButton_Click",window,new RoutedEventArgs()); Pump();
                var name=Find<TextBox>("GameNameTextBox"); name.Text="Änderungen – vollständig lesbare Schrift";
                Find<TextBox>("GameVersionTextBox").Text="1.0";
                Find<TextBox>("InstallSourceTextBox").Text="Lokale Installation"; Pump();
                var host=(ScrollViewer)name.Template.FindName("PART_ContentHost",name);
                Check(host.ViewportHeight >= name.FontSize * 1.15,"Text viewport has room for a complete line at " + size.Width + ": " + host.ViewportHeight);
                var save=Find<Button>("SaveGameButton"); var pos=save.TransformToAncestor(window).Transform(new Point());
                Check(pos.Y+save.ActualHeight <= window.ActualHeight,"Save action remains within window at " + size.Width);
                Shot("form-"+(int)size.Width);
                Call("SettingsNavigationButton_Click",window,new RoutedEventArgs()); Pump();
                Check(!string.IsNullOrWhiteSpace(Find<TextBox>("SettingsManagedGameFolderTextBox").Text) &&
                    Find<CheckBox>("SettingsManageGamesCheckBox").IsChecked==true &&
                    Find<CheckBox>("SettingsF95UpdatesCheckBox").IsChecked==settings.F95UpdateChecksEnabled,
                    "Managed GameLibrary controls load their saved defaults");
                Check(Find<Slider>("SettingsRemoteFpsSlider").Maximum==60 &&
                    Find<Slider>("SettingsRemoteWidthSlider").Minimum==1280 &&
                    Find<CheckBox>("SettingsRemotePrivacyCheckBox").IsChecked==false &&
                    Find<CheckBox>("SettingsRemotePrivacyCheckBox").IsEnabled==false &&
                    Find<CheckBox>("SettingsRemotePerformanceCheckBox").IsChecked==true,
                    "Remote host exposes HD/60 FPS, privacy curtain and streaming focus controls");
                var shell=Find<Grid>("ShellGrid");
                var shellOrigin=shell.TransformToAncestor(window).Transform(new Point());
                Check(shellOrigin.Y>=-.5,"Settings layout remains anchored to the window at "+size.Width+" (Y="+shellOrigin.Y+")");
                Shot("settings-"+(int)size.Width);
            }
            var settingsService=(SettingsService)typeof(MainWindow).GetField("settingsService",flags)!.GetValue(window)!;
            settingsService.Settings.UiScale=2; settingsService.Settings.ReduceMotion=true; Call("ApplyPresentation"); Pump(); Shot("settings-scale-200");
            Check(Find<RadioButton>("NavUpdatesButton") != null, "Bottom navigation survives large UI scale");
            settingsService.Settings.UiScale=1; Call("ApplyPresentation");
            var service=(GameService)typeof(MainWindow).GetField("gameService",flags)!.GetValue(window)!;
            string[] testArgs = Environment.GetCommandLineArgs();
            int coverIndex = Array.IndexOf(testArgs, "--fixture-covers");
            string[] covers = coverIndex >= 0 && coverIndex + 1 < testArgs.Length ? Directory.GetFiles(testArgs[coverIndex + 1]).Take(5).ToArray() : Array.Empty<string>();
            foreach (string cover in covers) _ = ImageCache.Load(cover,640).GetAwaiter().GetResult();
            for (int i=0;i<120;i++) service.AddGame(new Game {Id="ui-"+i,CoverPath=covers.Length>0?covers[i%covers.Length]:"",Name=i==0?"Eine sehr lange Spielbezeichnung mit vielen Wörtern und Sonderzeichen äöü": "Spiel "+i.ToString("000"),GameVersion="1.0",Notes="Notizen bleiben erhalten.",VersionInfo="Build 42",LastPlayed=i<4?DateTime.Now.AddDays(-i):null,Playtime=TimeSpan.FromHours(i),IsFavorite=i%3==0});
            Call("RefreshGameList"); Call("HomeNavigationButton_Click",window,new RoutedEventArgs()); Pump(); Shot("dashboard");
            Call("LibraryNavigationButton_Click",window,new RoutedEventArgs()); Pump(); Shot("library");
            var nav = Find<Border>("FloatingBottomNav");
            Check(nav.TransformToAncestor(window).Transform(new Point()).Y > window.ActualHeight / 2, "Floating navigation stays at the bottom without covering search");
            var gameList=Find<ItemsControl>("LibraryItemsControl");
            var virtualPanel=Descendant<GameLauncher.Controls.VirtualizingGamePanel>(gameList);
            Check(virtualPanel!=null&&virtualPanel.Children.Count<40,"Large library realizes only visible card rows");
            var card=Descendant<GameLauncher.Controls.GameCard>(gameList);
            var artwork=card==null?null:Descendant<GameLauncher.Controls.CroppedImage>(card);
            Check(artwork!=null&&Math.Abs(artwork.ActualHeight/artwork.ActualWidth-9d/16)<.03,"Game cards use landscape 16:9 artwork");
            Check(card != null, "Library contains a game card for size checks");
            settingsService.Settings.ReduceMotion=false;Call("ApplyPresentation");
            var wheel=new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount,-120) { RoutedEvent=UIElement.PreviewMouseWheelEvent,Source=card };
            card!.RaiseEvent(wheel);
            var scrollTimeout=System.Diagnostics.Stopwatch.StartNew();while(scrollTimeout.ElapsedMilliseconds<300){Pump();Thread.Sleep(10);}
            Check(wheel.Handled&&virtualPanel!.VerticalOffset>0&&virtualPanel.VerticalOffset<=120,"Wheel scroll interpolates bounded pixel movement without losing virtualization");
            virtualPanel!.SetVerticalOffset(0);Pump();settingsService.Settings.ReduceMotion=true;Call("ApplyPresentation");
            var sizeText = (TextBlock)card!.FindName("InstalledSizeText");
            var originalContext = card.DataContext;
            string sizeFolder = Path.Combine(paths.ArchivePath, "SizeFixture");
            Directory.CreateDirectory(sizeFolder);
            File.WriteAllBytes(Path.Combine(sizeFolder, "game.bin"), new byte[3072]);
            var sizeGame = new Game { Name = "Size fixture", GameVersion = "1.0", GamePath = sizeFolder, IsInstalled = true };
            void WaitForSize(string text)
            {
                var timeout = System.Diagnostics.Stopwatch.StartNew();
                while (sizeText.Text != text && timeout.Elapsed < TimeSpan.FromSeconds(10)) { Pump(); Thread.Sleep(10); }
                Check(sizeText.Text == text, "Game card size: " + text);
            }
            card.DataContext = sizeGame;
            WaitForSize("3.0 KB");
            Check(sizeText.Visibility == Visibility.Visible, "Installed size is visible on the card");
            Shot("library-installed-size");
            sizeGame.GamePath = Path.Combine(sizeFolder, "missing");
            WaitForSize("Größe unbekannt");
            sizeGame.IsInstalled = false;
            Check(sizeText.Visibility == Visibility.Collapsed, "Uninstalled cards hide installation size");
            sizeGame.GamePath = sizeFolder;
            sizeGame.IsInstalled = true;
            card.DataContext = new Game { Name = "Recycled card", IsInstalled = true, GamePath = Path.Combine(sizeFolder, "missing") };
            WaitForSize("Größe unbekannt");
            card.DataContext = originalContext;
            Call("LibrarySort_Click",Find<Button>("SortPlaytimeButton"),new RoutedEventArgs());
            Check(((Game)Find<ItemsControl>("LibraryItemsControl").Items[0]).Id == "ui-119","Playtime sort orders by real playtime");
            var checkedGame=service.GetGames()[1]; checkedGame.LastUpdateCheckUtc=DateTime.UtcNow;
            var updateGame=service.GetGames()[0]; updateGame.InstallSource="https://f95zone.to/threads/test.230209/";
            updateGame.UpdateAvailable=true; updateGame.LatestKnownVersion="v2.0";
            Call("RefreshGameList"); Call("AvailableUpdatesNavigationButton_Click",window,new RoutedEventArgs()); Pump(); Shot("updates");
            Check(Find<ItemsControl>("RecentlyUpdatedItemsControl").Items.Count == 1, "Recently checked list remains in the shared update flow");
            Check(Find<ItemsControl>("AvailableUpdatesItemsControl").Items.Count==1 &&
                Find<Border>("NavUpdatesBadge").Visibility==Visibility.Visible,
                "Available updates view and navigation badge reflect detected updates");
            updateGame.UpdateCheckError="Der Update-Dienst ist vorübergehend nicht erreichbar.";
            Call("RefreshGameList"); Pump();
            Check(Find<Border>("UpdateErrorsPanel").Visibility==Visibility.Visible &&
                Find<ItemsControl>("UpdateErrorsItemsControl").Items.Count==1,
                "Update failures stay visible instead of being treated as no update");
            Call("ShowGameDetails",updateGame); Pump();
            Call("IgnoreOnlineVersion_Click",window,new RoutedEventArgs());
            Check(!updateGame.UpdateAvailable && updateGame.IgnoredUpdateVersion=="v2.0",
                "Current online release can be ignored from game details");
            updateGame.LatestKnownVersion="v3.0";
            updateGame.UpdateAvailable=F95UpdateService.IsUpdateAvailable(updateGame.GameVersion,updateGame.LatestKnownVersion,updateGame.IgnoredUpdateVersion);
            Call("ShowGameDetails",updateGame); Call("MarkOnlineVersionInstalled_Click",window,new RoutedEventArgs());
            Check(updateGame.GameVersion=="v3.0" && !updateGame.UpdateAvailable,
                "Online release can be marked as the installed version");
            updateGame.UpdateAvailable=false; updateGame.UpdateCheckError=""; Call("RefreshGameList");
            Call("LibraryNavigationButton_Click",window,new RoutedEventArgs());
            Find<TextBox>("SearchGamesTextBox").Text="Spiel 011"; Call("ApplySearch"); Pump();
            Check(Find<ItemsControl>("LibraryItemsControl").Items.Count == 1,"Global search updates library");
            Find<TextBox>("SearchGamesTextBox").Clear();Call("ApplySearch");
            var archived=service.GetGames()[0]; archived.Status=GameLauncher.Constants.GameStatus.WaitingForUpdates; archived.IsInstalled=false; archived.InstallSource="https://example.com/game";
            Call("OpenReinstall",archived); Pump();
            Check(Find<Grid>("ReinstallChoiceDialog").Visibility==Visibility.Visible&&Find<Button>("SourceInstallButton").IsEnabled,
                "Archived game asks whether to install from source or link an installation");
            Call("CloseReinstallChoice_Click",window,new RoutedEventArgs());
            archived.Status=GameLauncher.Constants.GameStatus.Deleted; archived.IsInstalled=false;
            Call("RefreshGameList"); Call("DeletedButtonNav_Click",window,new RoutedEventArgs()); Pump();
            Check(Find<ItemsControl>("GameItemsControlDeleted").Items.Cast<Game>().Any(g=>g.Id==archived.Id),
                "Deleted game appears in its separate library");
            service.GetGames()[0].PathWarning="Startdatei fehlt";
            Call("ShowGameDetails",service.GetGames()[0]); Pump(); Shot("details");
            Check(Find<Border>("GameDetailPathWarningPanel").Visibility==Visibility.Visible,
                "Game details explain missing installation paths and offer relinking");
            Check(Equals(Find<Button>("GameDetailOpenSourceButton").Content,"Website öffnen ↗"),
                "Game detail offers a direct source website action");
            Check(Equals(Find<Button>("GameDetailFullDeleteButton").Content,"Vollständig löschen") &&
                Find<Button>("GameDetailFullDeleteButton").Visibility==Visibility.Visible,
                "Game detail keeps a clearly labeled full-delete action visible");
            var detailCrop=Find<GameLauncher.Controls.CroppedImage>("GameDetailCoverImage"); detailCrop.FocusX=.82; detailCrop.FocusY=.24;
            Call("GameDetailCoverImage_CropChanged",detailCrop,EventArgs.Empty);
            Check(Math.Abs(archived.CoverFocusX-.82)<.001&&Math.Abs(archived.CoverFocusY-.24)<.001,
                "Dragging detail artwork persists its crop position");
            Call("BackupsNavigation_Click",window,new RoutedEventArgs()); Pump(); Shot("backups");
            Check(Find<Grid>("BackupsPageHost").IsVisible, "Backups navigation opens connected view");
            Call("DownloadsNavigation_Click",window,new RoutedEventArgs()); Pump(); Shot("downloads");
            Check(Find<Grid>("DownloadsPageHost").IsVisible, "Downloads navigation opens connected view");
            Call("LibraryNavigationButton_Click",window,new RoutedEventArgs()); Pump();
            var converter = new GameLauncher.Converters.CardWidthConverter();
            Check((double)converter.Convert(1920d,typeof(double),null!,System.Globalization.CultureInfo.InvariantCulture) >= 368, "Wide libraries keep at most five medium landscape cards");
            Call("OpenDeleteDialog",service.GetGames()[0]); Pump(); Shot("archive-dialog");
            Call("CloseDeleteDialogButton_Click",window,new RoutedEventArgs());
            Call("SettingsNavigationButton_Click",window,new RoutedEventArgs());
            settingsService.Settings.UiScale=2; Call("ApplyPresentation");
            Call("OpenAddGameButton_Click",window,new RoutedEventArgs()); Pump(); Shot("form-scale-200");
            settingsService.Settings.UiScale=1;Call("ApplyPresentation");
            string catalogCover=Path.Combine(paths.GamesDataPath,"Covers","catalog-42.png");Directory.CreateDirectory(Path.GetDirectoryName(catalogCover)!);File.Copy(Path.Combine(paths.ArchivePath,"library.png"),catalogCover,true);
            var originalFactory=CatalogProviders.Create;CatalogProviders.Create=()=>new UiCatalogProvider();
            typeof(MainWindow).GetField("CatalogMetadataLoader",flags)!.SetValue(window,(Func<string,CancellationToken,Task<F95Metadata>>)((source,token)=>Task.FromResult(new F95Metadata("42","Fixture","1.2","Studio","Active","",Array.Empty<string>()){Tags=new[]{"adventure"}})));
            try
            {
                Find<TextBox>("GamePathTextBox").Text="Preserved local input";
                Call("SearchCatalog_Click",window,new RoutedEventArgs());Pump();
                Check(app.Windows.Count==1&&Find<Grid>("CatalogPageHost").IsVisible,"Catalog is a page without a second window");
                Find<TextBox>("CatalogQuery").Text="Fixture";
                Call("CatalogSearch_Click",window,new RoutedEventArgs());Pump();
                Check(Find<ListBox>("CatalogResults").Items.Count==1,"Inline catalog binds search results");
                Check(((CatalogCard)Find<ListBox>("CatalogResults").Items[0]).CoverPath==catalogCover,"Catalog reuses cached cover without gallery downloads");
                Shot("catalog-page");
                Find<ListBox>("CatalogResults").SelectedIndex=0;Call("CatalogUse_Click",window,new RoutedEventArgs());Pump();
                Check(Find<Grid>("AddGameView").IsVisible&&!Find<Grid>("CatalogPageHost").IsVisible,"Taking metadata returns to add page");
                Check(Find<TextBox>("GameNameTextBox").Text=="Fixture"&&Find<TextBox>("GameVersionTextBox").Text=="1.2"&&Find<TextBox>("InstallSourceTextBox").Text.EndsWith("/42/"),"Catalog fills name version and F95 source");
                Check(Find<TextBox>("GamePathTextBox").Text=="Preserved local input","Catalog roundtrip preserves local form paths");
                Check((string)typeof(MainWindow).GetField("GameCoverPath",flags)!.GetValue(window)! == catalogCover,"Catalog transfers selected cover into form");
                Check(service.GetGames().Count(g=>g.F95ThreadId=="42")==1&&service.GetGames().Single(g=>g.F95ThreadId=="42").Tags.Contains("adventure"),"Taking metadata remembers one wishlist entry with tags");
            }
            finally{CatalogProviders.Create=originalFactory;}
            var controlled=new UiControlledCatalogProvider();CatalogProviders.Create=()=>controlled;
            Call("SearchCatalog_Click",window,new RoutedEventArgs());Find<TextBox>("CatalogQuery").Text="old";Call("CatalogSearch_Click",window,new RoutedEventArgs());
            Find<TextBox>("CatalogQuery").Text="new";Call("CatalogSearch_Click",window,new RoutedEventArgs());
            controlled.New.TrySetResult(new[]{new CatalogResult("91","New result","Studio","https://f95zone.to/threads/91/")});Pump();
            controlled.Old.TrySetResult(new[]{new CatalogResult("90","Old result","Studio","https://f95zone.to/threads/90/")});Pump();
            Check(((CatalogCard)Find<ListBox>("CatalogResults").Items[0]).Title=="New result","Late old search response cannot replace current results");
            Call("CloseCatalog_Click",window,new RoutedEventArgs());Pump();CatalogProviders.Create=originalFactory;
            var choiceTask=(Task<Association?>)typeof(MainWindow).GetMethod("ChooseAssociationAsync",flags)!.Invoke(window,new object[]{new[]{new Association(service.GetGames()[0],82,"Abkürzung – bitte bestätigen",false)}})!;Pump();
            var choiceHost=Find<Grid>("ShellGrid").Children.OfType<Border>().Last();
            var choicePanel=(StackPanel)((ScrollViewer)choiceHost.Child).Content;
            Check(app.Windows.Count==1&&choiceHost.IsVisible,"Association suggestions are an inline confirmation with reasons");Shot("association-overlay");
            choicePanel.Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
            Check(choiceTask.IsCompletedSuccessfully&&choiceTask.Result?.Game==service.GetGames()[0],"Explicit choice returns the selected identity");
            var cancelTask=(Task<Association?>)typeof(MainWindow).GetMethod("ChooseAssociationAsync",flags)!.Invoke(window,new object[]{new[]{new Association(service.GetGames()[0],74,"Tippfehler",false)}})!;Pump();
            choiceHost=Find<Grid>("ShellGrid").Children.OfType<Border>().Last();choicePanel=(StackPanel)((ScrollViewer)choiceHost.Child).Content;
            choicePanel.Children.OfType<Button>().Last().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
            Check(cancelTask.IsCompletedSuccessfully&&cancelTask.Result==null&&(bool)typeof(MainWindow).GetField("associationAborted",flags)!.GetValue(window)!,"Cancelling association does not authorize separate installation");
            Call("OpenDiagnostics_Click",window,new RoutedEventArgs()); Pump();
            var diagnostics=app.Windows.Cast<Window>().Single(w=>w!=window&&w.Title=="Diagnoseprotokolle");
            Check(diagnostics.Owner==window,"Diagnostics is an owned independent window");diagnostics.Close();Pump();
            Check(window.IsVisible&&!window.Dispatcher.HasShutdownStarted,"Closing diagnostics keeps launcher alive");
            if(service.GetGames().Any(g=>File.Exists(g.CoverPath)))
            {
                Call("ShowGameDetails",service.GetGames().First(g=>File.Exists(g.CoverPath)));Call("OpenGallery_Click",window,new RoutedEventArgs());Pump();
                var gallery=Find<Border>("GalleryOverlayHost").Child as GameLauncher.Views.GalleryOverlay;
                Check(gallery?.IsVisible==true&&app.Windows.Count==1,"Gallery opens inline without another window");Shot("gallery-overlay");Call("CloseGallery");Pump();
                Check(window.IsVisible&&!window.Dispatcher.HasShutdownStarted,"Closing gallery keeps launcher alive");
            }
            Call("OpenAddGameButton_Click",window,new RoutedEventArgs());
            Find<TextBox>("GameNameTextBox").Text="Only a wish";Find<TextBox>("GameVersionTextBox").Clear();Find<TextBox>("InstallSourceTextBox").Clear();
            Call("WishlistButton_Click",window,new RoutedEventArgs());Pump();
            Check(service.GetGames().Count(g=>g.Name=="Only a wish"&&g.Status==GameLauncher.Constants.GameStatus.Wishlist)==1,"Wishlist accepts a title without paths, source or version");
            Call("WishlistNavigation_Click",window,new RoutedEventArgs());Pump();
            Check(Find<ItemsControl>("LibraryItemsControl").Items.Cast<Game>().All(g=>g.Status==GameLauncher.Constants.GameStatus.Wishlist),"Direct wishlist navigation filters the library");
            Call("AvailableUpdatesNavigationButton_Click",window,new RoutedEventArgs());Pump();
            Check(Find<ScrollViewer>("UpdatesScroll").Content is StackPanel,"Update lists share one natural document scroll flow");
            Console.WriteLine("UI screenshots: " + paths.ArchivePath);
        }
        finally { typeof(MainWindow).GetField("forceExit",flags)!.SetValue(window,true); window.Close(); app.Shutdown(); }
    }
}

internal sealed class UiCatalogProvider : IGameCatalogProvider
{
    public string Id=>"ui-fixture";
    public Task<IReadOnlyList<CatalogResult>> SearchAsync(string query,CancellationToken cancellation)=>Task.FromResult<IReadOnlyList<CatalogResult>>(new[]{new CatalogResult("42","Fixture","Studio","https://f95zone.to/threads/42/")});
}



internal sealed class UiControlledCatalogProvider : IGameCatalogProvider
{
 public string Id=>"controlled-ui";
 public TaskCompletionSource<IReadOnlyList<CatalogResult>> Old {get;}=new();
 public TaskCompletionSource<IReadOnlyList<CatalogResult>> New {get;}=new();
 public Task<IReadOnlyList<CatalogResult>> SearchAsync(string query,CancellationToken cancellation)=>query=="old"?Old.Task:New.Task;
}
