using System.Runtime.InteropServices;

namespace Lidless.App;

internal static class NativeMethods
{
    [Flags]
    internal enum ExecutionState : uint
    {
        SystemRequired = 0x00000001,
        DisplayRequired = 0x00000002,
        AwayModeRequired = 0x00000040,
        Continuous = 0x80000000,
    }

    internal enum PowerRequestType
    {
        DisplayRequired = 0,
        SystemRequired = 1,
        AwayModeRequired = 2,
        ExecutionRequired = 3,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct ReasonContext
    {
        public uint Version;
        public uint Flags;
        public IntPtr SimpleReasonString;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    private const uint PowerRequestContextVersion = 0;
    private const uint PowerRequestContextSimpleString = 1;

    [DllImport("kernel32.dll")]
    internal static extern ExecutionState SetThreadExecutionState(ExecutionState flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr PowerCreateRequest(ref ReasonContext context);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool PowerSetRequest(IntPtr handle, PowerRequestType type);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool PowerClearRequest(IntPtr handle, PowerRequestType type);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool DestroyIcon(IntPtr handle);

    internal static IntPtr CreatePowerRequest(string reason)
    {
        var buffer = Marshal.StringToHGlobalUni(reason);
        try
        {
            var context = new ReasonContext
            {
                Version = PowerRequestContextVersion,
                Flags = PowerRequestContextSimpleString,
                SimpleReasonString = buffer,
            };
            var handle = PowerCreateRequest(ref context);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                throw new InvalidOperationException(
                    $"PowerCreateRequest failed ({Marshal.GetLastWin32Error()}).");
            }

            return handle;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
