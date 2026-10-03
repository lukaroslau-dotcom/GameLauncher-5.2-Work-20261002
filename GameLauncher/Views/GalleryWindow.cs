using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GameLauncher.Service;
using Button = System.Windows.Controls.Button;
using Image = System.Windows.Controls.Image;
namespace GameLauncher.Views;

public sealed class GalleryWindow : Window
{
    private readonly List<string> paths;
    private readonly Image picture = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock caption = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16,0,16,0) };
    private readonly Slider zoom = new() { Minimum = 1, Maximum = 4, Value = 1, Width = 150, Margin = new Thickness(14,0,14,0) };
    private readonly ScrollViewer scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private int index, generation;
    public GalleryWindow(Window owner, IEnumerable<string> images, string? initial = null)
    {
        Owner=owner; Title="Bildergalerie"; Width=Math.Min(1200,SystemParameters.WorkArea.Width); Height=Math.Min(820,SystemParameters.WorkArea.Height);
        WindowStartupLocation=WindowStartupLocation.CenterOwner; Background=owner.Background; Foreground=owner.Foreground;
        Resources=new ResourceDictionary { Source=new Uri("/GameLauncher;component/Theme.xaml",UriKind.Relative) };
        paths=images.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); index=Math.Max(0,paths.IndexOf(initial??""));
        var root=new DockPanel { Margin=new Thickness(18) }; var bar=new StackPanel { Orientation=System.Windows.Controls.Orientation.Horizontal, Margin=new Thickness(0,0,0,12) }; DockPanel.SetDock(bar,Dock.Top);
        void Action(string text, Action action) { var b=new Button { Content=text, Margin=new Thickness(4) }; b.Click+=(_,_)=>action(); bar.Children.Add(b); }
        Action("←",()=>Move(-1)); bar.Children.Add(caption); Action("→",()=>Move(1)); bar.Children.Add(zoom);
        Action("Einpassen",()=>zoom.Value=1); Action("Vollbild",()=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized); Action("Schließen",Close);
        scroll.Content=picture; root.Children.Add(bar); root.Children.Add(scroll); Content=root;
        zoom.ValueChanged+=(_,_)=>ResizeImage(); scroll.SizeChanged+=(_,_)=>ResizeImage();
        PreviewKeyDown+=(_,e)=>{ if(e.Key==Key.Escape) {Close();e.Handled=true;} else if(e.Key==Key.Right) {Move(1);e.Handled=true;} else if(e.Key==Key.Left) {Move(-1);e.Handled=true;} };
        scroll.PreviewMouseWheel+=(_,e)=>{ if(Keyboard.Modifiers==ModifierKeys.Control){zoom.Value=Math.Clamp(zoom.Value+Math.Sign(e.Delta)*.2,1,4);e.Handled=true;} };
        Closed+=(_,_)=>{ generation++; picture.Source=null; }; Loaded+=async (_,_)=>await LoadCurrent();
    }
    private void ResizeImage() { picture.Width=Math.Max(100,scroll.ViewportWidth)*zoom.Value; picture.Height=Math.Max(100,scroll.ViewportHeight)*zoom.Value; }
    private async void Move(int delta) { if(paths.Count==0)return; index=(index+delta+paths.Count)%paths.Count; zoom.Value=1; await LoadCurrent(); }
    private async Task LoadCurrent()
    {
        if(paths.Count==0){caption.Text="Keine erreichbaren Bilder";return;}
        int request=++generation; caption.Text=$"{index+1} / {paths.Count}"; picture.Source=null;
        var bitmap=await ImageCache.Load(paths[index],2560); if(request!=generation)return;
        picture.Source=bitmap; if(bitmap==null)caption.Text+=" · Bild nicht lesbar"; ResizeImage();
    }
}
