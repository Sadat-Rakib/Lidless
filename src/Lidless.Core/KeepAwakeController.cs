namespace Lidless;

/// <summary>Reads whether keep-awake is currently engaged, and turns it on or off.</summary>
public interface IPowerSession
{
    /// <summary>
    /// Observed keep-awake engagement, or null when the OS could not be read.
    /// A failed read must never collapse into "off".
    /// </summary>
    bool? ReadEngaged();

    /// <summary>Set or clear keep-awake. Throws with a useful message on failure.</summary>
    void SetEngaged(bool enabled);
}

public interface IBatterySource
{
    BatteryInfo Read();
}

public interface IThermalSource
{
    bool IsThermalSerious();
}

public interface IClock
{
    DateTime Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime Now => DateTime.Now;
}

public interface IAlertPresenter
{
    void PresentFailure(bool targetEnable, string message);
}

public sealed class NoopAlertPresenter : IAlertPresenter
{
    public void PresentFailure(bool targetEnable, string message)
    {
    }
}

/// <summary>
/// Keep-awake session: toggle, safety, auto-off, auto-enable, and reconciliation.
/// All OS work goes through <see cref="IPowerSession"/> so the policy can be tested.
/// </summary>
public sealed class KeepAwakeController
{
    private readonly IPowerSession _power;
    private readonly IBatterySource _battery;
    private readonly IThermalSource _thermal;
    private readonly SettingsStore _store;
    private readonly IAlertPresenter _alerts;
    private readonly IClock _clock;

    private AutoWriteCoordinator _autoWrite;
    private StateSync _sync;
    private bool _hasConfirmedState;
    private PendingVerification? _pendingVerification;
    private BatteryInfo _currentBattery = new(100, true);

    public KeepAwakeController(
        IPowerSession power,
        IBatterySource battery,
        IThermalSource thermal,
        SettingsStore store,
        IAlertPresenter? alerts = null,
        IClock? clock = null)
    {
        _power = power;
        _battery = battery;
        _thermal = thermal;
        _store = store;
        _alerts = alerts ?? new NoopAlertPresenter();
        _clock = clock ?? new SystemClock();

        Settings = store.Load();
        Armed = store.LoadArmed();
        AutoOffMinutes = store.LoadAutoOffMinutes();
        OnboardingComplete = store.LoadOnboardingComplete();
        RefreshBattery();
        RefreshState();
        Reconcile();
    }

    public bool IsEnabled { get; private set; }
    public bool Armed { get; private set; }
    public SafetySettings Settings { get; private set; }
    public int AutoOffMinutes { get; private set; }
    public DateTime? AutoOffDeadline { get; private set; }
    public string AutoOffRemaining { get; private set; } = "";
    public string? LastError { get; private set; }
    public string? ExternalNotice { get; private set; }
    public string? VerificationNotice { get; private set; }
    public bool OnboardingComplete { get; private set; }
    public bool WatchdogRunning { get; set; }

    public BatteryInfo CurrentBattery => _currentBattery;

    public event EventHandler? Changed;

    public bool MasterToggleOn => Settings.AutoEnableWhenCharging ? Armed : IsEnabled;

    public IReadOnlyList<SafetyReason> AutoWarningReasons
    {
        get
        {
            if (!Settings.AutoEnableWhenCharging || !Armed || IsEnabled)
            {
                return Array.Empty<SafetyReason>();
            }

            return SafetyEvaluator.AllUnmetReasons(
                _currentBattery,
                _thermal.IsThermalSerious(),
                Settings,
                requirePower: true);
        }
    }

    public void CompleteOnboarding()
    {
        OnboardingComplete = true;
        _store.SaveOnboardingComplete(true);
        Notify();
    }

    public void UpdateSettings(SafetySettings next)
    {
        var wasAuto = Settings.AutoEnableWhenCharging;
        Settings = next.Clone();
        _store.Save(Settings);
        if (Settings.AutoEnableWhenCharging)
        {
            if (!wasAuto)
            {
                Armed = true;
                _store.SaveArmed(Armed);
                CancelAutoOff();
            }

            Reconcile(userDriven: true);
        }
        else if (_autoWrite.InFlightTarget is bool pending)
        {
            RefreshBattery();
            var conditions = SampleConditions();
            var settled = AutoEnablePolicy.HandoffToManual(pending, conditions, Settings);
            _autoWrite.Clear();
            SetEnabled(settled, note: null, origin: SetOrigin.Auto, conditions);
        }
        else
        {
            if (wasAuto && IsEnabled)
            {
                ArmAutoOff();
            }

            EvaluateSafety();
        }

        Notify();
    }

