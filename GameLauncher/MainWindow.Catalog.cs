using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameLauncher.Constants;
using GameLauncher.models;
using GameLauncher.Service;
using Button=System.Windows.Controls.Button;
using TextBox=System.Windows.Controls.TextBox;
using ListBox=System.Windows.Controls.ListBox;
namespace GameLauncher;
public partial class MainWindow
{
    private void WishlistNavigation_Click(object sender,RoutedEventArgs e)
    { libraryFilter=GameListFilter.Wishlist; LibrarySearchTextBox.Clear(); SearchGamesTextBox.Clear(); RefreshGameList(); ShowView(LibraryView); HighlightNav(NavLibraryButton); }
    private void SearchCatalog_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new Window {Owner=this,Title="Spiele nach Namen suchen",Width=850,Height=660,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Background,Foreground=Foreground};
        dialog.Resources=new ResourceDictionary {Source=new Uri("/GameLauncher;component/Theme.xaml",UriKind.Relative)};
        var root=new DockPanel {Margin=new Thickness(22)};var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);
        var query=new TextBox {Text=GameFormTitleTextBlock.IsVisible?GameNameTextBox.Text:SearchGamesTextBox.Text,MaxLength=160};
        var search=new Button {Content="F95-Katalog durchsuchen",Margin=new Thickness(0,10,0,12)};
        var status=new TextBlock {Text="Namen eingeben. Treffer können gemerkt und später mit lokalen Dateien verbunden werden.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};
        top.Children.Add(query);top.Children.Add(search);top.Children.Add(status);root.Children.Add(top);
        var bottom=new StackPanel();DockPanel.SetDock(bottom,Dock.Bottom);
        var detail=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,12)};
        var remember=new Button {Content="Merken & automatisch zuordnen",IsEnabled=false};
        bottom.Children.Add(detail);bottom.Children.Add(remember);root.Children.Add(bottom);
        var rows=new ListBox {DisplayMemberPath="Title"};root.Children.Add(rows);dialog.Content=root;
        var provider=CatalogProviders.Create();using var providerLifetime=provider as IDisposable;using var cancellation=new CancellationTokenSource();
        bool open=true;dialog.Closed+=(_,_)=>{open=false;cancellation.Cancel();};
        var service=new CatalogSearchService(gameService.Paths.ArchivePath,provider);
        if(provider is ICatalogAccountProvider account)
        {
            var accountButton=new Button {Content=account.IsSignedIn?"Abmelden":"Anmelden",Margin=new Thickness(0,0,0,10)};top.Children.Add(accountButton);
            accountButton.Click+=async (_,_)=>
            {
                accountButton.IsEnabled=false;
                try{if(account.IsSignedIn)await account.SignOutAsync(cancellation.Token);else await account.SignInAsync(cancellation.Token);if(open)accountButton.Content=account.IsSignedIn?"Abmelden":"Anmelden";}
                catch(OperationCanceledException){}catch(Exception ex){if(open)status.Text=ex.Message;}
                finally{if(open)accountButton.IsEnabled=true;}
            };
        }
        async Task Search()
        {
            if(!search.IsEnabled)return;search.IsEnabled=false;remember.IsEnabled=false;
            try {status.Text="Suche läuft …";var response=await service.SearchAsync(query.Text,cancellation.Token);if(!open)return;rows.ItemsSource=response.Results;status.Text=response.Notice;}
            catch(OperationCanceledException){}catch(Exception ex){if(open)status.Text=ex.Message;}
            finally{if(open)search.IsEnabled=true;}
        }
        search.Click+=async (_,_)=>await Search();query.KeyDown+=async (_,e)=>{if(e.Key==Key.Enter){e.Handled=true;await Search();}};
        rows.SelectionChanged+=(_,_)=>{remember.IsEnabled=rows.SelectedItem is CatalogResult;detail.Text=rows.SelectedItem is CatalogResult r?"Ersteller: "+r.Creator+"\n"+r.Source:"";};
        remember.Click+=async (_,_)=>
        {
            if(rows.SelectedItem is not CatalogResult result||fileOperationActive||f95UpdateRunning)return;
            remember.IsEnabled=false;search.IsEnabled=false;status.Text="Metadaten werden ergänzt …";
            try
            {
                F95Metadata? metadata=null;string? cover=null;var galleryPaths=new List<string>();string notice="";
                try
                {
                    metadata=await f95UpdateService.FetchMetadataAsync(result.Source,cancellation.Token);
                    if(metadata.ImageUrl.Length>0)cover=await f95UpdateService.DownloadImageAsync(metadata.ImageUrl,Path.Combine(gameService.Paths.GamesDataPath,"Covers"),"catalog-"+result.Id,cancellation.Token);
                    if(settingsService.Settings.AutoDownloadF95Gallery && metadata.PreviewUrls.Count>0) { status.Text="Galeriebilder werden ergänzt …"; galleryPaths=await f95UpdateService.DownloadGalleryAsync(metadata.PreviewUrls,Path.Combine(gameService.Paths.GamesDataPath,"F95Galleries",result.Id),result.Id,cancellation.Token); }
                }
                catch(Exception ex) when(ex is not OperationCanceledException){notice=" Metadaten später ergänzen: "+ex.Message;}
                cancellation.Token.ThrowIfCancellationRequested();
                bool existed=gameService.GetGames().Any(g=>g.F95ThreadId==result.Id||g.InstallSource==result.Source);
                var game=CatalogSearchService.Remember(gameService,result,query.Text.Trim());
                if(metadata!=null){MetadataEnrichment.Apply(game,metadata);game.LatestKnownVersion=metadata.Version;game.F95Status=metadata.Status;}
                if(cover!=null&&!game.UserEditedFields.Contains(nameof(Game.CoverPath)))game.CoverPath=cover;
                if(galleryPaths.Count>0)game.GalleryPaths=game.GalleryPaths.Concat(galleryPaths).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if(!TrySaveGames()){if(!existed)gameService.RemoveGame(game);throw new IOException("Der Treffer konnte nicht gespeichert werden.");}
                RefreshGameList();status.Text="Gemerkt. Eindeutige neue Installationen werden zugeordnet; Dateien werden dabei nicht verschoben."+notice;
            }
            catch(OperationCanceledException){}catch(Exception ex){if(open)status.Text=ex.Message;}
            finally{if(open){remember.IsEnabled=true;search.IsEnabled=true;}}
        };
        dialog.ShowDialog();
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
