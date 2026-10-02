using System.IO;
using System.Windows;
using System.Windows.Controls;
using GameLauncher.Service;
using Microsoft.Win32;

namespace GameLauncher.Views
{
    public partial class SetupWizardView : System.Windows.Controls.UserControl
    {
        private string selectedFolder = "";
        private bool scanning;
        private List<SetupScanResult> scanResults = new();
        private CancellationTokenSource? scanCts;

        public event Action<List<SetupScanResult>>? SetupCompleted;
        public event Action? SetupSkipped;

        public SetupWizardView()
        {
            InitializeComponent();
        }

        public void Reset()
        {
            Step1_Welcome.Visibility = Visibility.Visible;
            Step2_FolderScan.Visibility = Visibility.Collapsed;
            Step3_Confirmation.Visibility = Visibility.Collapsed;
            SetupBackButton.Visibility = Visibility.Collapsed;
            SetupSkipButton.Visibility = Visibility.Visible;
            SetupImportButton.Visibility = Visibility.Collapsed;
            selectedFolder = "";
            SetupFolderPathTextBox.Text = "";
            SetupFolderPathError.Visibility = Visibility.Collapsed;
            SetupScanButton.IsEnabled = false;
            SetupScanStatus.Text = "";
            SetupResultsPanel.Visibility = Visibility.Collapsed;
            scanResults.Clear();
        }

        private void Step1_Next_Click(object sender, RoutedEventArgs e)
        {
            Step1_Welcome.Visibility = Visibility.Collapsed;
            Step2_FolderScan.Visibility = Visibility.Visible;
            SetupBackButton.Visibility = Visibility.Visible;
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (Step2_FolderScan.Visibility == Visibility.Visible)
            {
                Step2_FolderScan.Visibility = Visibility.Collapsed;
                Step1_Welcome.Visibility = Visibility.Visible;
                SetupBackButton.Visibility = Visibility.Collapsed;
            }
            else if (Step3_Confirmation.Visibility == Visibility.Visible)
            {
                Step3_Confirmation.Visibility = Visibility.Collapsed;
                Step2_FolderScan.Visibility = Visibility.Visible;
                SetupImportButton.Visibility = Visibility.Collapsed;
                SetupSkipButton.Visibility = Visibility.Visible;
            }
        }

        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();
            if (dialog.ShowDialog() == true)
            {
                selectedFolder = dialog.FolderName;
                SetupFolderPathTextBox.Text = selectedFolder;
                SetupFolderPathError.Visibility = Visibility.Collapsed;
                SetupScanButton.IsEnabled = true;
            }
        }

        private async void ScanFolder_Click(object sender, RoutedEventArgs e)
        {
            if (scanning || string.IsNullOrWhiteSpace(selectedFolder) || !Directory.Exists(selectedFolder))
                return;

            scanning = true;
            SetupScanButton.IsEnabled = false;
            SetupScanStatus.Text = "Suche nach Spielen …";
            SetupResultsPanel.Visibility = Visibility.Collapsed;
            scanResults.Clear();

            scanCts?.Cancel();
            scanCts = new CancellationTokenSource();
            var token = scanCts.Token;

            try
            {
                var found = await Task.Run(() => DiscoveryService.ScanFolder(selectedFolder, token), token);
                if (token.IsCancellationRequested) return;

                scanResults = found
                    .Where(g => !g.IsArchive)
                    .Select(g => new SetupScanResult
                    {
                        Name = g.Name,
                        DetectionLabel = g.DetectionLabel,
                        DisplayPath = g.GamePath,
                        ExecutablePath = g.ExecutablePath,
                        Version = g.Version,
                        SavePath = g.SavePath,
                        CoverPath = g.CoverPath,
                        Source = g.Source,
                        Confidence = g.Confidence,
                        IsSelected = g.Confidence >= 90
                    })
                    .ToList();

                SetupResultsList.ItemsSource = scanResults;
                SetupResultsCount.Text = scanResults.Count + " Spiel" + (scanResults.Count != 1 ? "e" : "");
                SetupResultsPanel.Visibility = Visibility.Visible;
                SetupScanStatus.Text = scanResults.Count == 0
                    ? "Keine Spiele in diesem Ordner gefunden. Versuche einen anderen Ordner."
                    : $"{scanResults.Count} Spiel gefunden. Wähle aus, welche importiert werden sollen.";
                SetupImportButton.Visibility = scanResults.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                SetupSkipButton.Visibility = scanResults.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            }
            catch (OperationCanceledException) { SetupScanStatus.Text = "Scan abgebrochen."; }
            catch (Exception ex) { SetupScanStatus.Text = "Scan nicht möglich: " + ex.Message; }
            finally
            {
                scanning = false;
                SetupScanButton.IsEnabled = true;
            }
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            var selected = scanResults.Where(r => r.IsSelected).ToList();
            if (selected.Count == 0)
            {
                SetupScanStatus.Text = "Bitte mindestens ein Spiel auswählen.";
                return;
            }

            Step2_FolderScan.Visibility = Visibility.Collapsed;
            Step3_Confirmation.Visibility = Visibility.Visible;
            SetupBackButton.Visibility = Visibility.Collapsed;
            SetupSkipButton.Visibility = Visibility.Collapsed;
            SetupImportButton.Visibility = Visibility.Collapsed;
            SetupSummaryText.Text = $"{selected.Count} Spiel werden importiert und in deine Bibliothek aufgenommen. Du kannst sie später jederzeit bearbeiten.";
        }

        private void Step3_Finish_Click(object sender, RoutedEventArgs e)
        {
            var selected = scanResults.Where(r => r.IsSelected).ToList();
            SetupCompleted?.Invoke(selected);
        }

        private void Skip_Click(object sender, RoutedEventArgs e)
        {
            SetupSkipped?.Invoke();
        }

        internal List<SetupScanResult> GetSelectedGames()
        {
            return scanResults.Where(r => r.IsSelected).ToList();
        }
    }

    public class SetupScanResult
    {
        public string Name { get; set; } = "";
        public string DetectionLabel { get; set; } = "";
        public string DisplayPath { get; set; } = "";
        public string ExecutablePath { get; set; } = "";
        public string Version { get; set; } = "";
        public string SavePath { get; set; } = "";
        public string CoverPath { get; set; } = "";
        public string Source { get; set; } = "";
        public int Confidence { get; set; }
        public bool IsSelected { get; set; }
    }
}
