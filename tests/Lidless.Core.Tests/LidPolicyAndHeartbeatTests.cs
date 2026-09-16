using Lidless;

namespace Lidless.Tests;

public sealed class LidPolicyParserTests
{
    private const string SampleQuery = """
        Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)
          Subgroup GUID: 4f971e89-eebd-4455-a8de-9e59040e7347  (Power buttons and lid)
            Power Setting GUID: 5ca83367-6e45-459f-a27b-476b1d01c936  (Lid close action)
              Possible Setting Index: 000
              Possible Setting Friendly Name: Do nothing
              Possible Setting Index: 001
              Possible Setting Friendly Name: Sleep
            Current AC Power Setting Index: 0x00000000
            Current DC Power Setting Index: 0x00000001
        """;

    [Fact]
    public void ParsesAcDoNothingAndDcSleep()
    {
        var policy = LidPolicyParser.Parse(SampleQuery);
        Assert.NotNull(policy);
        Assert.Equal(LidAction.DoNothing, policy.Value.Ac);
        Assert.Equal(LidAction.Sleep, policy.Value.Dc);
        Assert.False(policy.Value.IsDoNothing);
    }

    [Fact]
    public void DecimalIndicesAreAccepted()
    {
        var output = "Current AC Power Setting Index: 0\nCurrent DC Power Setting Index: 0";
        var policy = LidPolicyParser.Parse(output);
        Assert.NotNull(policy);
        Assert.True(policy.Value.IsDoNothing);
        Assert.True(LidPolicyParser.IsLidDoNothing(output));
    }

    [Fact]
    public void MissingIndicesAreUnknownNotOff()
    {
        Assert.Null(LidPolicyParser.Parse("Power Scheme GUID: abc"));
        Assert.Null(LidPolicyParser.IsLidDoNothing(""));
        Assert.Null(LidPolicyParser.IsLidDoNothing("Current AC Power Setting Index: 0"));
    }

    [Fact]
    public void ParseActiveSchemeGuid()
    {
        var output = "Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)";
        Assert.Equal("381b4222-f694-41f0-9685-ff5bb260df2e", LidPolicyParser.ParseActiveSchemeGuid(output));
    }

    [Fact]
    public void DescribeLidActions()
    {
        Assert.Equal("Do nothing", LidPolicy.Describe(LidAction.DoNothing));
        Assert.Equal("Sleep", LidPolicy.Describe(LidAction.Sleep));
        Assert.Equal("Hibernate", LidPolicy.Describe(LidAction.Hibernate));
        Assert.Equal("Shut down", LidPolicy.Describe(LidAction.ShutDown));
    }
}

public sealed class HeartbeatRecordTests
{
    [Fact]
    public void RoundTripsBytes()
    {
        var original = new HeartbeatRecord
        {
            Magic = HeartbeatRecord.MagicValue,
            ParentProcessId = 4242,
            LastHeartbeatUnixMs = 1_700_000_000_000,
            AcLidAction = (int)LidAction.Sleep,
            DcLidAction = (int)LidAction.Hibernate,
            RestoreArmed = 1,
            Active = 1,
            ShutdownRequested = 0,
        };

        Assert.True(HeartbeatRecord.TryRead(original.ToBytes(), out var parsed));
        Assert.Equal(original, parsed);
        Assert.Equal(new LidPolicy(LidAction.Sleep, LidAction.Hibernate), parsed.OriginalLidPolicy);
    }

    [Fact]
    public void RejectsBadMagic()
    {
        var bytes = new HeartbeatRecord { Magic = 1 }.ToBytes();
        Assert.False(HeartbeatRecord.TryRead(bytes, out _));
    }

    [Fact]
    public void RestoresWhenParentIsDead()
    {
        var record = LiveRecord();
        Assert.True(HeartbeatRecord.ShouldRestore(record, parentAlive: false, DateTime.UtcNow, Watchdog.DefaultTimeout));
    }

    [Fact]
    public void RestoresWhenHeartbeatTimesOut()
    {
        var record = LiveRecord();
        record.LastHeartbeatUnixMs = DateTimeOffset.UtcNow.AddSeconds(-120).ToUnixTimeMilliseconds();
        Assert.True(HeartbeatRecord.ShouldRestore(record, parentAlive: true, DateTime.UtcNow, Watchdog.DefaultTimeout));
    }

    [Fact]
    public void DoesNotRestoreWhenHeartbeatIsFresh()
    {
        var record = LiveRecord();
        Assert.False(HeartbeatRecord.ShouldRestore(record, parentAlive: true, DateTime.UtcNow, Watchdog.DefaultTimeout));
    }

    [Fact]
    public void CleanShutdownDoesNotRestore()
    {
        var record = LiveRecord();
        record.ShutdownRequested = 1;
        Assert.False(HeartbeatRecord.ShouldRestore(record, parentAlive: false, DateTime.UtcNow, Watchdog.DefaultTimeout));
    }

    [Fact]
    public void UnarmedSnapshotDoesNotRestore()
    {
        var record = LiveRecord();
        record.RestoreArmed = 0;
        Assert.False(HeartbeatRecord.ShouldRestore(record, parentAlive: false, DateTime.UtcNow, Watchdog.DefaultTimeout));
    }

    private static HeartbeatRecord LiveRecord() => new()
    {
        Magic = HeartbeatRecord.MagicValue,
        ParentProcessId = 7,
        LastHeartbeatUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        AcLidAction = 1,
        DcLidAction = 1,
        RestoreArmed = 1,
        Active = 1,
        ShutdownRequested = 0,
    };
}
