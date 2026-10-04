using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GameLauncher.Service;
using Button = System.Windows.Controls.Button;
using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;
namespace GameLauncher.Views;

public sealed class GalleryOverlay : System.Windows.Controls.UserControl
{
    private readonly List<string> paths;
    private readonly Image picture = new() { Stretch = Stretch.Uniform, RenderTransformOrigin = new Point(.5,.5) };
    private readonly Grid stage = new() { ClipToBounds=true, Background=System.Windows.Media.Brushes.Transparent };
    private readonly TextBlock caption = new() { VerticalAlignment=VerticalAlignment.Center, Margin=new Thickness(12,0,20,0) };
    private readonly TextBlock zoomLabel = new() { VerticalAlignment=VerticalAlignment.Center, Margin=new Thickness(12,0,20,0) };
    private readonly ScaleTransform scale = new(1,1);
    private readonly TranslateTransform offset = new();
    private readonly Button previous, next;
    private int index, generation;
    private bool dragging;
    private Point dragStart, dragOffset;
    private double zoom=1;

    public GalleryOverlay(IEnumerable<string> images, Action close, string? initial=null)
    {
        Focusable=true;
        paths=images.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();index=Math.Max(0,paths.IndexOf(initial??""));
        var transforms=new TransformGroup();transforms.Children.Add(scale);transforms.Children.Add(offset);picture.RenderTransform=transforms;
        var root=new DockPanel {Margin=new Thickness(18)};
        var bar=new WrapPanel {Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(bar,Dock.Top);
        bar.Children.Add(caption);bar.Children.Add(zoomLabel);
        Button Action(string text,Action action) {var button=new Button {Content=text,Margin=new Thickness(4)};button.Click+=(_,_)=>action();return button;}
        bar.Children.Add(Action("Einpassen",ResetZoom));bar.Children.Add(Action("Schließen · Esc",close));
        bar.Children.Add(new TextBlock {Text="Mausrad: Zoom · Ziehen: Verschieben",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(18,0,0,0),Opacity=.7});
        stage.Children.Add(picture);
        previous=Action("❮",()=>MoveImage(-1));next=Action("❯",()=>MoveImage(1));
        foreach(var button in new[]{previous,next}) {button.Width=52;button.Height=72;button.FontSize=26;button.VerticalAlignment=VerticalAlignment.Center;button.Background=new SolidColorBrush(System.Windows.Media.Color.FromArgb(220,22,23,32));button.Visibility=paths.Count>1?Visibility.Visible:Visibility.Collapsed;stage.Children.Add(button);}
        previous.HorizontalAlignment=System.Windows.HorizontalAlignment.Left;next.HorizontalAlignment=System.Windows.HorizontalAlignment.Right;
        previous.ToolTip="Vorheriges Bild · ←";next.ToolTip="Nächstes Bild · →";
        root.Children.Add(bar);root.Children.Add(stage);Content=root;
        stage.SizeChanged+=(_,_)=>ClampPan();
        stage.PreviewMouseWheel+=(_,e)=>{ZoomAt(e.GetPosition(stage),Math.Pow(1.2,e.Delta/120d));e.Handled=true;};
        picture.MouseLeftButtonDown+=(_,e)=>{if(zoom<=1)return;BeginPan(e.GetPosition(stage));stage.CaptureMouse();e.Handled=true;};
        stage.MouseMove+=(_,e)=>{if(!dragging)return;if(e.LeftButton!=MouseButtonState.Pressed){EndPan();return;}PanTo(e.GetPosition(stage));e.Handled=true;};
        stage.MouseLeftButtonUp+=(_,e)=>{if(dragging){EndPan();e.Handled=true;}};
        stage.LostMouseCapture+=(_,_)=>{dragging=false;UpdateCursor();};
        IsVisibleChanged+=(_,_)=>{if(!IsVisible)EndPan();};
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){close();e.Handled=true;}else if(e.Key==Key.Right){MoveImage(1);e.Handled=true;}else if(e.Key==Key.Left){MoveImage(-1);e.Handled=true;}};
        Unloaded+=(_,_)=>{EndPan();generation++;picture.Source=null;};Loaded+=async (_,_)=>await LoadCurrent();
        ResetZoom();
    }
    private void ZoomAt(Point anchor,double factor)
    {
        EndPan();double nextZoom=Math.Clamp(zoom*factor,1,6),ratio=nextZoom/zoom;
        double x=anchor.X-stage.ActualWidth/2,y=anchor.Y-stage.ActualHeight/2;
        offset.X=x-(x-offset.X)*ratio;offset.Y=y-(y-offset.Y)*ratio;
        zoom=nextZoom;scale.ScaleX=scale.ScaleY=zoom;ClampPan();UpdateCursor();zoomLabel.Text=$"{zoom*100:0} %";
    }
    private void BeginPan(Point point){if(zoom<=1)return;dragging=true;dragStart=point;dragOffset=new Point(offset.X,offset.Y);UpdateCursor();}
    private void PanTo(Point point){if(!dragging)return;offset.X=dragOffset.X+point.X-dragStart.X;offset.Y=dragOffset.Y+point.Y-dragStart.Y;ClampPan();}
    private void EndPan(){dragging=false;if(stage.IsMouseCaptured)stage.ReleaseMouseCapture();UpdateCursor();}
    private void UpdateCursor()=>picture.Cursor=dragging?System.Windows.Input.Cursors.SizeAll:zoom>1?System.Windows.Input.Cursors.Hand:System.Windows.Input.Cursors.Arrow;
    private void ClampPan()
    {
        double width=stage.ActualWidth,height=stage.ActualHeight;
        if(picture.Source is {Width:>0,Height:>0} source){double fit=Math.Min(width/source.Width,height/source.Height);width=source.Width*fit;height=source.Height*fit;}
        double maxX=Math.Max(0,(width*zoom-stage.ActualWidth)/2),maxY=Math.Max(0,(height*zoom-stage.ActualHeight)/2);
        offset.X=Math.Clamp(offset.X,-maxX,maxX);offset.Y=Math.Clamp(offset.Y,-maxY,maxY);
    }
    private void ResetZoom(){EndPan();zoom=1;scale.ScaleX=scale.ScaleY=1;offset.X=offset.Y=0;zoomLabel.Text="100 %";UpdateCursor();}
    public async void MoveImage(int delta){if(paths.Count==0)return;index=(index+delta+paths.Count)%paths.Count;ResetZoom();await LoadCurrent();}
    private async Task LoadCurrent()
    {
        if(paths.Count==0){caption.Text="Keine erreichbaren Bilder";return;}
        int request=++generation;caption.Text=$"{index+1} / {paths.Count}";picture.Source=null;
        var bitmap=await ImageCache.Load(paths[index],2560);if(request!=generation)return;
        picture.Source=bitmap;if(bitmap==null)caption.Text+=" · Bild nicht lesbar";ClampPan();
    }
}