    public void SetMasterToggle(bool on)
    {
        if (Settings.AutoEnableWhenCharging)
        {
            SetArmed(on);
        }
        else
        {
            SetEnabled(on, origin: SetOrigin.User);
        }
    }

    public void KeepAwakeFor(int minutes)
    {
        var request = AutoOff.For(minutes, IsEnabled, Settings.AutoEnableWhenCharging);
        if (request is AutoOff.Request.IgnoredInAutoModeRequest)
        {
            return;
        }

        AutoOffMinutes = minutes;
        _store.SaveAutoOffMinutes(minutes);
        switch (request)
        {
            case AutoOff.Request.CancelTimerRequest:
                CancelAutoOff();
                break;
            case AutoOff.Request.ArmTimerRequest:
                ArmAutoOff();
                break;
            case AutoOff.Request.EnableThenArmTimerRequest:
                SetEnabled(true, origin: SetOrigin.User);
                break;
        }

        Notify();
    }

    public void Tick()
    {
        _autoWrite.AdvanceTick();
        RefreshState();
        if (Settings.AutoEnableWhenCharging)
        {
            Reconcile();
        }
        else
        {
            RefreshBattery();
            EvaluateSafety();
        }

        Notify();
    }

    public void AutoOffTick()
    {
        if (AutoOffDeadline is not { } deadline)
        {
            return;
        }

        var now = _clock.Now;
        if (AutoOff.IsExpired(deadline, now))
        {
            var minutes = AutoOffMinutes;
            CancelAutoOff();
            SetEnabled(
                false,
                note: $"Auto-off: {AutoOff.OptionLabel(minutes)} elapsed.",
                origin: SetOrigin.AutoOff);
        }
        else
        {
            AutoOffRemaining = AutoOff.FormatCountdown(AutoOff.Remaining(deadline, now));
        }

        Notify();
    }

    public void RefreshState()
    {
        var token = _sync.BeginRead();
        ApplyObserved(_power.ReadEngaged(), token);
    }

    public void Shutdown()
    {
        if (IsEnabled)
        {
            SetEnabled(false, origin: SetOrigin.User);
        }
    }

    private void SetArmed(bool on)
    {
        Armed = on;
        _store.SaveArmed(on);
        Reconcile(userDriven: true);
        Notify();
    }

    private void Reconcile(bool userDriven = false)
    {
        if (!Settings.AutoEnableWhenCharging)
        {
            return;
        }

        if (!userDriven && !_autoWrite.MayWrite)
        {
            return;
        }

        RefreshBattery();
        var conditions = SampleConditions();
        var effective = AutoEnablePolicy.EffectiveState(_autoWrite.InFlightTarget, IsEnabled);
        var target = AutoEnablePolicy.Target(
            Armed,
            effective,
            conditions.Battery,
            conditions.ThermalSerious,
            Settings);
        if (target is null)
        {
            return;
        }

        _autoWrite.Issued(target.Value);
        SetEnabled(target.Value, note: null, origin: SetOrigin.Auto, conditions);
    }

    private void EvaluateSafety()
    {
        if (!IsEnabled)
        {
            return;
        }

        var info = _battery.Read();
        _currentBattery = info;
        var reason = SafetyEvaluator.ReasonToDisable(info, _thermal.IsThermalSerious(), Settings);
        if (reason is not null)
        {
            SetEnabled(false, note: reason.Message, origin: SetOrigin.Safety);
        }
    }

    public void SetEnabled(bool target, string? note = null, SetOrigin origin = SetOrigin.User, SafetySnapshot? conditions = null)
    {
        if (StateReconciler.ClearsExternalNotice(origin))
        {
            ExternalNotice = null;
        }

        if (origin != SetOrigin.Auto)
        {
            _autoWrite.Clear();
        }

        if (target)
        {
            var checkedSnapshot = conditions ?? SampleConditions();
            var blocker = SafetyEvaluator.ReasonToDisable(
                checkedSnapshot.Battery,
                checkedSnapshot.ThermalSerious,
                Settings);
            if (blocker is not null)
            {
                LastError = blocker.Message;
                if (origin == SetOrigin.User)
                {
                    _alerts.PresentFailure(true, blocker.BlockedMessage);
                }

                if (origin == SetOrigin.Auto)
                {
                    _autoWrite.Clear();
                }

                Notify();
                return;
            }
        }

        _pendingVerification = null;
        VerificationNotice = null;
        var token = _sync.BeginMutation();

        try
        {
            _power.SetEngaged(target);
            if (!_sync.ShouldApply(token))
            {
                return;
            }

            IsEnabled = target;
            LastError = note;
            UpdateAutoOff(target);
            _pendingVerification = new PendingVerification(target);
            VerifySetApplied(target);
        }
        catch (Exception ex)
        {
            if (!_sync.ShouldApply(token))
            {
                return;
            }

            LastError = ex.Message;
            if (origin == SetOrigin.User)
            {
                _alerts.PresentFailure(target, ex.Message);
            }

            RecoverStateAfterFailedWrite();
        }

        Notify();
    }

