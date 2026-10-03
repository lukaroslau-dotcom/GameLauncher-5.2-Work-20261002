using System.IO;

namespace GameLauncher.models
{
    public class AppSettings
    {
        [System.Text.Json.Serialization.JsonExtensionData]
        public Dictionary<string, System.Text.Json.JsonElement>? AdditionalData { get; set; }
        public double BackgroundFocusX { get; set; } = .5;
        public double BackgroundFocusY { get; set; } = .5;
        public bool LaunchShortcutEnabled { get; set; }
        public string LaunchShortcutKey { get; set; } = "G";
        public string BackgroundPath { get; set; } = "";
        public string ManagedGameFolder { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GameLauncher", "GameLibrary");
        public bool ManageLocalGames { get; set; } = true;
        public double UiScale { get; set; } = 1;
        public bool ReduceTransparency { get; set; }
        public bool ReduceMotion { get; set; }
        public string ArchivePath { get; set; } = "";
        public string DefaultGameFolder { get; set; } = "";
        public string DefaultSaveFolder { get; set; } = "";
        public bool GlobalMouseShortcutEnabled { get; set; }
        public bool StartWithWindows { get; set; }
        public bool MinimizeToTray { get; set; }
        public bool KeyboardShortcutEnabled { get; set; } = true;
        public int ShortcutKey { get; set; } = 0x48;
        public uint ShortcutModifiers { get; set; } = 0x0003;
        public bool MinimizeGameOnShortcut { get; set; } = true;
        public bool LowEnergyWhilePlaying { get; set; } = true;
        public bool RestoreAfterGame { get; set; } = true;
        public bool AutoDiscoveryEnabled { get; set; } = true;
        public bool AutoAddDetectedGames { get; set; }
        public bool F95UpdateChecksEnabled { get; set; } = true;
        public bool F95UpdateNotificationsEnabled { get; set; } = true;
        public int F95UpdateCheckIntervalHours { get; set; } = 12;
        public int HoverPreviewDelaySeconds { get; set; } = 5;
        public bool AutoDownloadF95Gallery { get; set; } = true;
        public bool RemoteHostAutoStart { get; set; }
        public int RemoteHostPort { get; set; } = 17840;
        public int RemoteHostFps { get; set; } = 60;
        public int RemoteHostQuality { get; set; } = 75;
        public int RemoteHostWidth { get; set; } = 1280;
        public bool RemotePrivacyCurtain { get; set; }
        public bool RemotePerformanceMode { get; set; } = true;
        public bool RemoteStreamingV51Migrated { get; set; }
        public bool RemoteStreamingV52Migrated { get; set; }
        public bool IsFirstRun { get; set; } = true;
        public bool BackupBeforePlay { get; set; } = true;
        public bool BackupAfterPlay { get; set; } = true;
        public int BackupIntervalMinutes { get; set; }
    }
}
