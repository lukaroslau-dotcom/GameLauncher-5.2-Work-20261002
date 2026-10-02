using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GameLauncher.models;
using UserControl = System.Windows.Controls.UserControl;
using Brushes = System.Windows.Media.Brushes;
using System.IO;
using System.Windows.Threading;
namespace GameLauncher.Controls;
public partial class GameCard : UserControl
{
    public static int HoverPreviewDelaySeconds {get;set;}=5;
    private readonly DispatcherTimer previewTimer=new();
    private int previewIndex=-1;
    public static readonly RoutedEvent OpenRequestedEvent = EventManager.RegisterRoutedEvent(nameof(OpenRequested),RoutingStrategy.Bubble,typeof(RoutedEventHandler),typeof(GameCard));
    public static readonly RoutedEvent LaunchRequestedEvent = EventManager.RegisterRoutedEvent(nameof(LaunchRequested),RoutingStrategy.Bubble,typeof(RoutedEventHandler),typeof(GameCard));
    public static readonly RoutedEvent FavoriteRequestedEvent = EventManager.RegisterRoutedEvent(nameof(FavoriteRequested),RoutingStrategy.Bubble,typeof(RoutedEventHandler),typeof(GameCard));
    public static readonly RoutedEvent OpenFolderRequestedEvent = EventManager.RegisterRoutedEvent(nameof(OpenFolderRequested),RoutingStrategy.Bubble,typeof(RoutedEventHandler),typeof(GameCard));
    public static readonly RoutedEvent OpenF95RequestedEvent = EventManager.RegisterRoutedEvent(nameof(OpenF95Requested),RoutingStrategy.Bubble,typeof(RoutedEventHandler),typeof(GameCard));
    public static readonly RoutedEvent EditRequestedEvent = EventManager.RegisterRoutedEvent(nameof(EditRequested),RoutingStrategy.Bubble,typeof(RoutedEventHandler),typeof(GameCard));
    public static readonly RoutedEvent DeleteRequestedEvent = EventManager.RegisterRoutedEvent(nameof(DeleteRequested),RoutingStrategy.Bubble,typeof(RoutedEventHandler),typeof(GameCard));
    public static readonly RoutedEvent StatusChangeRequestedEvent = EventManager.RegisterRoutedEvent(nameof(StatusChangeRequested),RoutingStrategy.Bubble,typeof(RoutedEventHandler),typeof(GameCard));
    public event RoutedEventHandler OpenRequested {add=>AddHandler(OpenRequestedEvent,value);remove=>RemoveHandler(OpenRequestedEvent,value);}
    public event RoutedEventHandler LaunchRequested {add=>AddHandler(LaunchRequestedEvent,value);remove=>RemoveHandler(LaunchRequestedEvent,value);}
    public event RoutedEventHandler FavoriteRequested {add=>AddHandler(FavoriteRequestedEvent,value);remove=>RemoveHandler(FavoriteRequestedEvent,value);}
    public event RoutedEventHandler OpenFolderRequested {add=>AddHandler(OpenFolderRequestedEvent,value);remove=>RemoveHandler(OpenFolderRequestedEvent,value);}
    public event RoutedEventHandler OpenF95Requested {add=>AddHandler(OpenF95RequestedEvent,value);remove=>RemoveHandler(OpenF95RequestedEvent,value);}
    public event RoutedEventHandler EditRequested {add=>AddHandler(EditRequestedEvent,value);remove=>RemoveHandler(EditRequestedEvent,value);}
    public event RoutedEventHandler DeleteRequested {add=>AddHandler(DeleteRequestedEvent,value);remove=>RemoveHandler(DeleteRequestedEvent,value);}
    public event RoutedEventHandler StatusChangeRequested {add=>AddHandler(StatusChangeRequestedEvent,value);remove=>RemoveHandler(StatusChangeRequestedEvent,value);}
    public GameCard()
    {
        InitializeComponent();previewTimer.Tick+=PreviewTick;
        DataContextChanged += (_,_) => { StopPreview(); FavoriteButton.Content = DataContext is Game {IsFavorite:true} ? "★" : "☆"; };
        Loaded += (_, _) => ObserveSize();
        DataContextChanged += (_, _) => { if (IsLoaded) ObserveSize(); };
        Unloaded += (_, _) => { StopPreview(); StopSize(); };
    }
    private Game? sizeGame;
    private CancellationTokenSource? sizeCancellation;
    private void StopSize()
    {
        sizeCancellation?.Cancel();
        sizeCancellation = null;
        if (sizeGame != null) sizeGame.PropertyChanged -= SizePropertyChanged;
        sizeGame = null;
    }
    private void ObserveSize()
    {
        StopSize();
        sizeGame = DataContext as Game;
        if (sizeGame != null) sizeGame.PropertyChanged += SizePropertyChanged;
        RefreshSize();
    }
    private void SizePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Game.GamePath) or nameof(Game.IsInstalled)) RefreshSize();
    }
    private async void RefreshSize()
    {
        sizeCancellation?.Cancel();
        sizeCancellation = null;
        InstalledSizeText.Visibility = sizeGame?.IsInstalled == true ? Visibility.Visible : Visibility.Collapsed;
        InstalledSizeText.Text = "Größe wird ermittelt…";
        if (sizeGame?.IsInstalled != true) return;
        using var cancellation = new CancellationTokenSource();
        sizeCancellation = cancellation;
        string path = sizeGame.GamePath;
        try
        {
            long? bytes = await GameLauncher.Service.GameSizeService.CalculateAsync(path, cancellation.Token);
            if (!cancellation.IsCancellationRequested) InstalledSizeText.Text = GameLauncher.Service.GameSizeService.Format(bytes);
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(sizeCancellation, cancellation)) sizeCancellation = null; }
    }
    private void Request(RoutedEvent routed,RoutedEventArgs original) { original.Handled=true; RaiseEvent(new RoutedEventArgs(routed,this)); }
    private void Open_Click(object sender, MouseButtonEventArgs e) { if (!e.Handled) Request(OpenRequestedEvent,e); }
    private void Open_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) { if(e.Key==Key.Enter && ReferenceEquals(e.OriginalSource,Frame)) Request(OpenRequestedEvent,e); }
    private void Details_Click(object sender,RoutedEventArgs e)=>Request(OpenRequestedEvent,e);
    private void Launch_Click(object sender,RoutedEventArgs e)=>Request(LaunchRequestedEvent,e);
    private void Favorite_Click(object sender,RoutedEventArgs e)=>Request(FavoriteRequestedEvent,e);
    private void Frame_Enter(object sender,RoutedEventArgs e)
    {
        ArtOverlay.Visibility = Visibility.Visible;
        HoverPlayButton.Visibility = Visibility.Visible;
        if(DataContext is Game {GalleryPaths.Count:>0}){previewTimer.Stop();previewIndex=-1;previewTimer.Interval=TimeSpan.FromSeconds(Math.Clamp(HoverPreviewDelaySeconds,1,30));previewTimer.Start();}
    }
    private void Frame_Leave(object sender,RoutedEventArgs e)
    {
        if(!Frame.IsKeyboardFocusWithin&&!Frame.IsMouseOver){ArtOverlay.Visibility=Visibility.Collapsed;HoverPlayButton.Visibility=Visibility.Collapsed;}
        if(!Frame.IsKeyboardFocusWithin&&!Frame.IsMouseOver)StopPreview();
    }
    private void PreviewTick(object? sender,EventArgs e)
    {
        if(DataContext is not Game game||!Frame.IsVisible||(!Frame.IsMouseOver&&!Frame.IsKeyboardFocusWithin)){StopPreview();return;}
        var gallery=game.GalleryPaths.Where(File.Exists).ToList();if(gallery.Count==0){StopPreview();return;}
        previewIndex=(previewIndex+1)%gallery.Count;CardImage.ImagePath=gallery[previewIndex];previewTimer.Interval=TimeSpan.FromSeconds(2.5);
    }
    private void StopPreview(){previewTimer.Stop();previewIndex=-1;if(DataContext is Game game&&CardImage!=null)CardImage.ImagePath=game.CoverPath;}
    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (DataContext is not Game game) { GameContextMenu.IsEnabled = false; return; }
        GameContextMenu.IsEnabled = true;
        bool isWishlist = !game.IsInstalled && string.IsNullOrWhiteSpace(game.ExecutablePath);
        CtxPlay.Visibility = isWishlist ? Visibility.Collapsed : Visibility.Visible;
        CtxInstall.Visibility = isWishlist ? Visibility.Visible : Visibility.Collapsed;
        CtxOpenFolder.Visibility = game.IsInstalled ? Visibility.Visible : Visibility.Collapsed;
        CtxPlay.Header = game.IsInstalled ? "▶  Spielen" : "Erneut einbinden";
        CtxFavorite.Header = game.IsFavorite ? "☆  Favorit entfernen" : "★  Als Favorit markieren";
        CtxOpenF95.IsEnabled = !string.IsNullOrWhiteSpace(game.F95ThreadId);
        CtxStatusInstalled.IsChecked = game.Status == GameLauncher.Constants.GameStatus.Installed;
        CtxStatusWaiting.IsChecked = game.Status == GameLauncher.Constants.GameStatus.WaitingForUpdates;
        CtxStatusFinished.IsChecked = game.Status == GameLauncher.Constants.GameStatus.Finished;
    }
    private void CtxPlay_Click(object sender, RoutedEventArgs e) => RaiseEvent(new RoutedEventArgs(LaunchRequestedEvent, this));
    private void CtxInstall_Click(object sender, RoutedEventArgs e) => RaiseEvent(new RoutedEventArgs(OpenRequestedEvent, this));
    private void CtxOpenFolder_Click(object sender, RoutedEventArgs e) => RaiseEvent(new RoutedEventArgs(OpenFolderRequestedEvent, this));
    private void CtxOpenF95_Click(object sender, RoutedEventArgs e) => RaiseEvent(new RoutedEventArgs(OpenF95RequestedEvent, this));
    private void CtxEdit_Click(object sender, RoutedEventArgs e) => RaiseEvent(new RoutedEventArgs(EditRequestedEvent, this));
    private void CtxDelete_Click(object sender, RoutedEventArgs e) => RaiseEvent(new RoutedEventArgs(DeleteRequestedEvent, this));
    private void CtxFavorite_Click(object sender, RoutedEventArgs e) => RaiseEvent(new RoutedEventArgs(FavoriteRequestedEvent, this));
    private void CtxStatus_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string newStatus })
            RaiseEvent(new StatusChangeRoutedEventArgs(StatusChangeRequestedEvent, this, newStatus));
    }
}
public class StatusChangeRoutedEventArgs : RoutedEventArgs
{
    public string NewStatus { get; }
    public StatusChangeRoutedEventArgs(RoutedEvent routedEvent, object source, string newStatus) : base(routedEvent, source) { NewStatus = newStatus; }
}
