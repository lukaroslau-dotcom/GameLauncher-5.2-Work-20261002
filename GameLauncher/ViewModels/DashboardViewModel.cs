using System.ComponentModel;
using System.Runtime.CompilerServices;
namespace GameLauncher.ViewModels;
public sealed class DashboardViewModel : INotifyPropertyChanged
{
    private string summary = "Deine Sammlung beginnt hier.", totalTime = "0 Std.", favorites = "0", waiting = "0";
    public string Summary { get => summary; set { summary = value; Changed(); } }
    public string TotalTime { get => totalTime; set { totalTime = value; Changed(); } }
    public string Favorites { get => favorites; set { favorites = value; Changed(); } }
    public string Waiting { get => waiting; set { waiting = value; Changed(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(name));
}
