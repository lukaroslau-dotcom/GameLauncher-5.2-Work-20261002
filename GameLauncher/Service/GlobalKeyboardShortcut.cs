using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GameLauncher.Service;

public sealed class GlobalKeyboardShortcut : IDisposable
{
    private const int HotkeyId = 0x4C47;
    private readonly HwndSource source;
    private bool registered;
    public event Action? Triggered;
    public GlobalKeyboardShortcut(Window window)
    {
        source = HwndSource.FromHwnd(new WindowInteropHelper(window).EnsureHandle());
        source.AddHook(OnMessage);
    }
    public bool Configure(bool enabled, uint modifiers, int key)
    {
        if (registered) UnregisterHotKey(source.Handle, HotkeyId);
        registered = false;
        if (!enabled) return true;
        if (modifiers == 0 || key is < 1 or > 254) return false;
        registered = RegisterHotKey(source.Handle, HotkeyId, modifiers | 0x4000, (uint)key);
        return registered;
    }
    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
    {
        if (message == 0x0312 && wparam.ToInt32() == HotkeyId)
        {
            handled = true;
            Triggered?.Invoke();
        }
        return IntPtr.Zero;
    }
    public void Dispose()
    {
        if (registered) UnregisterHotKey(source.Handle, HotkeyId);
        source.RemoveHook(OnMessage);
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
