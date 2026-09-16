using Lidless;

namespace Lidless.Tests;

public sealed class AutoWriteCoordinatorTests
{
    [Fact]
    public void InFlightWriteBlocksASecondWrite()
    {
        var c = new AutoWriteCoordinator();
        Assert.True(c.MayWrite);
        c.Issued(true);
        Assert.False(c.MayWrite);
        Assert.True(c.InFlightTarget);
    }

    [Fact]
    public void ClaimSurvivesRepeatedObservationsWithinTheTurn()
    {
        var c = new AutoWriteCoordinator();
        c.Issued(true);
        for (var i = 0; i < 5; i++)
        {
            Assert.False(c.MayWrite);
        }

        Assert.Equal(AutoWriteCoordinator.State.InFlight(true), c.Current);
    }

    [Fact]
    public void MismatchDoesNotRetryInTheSameTurn()
    {
        var c = new AutoWriteCoordinator();
        c.Issued(true);
        c.Resolved();
        Assert.Equal(AutoWriteCoordinator.State.Deferred, c.Current);
        Assert.False(c.MayWrite);
    }

    [Fact]
    public void RepeatedResolutionCannotReopenWriting()
    {
        var c = new AutoWriteCoordinator();
        c.Issued(true);
        c.Resolved();
        c.Resolved();
        Assert.Equal(AutoWriteCoordinator.State.Deferred, c.Current);
        Assert.False(c.MayWrite);
    }

    [Fact]
    public void ResolutionWithoutAClaimIsIgnored()
    {
        var c = new AutoWriteCoordinator();
        c.Issued(true);
        c.Clear();
        c.Resolved();
        Assert.True(c.MayWrite);
    }

    [Fact]
    public void TickImmediatelyAfterIssueBlocksASecondWrite()
    {
        var c = new AutoWriteCoordinator();
        c.Issued(true);
        c.AdvanceTick();
        Assert.False(c.MayWrite);
    }

    [Fact]
    public void LostInFlightRecoversOnlyOnTheFollowingTick()
    {
        var c = new AutoWriteCoordinator();
        c.Issued(true);
        c.AdvanceTick();
        Assert.False(c.MayWrite);
        Assert.Equal(AutoWriteCoordinator.State.Deferred, c.Current);
        c.AdvanceTick();
        Assert.True(c.MayWrite);
    }

    [Fact]
    public void LateResolutionAfterATickTransitionSettlesCorrectly()
    {
        var c = new AutoWriteCoordinator();
        c.Issued(true);
        c.AdvanceTick();
        c.Resolved();
        Assert.Equal(AutoWriteCoordinator.State.Deferred, c.Current);
        Assert.False(c.MayWrite);
        c.AdvanceTick();
        Assert.True(c.MayWrite);
    }

    [Fact]
    public void LateResolutionAfterAReopeningTickDoesNotReclose()
    {
        var c = new AutoWriteCoordinator();
        c.Issued(true);
        c.Resolved();
        c.AdvanceTick();
        Assert.True(c.MayWrite);
        c.Resolved();
        Assert.True(c.MayWrite);
    }

    [Fact]
    public void UnverifiedWriteRetriesOnTheNextTick()
    {
        var c = new AutoWriteCoordinator();
        c.Issued(true);
        c.Resolved();
        Assert.False(c.MayWrite);
        c.AdvanceTick();
        Assert.True(c.MayWrite);
    }

    [Fact]
    public void FailedWriteDefersUntilTheNextTick()
    {
        var c = new AutoWriteCoordinator();
        c.Issued(false);
        c.Resolved();
        Assert.False(c.MayWrite);
        c.AdvanceTick();
        Assert.True(c.MayWrite);
    }

    [Fact]
    public void NoStateSurvivesTwoTicks()
    {
        foreach (var state in new AutoWriteCoordinator.State[]
                 {
                     AutoWriteCoordinator.State.Idle,
                     AutoWriteCoordinator.State.InFlight(true),
                     AutoWriteCoordinator.State.InFlight(false),
                     AutoWriteCoordinator.State.Deferred,
                 })
        {
            var c = new AutoWriteCoordinator(state);
            c.AdvanceTick();
            c.AdvanceTick();
            Assert.True(c.MayWrite);
        }
    }

    [Fact]
    public void TickTransitions()
    {
        var idle = new AutoWriteCoordinator(AutoWriteCoordinator.State.Idle);
        idle.AdvanceTick();
        Assert.Equal(AutoWriteCoordinator.State.Idle, idle.Current);

        var inFlight = new AutoWriteCoordinator(AutoWriteCoordinator.State.InFlight(true));
        inFlight.AdvanceTick();
        Assert.Equal(AutoWriteCoordinator.State.Deferred, inFlight.Current);

        var deferred = new AutoWriteCoordinator(AutoWriteCoordinator.State.Deferred);
        deferred.AdvanceTick();
        Assert.Equal(AutoWriteCoordinator.State.Idle, deferred.Current);
    }

    [Fact]
    public void UserWriteSupersedesAnInFlightAutoWrite()
    {
        var c = new AutoWriteCoordinator();
        c.Issued(true);
        c.Clear();
        Assert.True(c.MayWrite);
        Assert.Null(c.InFlightTarget);
    }

    [Fact]
    public void SupersededAutoReplyDoesNotDisturbTheUserWrite()
    {
        var c = new AutoWriteCoordinator();
        c.Issued(true);
        c.Clear();
        c.Resolved();
        Assert.True(c.MayWrite);
        Assert.Equal(AutoWriteCoordinator.State.Idle, c.Current);
    }
}
