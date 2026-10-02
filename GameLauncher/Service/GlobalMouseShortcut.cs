using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace GameLauncher.Service
{
    public sealed class GlobalMouseShortcut : IDisposable
    {
        private const int WhMouseLl = 14;
        private const int WmLButtonDown = 0x0201;
        private const int WmLButtonUp = 0x0202;
        private const int WmRButtonDown = 0x0204;
        private const int WmRButtonUp = 0x0205;

        private readonly Dispatcher dispatcher;
        private IntPtr hookId = IntPtr.Zero;
        private LowLevelMouseProc? proc;
        private bool leftDown;
        private bool rightDown;
        private DateTime lastTrigger = DateTime.MinValue;

        public event Action? Triggered;

        public bool IsActive => hookId != IntPtr.Zero;

        public GlobalMouseShortcut(Dispatcher dispatcher)
        {
            this.dispatcher = dispatcher;
        }

        public void Start()
        {
            if (hookId != IntPtr.Zero)
                return;

            proc = HookCallback;
            hookId = SetWindowsHookEx(
                WhMouseLl,
                proc,
                GetModuleHandle(IntPtr.Zero),
                0);
        }

        public void Stop()
        {
            if (hookId == IntPtr.Zero)
                return;

            UnhookWindowsHookEx(hookId);
            hookId = IntPtr.Zero;
            leftDown = false;
            rightDown = false;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int message = wParam.ToInt32();

                if (message == WmLButtonDown)
                    leftDown = true;
                else if (message == WmLButtonUp)
                    leftDown = false;
                else if (message == WmRButtonDown)
                    rightDown = true;
                else if (message == WmRButtonUp)
                    rightDown = false;

                if (leftDown && rightDown && DateTime.Now - lastTrigger > TimeSpan.FromSeconds(1.5))
                {
                    lastTrigger = DateTime.Now;
                    dispatcher.BeginInvoke(new Action(() => Triggered?.Invoke()));
                }
            }

            return CallNextHookEx(hookId, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            Stop();
        }

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(IntPtr lpModuleName);
    }
}
