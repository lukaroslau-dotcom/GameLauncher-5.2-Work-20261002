namespace GameLauncher.models;

public sealed record InstallationVersion(string GamePath, string ExecutablePath, string SavePath, string Version, DateTime RecordedUtc);
