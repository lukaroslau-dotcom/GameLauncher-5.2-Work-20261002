using System.IO;
using System.Windows;
using GameLauncher.Service;

namespace GameLauncher;

public partial class MainWindow
{
    private DownloadManager? downloads;
    private void InitializeDownloads()
    {
        downloads?.Dispose();
        string root = Path.Combine(gameService.Paths.ArchivePath, "Downloads");
        downloads = new DownloadManager(root);
        DownloadPage.Configure(downloads, root, async path =>
        {
            OpenAddGameButton_Click(this, new RoutedEventArgs());
            await PrepareArchiveForAddAsync(path);
        });

    }
    private void DownloadsNavigation_Click(object sender, RoutedEventArgs e)
    { DownloadPage.Refresh(); ShowView(DownloadsPageHost); }
}
