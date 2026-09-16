using Lidless;

namespace Lidless.Tests;

public sealed class StateReconcilerTests
{
    [Fact]
    public void ExternalEnableIsAdoptedAsDrift()
    {
        Assert.Equal(
            StateReconciler.Outcome.Drift(ExternalChange.EnabledOutside),
            StateReconciler.Reconcile(false, true, true));
        Assert.True(ExternalChange.EnabledOutside.NowEnabled());
    }

    [Fact]
    public void ExternalDisableInvalidatesStaleEnabledState()
    {
        Assert.Equal(
            StateReconciler.Outcome.Drift(ExternalChange.DisabledOutside),
            StateReconciler.Reconcile(true, true, false));
        Assert.False(ExternalChange.DisabledOutside.NowEnabled());
    }

    [Fact]
    public void UnknownReadKeepsShownEnabledState() =>
        Assert.Equal(StateReconciler.Outcome.Unknown, StateReconciler.Reconcile(true, true, null));

    [Fact]
    public void UnknownReadKeepsShownDisabledState() =>
        Assert.Equal(StateReconciler.Outcome.Unknown, StateReconciler.Reconcile(false, true, null));

    [Fact]
    public void AgreeingReadIsInSyncEvenWithoutBaseline()
    {
        Assert.Equal(StateReconciler.Outcome.InSync, StateReconciler.Reconcile(true, true, true));
        Assert.Equal(StateReconciler.Outcome.InSync, StateReconciler.Reconcile(false, true, false));
        Assert.Equal(StateReconciler.Outcome.InSync, StateReconciler.Reconcile(false, false, false));
    }

    [Fact]
    public void FirstDifferingObservationAdoptsWithoutWarning() =>
        Assert.Equal(StateReconciler.Outcome.Adopt(true), StateReconciler.Reconcile(false, false, true));

    [Fact]
    public void FirstObservationUnknownStaysUnknown() =>
        Assert.Equal(StateReconciler.Outcome.Unknown, StateReconciler.Reconcile(false, false, null));

    [Fact]
    public void UnreadableLidPolicyReconcilesToUnknown()
    {
        foreach (var output in new[] { "", "Power Scheme GUID: abc", "Current AC Power Setting Index: yes" })
        {
            var observed = LidPolicyParser.IsLidDoNothing(output);
            Assert.Null(observed);
            Assert.Equal(StateReconciler.Outcome.Unknown, StateReconciler.Reconcile(true, true, observed));
        }
    }

    [Fact]
    public void FreshReadApplies()
    {
        var sync = new StateSync();
        var token = sync.BeginRead();
        Assert.True(sync.ShouldApply(token));
    }

    [Fact]
    public void NewerReadInvalidatesTheOlderReply()
    {
        var sync = new StateSync();
        var first = sync.BeginRead();
        var second = sync.BeginRead();
        Assert.False(sync.ShouldApply(first));
        Assert.True(sync.ShouldApply(second));
    }

    [Fact]
    public void MutationInvalidatesAnInFlightRead()
    {
        var sync = new StateSync();
        var read = sync.BeginRead();
        sync.BeginMutation();
        Assert.False(sync.ShouldApply(read));
    }

    [Fact]
    public void AbaRoundTripStillInvalidatesTheRead()
    {
        var sync = new StateSync();
        var read = sync.BeginRead();
        sync.BeginMutation();
        sync.BeginMutation();
        Assert.False(sync.ShouldApply(read));
    }

    [Fact]
    public void StaleWriteCompletionIsRejected()
    {
        var sync = new StateSync();
        var first = sync.BeginMutation();
        var second = sync.BeginMutation();
        Assert.False(sync.ShouldApply(first));
        Assert.True(sync.ShouldApply(second));
    }

    [Fact]
    public void OutOfOrderWriteRepliesApplyOnlyTheLatest()
    {
        var sync = new StateSync();
        var older = sync.BeginMutation();
        var newer = sync.BeginMutation();
        Assert.True(sync.ShouldApply(newer));
        Assert.False(sync.ShouldApply(older));
    }

