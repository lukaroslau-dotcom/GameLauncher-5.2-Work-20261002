using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameLauncher.Constants;
using GameLauncher.models;
using GameLauncher.Service;
namespace GameLauncher;
public partial class MainWindow
{
    private IGameCatalogProvider? catalogProvider;
    private CancellationTokenSource? catalogCancellation;
    private UIElement? catalogReturnPage;
    private int catalogGeneration;
    private bool catalogTaking;
    // Injectable boundary used by isolated UI checks; production uses the existing metadata service.
    internal Func<string,CancellationToken,Task<F95Metadata>>? CatalogMetadataLoader = null;
    private void WishlistNavigation_Click(object sender,RoutedEventArgs e)
    {libraryFilter=GameListFilter.Wishlist;LibrarySearchTextBox.Clear();SearchGamesTextBox.Clear();RefreshGameList();ShowView(LibraryView);HighlightNav(NavLibraryButton);}
    private void SearchCatalog_Click(object sender,RoutedEventArgs e)
    {
        if(CatalogPageHost.IsVisible)return;
        catalogReturnPage=PageHost.Children.Cast<UIElement>().FirstOrDefault(p=>p.Visibility==Visibility.Visible)??HomeView;
        CatalogQuery.Text=AddGameView.IsVisible?GameNameTextBox.Text:SearchGamesTextBox.Text;
        CancelF95MetadataLoad();ShowView(CatalogPageHost);
        catalogProvider=CatalogProviders.Create();CatalogResults.ItemsSource=null;CatalogUseButton.IsEnabled=false;
        CatalogStatus.Text="Namen eingeben. Nur Cover werden als Vorschau geladen; keine Galerie.";
        CatalogAccountButton.Visibility=catalogProvider is ICatalogAccountProvider?Visibility.Visible:Visibility.Collapsed;
        CatalogQuery.Focus();
    }
    private void CancelCatalog()
    {
        catalogGeneration++;catalogCancellation?.Cancel();catalogCancellation?.Dispose();catalogCancellation=null;
        (catalogProvider as IDisposable)?.Dispose();catalogProvider=null;catalogTaking=false;
        if(CatalogSearchButton!=null)CatalogSearchButton.IsEnabled=true;
    }
    private void CloseCatalog_Click(object sender,RoutedEventArgs e)=>ShowView(catalogReturnPage??HomeView);
    private async void CatalogAccount_Click(object sender,RoutedEventArgs e)
    {
        if(catalogProvider is not ICatalogAccountProvider account)return;
        catalogCancellation??=new();CatalogAccountButton.IsEnabled=false;
        try{if(account.IsSignedIn)await account.SignOutAsync(catalogCancellation.Token);else await account.SignInAsync(catalogCancellation.Token);CatalogAccountButton.Content=account.IsSignedIn?"Abmelden":"Anmelden";}
        catch(OperationCanceledException){}catch(Exception ex){CatalogStatus.Text=ex.Message;}finally{CatalogAccountButton.IsEnabled=true;}
    }
    private async void CatalogSearch_Click(object sender,RoutedEventArgs e)=>await SearchCatalogAsync();
    private async void CatalogQuery_KeyDown(object sender,System.Windows.Input.KeyEventArgs e){if(e.Key==Key.Enter){e.Handled=true;await SearchCatalogAsync();}}
    private void CatalogSelectionChanged(object sender,SelectionChangedEventArgs e)=>CatalogUseButton.IsEnabled=!catalogTaking&&CatalogResults.SelectedItem is CatalogCard;
    private async Task SearchCatalogAsync()
    {
        if(catalogProvider==null||catalogTaking)return;
        catalogCancellation?.Cancel();catalogCancellation?.Dispose();catalogCancellation=new();
        var token=catalogCancellation.Token;int generation=++catalogGeneration;
        CatalogResults.ItemsSource=null;CatalogStatus.Text="Suche läuft …";
        try
        {
            var response=await new CatalogSearchService(gameService.Paths.ArchivePath,catalogProvider).SearchAsync(CatalogQuery.Text,token);
            if(generation!=catalogGeneration)return;
            var cards=response.Results.Select(r=>new CatalogCard(r)).ToList();CatalogResults.ItemsSource=cards;
            CatalogStatus.Text=cards.Count==0?"Keine Treffer. Versuche den vollständigen Titel oder weniger Suchwörter.":response.Notice;
            using var limit=new SemaphoreSlim(3);
            foreach(var card in cards)card.Loading=LoadCatalogCover(card,limit,token);
            await Task.WhenAll(cards.Select(c=>c.Loading!));
        }
        catch(OperationCanceledException){}catch(Exception ex){if(generation==catalogGeneration)CatalogStatus.Text=ex.Message;}
    }
    private async Task LoadCatalogCover(CatalogCard card,SemaphoreSlim limit,CancellationToken token)
    {
        try
        {
            await limit.WaitAsync(token);
            try
            {

                string folder=Path.Combine(gameService.Paths.GamesDataPath,"Covers");
                string? cached=Directory.Exists(folder)?Directory.EnumerateFiles(folder,"catalog-"+card.Result.Id+".*").FirstOrDefault(p=>new FileInfo(p).Length>0):null;
                if(cached!=null)card.CoverPath=cached;
                else if(card.Result.CoverUrl.Length>0)card.CoverPath=await f95UpdateService.DownloadImageAsync(card.Result.CoverUrl,folder,"catalog-"+card.Result.Id,token)??"";
                token.ThrowIfCancellationRequested();
                bool known=gameService.FindGameByF95ThreadId(card.Result.Id)!=null;
                card.Status=(known?"Bereits in deiner Bibliothek · ":"")+card.Result.Version+(card.CoverPath.Length==0?" · Kein Cover verfügbar":"");
            }
            finally{limit.Release();}
        }
        catch(OperationCanceledException){}catch(Exception){card.Status="Vorschau nicht verfügbar · Übernahme erneut versuchen";}
    }
    private async void CatalogUse_Click(object sender,RoutedEventArgs e)
    {
        if(CatalogResults.SelectedItem is not CatalogCard card||catalogTaking||fileOperationActive)return;
        catalogTaking=true;CatalogUseButton.IsEnabled=false;CatalogSearchButton.IsEnabled=false;
        int generation=catalogGeneration;var token=catalogCancellation?.Token??CancellationToken.None;
        try
        {
            CatalogStatus.Text="Metadaten werden übernommen …";
            if(card.Loading!=null)await card.Loading;
            if(card.Metadata==null)card.Metadata=await (CatalogMetadataLoader?.Invoke(card.Result.Source,token)??f95UpdateService.FetchMetadataAsync(card.Result.Source,token));
            if(card.CoverPath.Length==0&&card.Metadata.ImageUrl.Length>0)card.CoverPath=await f95UpdateService.DownloadImageAsync(card.Metadata.ImageUrl,Path.Combine(gameService.Paths.GamesDataPath,"Covers"),"catalog-"+card.Result.Id,token)??"";
            token.ThrowIfCancellationRequested();if(generation!=catalogGeneration)return;
            ApplyCatalogToForm(card);
        }
        catch(OperationCanceledException){}catch(Exception ex){if(generation==catalogGeneration)CatalogStatus.Text="Übernahme nicht abgeschlossen: "+ex.Message;}
        finally{if(generation==catalogGeneration){catalogTaking=false;CatalogUseButton.IsEnabled=true;CatalogSearchButton.IsEnabled=true;}}
    }
    private void ApplyCatalogToForm(CatalogCard card)
    {
        var metadata=card.Metadata??throw new IOException("Metadaten fehlen.");
        var existing=gameService.FindGameByF95ThreadId(card.Result.Id);
        if(existing==null){var wishes=gameService.GetGames().Where(g=>!g.IsInstalled&&g.Status==GameStatus.Wishlist&&string.IsNullOrWhiteSpace(g.F95ThreadId)&&SmartAssociation.Key(g.Name)==SmartAssociation.Key(card.Title)).ToList();if(wishes.Count==1)existing=wishes[0];}
        var snapshot=existing==null?null:System.Text.Json.JsonSerializer.Serialize(existing);
        var game=CatalogSearchService.Remember(gameService,card.Result,CatalogQuery.Text.Trim());
        try
        {
            MetadataEnrichment.Apply(game,metadata);game.LatestKnownVersion=metadata.Version;game.F95Status=metadata.Status;
            if(game.CoverPath.Length==0&&card.CoverPath.Length>0)game.CoverPath=card.CoverPath;
            gameService.SaveGames();
        }
        catch
        {
            if(existing==null)gameService.RemoveGame(game);
            else {gameService.RemoveGame(game);gameService.AddGame(System.Text.Json.JsonSerializer.Deserialize<Game>(snapshot!)!);}
            throw;
        }
        // Preserve the form's local paths and notes when returning from its search page.
        if(catalogReturnPage!=AddGameView)ResetForm();
        CancelF95MetadataLoad();
        pendingF95Metadata=metadata;pendingGalleryPaths=new();lastMetadataSource=card.Result.Source;
        GameNameTextBox.Text=string.IsNullOrWhiteSpace(metadata.Name)?card.Title:metadata.Name;
        GameVersionTextBox.Text=metadata.Version;InstallSourceTextBox.Text=card.Result.Source;
        GameCoverPath=card.CoverPath;GameCoverImage.ImagePath=card.CoverPath;GameCoverImage.FocusX=GameCoverImage.FocusY=.5;
        F95MetadataStatusText.Text="Auf der Wunschliste gemerkt · Metadaten übernommen · "+metadata.Developer+" · "+string.Join(", ",metadata.Tags);
        ShowView(AddGameView);HighlightNav(NavAddGameButton);RefreshGameList();
    }
    private readonly Dictionary<string,(long Bytes,DateTime Write,DateTime Seen)> stableCandidates=new(StringComparer.OrdinalIgnoreCase);
    private int AutoAssociateDetected(IEnumerable<ScannedExecutable> found)
    {
        int linked=0;
        foreach(var candidate in found.Where(c=>!c.IsArchive&&c.Confidence>=90))
        {
            var matches=SmartAssociation.Rank(gameService.GetGames(),candidate);
            if(matches.Count==0||!matches[0].CanAutoLink||matches.Count(m=>m.Score==matches[0].Score)!=1)continue;
            try
            {
                var info=new FileInfo(candidate.ExecutablePath);if(!info.Exists)continue;
                if(!stableCandidates.TryGetValue(info.FullName,out var prior)||prior.Bytes!=info.Length||prior.Write!=info.LastWriteTimeUtc)
                {stableCandidates[info.FullName]=(info.Length,info.LastWriteTimeUtc,DateTime.UtcNow);ScheduleAssociationScan();continue;}
                if(DateTime.UtcNow-prior.Seen<TimeSpan.FromSeconds(8)){ScheduleAssociationScan();continue;}
                using(var read=new FileStream(info.FullName,FileMode.Open,FileAccess.Read,FileShare.Read)){}
                if(SmartAssociation.Link(gameService,matches[0],candidate)){linked++;ShowToast("Automatisch zugeordnet: "+matches[0].Game.Name);}
            }
            catch(Exception ex){System.Diagnostics.Debug.WriteLine(ex);}
        }
        if(linked>0)RefreshGameList();return linked;
    }
    private bool associationScanScheduled;
    private async void ScheduleAssociationScan()
    {
        if(associationScanScheduled)return;associationScanScheduled=true;await Task.Delay(8500);associationScanScheduled=false;
        if(!runtimeClosed&&!quietMode&&settingsService.Settings.AutoDiscoveryEnabled)await ScanDetectedGamesAsync();
    }
}
