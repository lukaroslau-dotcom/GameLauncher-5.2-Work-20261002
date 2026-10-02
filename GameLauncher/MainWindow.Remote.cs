using GameLauncher.Service;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace GameLauncher;

public partial class MainWindow
{
    private readonly RemoteHostService remoteHost=new();
    private PrivacyCurtainWindow? privacyCurtain;
    private bool remotePerformanceActive;

    private void InitializeRemoteHost()=>remoteHost.StatusChanged+=()=>Dispatcher.BeginInvoke(OnRemoteHostStatusChanged);
    private void ApplyRemoteHostSettings()
    {
        var settings=settingsService.Settings;
        if(settings.RemoteHostAutoStart&&!remoteHost.IsRunning)
        {
            try{remoteHost.Start(settings.RemoteHostPort,settings.RemoteHostFps,settings.RemoteHostQuality,settings.RemoteHostWidth);}
            catch(Exception ex){System.Diagnostics.Debug.WriteLine("Remote host: "+ex.Message);}
        }
        OnRemoteHostStatusChanged();
    }
    private void OnRemoteHostStatusChanged()
    {
        RefreshRemoteHostPanel();
        bool connected=remoteHost.IsRunning&&remoteHost.ClientCount>0;
        remotePerformanceActive=connected&&settingsService.Settings.RemotePerformanceMode;
        SetLauncherPriority(quietMode);
        discovery.Pause(quietMode||remotePerformanceActive);
        if(remotePerformanceActive)f95UpdateTimer.Stop();else ConfigureF95UpdateTimer();
        SetThreadExecutionState(connected?ExecutionState.Continuous|ExecutionState.SystemRequired|ExecutionState.DisplayRequired:ExecutionState.Continuous);

        // Never place a black window over a DXGI monitor capture: Desktop Duplication
        // would transmit that same black surface to the phone. A physical display-off
        // mode would also be woken immediately by the injected touch/mouse input.
        ClosePrivacyCurtain();
    }
    private void ClosePrivacyCurtain(){if(privacyCurtain==null)return;privacyCurtain.Close();privacyCurtain=null;}
    private void RefreshRemoteHostPanel()
    {
        if(!IsInitialized)return;
        RemoteHostToggleButton.Content=remoteHost.IsRunning?"Host-Modus stoppen":"Host-Modus starten";
        RemoteHostStateText.Text=remoteHost.IsRunning?$"An · {remoteHost.ClientCount} verbunden"+(remoteHost.ClientCount>0?$" · {remoteHost.ActualFps} FPS":""):"Aus";
        RemoteHostAddressTextBox.Text=remoteHost.IsRunning?(remoteHost.Urls.FirstOrDefault()??$"http://127.0.0.1:{remoteHost.Port}"):"Host-Modus ist aus";
        RemoteHostPinText.Text=remoteHost.Pin;
        RemoteHostPerformanceText.Text=remoteHost.IsRunning?$"{remoteHost.CaptureTechnology} · Ziel {(int)SettingsRemoteFpsSlider.Value} FPS · {(int)SettingsRemoteWidthSlider.Value}px":"DXGI-Vollbildaufnahme bereit";
    }
    private void RemoteHostToggle_Click(object sender,RoutedEventArgs e)
    {
        if(remoteHost.IsRunning){remoteHost.Stop();ClosePrivacyCurtain();ShowToast("Handy-Host gestoppt.");return;}
        try
        {
            int port=int.TryParse(SettingsRemotePortTextBox.Text,out int parsed)?Math.Clamp(parsed,1025,65535):17840;
            remoteHost.Start(port,(int)SettingsRemoteFpsSlider.Value,(int)SettingsRemoteQualitySlider.Value,(int)SettingsRemoteWidthSlider.Value);
            RefreshRemoteHostPanel();ShowToast("Handy-Host läuft. Adresse und PIN auf dem iPhone öffnen.");
        }
        catch(Exception ex){ShowToast("Host konnte nicht starten: "+ex.Message);}
    }
    private void RemotePerformancePreset_Click(object sender,RoutedEventArgs e)
    {
        SettingsRemoteFpsSlider.Value=60;SettingsRemoteQualitySlider.Value=75;SettingsRemoteWidthSlider.Value=1280;
        ShowToast("60-FPS-HD-Profil gewählt. Bitte Einstellungen speichern.");
    }
    private void CopyRemoteAddress_Click(object sender,RoutedEventArgs e)
    {
        if(!remoteHost.IsRunning){ShowToast("Host-Modus zuerst starten.");return;}
        System.Windows.Clipboard.SetText(RemoteHostAddressTextBox.Text);ShowToast("Adresse kopiert.");
    }
    private void CopyRemotePin_Click(object sender,RoutedEventArgs e)
    {
        if(!remoteHost.IsRunning){ShowToast("Host-Modus zuerst starten.");return;}
        System.Windows.Clipboard.SetText(remoteHost.Pin);ShowToast("PIN kopiert.");
    }

    [Flags] private enum ExecutionState:uint { Continuous=0x80000000,SystemRequired=0x1,DisplayRequired=0x2 }
    [DllImport("kernel32.dll")] private static extern ExecutionState SetThreadExecutionState(ExecutionState state);
}
