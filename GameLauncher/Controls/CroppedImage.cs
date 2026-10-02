using MouseEventArgs=System.Windows.Input.MouseEventArgs;
using Cursors=System.Windows.Input.Cursors;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameLauncher.Service;
using Point = System.Windows.Point;
using System.IO;
using System.Windows.Threading;
namespace GameLauncher.Controls;
public sealed class CroppedImage : FrameworkElement
{
    public static readonly DependencyProperty ImagePathProperty=DependencyProperty.Register(nameof(ImagePath),typeof(string),typeof(CroppedImage),new PropertyMetadata("",PathChanged));
    public static readonly DependencyProperty SourceProperty=DependencyProperty.Register(nameof(Source),typeof(ImageSource),typeof(CroppedImage),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FocusXProperty=DependencyProperty.Register(nameof(FocusX),typeof(double),typeof(CroppedImage),new FrameworkPropertyMetadata(.5,FrameworkPropertyMetadataOptions.AffectsRender|FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,null,Clamp));
    public static readonly DependencyProperty FocusYProperty=DependencyProperty.Register(nameof(FocusY),typeof(double),typeof(CroppedImage),new FrameworkPropertyMetadata(.5,FrameworkPropertyMetadataOptions.AffectsRender|FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,null,Clamp));
    public static readonly DependencyProperty FitInsideProperty=DependencyProperty.Register(nameof(FitInside),typeof(bool),typeof(CroppedImage),new FrameworkPropertyMetadata(false,FrameworkPropertyMetadataOptions.AffectsRender));
    public string ImagePath {get=>(string)GetValue(ImagePathProperty);set=>SetValue(ImagePathProperty,value);}
    public ImageSource? Source {get=>(ImageSource?)GetValue(SourceProperty);set=>SetValue(SourceProperty,value);}
    public double FocusX {get=>(double)GetValue(FocusXProperty);set=>SetValue(FocusXProperty,value);}
    public double FocusY {get=>(double)GetValue(FocusYProperty);set=>SetValue(FocusYProperty,value);}
    public bool FitInside {get=>(bool)GetValue(FitInsideProperty);set=>SetValue(FitInsideProperty,value);}
    public bool CanDrag {get;set;}
    public bool AnimateGif {get;set;}=true;
    public int DecodeWidth {get;set;}=640;
    public event EventHandler? CropChanged;
    private int revision;
    private int loadingRevision=-1;
    private Point? last;
    private bool cropChanged;
    private DispatcherTimer? animationTimer;
    private AnimatedImage? animation;
    private int animationIndex;
    public CroppedImage()
    {
        Loaded+=(_,_)=>{ResumeAnimation();BeginLoad(ImagePath);};
        Unloaded+=(_,_)=>PauseAnimation();
        IsVisibleChanged+=(_,_)=>{if(IsVisible){ResumeAnimation();BeginLoad(ImagePath);}else PauseAnimation();};
    }
    private static object Clamp(DependencyObject d,object v)=>v is double n&&double.IsFinite(n)?Math.Clamp(n,0,1):.5;
    private static void PathChanged(DependencyObject d,DependencyPropertyChangedEventArgs e)
    {
        var image=(CroppedImage)d;++image.revision;image.loadingRevision=-1;image.StopAnimation();image.Source=null;image.BeginLoad(e.NewValue as string??"");
    }
    private async void BeginLoad(string path)
    {
        if(string.IsNullOrWhiteSpace(path)||!IsLoaded||!IsVisible||loadingRevision==revision)return;
        int request=revision;loadingRevision=request;
        if(AnimateGif&&ImageCache.IsGif(path))
        {
            var animated=await ImageCache.LoadGif(path,DecodeWidth);
            if(request!=revision)return;
            if(animated!=null){StartAnimation(animated);return;}
        }
        var result=await ImageCache.Load(path,DecodeWidth);
        if(request==revision)Source=result;
    }
    private void StartAnimation(AnimatedImage frames)
    {
        animation=frames;animationIndex=0;Source=frames.Frames[0];ResumeAnimation();
    }
    private void ResumeAnimation()
    {
        if(animation==null||!IsLoaded||!IsVisible||animationTimer!=null)return;
        // ContextIdle can starve forever while a virtualized library is rendering or scrolling.
        // Render priority keeps the GIF clock moving without busy-waiting.
        animationTimer=new DispatcherTimer(DispatcherPriority.Render,Dispatcher){Interval=animation.Delays[animationIndex]};
        animationTimer.Tick+=AnimationTick;animationTimer.Start();
    }
    private void AnimationTick(object? sender,EventArgs e)
    {
        if(animation==null){PauseAnimation();return;}
        animationIndex=(animationIndex+1)%animation.Frames.Count;
        Source=animation.Frames[animationIndex];
        InvalidateVisual();
        if(animationTimer!=null)animationTimer.Interval=animation.Delays[animationIndex];
    }
    private void PauseAnimation(){if(animationTimer==null)return;animationTimer.Stop();animationTimer.Tick-=AnimationTick;animationTimer=null;}
    private void StopAnimation(){PauseAnimation();animation=null;animationIndex=0;}
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(System.Windows.Media.Brushes.Transparent,null,new Rect(RenderSize));
        if(Source is not BitmapSource bitmap || ActualWidth<=0 || ActualHeight<=0) return;
        double scale=FitInside?Math.Min(ActualWidth/bitmap.PixelWidth,ActualHeight/bitmap.PixelHeight):Math.Max(ActualWidth/bitmap.PixelWidth,ActualHeight/bitmap.PixelHeight);
        double width=bitmap.PixelWidth*scale,height=bitmap.PixelHeight*scale;
        dc.PushClip(new RectangleGeometry(new Rect(RenderSize),12,12));
        dc.DrawImage(bitmap,new Rect(-(width-ActualWidth)*FocusX,-(height-ActualHeight)*FocusY,width,height));dc.Pop();
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if(!CanDrag||Source==null)return;last=e.GetPosition(this);cropChanged=false;CaptureMouse();Cursor=Cursors.SizeAll;e.Handled=true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if(last is not Point before || Source is not BitmapSource bitmap)return;
        var now=e.GetPosition(this);double scale=Math.Max(ActualWidth/bitmap.PixelWidth,ActualHeight/bitmap.PixelHeight);
        double overflowX=bitmap.PixelWidth*scale-ActualWidth,overflowY=bitmap.PixelHeight*scale-ActualHeight;
        if(overflowX>0){SetCurrentValue(FocusXProperty,FocusX-(now.X-before.X)/overflowX);cropChanged=true;}
        if(overflowY>0){SetCurrentValue(FocusYProperty,FocusY-(now.Y-before.Y)/overflowY);cropChanged=true;}
        last=now;e.Handled=true;
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e){if(last!=null){ReleaseMouseCapture();last=null;Cursor=Cursors.Arrow;if(cropChanged)CropChanged?.Invoke(this,EventArgs.Empty);cropChanged=false;e.Handled=true;}}
    protected override void OnLostMouseCapture(MouseEventArgs e){last=null;Cursor=Cursors.Arrow;}
}
