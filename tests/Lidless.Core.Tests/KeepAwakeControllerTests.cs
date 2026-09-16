using Lidless;

namespace Lidless.Tests;

public sealed class KeepAwakeControllerTests
{
    [Fact]
    public void UserToggleEngagesPowerSession()
    {
        var harness = Harness.Create();
        harness.Controller.SetMasterToggle(true);
        Assert.True(harness.Power.Engaged);
        Assert.True(harness.Controller.IsEnabled);
        Assert.True(harness.Controller.MasterToggleOn);
    }

    [Fact]
    public void SafetyBlocksEnableWhenHot()
    {
        var harness = Harness.Create(thermalSerious: true);
        harness.Controller.SetMasterToggle(true);
        Assert.False(harness.Power.Engaged);
        Assert.False(harness.Controller.IsEnabled);
        Assert.Equal(SafetyReason.HighThermal.Message, harness.Controller.LastError);
        Assert.True(harness.Alerts.Presented);
    }

    [Fact]
    public void LowBatteryPausesWhileEnabled()
    {
        var harness = Harness.Create();
        harness.Controller.SetMasterToggle(true);
        harness.Battery.Info = new BatteryInfo(10, false);
        harness.Controller.Tick();
        Assert.False(harness.Controller.IsEnabled);
        Assert.Contains("battery 10%", harness.Controller.LastError);
    }

    [Fact]
    public void KeepAwakeForTurnsOnAndArmsTimer()
    {
        var harness = Harness.Create(now: new DateTime(2026, 1, 1, 12, 0, 0));
        harness.Controller.KeepAwakeFor(15);
        Assert.True(harness.Controller.IsEnabled);
        Assert.Equal(15, harness.Controller.AutoOffMinutes);
        Assert.Equal("15:00", harness.Controller.AutoOffRemaining);
    }

    [Fact]
    public void AutoOffExpiresAndDisables()
    {
        var clock = new FakeClock(new DateTime(2026, 1, 1, 12, 0, 0));
        var harness = Harness.Create(clock: clock);
        harness.Controller.KeepAwakeFor(15);
        clock.Now = clock.Now.AddMinutes(15);
        harness.Controller.AutoOffTick();
        Assert.False(harness.Controller.IsEnabled);
        Assert.Contains("Auto-off", harness.Controller.LastError);
    }

    [Fact]
    public void AutoModeIgnoresDurationPicker()
    {
        var harness = Harness.Create();
        var auto = SafetySettings.Default.Clone();
        auto.AutoEnableWhenCharging = true;
        harness.Controller.UpdateSettings(auto);
        harness.Controller.KeepAwakeFor(15);
        Assert.Equal(0, harness.Controller.AutoOffMinutes);
    }

    [Fact]
    public void AutoModeEnablesWhenArmedAndOnPower()
    {
        var harness = Harness.Create(onAc: true);
        var auto = SafetySettings.Default.Clone();
        auto.AutoEnableWhenCharging = true;
        harness.Controller.UpdateSettings(auto);
        Assert.True(harness.Controller.Armed);
        Assert.True(harness.Controller.IsEnabled);
    }

    [Fact]
    public void AutoModeStaysArmedButOffWhenUnplugged()
    {
        var harness = Harness.Create(onAc: false, percent: 90);
        var auto = SafetySettings.Default.Clone();
        auto.AutoEnableWhenCharging = true;
        harness.Controller.UpdateSettings(auto);
        Assert.True(harness.Controller.Armed);
        Assert.False(harness.Controller.IsEnabled);
        Assert.Contains(SafetyReason.NotOnPower, harness.Controller.AutoWarningReasons);
    }

    [Fact]
    public void FailedWriteLeavesStateUnconfirmed()
    {
        var harness = Harness.Create();
        harness.Power.FailNext = true;
        harness.Controller.SetMasterToggle(true);
        Assert.False(harness.Controller.IsEnabled);
        Assert.Equal("access denied", harness.Controller.LastError);
    }

    [Fact]
    public void ExternalDisableIsAdoptedAsNotice()
    {
        var harness = Harness.Create();
        harness.Controller.SetMasterToggle(true);
        harness.Power.Engaged = false;
        harness.Controller.Tick();
        Assert.False(harness.Controller.IsEnabled);
        Assert.Contains("outside Lidless", harness.Controller.ExternalNotice);
    }

    [Fact]
    public void NoLimitCancelsTimerWithoutDisabling()
    {
        var harness = Harness.Create();
        harness.Controller.KeepAwakeFor(15);
        harness.Controller.KeepAwakeFor(0);
        Assert.True(harness.Controller.IsEnabled);
        Assert.Equal("", harness.Controller.AutoOffRemaining);
    }

    [Fact]
    public void ShutdownTurnsKeepAwakeOff()
    {
        var harness = Harness.Create();
        harness.Controller.SetMasterToggle(true);
        harness.Controller.Shutdown();
        Assert.False(harness.Controller.IsEnabled);
        Assert.False(harness.Power.Engaged);
    }

    private sealed class Harness
    {
        public required FakePowerSession Power { get; init; }
        public required FakeBattery Battery { get; init; }
        public required FakeThermal Thermal { get; init; }
        public required RecordingAlerts Alerts { get; init; }
        public required KeepAwakeController Controller { get; init; }

        public static Harness Create(
            bool onAc = true,
            int percent = 80,
            bool thermalSerious = false,
            DateTime? now = null,
            FakeClock? clock = null)
        {
            var power = new FakePowerSession();
            var battery = new FakeBattery(new BatteryInfo(percent, onAc));
            var thermal = new FakeThermal(thermalSerious);
            var alerts = new RecordingAlerts();
            var controller = new KeepAwakeController(
                power,
                battery,
                thermal,
                new SettingsStore(new MemoryKeyValueStore()),
                alerts,
                clock ?? new FakeClock(now ?? new DateTime(2026, 1, 1, 8, 0, 0)));
            return new Harness
            {
                Power = power,
                Battery = battery,
                Thermal = thermal,
                Alerts = alerts,
                Controller = controller,
            };
        }
    }

    private sealed class FakePowerSession : IPowerSession
    {
        public bool Engaged { get; set; }
        public bool FailNext { get; set; }
        public bool Unknown { get; set; }

        public bool? ReadEngaged() => Unknown ? null : Engaged;

        public void SetEngaged(bool enabled)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("access denied");
            }

            Engaged = enabled;
        }
    }

    private sealed class FakeBattery : IBatterySource
    {
        public FakeBattery(BatteryInfo info) => Info = info;
        public BatteryInfo Info { get; set; }
        public BatteryInfo Read() => Info;
    }

    private sealed class FakeThermal : IThermalSource
    {
        public FakeThermal(bool serious) => Serious = serious;
        public bool Serious { get; set; }
        public bool IsThermalSerious() => Serious;
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTime now) => Now = now;
        public DateTime Now { get; set; }
    }

    private sealed class RecordingAlerts : IAlertPresenter
    {
        public bool Presented { get; private set; }
        public void PresentFailure(bool targetEnable, string message) => Presented = true;
    }
}
