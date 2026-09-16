namespace Lidless;

/// <summary>User-tunable safety preferences for keep-awake.</summary>
public sealed class SafetySettings : IEquatable<SafetySettings>
{
    public int LowBatteryThreshold { get; set; }
    public bool OnlyWhileCharging { get; set; }
    public bool PauseOnHighThermal { get; set; }
    public bool AutoEnableWhenCharging { get; set; }

    public static SafetySettings Default { get; } = new()
    {
        LowBatteryThreshold = 20,
        OnlyWhileCharging = false,
        PauseOnHighThermal = true,
        AutoEnableWhenCharging = false,
    };

    public SafetySettings Clone() => new()
    {
        LowBatteryThreshold = LowBatteryThreshold,
        OnlyWhileCharging = OnlyWhileCharging,
        PauseOnHighThermal = PauseOnHighThermal,
        AutoEnableWhenCharging = AutoEnableWhenCharging,
    };

    public bool Equals(SafetySettings? other) =>
        other is not null
        && LowBatteryThreshold == other.LowBatteryThreshold
        && OnlyWhileCharging == other.OnlyWhileCharging
        && PauseOnHighThermal == other.PauseOnHighThermal
        && AutoEnableWhenCharging == other.AutoEnableWhenCharging;

    public override bool Equals(object? obj) => Equals(obj as SafetySettings);
    public override int GetHashCode() =>
        HashCode.Combine(LowBatteryThreshold, OnlyWhileCharging, PauseOnHighThermal, AutoEnableWhenCharging);
}

/// <summary>Why keep-awake was (or should be) auto-disabled.</summary>
public abstract record SafetyReason
{
    public static SafetyReason HighThermal { get; } = new HighThermalReason();
    public static SafetyReason NotCharging { get; } = new NotChargingReason();
    public static SafetyReason NotOnPower { get; } = new NotOnPowerReason();
    public static SafetyReason LowBattery(int percent) => new LowBatteryReason(percent);

    public abstract string Message { get; }
    public abstract string BlockedMessage { get; }
    public abstract string CheckLabel { get; }

    public sealed record HighThermalReason : SafetyReason
    {
        public override string Message => "Auto-paused: the PC is running hot.";
        public override string BlockedMessage =>
            "Your PC is running hot, so keep-awake is paused. It'll be available again once the PC cools down.";
        public override string CheckLabel => "Running hot";
    }

    public sealed record NotChargingReason : SafetyReason
    {
        public override string Message => "Auto-paused: not on charger.";
        public override string BlockedMessage =>
            "\u201COnly while charging\u201D is on, so connect your PC to power to keep it awake.";
        public override string CheckLabel => "Not on charger";
    }

    public sealed record NotOnPowerReason : SafetyReason
    {
        public override string Message => "Auto-paused: not connected to power.";
        public override string BlockedMessage => "Connect your PC to power to keep it awake.";
        public override string CheckLabel => "Not connected to power";
    }

    public sealed record LowBatteryReason(int Percent) : SafetyReason
    {
        public override string Message => $"Auto-paused: battery {Percent}% on battery power.";
        public override string BlockedMessage =>
            $"Battery is at {Percent}%. Charge above the low-battery cutoff to keep your PC awake.";
        public override string CheckLabel => $"Battery {Percent}% is at or below the cutoff";
    }
}

/// <summary>
/// One sample of everything a safety decision reads from the world.
/// Exists so a decision and the write it authorises can be judged against the same conditions.
/// </summary>
public readonly struct SafetySnapshot : IEquatable<SafetySnapshot>
{
    public SafetySnapshot(BatteryInfo battery, bool thermalSerious)
    {
        Battery = battery;
        ThermalSerious = thermalSerious;
    }

    public BatteryInfo Battery { get; }
    public bool ThermalSerious { get; }

    public bool Equals(SafetySnapshot other) =>
        Battery.Equals(other.Battery) && ThermalSerious == other.ThermalSerious;

    public override bool Equals(object? obj) => obj is SafetySnapshot other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Battery, ThermalSerious);
}

/// <summary>Watchdog decision logic (pure, unit-testable).</summary>
public static class Watchdog
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(90);

    /// <summary>
    /// True when the app has gone quiet longer than <paramref name="timeout"/>,
    /// so the helper should auto-restore normal sleep / lid behavior.
    /// </summary>
    public static bool ShouldAutoRestore(DateTime lastHeartbeat, DateTime now, TimeSpan timeout) =>
        now - lastHeartbeat > timeout;
}

