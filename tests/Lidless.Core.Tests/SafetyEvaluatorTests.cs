using Lidless;

namespace Lidless.Tests;

public sealed class SafetyEvaluatorTests
{
    private static readonly SafetySettings Defaults = SafetySettings.Default.Clone();

    private static SafetySettings Auto
    {
        get
        {
            var s = SafetySettings.Default.Clone();
            s.AutoEnableWhenCharging = true;
            return s;
        }
    }

    [Fact]
    public void SafeWhenChargingAndCool()
    {
        var info = new BatteryInfo(50, true);
        Assert.Null(SafetyEvaluator.ReasonToDisable(info, thermalSerious: false, Defaults));
    }

    [Fact]
    public void ThermalTakesPriority()
    {
        var info = new BatteryInfo(100, true);
        Assert.Equal(SafetyReason.HighThermal, SafetyEvaluator.ReasonToDisable(info, true, Defaults));
    }

    [Fact]
    public void ThermalIgnoredWhenSettingOff()
    {
        var s = Defaults.Clone();
        s.PauseOnHighThermal = false;
        var info = new BatteryInfo(100, true);
        Assert.Null(SafetyEvaluator.ReasonToDisable(info, true, s));
    }

    [Fact]
    public void OnlyWhileChargingTriggersOnBattery()
    {
        var s = Defaults.Clone();
        s.OnlyWhileCharging = true;
        var info = new BatteryInfo(90, false);
        Assert.Equal(SafetyReason.NotCharging, SafetyEvaluator.ReasonToDisable(info, false, s));
    }

    [Fact]
    public void LowBatteryTriggers()
    {
        var info = new BatteryInfo(15, false);
        Assert.Equal(SafetyReason.LowBattery(15), SafetyEvaluator.ReasonToDisable(info, false, Defaults));
    }

    [Fact]
    public void ChargingOverridesLowBattery()
    {
        var info = new BatteryInfo(5, true);
        Assert.Null(SafetyEvaluator.ReasonToDisable(info, false, Defaults));
    }

    [Fact]
    public void ReasonMessages()
    {
        Assert.Equal("Auto-paused: the PC is running hot.", SafetyReason.HighThermal.Message);
        Assert.Equal("Auto-paused: not on charger.", SafetyReason.NotCharging.Message);
        Assert.Equal("Auto-paused: battery 12% on battery power.", SafetyReason.LowBattery(12).Message);
        Assert.Equal("Auto-paused: not connected to power.", SafetyReason.NotOnPower.Message);
    }

    [Fact]
    public void NotOnPowerPhrasing()
    {
        Assert.Equal("Connect your PC to power to keep it awake.", SafetyReason.NotOnPower.BlockedMessage);
        Assert.Equal("Not connected to power", SafetyReason.NotOnPower.CheckLabel);
    }

    [Fact]
    public void ThresholdZeroNeverTriggersLowBattery()
    {
        var s = Defaults.Clone();
        s.LowBatteryThreshold = 0;
        var info = new BatteryInfo(1, false);
        Assert.Null(SafetyEvaluator.ReasonToDisable(info, false, s));
    }

    [Fact]
    public void AutoEnableActivatesOnPowerWhenSafe() =>
        Assert.True(AutoEnablePolicy.CanActivate(new BatteryInfo(5, true), false, Defaults));

    [Fact]
    public void AutoEnableNeverActivatesOnBattery() =>
        Assert.False(AutoEnablePolicy.CanActivate(new BatteryInfo(100, false), false, Defaults));

    [Fact]
    public void AutoEnableBlockedByThermalOnPower() =>
        Assert.False(AutoEnablePolicy.CanActivate(new BatteryInfo(100, true), true, Defaults));

    [Fact]
    public void OnlyWhileChargingIsMootOnPower()
    {
        var s = Defaults.Clone();
        s.OnlyWhileCharging = true;
        Assert.True(AutoEnablePolicy.CanActivate(new BatteryInfo(50, true), false, s));
    }

    [Fact]
    public void TargetIsNilWhenAutoModeOff() =>
        Assert.Null(AutoEnablePolicy.Target(true, false, new BatteryInfo(90, true), false, Defaults));

    [Fact]
    public void TargetTurnsOnWhenArmedAndPluggedIn() =>
        Assert.True(AutoEnablePolicy.Target(true, false, new BatteryInfo(90, true), false, Auto));

    [Fact]
    public void TargetTurnsOffWhenUnplugged() =>
        Assert.False(AutoEnablePolicy.Target(true, true, new BatteryInfo(90, false), false, Auto));

    [Fact]
    public void TargetTurnsOffWhenDisarmed() =>
        Assert.False(AutoEnablePolicy.Target(false, true, new BatteryInfo(90, true), false, Auto));

    [Fact]
    public void TargetTurnsOffWhenRunningHot() =>
        Assert.False(AutoEnablePolicy.Target(true, true, new BatteryInfo(90, true), true, Auto));

