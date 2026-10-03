using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameLauncher.Service;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
namespace GameLauncher;
public partial class MainWindow
{
    private void GalleryImage_Click(object sender, MouseButtonEventArgs e)
    { if (sender is FrameworkElement { DataContext: string path }) OpenGallery(path); }
    private void OpenGallery_Click(object sender, RoutedEventArgs e) => OpenGallery(selectedGame?.CoverPath);
    private void OpenGallery(string? initial)
    {
        if(selectedGame==null)return;
        var paths=new[]{selectedGame.CoverPath}.Concat(selectedGame.GalleryPaths).Where(File.Exists).ToList();
        if(paths.Count==0){ShowToast("Keine erreichbaren Bilder.");return;}
        new Views.GalleryWindow(this,paths,initial).Show();
    }
    private void OpenDiagnosticsWindow()
    {
        var dialog=new Window { Owner=this,Title="Diagnoseprotokolle", Width=940,Height=650,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Background,Foreground=Foreground };
        dialog.Resources=new ResourceDictionary {Source=new Uri("/GameLauncher;component/Theme.xaml",UriKind.Relative)};
        var root=new DockPanel { Margin=new Thickness(20) }; var close=new Button { Content="Schließen",HorizontalAlignment=System.Windows.HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0) }; DockPanel.SetDock(close,Dock.Bottom); close.Click+=(_,_)=>dialog.Close(); root.Children.Add(close);
        var text=new TextBox {IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}; root.Children.Add(text);
        try
        {
            string folder=Path.Combine(AppPaths.Default().ArchivePath,"Logs");
            var files=Directory.Exists(folder)?Directory.EnumerateFiles(folder,"*.jsonl").OrderByDescending(File.GetLastWriteTimeUtc).Take(3).ToList():new();
            text.Text=files.Count==0?"Noch keine Diagnoseereignisse vorhanden.":string.Join("\n\n",files.Select(file=>Path.GetFileName(file)+"\n"+string.Join("\n",File.ReadLines(file).TakeLast(150))));
        }
        catch(Exception ex){text.Text="Protokolle nicht lesbar: "+ex.Message;}
        dialog.Content=root; dialog.Show();
    }
}
