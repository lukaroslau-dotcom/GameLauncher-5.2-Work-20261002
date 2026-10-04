using System.Windows;
using System.Windows.Controls;
using GameLauncher.Service;
using Button=System.Windows.Controls.Button;
namespace GameLauncher;
public partial class MainWindow
{
    private TaskCompletionSource<Association?>? associationPrompt;
    private bool associationAborted;
    private async Task<Association?> ChooseAssociationAsync(IReadOnlyList<Association> matches)
    {
        var completion=new TaskCompletionSource<Association?>(TaskCreationOptions.RunContinuationsAsynchronously);
        associationPrompt=completion;associationAborted=false;
        var host=new Border {Background=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(248,12,13,20)),Padding=new Thickness(30)};
        System.Windows.Controls.Panel.SetZIndex(host,950);System.Windows.Input.KeyboardNavigation.SetTabNavigation(host,System.Windows.Input.KeyboardNavigationMode.Cycle);
        var panel=new StackPanel {MaxWidth=760,VerticalAlignment=VerticalAlignment.Center};host.Child=new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        panel.Children.Add(new TextBlock {Text="Gehört dieses Spiel zu deiner Bibliothek?",FontSize=25,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,20)});
        foreach(var match in matches.Take(5))
        {
            var button=new Button {Height=double.NaN,MinHeight=86,Content=new TextBlock {Text=match.Game.Name+"\n"+match.Reason+"\n"+(match.Game.IsInstalled?"Vorhandene Installation – Versionswechsel prüfen":"Wunschliste verbinden · Metadaten behalten"),TextWrapping=TextWrapping.Wrap},HorizontalContentAlignment=System.Windows.HorizontalAlignment.Left,Margin=new Thickness(0,6,0,6),Padding=new Thickness(16)};
            button.Click+=(_,_)=>completion.TrySetResult(match);panel.Children.Add(button);
        }
        var skip=new Button {Content="Keiner davon · separat hinzufügen",Margin=new Thickness(0,18,0,0)};skip.Click+=(_,_)=>completion.TrySetResult(null);panel.Children.Add(skip);
        var cancel=new Button {Content="Abbrechen",Margin=new Thickness(0,8,0,0)};cancel.Click+=(_,_)=>{associationAborted=true;completion.TrySetResult(null);};panel.Children.Add(cancel);
        void OnClosed(object? sender,EventArgs e){associationAborted=true;completion.TrySetResult(null);}
        Closed+=OnClosed;ShellGrid.Children.Add(host);ContentShell.IsEnabled=false;skip.Focus();
        try{return await completion.Task;}finally{associationPrompt=null;Closed-=OnClosed;ShellGrid.Children.Remove(host);ContentShell.IsEnabled=true;}
    }
}
