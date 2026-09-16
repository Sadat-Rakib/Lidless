using System.Diagnostics;
using System.Threading;

namespace Lidless.App;

/// <summary>
/// Separate process started as <c>Lidless.exe --watchdog</c>. If the tray app
/// crashes or hangs past the heartbeat timeout, this restores the previous
/// lid-close actions so the PC cannot get stuck awake.
/// </summary>
internal static class WatchdogProgram
{
    public static int Run()
    {
        HeartbeatServer? heartbeat = null;
        for (var attempt = 0; attempt < 20 && heartbeat is null; attempt++)
        {
            try
            {
                heartbeat = HeartbeatServer.Open();
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
        }

        if (heartbeat is null)
        {
            return 1;
        }

        using (heartbeat)
        {
            while (true)
            {
                if (!heartbeat.TryRead(out var record))
                {
                    Thread.Sleep(1000);
                    continue;
                }

                if (record.ShutdownRequested != 0)
                {
                    return 0;
                }

                var parentAlive = IsAlive(record.ParentProcessId);
                if (HeartbeatRecord.ShouldRestore(record, parentAlive, DateTime.UtcNow, Watchdog.DefaultTimeout))
                {
                    Restore(record);
                    SessionStore.Clear();
                    return 0;
                }

                if (!parentAlive && record.RestoreArmed == 0)
                {
                    return 0;
                }

                Thread.Sleep(5000);
            }
        }
    }

    private static bool IsAlive(int pid)
    {
        if (pid <= 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void Restore(HeartbeatRecord record)
    {
        try
        {
            new PowerCfgClient().SetLidPolicy(record.OriginalLidPolicy);
        }
        catch
        {
            // Last-resort: still try "sleep" on both AC and DC if the snapshot is unusable.
            try
            {
                new PowerCfgClient().SetLidPolicy(new LidPolicy(LidAction.Sleep, LidAction.Sleep));
            }
            catch
            {
                // Nothing else we can do without the user's session.
            }
        }

        NativeMethods.SetThreadExecutionState(NativeMethods.ExecutionState.Continuous);
    }
}
