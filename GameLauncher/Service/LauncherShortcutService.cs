using System.IO;
using System.Runtime.InteropServices;
namespace GameLauncher.Service;
public static class LauncherShortcutService
{
    public static string NormalizeKey(string? key)
    {
        string value=(key??"G").Trim().ToUpperInvariant();
        if(value.Length!=1||!(value[0] is >= 'A' and <= 'Z' or >= '0' and <= '9'))throw new ArgumentException("Bitte einen Buchstaben A–Z oder eine Ziffer 0–9 wählen.");
        return value;
    }
    public static void Apply(bool enabled,string key)
    {
        if(Environment.GetCommandLineArgs().Contains("--data-root"))return;
        string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"GameLauncher starten.lnk");
        if(!enabled){if(File.Exists(path))File.Delete(path);return;}
        string exe=Path.Combine(AppContext.BaseDirectory,"GameLauncher.exe");
        if(!File.Exists(exe))throw new IOException("Die Startkombination bitte in der fertigen GameLauncher.exe einrichten.");
        Write(path,exe,NormalizeKey(key));
    }
    public static void Write(string shortcutPath,string exe,string key)
    {
        object? shell=null,link=null;
        try
        {
            var type=Type.GetTypeFromProgID("WScript.Shell")??throw new InvalidOperationException("Windows-Verknüpfungen sind nicht verfügbar.");
            shell=Activator.CreateInstance(type)!;
            dynamic dynamicShell=shell;link=dynamicShell.CreateShortcut(shortcutPath);dynamic shortcut=link;
            shortcut.TargetPath=exe;shortcut.WorkingDirectory=Path.GetDirectoryName(exe);shortcut.Description="GameLauncher öffnen";
            shortcut.Hotkey="CTRL+ALT+"+NormalizeKey(key);shortcut.Save();
        }
        finally{if(link!=null&&Marshal.IsComObject(link))Marshal.FinalReleaseComObject(link);if(shell!=null&&Marshal.IsComObject(shell))Marshal.FinalReleaseComObject(shell);}
    }
}
