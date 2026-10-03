using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using GameLauncher.Service;

namespace GameLauncher.Views;

public partial class DownloadsView : System.Windows.Controls.UserControl
{
    private DownloadManager? manager;
    private string root = "";
    private Func<string, Task>? import;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    public DownloadsView()
    {
        InitializeComponent();
        timer.Tick += (_, _) => { if (manager?.IsActive == true) Rows.Items.Refresh(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) { Refresh(); timer.Start(); } else timer.Stop(); };
        Unloaded += (_, _) => timer.Stop();
    }
    public void Configure(DownloadManager downloads, string directory, Func<string, Task> importArchive)
    { manager = downloads; root = directory; import = importArchive; }
    public void Refresh() { if (manager != null) { Rows.ItemsSource = manager.Jobs; Rows.Items.Refresh(); } }
    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (manager == null) return;
        try
        {
            var job = manager.Add(SourceUrl.Text.Trim(), TargetName.Text.Trim(), ExpectedHash.Text.Trim());
            SourceUrl.Clear(); TargetName.Clear(); ExpectedHash.Clear(); Refresh();
            await manager.RunAsync(job); Refresh();
        }
        catch (Exception ex) { Feedback.Text = ex.Message; }
    }
    private async void Resume_Click(object sender, RoutedEventArgs e)
    {
        if (manager == null || sender is not FrameworkElement { DataContext: DownloadJob job }) return;
        try { await manager.RunAsync(job); } catch (Exception ex) { Feedback.Text = ex.Message; } Refresh();
    }
    private void Pause_Click(object sender, RoutedEventArgs e) => Pause(sender, false);
    private void Cancel_Click(object sender, RoutedEventArgs e) => Pause(sender, true);
    private void Pause(object sender, bool cancel)
    {
        try { if (sender is FrameworkElement { DataContext: DownloadJob job }) manager?.Pause(job, cancel); Refresh(); }
        catch (Exception ex) { Feedback.Text = ex.Message; }
    }
    private void PauseAll_Click(object sender, RoutedEventArgs e)
    { try { manager?.PauseAll(); Refresh(); } catch (Exception ex) { Feedback.Text = ex.Message; } }
    private void Folder_Click(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(root)) return;
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true }; start.ArgumentList.Add(root); Process.Start(start);
    }
    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (manager == null || import == null || sender is not FrameworkElement { DataContext: DownloadJob job }) return;
        if (job.Status != "Abgeschlossen" || !ArchiveExtractionService.IsSupported(job.FileName))
        { Feedback.Text = "Zuerst einen ZIP-, RAR- oder 7z-Download abschließen."; return; }
        try { await import(manager.FinalPath(job)); } catch (Exception ex) { Feedback.Text = ex.Message; }
    }
}
