using Lidless;

namespace Lidless.Tests;

public sealed class SharedLogicTests
{
    [Fact]
    public void WatchdogFiresAfterTimeout()
    {
        var last = DateTime.UnixEpoch.AddSeconds(1000);
        var now = DateTime.UnixEpoch.AddSeconds(1100);
        Assert.True(Watchdog.ShouldAutoRestore(last, now, TimeSpan.FromSeconds(90)));
    }

    [Fact]
    public void WatchdogQuietWithinTimeout()
    {
        var last = DateTime.UnixEpoch.AddSeconds(1000);
        var now = DateTime.UnixEpoch.AddSeconds(1060);
        Assert.False(Watchdog.ShouldAutoRestore(last, now, TimeSpan.FromSeconds(90)));
    }

    [Fact]
    public void SafetyDisablesOnLowBattery() =>
        Assert.True(SafetyPolicy.ShouldDisableForBattery(new BatteryInfo(15, false), 20));

    [Fact]
    public void SafetyAllowsOnAc() =>
        Assert.False(SafetyPolicy.ShouldDisableForBattery(new BatteryInfo(5, true), 20));

    [Fact]
    public void SafetyAllowsAboveThreshold() =>
        Assert.False(SafetyPolicy.ShouldDisableForBattery(new BatteryInfo(80, false), 20));

    [Fact]
    public void AutoOffDeadlineIsStartPlusMinutes()
    {
        var start = DateTime.UnixEpoch.AddSeconds(1000);
        Assert.Equal(DateTime.UnixEpoch.AddSeconds(1000 + 1800), AutoOff.Deadline(start, 30));
    }

    [Fact]
    public void AutoOffRemainingClampsToZero()
    {
        var deadline = DateTime.UnixEpoch.AddSeconds(1000);
        var now = DateTime.UnixEpoch.AddSeconds(1100);
        Assert.Equal(TimeSpan.Zero, AutoOff.Remaining(deadline, now));
    }

    [Fact]
    public void AutoOffRemainingCountsDown()
    {
        var deadline = DateTime.UnixEpoch.AddSeconds(1100);
        var now = DateTime.UnixEpoch.AddSeconds(1040);
        Assert.Equal(TimeSpan.FromSeconds(60), AutoOff.Remaining(deadline, now));
    }

    [Fact]
    public void AutoOffExpiry()
    {
        var deadline = DateTime.UnixEpoch.AddSeconds(1000);
        Assert.True(AutoOff.IsExpired(deadline, deadline));
        Assert.True(AutoOff.IsExpired(deadline, DateTime.UnixEpoch.AddSeconds(1001)));
        Assert.False(AutoOff.IsExpired(deadline, DateTime.UnixEpoch.AddSeconds(999)));
    }

    [Fact]
    public void AutoOffCountdownFormatting()
    {
        Assert.Equal("0:30", AutoOff.FormatCountdown(TimeSpan.FromSeconds(30)));
        Assert.Equal("9:42", AutoOff.FormatCountdown(TimeSpan.FromSeconds(582)));
        Assert.Equal("1:05:09", AutoOff.FormatCountdown(TimeSpan.FromSeconds(3909)));
        Assert.Equal("0:00", AutoOff.FormatCountdown(TimeSpan.Zero));
    }

    [Fact]
    public void RequestEnablesKeepAwakeWhenItIsOff() =>
        Assert.Equal(AutoOff.Request.EnableThenArmTimer(15), AutoOff.For(15, false, false));

    [Fact]
    public void RequestJustArmsTimerWhenAlreadyOn() =>
        Assert.Equal(AutoOff.Request.ArmTimer(30), AutoOff.For(30, true, false));

    [Fact]
    public void RequestForNoLimitCancelsTimerWithoutDisabling()
    {
        Assert.Equal(AutoOff.Request.CancelTimer, AutoOff.For(0, true, false));
        Assert.Equal(AutoOff.Request.CancelTimer, AutoOff.For(0, false, false));
    }

    [Fact]
    public void RequestIsIgnoredInAutoModeWhateverElseIsTrue()
    {
        foreach (var minutes in new[] { 0, 15, 240 })
        {
            foreach (var enabled in new[] { true, false })
            {
                Assert.Equal(AutoOff.Request.IgnoredInAutoMode, AutoOff.For(minutes, enabled, true));
            }
        }
    }

    [Fact]
    public void RequestCoversEveryPreset()
    {
        foreach (var minutes in AutoOff.PresetMinutes)
        {
            Assert.Equal(AutoOff.Request.EnableThenArmTimer(minutes), AutoOff.For(minutes, false, false));
        }
    }

    [Fact]
    public void DurationLabelNamesTheNoLimitCase()
    {
        Assert.Equal("No limit", AutoOff.DurationLabel(0));
        Assert.Equal("15 min", AutoOff.DurationLabel(15));
        Assert.Equal("1 hour", AutoOff.DurationLabel(60));
    }

    [Fact]
    public void AutoOffOptionLabels()
    {
        Assert.Equal("15 min", AutoOff.OptionLabel(15));
        Assert.Equal("30 min", AutoOff.OptionLabel(30));
        Assert.Equal("1 hour", AutoOff.OptionLabel(60));
        Assert.Equal("2 hours", AutoOff.OptionLabel(120));
        Assert.Equal("4 hours", AutoOff.OptionLabel(240));
    }

    [Fact]
    public void OnboardingDefaultsToIncomplete()
    {
        var store = new SettingsStore(new MemoryKeyValueStore());
        Assert.False(store.LoadOnboardingComplete());
    }

    [Fact]
    public void OnboardingCompletePersists()
    {
        var store = new SettingsStore(new MemoryKeyValueStore());
        store.SaveOnboardingComplete(true);
        Assert.True(store.LoadOnboardingComplete());
    }
}