/// <summary>Battery safety policy (pure, unit-testable).</summary>
public static class SafetyPolicy
{
    /// <summary>
    /// True when keep-awake should be auto-disabled to protect the battery:
    /// running on battery (not AC) at or below the threshold percent.
    /// </summary>
    public static bool ShouldDisableForBattery(BatteryInfo info, int threshold) =>
        !info.OnAc && info.Percent <= threshold;
}

/// <summary>Pure safety decision. No side effects, fully unit-testable.</summary>
public static class SafetyEvaluator
{
    /// <summary>
    /// The reason keep-awake should be disabled given current conditions, or
    /// null if it's safe to stay awake. Checked in priority order: thermal first
    /// (hardware protection), then charging policy, then battery.
    /// A <c>LowBatteryThreshold</c> of 0 means "Never".
    /// </summary>
    public static SafetyReason? ReasonToDisable(BatteryInfo battery, bool thermalSerious, SafetySettings settings)
    {
        if (settings.PauseOnHighThermal && thermalSerious)
        {
            return SafetyReason.HighThermal;
        }

        if (settings.OnlyWhileCharging && !battery.OnAc)
        {
            return SafetyReason.NotCharging;
        }

        return LowBatteryReason(battery, settings);
    }

    private static SafetyReason? LowBatteryReason(BatteryInfo battery, SafetySettings settings)
    {
        if (settings.LowBatteryThreshold <= 0 || battery.OnAc || battery.Percent > settings.LowBatteryThreshold)
        {
            return null;
        }

        return SafetyReason.LowBattery(battery.Percent);
    }

    /// <summary>
    /// Every currently-unmet check, for the auto-mode warning list — unlike
    /// <see cref="ReasonToDisable"/>, which stops at the first.
    /// </summary>
    public static IReadOnlyList<SafetyReason> AllUnmetReasons(
        BatteryInfo battery,
        bool thermalSerious,
        SafetySettings settings,
        bool requirePower)
    {
        var reasons = new List<SafetyReason>();
        if (settings.PauseOnHighThermal && thermalSerious)
        {
            reasons.Add(SafetyReason.HighThermal);
        }

        if (requirePower && !battery.OnAc)
        {
            reasons.Add(SafetyReason.NotOnPower);
        }
        else if (settings.OnlyWhileCharging && !battery.OnAc)
        {
            reasons.Add(SafetyReason.NotCharging);
        }

        var lowBattery = LowBatteryReason(battery, settings);
        if (lowBattery is not null)
        {
            reasons.Add(lowBattery);
        }

        return reasons;
    }
}

/// <summary>
/// Pure decision for auto-enable mode ("Automatically enable when charging").
/// Keep-awake may activate only while on external power and every enabled
/// safety check passes.
/// </summary>
public static class AutoEnablePolicy
{
    public static bool CanActivate(BatteryInfo battery, bool thermalSerious, SafetySettings settings)
    {
        if (!battery.OnAc)
        {
            return false;
        }

        return SafetyEvaluator.ReasonToDisable(battery, thermalSerious, settings) is null;
    }

    /// <summary>
    /// The live keep-awake state auto mode wants, or null when there's nothing to do.
    /// </summary>
    public static bool? Target(
        bool armed,
        bool currentlyEnabled,
        BatteryInfo battery,
        bool thermalSerious,
        SafetySettings settings)
    {
        if (!settings.AutoEnableWhenCharging)
        {
            return null;
        }

        var shouldBeOn = armed && CanActivate(battery, thermalSerious, settings);
        return shouldBeOn == currentlyEnabled ? null : shouldBeOn;
    }

    /// <summary>Where keep-awake is heading: pending write target, or the live state.</summary>
    public static bool EffectiveState(bool? pendingTarget, bool live) => pendingTarget ?? live;

    /// <summary>Leaving auto mode while a write is still travelling: the state to settle on.</summary>
    public static bool HandoffToManual(bool pendingTarget, SafetySnapshot conditions, SafetySettings settings)
    {
        if (!pendingTarget)
        {
            return false;
        }

        return WriteIsPermitted(target: true, conditions, settings);
    }

    /// <summary>Whether the safety check on the way out of SetEnabled will let a write of target through.</summary>
    public static bool WriteIsPermitted(bool target, SafetySnapshot conditions, SafetySettings settings)
    {
        if (!target)
        {
            return true;
        }

        return SafetyEvaluator.ReasonToDisable(conditions.Battery, conditions.ThermalSerious, settings) is null;
    }
}