    [Fact]
    public void DuplicateTargetWritesStillInvalidateTheOlder()
    {
        var sync = new StateSync();
        var first = sync.BeginMutation();
        var second = sync.BeginMutation();
        Assert.NotEqual(first, second);
        Assert.False(sync.ShouldApply(first));
    }

    [Fact]
    public void OnlyUserActionClearsTheExternalNotice()
    {
        Assert.True(StateReconciler.ClearsExternalNotice(SetOrigin.User));
        Assert.False(StateReconciler.ClearsExternalNotice(SetOrigin.Safety));
        Assert.False(StateReconciler.ClearsExternalNotice(SetOrigin.AutoOff));
    }

    [Fact]
    public void DriftMessagesDescribeTheEventNotTheResultingState()
    {
        foreach (var change in new[] { ExternalChange.EnabledOutside, ExternalChange.DisabledOutside })
        {
            Assert.Contains("outside Lidless", change.Message());
            Assert.DoesNotContain("will", change.Message());
        }
    }

    [Fact]
    public void VerifiedWhenReadBackMatchesTarget()
    {
        Assert.Equal(StateReconciler.VerifyOutcome.Verified, StateReconciler.VerifyAfterSet(true, true));
        Assert.Equal(StateReconciler.VerifyOutcome.Verified, StateReconciler.VerifyAfterSet(false, false));
    }

    [Fact]
    public void UnknownReadBackIsUnverifiedNotFailure()
    {
        Assert.Equal(StateReconciler.VerifyOutcome.Unverified, StateReconciler.VerifyAfterSet(true, null));
        Assert.Equal(StateReconciler.VerifyOutcome.Unverified, StateReconciler.VerifyAfterSet(false, null));
    }

    [Fact]
    public void MismatchedReadBackReportsActualState()
    {
        Assert.Equal(StateReconciler.VerifyOutcome.Mismatch(false), StateReconciler.VerifyAfterSet(true, false));
        Assert.Equal(StateReconciler.VerifyOutcome.Mismatch(true), StateReconciler.VerifyAfterSet(false, true));
    }

    [Fact]
    public void UnverifiedMessageDoesNotClaimSuccess()
    {
        Assert.Contains("Couldn\u2019t confirm", StateReconciler.UnverifiedMessage(true));
        Assert.Contains("Couldn\u2019t confirm", StateReconciler.UnverifiedMessage(false));
        Assert.NotEqual(StateReconciler.UnverifiedMessage(true), StateReconciler.UnverifiedMessage(false));
    }

    [Fact]
    public void PendingConfirmsOnMatchingFreshRead() =>
        Assert.Equal(
            StateReconciler.PendingResolution.Confirmed,
            StateReconciler.Resolve(new PendingVerification(true), true));

    [Fact]
    public void PendingResolvesToWriteMismatchNotExternalDrift()
    {
        Assert.Equal(
            StateReconciler.PendingResolution.WriteMismatch(false),
            StateReconciler.Resolve(new PendingVerification(true), false));
        Assert.Equal(
            StateReconciler.PendingResolution.WriteMismatch(true),
            StateReconciler.Resolve(new PendingVerification(false), true));
    }

    [Fact]
    public void PendingStaysUnverifiedWhenTheReadFailsAgain() =>
        Assert.Equal(
            StateReconciler.PendingResolution.StillUnverified,
            StateReconciler.Resolve(new PendingVerification(true), null));

    [Fact]
    public void VerificationMessagesAreDistinctFromExternalChangeMessages()
    {
        var external = new[]
        {
            ExternalChange.EnabledOutside.Message(),
            ExternalChange.DisabledOutside.Message(),
        };
        var verification = new[]
        {
            StateReconciler.UnverifiedMessage(true),
            StateReconciler.UnverifiedMessage(false),
            StateReconciler.WriteMismatchMessage(true),
            StateReconciler.WriteMismatchMessage(false),
        };

        foreach (var message in verification)
        {
            Assert.DoesNotContain(message, external);
            Assert.DoesNotContain("outside Lidless", message);
        }
    }
}