    private void VerifySetApplied(bool target)
    {
        var token = _sync.BeginRead();
        var observed = _power.ReadEngaged();
        if (!_sync.ShouldApply(token))
        {
            return;
        }

        ApplyVerification(StateReconciler.VerifyAfterSet(target, observed), target);
    }

    private void ApplyVerification(StateReconciler.VerifyOutcome outcome, bool target)
    {
        _autoWrite.Resolved();
        switch (outcome)
        {
            case StateReconciler.VerifyOutcome.VerifiedOutcome:
                _pendingVerification = null;
                VerificationNotice = null;
                _hasConfirmedState = true;
                break;
            case StateReconciler.VerifyOutcome.UnverifiedOutcome:
                VerificationNotice = StateReconciler.UnverifiedMessage(target);
                break;
            case StateReconciler.VerifyOutcome.MismatchOutcome mismatch:
                _pendingVerification = null;
                VerificationNotice = StateReconciler.WriteMismatchMessage(mismatch.Actual);
                _hasConfirmedState = true;
                AdoptSystemState(mismatch.Actual);
                break;
        }
    }

    private void ApplyObserved(bool? observed, StateSync.ReadToken token)
    {
        if (!_sync.ShouldApply(token))
        {
            return;
        }

        if (_pendingVerification is { } pending)
        {
            switch (StateReconciler.Resolve(pending, observed))
            {
                case StateReconciler.PendingResolution.StillUnverifiedOutcome:
                    break;
                case StateReconciler.PendingResolution.ConfirmedOutcome:
                    _autoWrite.Resolved();
                    _pendingVerification = null;
                    VerificationNotice = null;
                    _hasConfirmedState = true;
                    break;
                case StateReconciler.PendingResolution.WriteMismatchOutcome mismatch:
                    _autoWrite.Resolved();
                    _pendingVerification = null;
                    VerificationNotice = StateReconciler.WriteMismatchMessage(mismatch.Actual);
                    _hasConfirmedState = true;
                    AdoptSystemState(mismatch.Actual);
                    break;
            }

            return;
        }

        switch (StateReconciler.Reconcile(IsEnabled, _hasConfirmedState, observed))
        {
            case StateReconciler.Outcome.UnknownOutcome:
                break;
            case StateReconciler.Outcome.InSyncOutcome:
                _hasConfirmedState = true;
                break;
            case StateReconciler.Outcome.AdoptOutcome adopt:
                _hasConfirmedState = true;
                AdoptSystemState(adopt.Enabled);
                break;
            case StateReconciler.Outcome.DriftOutcome drift:
                _hasConfirmedState = true;
                ExternalNotice = drift.Change.Message();
                AdoptSystemState(drift.Change.NowEnabled());
                break;
        }
    }

    private void AdoptSystemState(bool enabled)
    {
        IsEnabled = enabled;
        _sync.BeginMutation();
        UpdateAutoOff(enabled);
        if (Settings.AutoEnableWhenCharging)
        {
            Reconcile();
        }
        else if (enabled)
        {
            EvaluateSafety();
        }
    }

    private void RecoverStateAfterFailedWrite()
    {
        _autoWrite.Resolved();
        _hasConfirmedState = false;
        RefreshState();
    }

    private void UpdateAutoOff(bool enabled)
    {
        if (enabled)
        {
            ArmAutoOff();
        }
        else
        {
            CancelAutoOff();
        }
    }

    private void ArmAutoOff()
    {
        CancelAutoOff();
        if (!IsEnabled || AutoOffMinutes <= 0 || Settings.AutoEnableWhenCharging)
        {
            return;
        }

        AutoOffDeadline = AutoOff.Deadline(_clock.Now, AutoOffMinutes);
        AutoOffRemaining = AutoOff.FormatCountdown(AutoOff.Remaining(AutoOffDeadline.Value, _clock.Now));
    }

    private void CancelAutoOff()
    {
        AutoOffDeadline = null;
        AutoOffRemaining = "";
    }

    private void RefreshBattery() => _currentBattery = _battery.Read();

    private SafetySnapshot SampleConditions() =>
        new(_currentBattery, _thermal.IsThermalSerious());

    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);
}
