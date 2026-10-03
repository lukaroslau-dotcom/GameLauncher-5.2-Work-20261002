using Image = System.Windows.Controls.Image;
using Color = System.Windows.Media.Color;
using Orientation = System.Windows.Controls.Orientation;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GameLauncher.models;
using GameLauncher.Service;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using Brushes = System.Windows.Media.Brushes;

namespace GameLauncher;
public partial class MainWindow
{
    private ItemsControl GameItemsControl => HomeDashboard.GameItemsControl;
    private FrameworkElement HomeEmptyState => HomeDashboard.HomeEmptyState;
    private FrameworkElement FeaturedHost => HomeDashboard.FeaturedHost;
    private Controls.CroppedImage FeaturedImage => HomeDashboard.FeaturedImage;
    private TextBlock FeaturedTitle => HomeDashboard.FeaturedTitle;
    private TextBlock FeaturedMeta => HomeDashboard.FeaturedMeta;
    private TextBlock CollectionSummaryText => HomeDashboard.CollectionSummaryText;
    private IInputElement? focusBeforeDialog;
    private void DialogVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (ContentShell == null || DeleteGameDialog == null || AbsoluteDeleteDialog == null || SaveConflictDialog == null || BusyOverlay == null || ReinstallChoiceDialog == null || SaveFinderDialog == null || DuplicateGameDialog == null) return;
        bool modal = ReinstallChoiceDialog.IsVisible || SaveFinderDialog.IsVisible || DeleteGameDialog.IsVisible || AbsoluteDeleteDialog.IsVisible || SaveConflictDialog.IsVisible || BusyOverlay.IsVisible || DuplicateGameDialog.IsVisible;
        if (modal && ContentShell.IsEnabled) focusBeforeDialog = System.Windows.Input.Keyboard.FocusedElement;
        ContentShell.IsEnabled = !modal;
        if (e.NewValue is true && sender is FrameworkElement dialog)
            Dispatcher.BeginInvoke(new Action(() => dialog.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.First))));
        else if (!modal && focusBeforeDialog != null) { System.Windows.Input.Keyboard.Focus(focusBeforeDialog); focusBeforeDialog = null; }
    }
    private string librarySort = "Name";
    private bool syncingSearch;
    private readonly HashSet<string> selectedGameIds = new();
    private void ApplyPresentation()
    {
        var settings = settingsService.Settings;
        Controls.SmoothScroll.ReducedMotion = settings.ReduceMotion;
        double scale = double.IsFinite(settings.UiScale) ? Math.Clamp(settings.UiScale, .8, 2) : 1;
        ShellGrid.LayoutTransform = new ScaleTransform(scale, scale);
        bool opaque = settings.ReduceTransparency || SystemParameters.HighContrast || RenderCapability.Tier == 0;
        HomeDashboard.QuietToggle.IsChecked = settings.LowEnergyWhilePlaying;
        Controls.GameCard.HoverPreviewDelaySeconds=Math.Clamp(settings.HoverPreviewDelaySeconds,1,30);
        UpdateCompactLayout();
    }
    private void UpdateCompactLayout()
    {
        if (ShellGrid == null) return;
        double scale = settingsService?.Settings.UiScale ?? 1;
        double effectiveWidth = ActualWidth / (double.IsFinite(scale) && scale > 0 ? scale : 1);
        bool compact = effectiveWidth < 850;
        foreach (var button in new[] { NavHomeButton, NavLibraryButton, NavUpdatesButton, NavAddGameButton, NavSettingsButton })
        {
            if (button == null) continue;
            var label = FindNavLabel(button);
            if (label != null)
            {
                label.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
                button.ToolTip = label.Text;
                System.Windows.Automation.AutomationProperties.SetName(button, label.Text);
            }
            button.Padding = compact ? new Thickness(8,0,8,0) : new Thickness(14,0,14,0);
        }
        SidebarStatsText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        StatusLineText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        ContentShell.Margin = compact ? new Thickness(12,12,12,80) : new Thickness(24,18,20,80);
    }
    private static TextBlock? FindNavLabel(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock tb && tb.FontFamily?.Source == "Segoe UI Variable, Segoe UI" && !string.IsNullOrEmpty(tb.Text) && tb.Text != "0")
                return tb;
            var found = FindNavLabel(child);
            if (found != null) return found;
        }
        return null;
    }
    private void LoadPresentationSettings()
    {
        var s = settingsService.Settings;
        SettingsLaunchShortcutCheckBox.IsChecked=s.LaunchShortcutEnabled;
        SettingsLaunchKeyTextBox.Text=s.LaunchShortcutKey;
        SettingsBackgroundTextBox.Text = s.BackgroundPath;
        BackgroundPreview.FocusX=s.BackgroundFocusX;BackgroundPreview.FocusY=s.BackgroundFocusY;
        SettingsScaleSlider.Value = double.IsFinite(s.UiScale) ? Math.Clamp(s.UiScale,.8,2) : 1;
        SettingsTransparencyCheckBox.IsChecked = s.ReduceTransparency;
        SettingsMotionCheckBox.IsChecked = s.ReduceMotion;
    }
    private void SavePresentationSettings(AppPaths destination)
    {
        var s = settingsService.Settings;
        string selected = SettingsBackgroundTextBox.Text.Trim();
        if (selected.Length > 0 && !PathSafety.Same(selected, s.BackgroundPath))
        {
            if (TryLoadBitmap(selected) == null) throw new IOException("Das Hintergrundbild ist nicht lesbar.");
            string folder = Path.Combine(destination.GamesDataPath,"Media");
            Directory.CreateDirectory(folder);
            string target = Path.Combine(folder, "background-" + Guid.NewGuid().ToString("N") + Path.GetExtension(selected));
            File.Copy(selected,target);
            selected = target;
        }
        else if (selected.Length > 0 && PathSafety.Within(selected,gameService.Paths.ArchivePath) && !PathSafety.Same(destination.ArchivePath,gameService.Paths.ArchivePath))
            selected = Path.Combine(destination.ArchivePath,Path.GetRelativePath(gameService.Paths.ArchivePath,selected));
        s.LaunchShortcutEnabled=SettingsLaunchShortcutCheckBox.IsChecked==true;
        s.LaunchShortcutKey=LauncherShortcutService.NormalizeKey(SettingsLaunchKeyTextBox.Text);
        if(s.LaunchShortcutEnabled&&s.KeyboardShortcutEnabled&&s.ShortcutModifiers==3&&s.ShortcutKey==s.LaunchShortcutKey[0])throw new InvalidOperationException("Startkombination und Desktop-Kürzel müssen verschieden sein.");
        LauncherShortcutService.Apply(s.LaunchShortcutEnabled,s.LaunchShortcutKey);
        s.BackgroundPath = selected;
        s.BackgroundFocusX=BackgroundPreview.FocusX;s.BackgroundFocusY=BackgroundPreview.FocusY;
        s.UiScale = SettingsScaleSlider.Value;
        s.ReduceTransparency = SettingsTransparencyCheckBox.IsChecked == true;
        s.ReduceMotion = SettingsMotionCheckBox.IsChecked == true;
    }
    private void SelectBackground_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Bilder|*.png;*.jpg;*.jpeg;*.bmp", Title = "Hintergrundbild wählen" };
        if (dialog.ShowDialog(this) != true) return;
        if (TryLoadBitmap(dialog.FileName) == null) { ShowToast("Dieses Bild kann nicht geöffnet werden."); return; }
        SettingsBackgroundTextBox.Text = dialog.FileName;
    }
    private void ResetBackground_Click(object sender, RoutedEventArgs e) => SettingsBackgroundTextBox.Clear();
    private void DashboardQuiet_Click(object sender, RoutedEventArgs e)
    {
        bool old = settingsService.Settings.LowEnergyWhilePlaying;
        try
        {
            settingsService.Settings.LowEnergyWhilePlaying = HomeDashboard.QuietToggle.IsChecked == true;
            settingsService.Save(gameService.Paths);
        }
        catch (Exception ex) { settingsService.Settings.LowEnergyWhilePlaying = old; HomeDashboard.QuietToggle.IsChecked = old; ShowToast(ex.Message); }
    }
    private void LibrarySort_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button) librarySort = button.Tag as string ?? "Name";
        RefreshLibraryList();
        UpdateLibraryFilterSummary();
    }
    private void CancelScan_Click(object sender, RoutedEventArgs e) => scanCancellation?.Cancel();
    private void Card_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter && ReferenceEquals(e.OriginalSource,sender) && sender is Border { DataContext: Game game }) { ShowGameDetails(game); e.Handled = true; }
    }
    private void AnimatePage(UIElement page)
    {
        if (!settingsService.Settings.ReduceMotion && SystemParameters.ClientAreaAnimation)
            page.BeginAnimation(OpacityProperty,new DoubleAnimation(.65,1,TimeSpan.FromMilliseconds(120)));
        else page.BeginAnimation(OpacityProperty,null);
    }
}
