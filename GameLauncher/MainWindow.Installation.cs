using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Text.RegularExpressions;
using GameLauncher.Constants;
using GameLauncher.models;
using GameLauncher.Service;
namespace GameLauncher;
public partial class MainWindow
{
    private bool searchingSaves,savePickerForReinstall;
    private sealed record SaveProbe(bool ForReinstall,string Folder,string Name);
    private readonly Dictionary<string,SaveProbe> saveProbeSessions = new();
    private void OpenReinstall(Game game)
    {
        reinstallGame=game;ReinstallChoiceTitle.Text=game.Name;
        SourceInstallButton.IsEnabled=Uri.TryCreate(game.InstallSource,UriKind.Absolute,out var uri)&&uri.Scheme is "http" or "https";
        ReinstallSourceText.Text=SourceInstallButton.IsEnabled?game.InstallSource:"Für dieses Spiel ist kein gültiger Quellenlink hinterlegt. Du kannst ihn unter Bearbeiten ergänzen.";
        ReinstallChoiceDialog.Visibility=Visibility.Visible;
    }
    private void CloseReinstallChoice_Click(object sender,RoutedEventArgs e)=>ReinstallChoiceDialog.Visibility=Visibility.Collapsed;
    private void SourceInstall_Click(object sender,RoutedEventArgs e)
    {
        if(reinstallGame==null)return;
        if(!Uri.TryCreate(reinstallGame.InstallSource,UriKind.Absolute,out var uri)||uri.Scheme is not ("http" or "https")){ShowToast("Bitte zuerst einen gültigen Quellenlink eintragen.");return;}
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true});
            ReinstallChoiceDialog.Visibility=Visibility.Collapsed;ShowReinstallForm(reinstallGame);
            ShowToast("Quelle geöffnet. Installiere das Spiel und wähle anschließend seine Startdatei.");
        }catch(Exception ex){ShowToast("Quelle konnte nicht geöffnet werden: "+ex.Message);}
    }
    private void LinkInstalled_Click(object sender,RoutedEventArgs e)
    {
        if(reinstallGame==null)return;
        string? file=PickExe(settingsService.Settings.DefaultGameFolder);if(file==null)return;
        var game=reinstallGame;ReinstallChoiceDialog.Visibility=Visibility.Collapsed;ShowReinstallForm(game);PrefillReinstall(file);
    }
    private void PrefillReinstall(string file)
    {
        var info=DiscoveryService.Describe(file);
        installExecutablePath=file;installGamePath=info?.GamePath??Path.GetDirectoryName(file)!;
        InstallExecutableTextBox.Text=file;InstallGamePathTextBox.Text=installGamePath;
        if(!string.IsNullOrWhiteSpace(info?.Version)&&info.Version!="Unknown")InstallVersionTextBox.Text=info.Version;
        string save=info?.SavePath??"";
        if(save.Length==0&&reinstallGame!=null&&Directory.Exists(reinstallGame.SavePath))save=reinstallGame.SavePath;
        installSavePath=save;InstallSavePathTextBox.Text=save;
    }
    private async void FindSaves_Click(object sender,RoutedEventArgs e)
    {
        bool forReinstall=InstallGameView.Visibility==Visibility.Visible;
        string folder=forReinstall?installGamePath:GameFolderPath;
        string name=forReinstall?reinstallGame?.Name??"":GameNameTextBox.Text;
        await ShowSaveFinderAsync(forReinstall,folder,name);
    }
    private async Task ShowSaveFinderAsync(bool forReinstall,string folder,string name)
    {
        if(searchingSaves)return;searchingSaves=true;
        savePickerForReinstall=forReinstall;
        SaveFinderStatus.Text="Suche in der Installation und üblichen lokalen Speicherorten …";
        SaveCandidates.ItemsSource=null;SaveFinderDialog.Visibility=Visibility.Visible;
        try
        {
            var candidates=await Task.Run(()=>SaveLocationService.Find(folder,name,CancellationToken.None));
            SaveCandidates.ItemsSource=candidates;
            SaveFinderStatus.Text=candidates.Count>0?"Mögliche Speicherorte gefunden. Bitte wähle den passenden Ordner.":"Noch nichts gefunden. Starte das Spiel, speichere einmal und suche danach erneut. Du kannst den Pfad auch manuell eintragen, z. B. mit %APPDATA%.";
        }
        catch(Exception ex){SaveFinderStatus.Text="Suche nicht möglich: "+ex.Message;}
        finally{searchingSaves=false;}
    }

    private void LaunchToCreateSave_Click(object sender,RoutedEventArgs e)
    {
        bool forReinstall=InstallGameView.Visibility==Visibility.Visible;
        string executable=forReinstall?installExecutablePath:GameExecutablePath;
        string folder=forReinstall?installGamePath:GameFolderPath;
        string name=forReinstall?reinstallGame?.Name??"Spiel":GameNameTextBox.Text.Trim();
        if(!File.Exists(executable)||!Directory.Exists(folder))
        {
            ShowToast("Bitte zuerst eine gültige Startdatei wählen.");
            return;
        }
        if(saveProbeSessions.Count>0)
        {
            ShowToast("Ein Spiel läuft bereits zur Spielstandsuche.");
            return;
        }
        string id="save-probe-"+Guid.NewGuid().ToString("N");
        var probeGame=new Game {Id=id,Name=name.Length>0?name:"Spiel",GamePath=folder,ExecutablePath=executable,IsInstalled=true,Status=GameStatus.Installed};
        saveProbeSessions[id]=new(forReinstall,folder,probeGame.Name);
        if(!launchService.TryLaunch(probeGame)){saveProbeSessions.Remove(id);return;}
        ProbeSaveButton.IsEnabled=InstallProbeSaveButton.IsEnabled=false;
        ShowToast("Spiel gestartet. Speichere einmal und beende es; danach beginnt die Save-Suche automatisch.");
    }

    private async Task FinishSaveProbeAsync(SaveProbe probe)
    {
        ProbeSaveButton.IsEnabled=InstallProbeSaveButton.IsEnabled=true;
        ShowToast("Spiel beendet. Der neue Save-Ordner wird gesucht …");
        await ShowSaveFinderAsync(probe.ForReinstall,probe.Folder,probe.Name);
    }

    private async Task<bool> MoveGameToManagedLibraryAsync(Game game, bool requested)
    {
        if (!requested || GameStorageService.IsPlatformManaged(game) ||
            string.IsNullOrWhiteSpace(game.GamePath) ||
            PathSafety.Within(game.GamePath, settingsService.Settings.ManagedGameFolder)) return true;

        GameStorageMoveResult? moved = null;
        bool success = await RunFileOperationAsync(() =>
        {
            moved = GameStorageService.MoveToLibrary(game.GamePath, game.ExecutablePath, game.SavePath,
                game.CoverPath, settingsService.Settings.ManagedGameFolder, CancellationToken.None);
            return moved.Success;
        }, "Spiel wird sicher in die GameLibrary übernommen …");
        if (!success || moved == null)
        {
            ShowToast(moved?.Error ?? "Das Spiel konnte nicht übernommen werden.");
            return false;
        }
        game.GamePath=moved.GamePath; game.ExecutablePath=moved.ExecutablePath;
        game.SavePath=moved.SavePath; game.CoverPath=moved.CoverPath;
        if (!string.IsNullOrWhiteSpace(moved.Error)) ShowToast(moved.Error);
        return true;
    }

    private async void MoveExistingGames_Click(object sender, RoutedEventArgs e)
    {
        string root = SettingsManagedGameFolderTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(root)) { ShowToast("Bitte zuerst einen GameLibrary-Ordner wählen."); return; }
        var candidates = gameService.GetGames().Where(game => game.IsInstalled && Directory.Exists(game.GamePath) &&
            !GameStorageService.IsPlatformManaged(game) && !PathSafety.Within(game.GamePath, root)).ToList();
        if (candidates.Count == 0) { ShowToast("Es gibt keine lokalen Spiele, die übernommen werden müssen."); return; }
        if (System.Windows.MessageBox.Show($"{candidates.Count} lokale Spiele werden vollständig in die GameLibrary verschoben. Fortfahren?",
            "Spiele übernehmen", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        int movedCount=0; var errors=new List<string>();
        foreach (var game in candidates)
        {
            GameStorageMoveResult? result=null;
            bool success=await RunFileOperationAsync(() =>
            {
                result=GameStorageService.MoveToLibrary(game.GamePath,game.ExecutablePath,game.SavePath,game.CoverPath,root,CancellationToken.None);
                return result.Success;
            }, $"{game.Name} wird übernommen …");
            if (success && result != null)
            {
                game.GamePath=result.GamePath; game.ExecutablePath=result.ExecutablePath;
                game.SavePath=result.SavePath; game.CoverPath=result.CoverPath; movedCount++;
                if (!string.IsNullOrWhiteSpace(result.Error)) errors.Add(game.Name+": "+result.Error);
            }
            else errors.Add(game.Name+": "+(result?.Error??"Übernahme fehlgeschlagen"));
        }
        if (movedCount>0) TrySaveGames();
        RefreshGameList();
        ShowToast(errors.Count==0?$"{movedCount} Spiele wurden übernommen.":$"{movedCount} übernommen · {errors.Count} Hinweise. Details im nächsten Fenster.");
        if(errors.Count>0) System.Windows.MessageBox.Show(string.Join(Environment.NewLine+Environment.NewLine,errors),"Übernahme abgeschlossen",MessageBoxButton.OK,MessageBoxImage.Information);
    }
    private void ChooseSaveCandidate_Click(object sender,RoutedEventArgs e)
    {
        if(sender is FrameworkElement {DataContext:SaveLocation candidate})
        {
            if(savePickerForReinstall){installSavePath=candidate.Path;InstallSavePathTextBox.Text=candidate.Path;}
            else{GameSaveFolderPath=candidate.Path;SavePathTextBox.Text=candidate.Path;}
            SaveFinderDialog.Visibility=Visibility.Collapsed;
        }
    }

    private async void SmartArchiveButton_Click(object sender,RoutedEventArgs e)
    {
        // Metadata may still be loading after a pasted F95 link. Finish it first so
        // the extracted installation is combined with the correct title/version.
        await EnsureF95MetadataForSaveAsync();
        string? archive=PickArchiveFile();
        if(archive==null)return;
        await PrepareArchiveForAddAsync(archive);
    }

    private async Task PrepareArchiveForAddAsync(string archive)
    {
        var extracted=await ExtractGameArchiveAsync(archive);
        if(extracted.Result?.Success!=true||extracted.Game==null)return;

        var matches = SmartAssociation.Rank(gameService.GetGames(), extracted.Game);
        if (matches.Count > 0 && matches[0].CanAutoLink && matches.Count(m => m.Score == matches[0].Score) == 1 && SmartAssociation.Link(gameService, matches[0], extracted.Game))
        { RefreshGameList(); ShowGameDetails(matches[0].Game); ShowToast("Archiv mit gemerktem Spiel verbunden. Metadaten und ID bleiben erhalten."); return; }
        Game? existing=FindLikelyGame(extracted.Game.Name);
        if(existing!=null && System.Windows.MessageBox.Show($"{existing.Name} ist bereits in deiner Bibliothek. Soll die neue Installation als Update eingebunden werden?",
            "Update erkannt",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes)
        {
            ShowReinstallForm(existing);PrefillReinstall(extracted.Game.ExecutablePath);
            if(extracted.Game.Version!="Unknown")InstallVersionTextBox.Text=extracted.Game.Version;
            var updateSave=await Task.Run(()=>SaveLocationService.FindBest(installGamePath,existing.Name,CancellationToken.None));
            if(updateSave!=null){installSavePath=updateSave.Path;InstallSavePathTextBox.Text=updateSave.Path;}
            ShowToast("Neue Spielversion erkannt. Prüfe nur noch Save-Übertragung und alte Version.");
            return;
        }

        GameFolderPath=extracted.Game.GamePath;
        GameExecutablePath=extracted.Game.ExecutablePath;
        GamePathTextBox.Text=GameFolderPath;
        ExecutableTextBox.Text=GameExecutablePath;
        PrefillFromExecutable(GameExecutablePath);
        var save=await Task.Run(()=>SaveLocationService.FindBest(GameFolderPath,GameNameTextBox.Text,CancellationToken.None));
        if(save!=null){GameSaveFolderPath=save.Path;SavePathTextBox.Text=save.Path;}
        ClearValidation(GamePathTextBox);ClearValidation(ExecutableTextBox);
        F95MetadataStatusText.Text+=(save!=null?" · Save-Ordner erkannt":" · Save wird nach dem ersten Start erkannt");
        ShowToast(save!=null?"Entpackt: Ordner, Startdatei und Save wurden erkannt.":"Entpackt: Ordner und Startdatei wurden erkannt. Save kann nach dem ersten Start gesucht werden.");
    }

    private Game? FindLikelyGame(string candidateName)
    {
        static string Key(string value)=>Regex.Replace(value.ToLowerInvariant(),@"(?:\bv?\d+(?:[._-]\d+)+\b)|[^a-z0-9]","");
        string wanted=Key(candidateName);
        if(wanted.Length<4)return null;
        return gameService.GetGames().Where(game=>game.Status!=GameStatus.Deleted)
            .Select(game=>(game,key:Key(game.Name)))
            .Where(item=>item.key.Length>=4&&(wanted.Contains(item.key,StringComparison.Ordinal)||item.key.Contains(wanted,StringComparison.Ordinal)))
            .OrderByDescending(item=>item.key.Length).Select(item=>item.game).FirstOrDefault();
    }
    private void CloseSaveFinder_Click(object sender,RoutedEventArgs e)=>SaveFinderDialog.Visibility=Visibility.Collapsed;
    private void UnknownSave_Click(object sender,RoutedEventArgs e)
    {
        if(InstallGameView.Visibility==Visibility.Visible){InstallSavePathTextBox.Clear();installSavePath="";RestoreSaveCheckBox.IsChecked=false;}
        else{SavePathTextBox.Clear();GameSaveFolderPath="";}
        ShowToast("Save-Ordner bleibt vorerst offen. Nach dem ersten Speichern über Bearbeiten → Saves finden ergänzen.");
    }

    private static string? PickArchiveFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Komprimiertes Spiel auswählen",
            Filter = "Spielarchive (*.zip;*.rar;*.7z)|*.zip;*.rar;*.7z|Alle Dateien (*.*)|*.*",
            InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads")
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private string? GetExtractionRoot()
    {
        string configured = settingsService.Settings.ManagedGameFolder;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            try { Directory.CreateDirectory(configured); return Path.GetFullPath(configured); }
            catch { }
        }
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "GameLibrary-Ordner für entpackte Spiele auswählen",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog(this) != true) return null;
        settingsService.Settings.ManagedGameFolder = dialog.FolderName;
        return dialog.FolderName;
    }

    private async Task<(ArchiveExtractionResult? Result, ScannedExecutable? Game)> ExtractGameArchiveAsync(string archive)
    {
        string? root = GetExtractionRoot();
        if (root == null) return (null, null);
        ArchiveExtractionResult? result = null;
        bool success = await RunFileOperationAsync(() =>
        {
            result = ArchiveExtractionService.Extract(archive, root, fileOperationCancellation!.Token);
            return result.Success;
        }, "Spielarchiv wird geprüft und entpackt …", true);
        if (!success || result == null)
        {
            ShowToast(result?.Error ?? "Das Archiv konnte nicht entpackt werden.");
            return (result, null);
        }

        ScannedExecutable? game;
        try
        {
            var candidates = await Task.Run(() => DiscoveryService.ScanFolder(result.Destination, CancellationToken.None)
                .Where(item => !item.IsArchive).OrderByDescending(item => item.Confidence).ToList());
            game = candidates.FirstOrDefault();
            if (game == null)
            {
                string? executable = PickExe(result.Destination);
                if (executable != null) game = DiscoveryService.Describe(executable);
            }
        }
        catch(Exception ex)
        {
            Debug.WriteLine(ex);
            ShowToast("Das Spiel wurde entpackt, aber die automatische Erkennung ist fehlgeschlagen. Der Ordner bleibt erhalten.");
            return (result,null);
        }
        if (game != null) game.InstallSource = "Entpacktes Archiv: " + Path.GetFileName(archive);
        return (result, game);
    }

    private async void ExtractArchiveForReinstall_Click(object sender, RoutedEventArgs e)
    {
        Game? gameToReinstall = reinstallGame;
        if (gameToReinstall == null) return;
        string? archive = PickArchiveFile();
        if (archive == null) return;
        ReinstallChoiceDialog.Visibility = Visibility.Collapsed;
        var extracted = await ExtractGameArchiveAsync(archive);
        if (extracted.Result?.Success != true) return;

        ShowReinstallForm(gameToReinstall);
        if (extracted.Game != null)
        {
            PrefillReinstall(extracted.Game.ExecutablePath);
            if (!string.IsNullOrWhiteSpace(extracted.Game.Version) && extracted.Game.Version != "Unknown")
                InstallVersionTextBox.Text = extracted.Game.Version;
        }
        ShowToast(extracted.Result.SourceDeleted
            ? $"Mit {extracted.Result.Tool} entpackt; das Archiv wurde gelöscht."
            : "Entpackt; das Originalarchiv bleibt erhalten.");
    }
}
