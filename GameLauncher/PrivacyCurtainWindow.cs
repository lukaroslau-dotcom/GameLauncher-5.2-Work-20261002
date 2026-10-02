using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace GameLauncher;

/// <summary>
/// Blacks out the local primary display while remaining invisible to supported
/// Windows capture APIs. The window never activates and lets input pass through.
/// </summary>
internal sealed class PrivacyCurtainWindow : Window
{
    private const int ExtendedStyle = -20;
    private const long Transparent = 0x20, ToolWindow = 0x80, NoActivate = 0x08000000;
    private static readonly IntPtr TopmostHandle = new(-1);
    private const uint ExcludeFromCapture = 0x11;
    private const uint NoActivateFlag = 0x0010, ShowWindowFlag = 0x0040;

    public bool IsSupported { get; private set; }

    public PrivacyCurtainWindow()
    {
        Title = "GameLauncher Streaming-Sichtschutz";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        Background = System.Windows.Media.Brushes.Black;
        Opacity = 0;
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        long style = GetWindowLongPtr(handle, ExtendedStyle).ToInt64();
        SetWindowLongPtr(handle, ExtendedStyle, new IntPtr(style | Transparent | ToolWindow | NoActivate));
        if (!SetWindowDisplayAffinity(handle, ExcludeFromCapture))
        {
            Dispatcher.BeginInvoke(Close);
            return;
        }

        var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds;
        if (bounds != null)
            SetWindowPos(handle, TopmostHandle, bounds.Value.Left, bounds.Value.Top,
                bounds.Value.Width, bounds.Value.Height, NoActivateFlag | ShowWindowFlag);
        IsSupported = true;
        Opacity = 1;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern IntPtr GetWindowLong32(IntPtr window, int index);
    private static IntPtr GetWindowLongPtr(IntPtr window, int index) => IntPtr.Size == 8 ? GetWindowLongPtr64(window, index) : GetWindowLong32(window, index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern IntPtr SetWindowLong32(IntPtr window, int index, IntPtr value);
    private static IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value) => IntPtr.Size == 8 ? SetWindowLongPtr64(window, index, value) : SetWindowLong32(window, index, value);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