    [Fact]
    public void TargetIsNilWhenAlreadyCorrect()
    {
        Assert.Null(AutoEnablePolicy.Target(true, true, new BatteryInfo(90, true), false, Auto));
        Assert.Null(AutoEnablePolicy.Target(false, false, new BatteryInfo(90, true), false, Auto));
        Assert.Null(AutoEnablePolicy.Target(true, false, new BatteryInfo(90, false), false, Auto));
    }

    [Fact]
    public void TargetIgnoresCutoffWhileOnPower()
    {
        var s = Auto.Clone();
        s.LowBatteryThreshold = 95;
        Assert.True(AutoEnablePolicy.Target(true, false, new BatteryInfo(10, true), false, s));
    }

    [Fact]
    public void AllUnmetReasonsEmptyWhenSafeOnPower()
    {
        var info = new BatteryInfo(80, true);
        Assert.Empty(SafetyEvaluator.AllUnmetReasons(info, false, Defaults, requirePower: true));
    }

    [Fact]
    public void AllUnmetReasonsListsPowerAndBatteryOffPower()
    {
        var s = Defaults.Clone();
        s.LowBatteryThreshold = 50;
        var info = new BatteryInfo(34, false);
        var reasons = SafetyEvaluator.AllUnmetReasons(info, false, s, requirePower: true);
        Assert.Equal(new SafetyReason[] { SafetyReason.NotOnPower, SafetyReason.LowBattery(34) }, reasons);
    }

    [Fact]
    public void AllUnmetReasonsDedupesPowerBullet()
    {
        var s = Defaults.Clone();
        s.OnlyWhileCharging = true;
        s.LowBatteryThreshold = 0;
        var info = new BatteryInfo(90, false);
        var reasons = SafetyEvaluator.AllUnmetReasons(info, false, s, requirePower: true);
        Assert.Equal(new[] { SafetyReason.NotOnPower }, reasons);
    }

    [Fact]
    public void LowBatteryAgreesAcrossBothEvaluators()
    {
        var s = Defaults.Clone();
        s.LowBatteryThreshold = 20;
        foreach (var percent in new[] { 0, 1, 19, 20, 21, 100 })
        {
            var info = new BatteryInfo(percent, false);
            var single = SafetyEvaluator.ReasonToDisable(info, false, s) is SafetyReason.LowBatteryReason;
            var listed = SafetyEvaluator.AllUnmetReasons(info, false, s, requirePower: false)
                .Any(r => r is SafetyReason.LowBatteryReason);
            Assert.Equal(single, listed);
        }
    }

    [Fact]
    public void AllUnmetReasonsIncludesThermal()
    {
        var info = new BatteryInfo(90, false);
        var reasons = SafetyEvaluator.AllUnmetReasons(info, true, Defaults, requirePower: true);
        Assert.Equal(SafetyReason.HighThermal, reasons[0]);
        Assert.Contains(SafetyReason.NotOnPower, reasons);
    }

    [Fact]
    public void SettingsStoreDefaultsWhenUnseeded()
    {
        var store = new SettingsStore(new MemoryKeyValueStore());
        Assert.Equal(SafetySettings.Default, store.Load());
    }

    [Fact]
    public void SettingsStoreRoundTrip()
    {
        var store = new SettingsStore(new MemoryKeyValueStore());
        var s = SafetySettings.Default.Clone();
        s.OnlyWhileCharging = true;
        s.PauseOnHighThermal = false;
        s.LowBatteryThreshold = 35;
        s.AutoEnableWhenCharging = true;
        store.Save(s);
        Assert.Equal(s, store.Load());
    }

    [Fact]
    public void ArmedRoundTrip()
    {
        var store = new SettingsStore(new MemoryKeyValueStore());
        Assert.False(store.LoadArmed());
        store.SaveArmed(true);
        Assert.True(store.LoadArmed());
    }

    [Fact]
    public void EffectiveStatePrefersPendingTarget()
    {
        Assert.True(AutoEnablePolicy.EffectiveState(true, false));
        Assert.False(AutoEnablePolicy.EffectiveState(false, true));
        Assert.True(AutoEnablePolicy.EffectiveState(null, true));
    }

    [Fact]
    public void HandoffToManualAllowsOnWhenSafe()
    {
        var conditions = new SafetySnapshot(new BatteryInfo(80, true), false);
        Assert.True(AutoEnablePolicy.HandoffToManual(true, conditions, Defaults));
        Assert.False(AutoEnablePolicy.HandoffToManual(false, conditions, Defaults));
    }

    [Fact]
    public void WriteIsPermittedNeverRefusesOff()
    {
        var hot = new SafetySnapshot(new BatteryInfo(10, false), true);
        Assert.True(AutoEnablePolicy.WriteIsPermitted(false, hot, Defaults));
        Assert.False(AutoEnablePolicy.WriteIsPermitted(true, hot, Defaults));
    }
}
