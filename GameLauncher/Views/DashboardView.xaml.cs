using System.Windows;
using GameLauncher.ViewModels;
namespace GameLauncher.Views;
public partial class DashboardView : System.Windows.Controls.UserControl
{
    public DashboardViewModel Model { get; } = new();
    public event RoutedEventHandler? PlayRequested, SettingsRequested, LibraryRequested, DiscoverRequested, QuietChanged;
    public DashboardView() { InitializeComponent(); }
    private void Play_Click(object sender, RoutedEventArgs e) => PlayRequested?.Invoke(this,e);
    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this,e);
    private void Library_Click(object sender, RoutedEventArgs e) => LibraryRequested?.Invoke(this,e);
    private void Discover_Click(object sender, RoutedEventArgs e) => DiscoverRequested?.Invoke(this,e);
    private void Quiet_Click(object sender, RoutedEventArgs e) => QuietChanged?.Invoke(this,e);
}
