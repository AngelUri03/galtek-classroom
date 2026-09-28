using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GaltekClassroom.Bootstrapper
{
    // WiX 5.0.2 declares IBootstrapperEngine::Apply(__in HWND), unlike Detect
    // and Elevate whose HWND parameters are optional. Headless BAs therefore
    // own a real, never-shown native window for the complete Apply lifetime.
    internal sealed class ApplyOwnerWindow : IDisposable
    {
        private const int WsExToolWindow = 0x00000080;
        private const int WsExNoActivate = 0x08000000;
        private const int WsPopup = unchecked((int)0x80000000);
        private const int GwlExStyle = -20;
        private readonly uint ownerThreadId;
        private IntPtr handle;

        private ApplyOwnerWindow(IntPtr handle, uint ownerThreadId)
        {
            this.handle = handle;
            this.ownerThreadId = ownerThreadId;
        }

        public IntPtr Handle => this.handle;
        public bool IsVisible => this.handle != IntPtr.Zero && IsWindowVisible(this.handle);
        public bool IsValid => this.handle != IntPtr.Zero && IsWindow(this.handle);

        public bool IsOwnedByCurrentProcess
        {
            get
            {
                if (!this.IsValid) return false;
                GetWindowThreadProcessId(this.handle, out var processId);
                using (var process = Process.GetCurrentProcess()) return processId == (uint)process.Id;
            }
        }

        public bool HasHeadlessStyles
        {
            get
            {
                if (!this.IsValid) return false;
                var style = GetWindowLongPtr(this.handle, GwlExStyle).ToInt64();
                return (style & WsExToolWindow) != 0 && (style & WsExNoActivate) != 0;
            }
        }

        public static ApplyOwnerWindow Create()
        {
            var threadId = GetCurrentThreadId();
            var handle = CreateWindowEx(
                WsExToolWindow | WsExNoActivate,
                "STATIC",
                "GaltekClassroom.Burn.ApplyOwner",
                WsPopup,
                0,
                0,
                0,
                0,
                IntPtr.Zero,
                IntPtr.Zero,
                GetModuleHandle(null),
                IntPtr.Zero);

            if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "APPLY_OWNER_WINDOW_CREATE_FAILED");
            var result = new ApplyOwnerWindow(handle, threadId);
            if (!result.IsValid || result.IsVisible || !result.IsOwnedByCurrentProcess || !result.HasHeadlessStyles)
            {
                result.Dispose();
                throw new InvalidOperationException("APPLY_OWNER_WINDOW_CONTRACT_INVALID");
            }
            return result;
        }

        public void Dispose()
        {
            if (this.handle == IntPtr.Zero) return;
            if (GetCurrentThreadId() != this.ownerThreadId) throw new InvalidOperationException("APPLY_OWNER_WINDOW_WRONG_THREAD");
            var target = this.handle;
            this.handle = IntPtr.Zero;
            if (!DestroyWindow(target)) throw new Win32Exception(Marshal.GetLastWin32Error(), "APPLY_OWNER_WINDOW_DESTROY_FAILED");
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style,
            int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr32(IntPtr window, int index);

        private static IntPtr GetWindowLongPtr(IntPtr window, int index) =>
            IntPtr.Size == 8 ? GetWindowLongPtr64(window, index) : GetWindowLongPtr32(window, index);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string moduleName);
    }
}
