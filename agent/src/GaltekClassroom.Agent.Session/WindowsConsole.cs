using System.Runtime.InteropServices;

namespace GaltekClassroom.Agent.Session;

internal static class WindowsConsole
{
    private const uint AttachParentProcess = 0xFFFFFFFF;

    public static void AttachToParentForCommandLine()
    {
        if (OperatingSystem.IsWindows())
        {
            _ = AttachConsole(AttachParentProcess);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint processId);
}
