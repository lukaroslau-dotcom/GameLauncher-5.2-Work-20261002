using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace GameLauncher.Service;

/// <summary>Fits the restored window to the current monitor's work area, including mixed DPI.</summary>
public sealed class WindowSizing : IDisposable
{
    private readonly Window window;
    private HwndSource? source;
    private string? monitor;
    private bool pending;
    public WindowSizing(Window window)
    {
        this.window = window;
        window.SourceInitialized += SourceInitialized;
        window.Loaded += Loaded;
        window.LocationChanged += LocationChanged;
        window.Closed += Closed;
    }
    private void Loaded(object sender, RoutedEventArgs e) => Fit(true);
    private void SourceInitialized(object? sender, EventArgs e)
    {
        source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        source?.AddHook(Hook);
        Fit(true);
    }
    private void LocationChanged(object? sender, EventArgs e)
    {
        if (source != null && Forms.Screen.FromHandle(source.Handle).DeviceName != monitor) ScheduleFit();
    }
    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Let WPF apply WM_DPICHANGED's suggested rectangle before clamping it.
        if (msg is 0x02E0 or 0x007E or 0x001A) ScheduleFit();
        return IntPtr.Zero;
    }
    private void ScheduleFit()
    {
        if (pending || window.Dispatcher.HasShutdownStarted) return;
        pending = true;
        window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => { pending = false; Fit(false); }));
    }
    public static Rect FitBounds(Rect work, Rect current, double dpiScale, bool initial)
    {
        double inset = Math.Min(12 * dpiScale, Math.Min(work.Width, work.Height) / 20);
        double availableWidth = Math.Max(1, work.Width - 2 * inset);
        double availableHeight = Math.Max(1, work.Height - 2 * inset);
        double width = Math.Min(initial ? 1360 * dpiScale : current.Width, availableWidth);
        double height = Math.Min(initial ? 900 * dpiScale : current.Height, availableHeight);
        double left = initial ? work.Left + (work.Width - width) / 2 : Math.Clamp(current.Left, work.Left + inset, work.Right - inset - width);
        double top = initial ? work.Top + (work.Height - height) / 2 : Math.Clamp(current.Top, work.Top + inset, work.Bottom - inset - height);
        return new Rect(left, top, width, height);
    }
    private void Fit(bool initial)
    {
        if (source == null) return;
        var screen = Forms.Screen.FromHandle(source.Handle);
        monitor = screen.DeviceName;
        var area = screen.WorkingArea;
        double scale = Math.Max(1, GetDpiForWindow(source.Handle)) / 96.0;
        var work = new Rect(area.X, area.Y, area.Width, area.Height);
        // Minimum sizes must never force a window beyond a small / highly scaled screen.
        window.MinWidth = Math.Min(1000, Math.Max(1, area.Width / scale - 24));
        window.MinHeight = Math.Min(650, Math.Max(1, area.Height / scale - 24));
        if (window.WindowState != WindowState.Normal) return;
        if (!GetWindowRect(source.Handle, out var bounds)) return;
        var fitted = FitBounds(work, new Rect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top), scale, initial);
        SetWindowPos(source.Handle, IntPtr.Zero, (int)fitted.X, (int)fitted.Y, (int)fitted.Width, (int)fitted.Height, 0x0014);
    }
    private void Closed(object? sender, EventArgs e) => Dispose();
    public void Dispose()
    {
        source?.RemoveHook(Hook);
        source = null;
        window.SourceInitialized -= SourceInitialized;
        window.Loaded -= Loaded;
        window.LocationChanged -= LocationChanged;
        window.Closed -= Closed;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
