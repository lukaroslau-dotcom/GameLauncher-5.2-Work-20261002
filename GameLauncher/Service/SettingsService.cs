using GameLauncher.models;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace GameLauncher.Service
{
    public class SettingsService
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public AppSettings Settings { get; private set; } = new AppSettings();
        public void RestoreSettings(string json) => Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

        public AppPaths LoadAndResolvePaths()
        {
            AppPaths defaultPaths = AppPaths.Default();
            defaultPaths.EnsureCreated();

            Settings = LoadFrom(defaultPaths.SettingsFilePath);

            if (!string.IsNullOrWhiteSpace(Settings.ArchivePath) &&
                !string.Equals(
                    Path.GetFullPath(Settings.ArchivePath),
                    defaultPaths.ArchivePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                AppPaths custom = new AppPaths(Settings.ArchivePath);
                try { custom.EnsureCreated(); }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                    Settings.ArchivePath = defaultPaths.ArchivePath;
                    return defaultPaths;
                }

                AppSettings customSettings = LoadFrom(custom.SettingsFilePath);
                if (!string.IsNullOrWhiteSpace(customSettings.ArchivePath))
                    Settings = customSettings;
                else
                    Settings.ArchivePath = custom.ArchivePath;

                return custom;
            }

            Settings.ArchivePath = defaultPaths.ArchivePath;
            return defaultPaths;
        }

        public void Save(AppPaths paths)
        {
            paths.EnsureCreated();
            Settings.ArchivePath = paths.ArchivePath;

            SaveTo(paths.SettingsFilePath, Settings);

            AppPaths defaultPaths = AppPaths.Default();
            if (!string.Equals(paths.SettingsFilePath, defaultPaths.SettingsFilePath, StringComparison.OrdinalIgnoreCase))
            {
                defaultPaths.EnsureCreated();
                SaveTo(defaultPaths.SettingsFilePath, Settings);
            }
        }

        public void ApplyStartWithWindows(bool enabled)
        {
            if (Environment.GetCommandLineArgs().Contains("--data-root")) return;
            const string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
            string exePath = Environment.ProcessPath ?? "";

            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(runKey, true);
                if (key == null)
                    return;

                if (enabled && !string.IsNullOrWhiteSpace(exePath))
                    key.SetValue("GameLauncher", $"\"{exePath}\"");
                else
                    key.DeleteValue("GameLauncher", false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Autostart konnte nicht gesetzt werden: {ex.Message}");
            }
        }

        private static AppSettings LoadFrom(string filePath)
        {
            if (!File.Exists(filePath))
                return new AppSettings();

            try
            {
                string json = File.ReadAllText(filePath);
                AppSettings settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                if (!settings.RemoteStreamingV51Migrated)
                {
                    // Apply the low-latency HD profile once when upgrading from v5.
                    settings.RemoteHostFps = 60;
                    settings.RemoteHostQuality = 75;
                    settings.RemoteHostWidth = 1280;
                    settings.RemoteStreamingV51Migrated = true;
                }
                if (!settings.RemoteStreamingV52Migrated)
                {
                    // A black overlay is part of the duplicated desktop and therefore also
                    // blackens the DXGI stream. Prefer a usable phone picture on upgrades.
                    settings.RemotePrivacyCurtain = false;
                    settings.RemoteStreamingV52Migrated = true;
                }
                return settings;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Settings konnten nicht geladen werden: {ex.Message}");
                return new AppSettings();
            }
        }

        private static void SaveTo(string filePath, AppSettings settings)
        {
            string json = JsonSerializer.Serialize(settings, JsonOptions);
            string temp = filePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, filePath, overwrite: true);
        }
    }
}
