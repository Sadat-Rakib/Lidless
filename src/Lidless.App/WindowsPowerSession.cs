using System.Diagnostics;

namespace Lidless.App;

/// <summary>
/// Sets/restores lid-close policy and holds a Windows power request so the
/// machine stays awake. A named heartbeat is updated so the watchdog process
/// can restore lid behavior if this process dies.
/// </summary>
internal sealed class WindowsPowerSession : IPowerSession, IDisposable
{
    private readonly PowerCfgClient _powerCfg = new();
    private readonly HeartbeatServer _heartbeat;
    private readonly object _gate = new();
    private IntPtr _request = IntPtr.Zero;
    private LidPolicy? _original;
    private bool _ownsSession;
    private bool _disposed;

    public WindowsPowerSession(HeartbeatServer heartbeat) => _heartbeat = heartbeat;

    public bool WatchdogAlive { get; set; }

    public bool? ReadEngaged()
    {
        lock (_gate)
        {
            if (!_ownsSession)
            {
                return false;
            }

            try
            {
                var policy = _powerCfg.ReadLidPolicy();
                if (policy is null)
                {
                    return null;
                }

                return policy.Value.IsDoNothing;
            }
            catch
            {
                return null;
            }
        }
    }

    public void SetEngaged(bool enabled)
    {
        lock (_gate)
        {
            if (enabled)
            {
                Enable();
            }
            else
            {
                Disable();
            }
        }
    }

    public void PulseHeartbeat()
    {
        lock (_gate)
        {
            WriteHeartbeat(shutdown: false);
        }
    }

    public void RequestWatchdogShutdown()
    {
        lock (_gate)
        {
            WriteHeartbeat(shutdown: true);
        }
    }

    /// <summary>
    /// If a previous instance left lid-close set to Do nothing, restore it.
    /// Safe to call at process start before the controller exists.
    /// </summary>
    public static bool RestoreOrphanedSession()
    {
        var session = SessionStore.Load();
        if (session is null || !session.RestoreArmed)
        {
            return false;
        }

        try
        {
            new PowerCfgClient().SetLidPolicy(new LidPolicy((LidAction)session.Ac, (LidAction)session.Dc));
        }
        finally
        {
            SessionStore.Clear();
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (_ownsSession)
            {
                SetEngaged(false);
            }
        }
        catch
        {
            // Best-effort restore during shutdown.
        }

        ReleaseRequest();
        _disposed = true;
    }

    private void Enable()
    {
        _original ??= _powerCfg.ReadLidPolicy()
                      ?? throw new InvalidOperationException("Could not read the current lid-close action.");

        SessionStore.Save(_original.Value, restoreArmed: true);
        WriteHeartbeat(shutdown: false, restoreArmed: true, active: false);

        try
        {
            _powerCfg.SetLidPolicy(new LidPolicy(LidAction.DoNothing, LidAction.DoNothing));
            HoldPowerRequest();
        }
        catch
        {
            try { _powerCfg.SetLidPolicy(_original.Value); } catch { /* restore is best-effort */ }
            SessionStore.Clear();
            WriteHeartbeat(shutdown: false, restoreArmed: false, active: false);
            throw;
        }

        _ownsSession = true;
        WriteHeartbeat(shutdown: false, restoreArmed: true, active: true);
    }

    private void Disable()
    {
        ReleaseRequest();
        NativeMethods.SetThreadExecutionState(NativeMethods.ExecutionState.Continuous);

        if (_original is { } original)
        {
            try
            {
                _powerCfg.SetLidPolicy(original);
            }
            finally
            {
                SessionStore.Clear();
            }
        }

        _ownsSession = false;
        WriteHeartbeat(shutdown: false, restoreArmed: false, active: false);
        _original = null;
    }

    private void HoldPowerRequest()
    {
        NativeMethods.SetThreadExecutionState(
            NativeMethods.ExecutionState.Continuous
            | NativeMethods.ExecutionState.SystemRequired
            | NativeMethods.ExecutionState.AwayModeRequired);

        if (_request == IntPtr.Zero)
        {
            _request = NativeMethods.CreatePowerRequest("Lidless keep-awake");
        }

        if (!NativeMethods.PowerSetRequest(_request, NativeMethods.PowerRequestType.SystemRequired))
        {
            throw new InvalidOperationException("PowerSetRequest(SystemRequired) failed — keep-awake may not hold.");
        }

        // Away/execution requests are best-effort; some SKUs reject them.
        NativeMethods.PowerSetRequest(_request, NativeMethods.PowerRequestType.AwayModeRequired);
        NativeMethods.PowerSetRequest(_request, NativeMethods.PowerRequestType.ExecutionRequired);
    }

    private void ReleaseRequest()
    {
        if (_request == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.PowerClearRequest(_request, NativeMethods.PowerRequestType.SystemRequired);
        NativeMethods.PowerClearRequest(_request, NativeMethods.PowerRequestType.AwayModeRequired);
        NativeMethods.PowerClearRequest(_request, NativeMethods.PowerRequestType.ExecutionRequired);
        NativeMethods.CloseHandle(_request);
        _request = IntPtr.Zero;
    }

    private void WriteHeartbeat(bool shutdown, bool? restoreArmed = null, bool? active = null)
    {
        var record = new HeartbeatRecord
        {
            Magic = HeartbeatRecord.MagicValue,
            ParentProcessId = Environment.ProcessId,
            LastHeartbeatUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            AcLidAction = (int)(_original?.Ac ?? LidAction.Sleep),
            DcLidAction = (int)(_original?.Dc ?? LidAction.Sleep),
            RestoreArmed = (restoreArmed ?? (_original is not null && _ownsSession)) ? 1 : 0,
            Active = (active ?? _ownsSession) ? 1 : 0,
            ShutdownRequested = shutdown ? 1 : 0,
        };

        _heartbeat.Write(record);
    }
}

internal static class WatchdogHost
{
    public static Process Start()
    {
        var path = Environment.ProcessPath
                   ?? throw new InvalidOperationException("Cannot locate Lidless.exe to start the watchdog.");
        var info = new ProcessStartInfo
        {
            FileName = path,
            Arguments = "--watchdog",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        return Process.Start(info)
               ?? throw new InvalidOperationException("Failed to start the Lidless watchdog process.");
    }
}
