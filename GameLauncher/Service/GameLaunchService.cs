using GameLauncher.Constants;
using GameLauncher.models;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace GameLauncher.Service;

public sealed class GameLaunchService : IDisposable
{
    private sealed class Session(Game game, Process process)
    {
        public Game Game { get; } = game;
        public Stopwatch Clock { get; } = Stopwatch.StartNew();
        public Dictionary<int, Process> Processes { get; } = new() { [process.Id] = process };
        public TimeSpan Accounted { get; set; }
    }
    private readonly object gate = new();
    private readonly Dictionary<string, Session> sessions = new();
    private readonly CancellationTokenSource stop = new();
    public event Action<Game, TimeSpan>? SessionEnded;
    public event Action<string>? LaunchFailed;
    public bool HasRunningGames { get { lock (gate) return sessions.Count > 0; } }
    public bool IsRunning(string id) { lock (gate) return sessions.ContainsKey(id); }
    public string RunningTitle { get { lock (gate) return sessions.Values.FirstOrDefault()?.Game.Name ?? ""; } }

    public bool TryLaunch(Game game)
    {
        lock (gate)
        {
            if (sessions.ContainsKey(game.Id)) return false;
            if (!game.IsInstalled || game.Status == GameStatus.Deleted ||
                !File.Exists(game.ExecutablePath) || !Directory.Exists(game.GamePath) || !PathSafety.Within(game.ExecutablePath, game.GamePath))
            {
                LaunchFailed?.Invoke("Die Installation ist nicht verfuegbar. Bitte die Spielpfade pruefen.");
                return false;
            }
            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = game.ExecutablePath,
                    WorkingDirectory = Path.GetDirectoryName(game.ExecutablePath)!,
                    Arguments = game.LaunchArguments,
                    // Let Windows handle application manifests, SmartScreen and UAC.
                    // Starting directly used to turn those normal consent flows into a
                    // launcher error for games that require elevation.
                    UseShellExecute = true
                });
                if (process == null) throw new IOException("No process returned.");
                var session = new Session(game, process);
                sessions.Add(game.Id, session);
                _ = MonitorAsync(session, stop.Token);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                LaunchFailed?.Invoke("Das Spiel konnte nicht gestartet werden: " + ex.Message);
                return false;
            }
        }
    }

    // Child launchers can exit before the actual game window.
    private async Task MonitorAsync(Session session, CancellationToken cancellation)
    {
        TimeSpan? allExitedAt = null;
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellation).ConfigureAwait(false);
                var processTree = ProcessTree.Snapshot();
                lock (gate)
                {
                    var liveIds = session.Processes.Where(p => !Exited(p.Value)).Select(p => p.Key).ToHashSet();
                    if (session.Clock.Elapsed < TimeSpan.FromSeconds(6))
                        liveIds.UnionWith(session.Processes.Keys);
                    bool added;
                    do
                    {
                        added = false;
                        foreach (var entry in processTree)
                        {
                            if (session.Processes.ContainsKey(entry.Id) || !liveIds.Contains(entry.ParentId)) continue;
                            try
                            {
                                var child = Process.GetProcessById(entry.Id);
                                string executable = child.MainModule?.FileName ?? "";
                                if (child.StartTime.ToUniversalTime() >= DateTime.UtcNow - session.Clock.Elapsed - TimeSpan.FromSeconds(1) &&
                                    executable.Length > 0 && PathSafety.Within(executable, session.Game.GamePath))
                                {
                                    session.Processes.Add(entry.Id, child);
                                    liveIds.Add(entry.Id);
                                    added = true;
                                }
                                else child.Dispose();
                            }
                            catch (Exception ex) { Debug.WriteLine(ex.Message); }
                        }
                    } while (added);
                    if (session.Processes.Values.All(Exited))
                    {
                        allExitedAt ??= session.Clock.Elapsed;
                        if (session.Clock.Elapsed - allExitedAt.Value >= TimeSpan.FromSeconds(2)) break;
                    }
                    else allExitedAt = null;
                }
            }
            TimeSpan remaining;
            lock (gate)
            {
                if (!sessions.Remove(session.Game.Id)) return;
                remaining = (allExitedAt ?? session.Clock.Elapsed) - session.Accounted;
                foreach (var process in session.Processes.Values) process.Dispose();
            }
            SessionEnded?.Invoke(session.Game, remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    private static bool Exited(Process process)
    {
        try { return process.HasExited; }
        catch (InvalidOperationException) { return true; }
    }
    public IReadOnlyList<(Game Game, TimeSpan Duration)> Checkpoint()
    {
        lock (gate)
        {
            return sessions.Values.Select(session =>
            {
                var elapsed = session.Clock.Elapsed;
                var result = (session.Game, elapsed - session.Accounted);
                session.Accounted = elapsed;
                return result;
            }).ToList();
        }
    }
    public int MinimizeGames() => ChangeWindows(false);
    public int RestoreGames() => ChangeWindows(true);
    public int ActivateGame(string id) => ChangeWindows(true,id);
    public void SetPerformanceMode(bool enabled)
    {
        lock (gate)
        {
            foreach (Process process in sessions.Values.SelectMany(session => session.Processes.Values))
            {
                if (Exited(process)) continue;
                try { process.PriorityClass = enabled ? ProcessPriorityClass.AboveNormal : ProcessPriorityClass.Normal; }
                catch (Exception ex) { Debug.WriteLine(ex.Message); }
            }
        }
    }
    private int ChangeWindows(bool restore, string? id = null)
    {
        HashSet<int> ids;
        lock (gate) ids = sessions.Values.Where(s => id == null || s.Game.Id == id).SelectMany(s => s.Processes)
            .Where(p => !Exited(p.Value)).Select(p => p.Key).ToHashSet();
        int changed = 0;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint pid);
            if (ids.Contains((int)pid) && IsWindowVisible(window))
            {
                if (restore) { if (IsIconic(window)) ShowWindowAsync(window, 9); SetForegroundWindow(window); changed++; }
                else if (!restore && !IsIconic(window)) { ShowWindowAsync(window, 6); changed++; }
            }
            return true;
        }, IntPtr.Zero);
        return changed;
    }
    public void Dispose()
    {
        stop.Cancel();
        lock (gate)
        {
            foreach (var session in sessions.Values)
                foreach (var process in session.Processes.Values) process.Dispose();
            sessions.Clear();
        }
    }
    private delegate bool EnumWindowsProc(IntPtr window, IntPtr param);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
}

internal static class ProcessTree
{
    internal record Entry(int Id, int ParentId);
    internal static List<Entry> Snapshot()
    {
        var result = new List<Entry>();
        IntPtr snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == new IntPtr(-1)) return result;
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            if (Process32FirstW(snapshot, ref entry))
                do { result.Add(new((int)entry.ProcessId, (int)entry.ParentId)); }
                while (Process32NextW(snapshot, ref entry));
        }
        finally { CloseHandle(snapshot); }
        return result;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, ProcessId;
        public UIntPtr Heap;
        public uint ModuleId, Threads, ParentId;
        public int Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Exe;
    }
    [DllImport("kernel32.dll")] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
