using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace GameLauncher.models
{
    public class Game : INotifyPropertyChanged
    {
        [JsonExtensionData]
        public Dictionary<string, System.Text.Json.JsonElement>? AdditionalData { get; set; }
        public List<InstallationVersion> InstallationHistory { get; set; } = new();
        public string LaunchArguments { get; set; } = "";
        public List<string> Aliases { get; set; } = new();
        public List<string> Tags { get; set; } = new();
        public string Description { get; set; } = "";
        public string Engine { get; set; } = "";
        public DateTime? MetadataUpdatedUtc { get; set; }
        public DateTime? SearchIntentUtc { get; set; }
        private string id = "";
        private string name = "";
        private double coverFocusX=.5, coverFocusY=.5;
        public double CoverFocusX { get=>coverFocusX; set=>SetField(ref coverFocusX,double.IsFinite(value)?Math.Clamp(value,0,1):.5); }
        public double CoverFocusY { get=>coverFocusY; set=>SetField(ref coverFocusY,double.IsFinite(value)?Math.Clamp(value,0,1):.5); }
        private string notes = "";
        private string versionInfo = "";
        public string Notes { get => notes; set => SetField(ref notes, value ?? ""); }
        public string VersionInfo { get => versionInfo; set => SetField(ref versionInfo, value ?? ""); }
        private string coverPath = "";
        private string gamePath = "";
        private string executablePath = "";
        private string savePath = "";
        private string installSource = "";
        private string gameVersion = "";
        private string f95ThreadId = "";
        private string latestKnownVersion = "";
        private string ignoredUpdateVersion = "";
        private string f95LastChanged = "";
        private DateTime? lastUpdateCheckUtc;
        private bool updateAvailable;
        private string updateCheckError = "";
        private string f95Developer = "";
        private string f95Status = "";
        private string modUrl = "";
        private List<string> galleryPaths = new();
        private bool isInstalled;
        private string status = GameLauncher.Constants.GameStatus.Installed;
        private bool isFavorite;
        private DateTime? lastPlayed;
        private TimeSpan playtime = TimeSpan.Zero;
        private DateTime addedDate = DateTime.MinValue;
        private HashSet<string> userEditedFields = new(StringComparer.OrdinalIgnoreCase);
        private string pathWarning = "";

        [JsonIgnore]
        public string PathWarning
        {
            get => pathWarning;
            set
            {
                string next = value ?? "";
                if (pathWarning == next) return;
                pathWarning = next;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PathWarning)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasPathWarning)));
            }
        }

        [JsonIgnore]
        public bool HasPathWarning => pathWarning.Length > 0;

        public string Id
        {
            get => id;
            set => SetField(ref id, value);
        }

        public string Name
        {
            get => name;
            set => SetField(ref name, value);
        }

        public string CoverPath
        {
            get => coverPath;
            set => SetField(ref coverPath, value);
        }

        public string GamePath
        {
            get => gamePath;
            set => SetField(ref gamePath, value);
        }

        public string ExecutablePath
        {
            get => executablePath;
            set => SetField(ref executablePath, value);
        }

        public string SavePath
        {
            get => savePath;
            set => SetField(ref savePath, value);
        }

        public string InstallSource
        {
            get => installSource;
            set => SetField(ref installSource, value);
        }

        public string GameVersion
        {
            get => gameVersion;
            set => SetField(ref gameVersion, value);
        }

        public string F95ThreadId
        {
            get => f95ThreadId;
            set => SetField(ref f95ThreadId, value ?? "");
        }

        public string LatestKnownVersion
        {
            get => latestKnownVersion;
            set => SetField(ref latestKnownVersion, value ?? "");
        }

        public string IgnoredUpdateVersion
        {
            get => ignoredUpdateVersion;
            set => SetField(ref ignoredUpdateVersion, value ?? "");
        }

        public string F95LastChanged
        {
            get => f95LastChanged;
            set => SetField(ref f95LastChanged, value ?? "");
        }

        public DateTime? LastUpdateCheckUtc
        {
            get => lastUpdateCheckUtc;
            set => SetField(ref lastUpdateCheckUtc, value);
        }

        public bool UpdateAvailable
        {
            get => updateAvailable;
            set => SetField(ref updateAvailable, value);
        }

        public string UpdateCheckError
        {
            get => updateCheckError;
            set => SetField(ref updateCheckError, value ?? "");
        }

        public string F95Developer
        {
            get => f95Developer;
            set => SetField(ref f95Developer, value ?? "");
        }

        public string F95Status
        {
            get => f95Status;
            set => SetField(ref f95Status, value ?? "");
        }

        public string ModUrl
        {
            get => modUrl;
            set => SetField(ref modUrl, value ?? "");
        }

        public List<string> GalleryPaths
        {
            get => galleryPaths;
            set => SetField(ref galleryPaths, value ?? new());
        }

        public bool IsInstalled
        {
            get => isInstalled;
            set => SetField(ref isInstalled, value);
        }

        public string Status
        {
            get => status;
            set => SetField(ref status, value);
        }

        public bool IsFavorite
        {
            get => isFavorite;
            set => SetField(ref isFavorite, value);
        }

        public DateTime? LastPlayed
        {
            get => lastPlayed;
            set => SetField(ref lastPlayed, value);
        }

        public TimeSpan Playtime
        {
            get => playtime;
            set => SetField(ref playtime, value);
        }

        public DateTime AddedDate
        {
            get => addedDate;
            set => SetField(ref addedDate, value);
        }

        public HashSet<string> UserEditedFields
        {
            get => userEditedFields;
            set => SetField(ref userEditedFields, value ?? new(StringComparer.OrdinalIgnoreCase));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
