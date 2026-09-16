namespace Lidless;

/// <summary>
/// A change to keep-awake made outside this app — powercfg in a terminal,
/// Windows Settings, the watchdog, or another Lidless instance.
/// </summary>
public enum ExternalChange
{
    EnabledOutside,
    DisabledOutside,
}

public static class ExternalChangeText
{
    public static bool NowEnabled(this ExternalChange change) => change == ExternalChange.EnabledOutside;

    /// <summary>Describes the event, not the resulting state.</summary>
    public static string Message(this ExternalChange change) => change switch
    {
        ExternalChange.EnabledOutside => "Sleep prevention was turned on outside Lidless.",
        ExternalChange.DisabledOutside => "Sleep prevention was turned off outside Lidless.",
        _ => throw new ArgumentOutOfRangeException(nameof(change)),
    };
}

/// <summary>Why keep-awake is being set.</summary>
public enum SetOrigin
{
    User,
    Safety,
    AutoOff,
    Auto,
}

/// <summary>A write whose read-back hasn't confirmed it yet.</summary>
public readonly struct PendingVerification : IEquatable<PendingVerification>
{
    public PendingVerification(bool target) => Target = target;
    public bool Target { get; }
    public bool Equals(PendingVerification other) => Target == other.Target;
    public override bool Equals(object? obj) => obj is PendingVerification other && Equals(other);
    public override int GetHashCode() => Target.GetHashCode();
}

/// <summary>Pure decision logic for keeping the UI in step with the real system flag.</summary>
public static class StateReconciler
{
    public abstract record Outcome
    {
        public static Outcome Unknown { get; } = new UnknownOutcome();
        public static Outcome InSync { get; } = new InSyncOutcome();
        public static Outcome Adopt(bool enabled) => new AdoptOutcome(enabled);
        public static Outcome Drift(ExternalChange change) => new DriftOutcome(change);

        public sealed record UnknownOutcome : Outcome;
        public sealed record InSyncOutcome : Outcome;
        public sealed record AdoptOutcome(bool Enabled) : Outcome;
        public sealed record DriftOutcome(ExternalChange Change) : Outcome;
    }

    public static Outcome Reconcile(bool shown, bool hasBaseline, bool? observed)
    {
        if (observed is null)
        {
            return Outcome.Unknown;
        }

        if (observed.Value == shown)
        {
            return Outcome.InSync;
        }

        return hasBaseline
            ? Outcome.Drift(observed.Value ? ExternalChange.EnabledOutside : ExternalChange.DisabledOutside)
            : Outcome.Adopt(observed.Value);
    }

    public static bool ClearsExternalNotice(SetOrigin origin) => origin == SetOrigin.User;

    public abstract record VerifyOutcome
    {
        public static VerifyOutcome Verified { get; } = new VerifiedOutcome();
        public static VerifyOutcome Unverified { get; } = new UnverifiedOutcome();
        public static VerifyOutcome Mismatch(bool actual) => new MismatchOutcome(actual);

        public sealed record VerifiedOutcome : VerifyOutcome;
        public sealed record UnverifiedOutcome : VerifyOutcome;
        public sealed record MismatchOutcome(bool Actual) : VerifyOutcome;
    }

    public static VerifyOutcome VerifyAfterSet(bool target, bool? observed)
    {
        if (observed is null)
        {
            return VerifyOutcome.Unverified;
        }

        return observed.Value == target ? VerifyOutcome.Verified : VerifyOutcome.Mismatch(observed.Value);
    }

    public abstract record PendingResolution
    {
        public static PendingResolution StillUnverified { get; } = new StillUnverifiedOutcome();
        public static PendingResolution Confirmed { get; } = new ConfirmedOutcome();
        public static PendingResolution WriteMismatch(bool actual) => new WriteMismatchOutcome(actual);

        public sealed record StillUnverifiedOutcome : PendingResolution;
        public sealed record ConfirmedOutcome : PendingResolution;
        public sealed record WriteMismatchOutcome(bool Actual) : PendingResolution;
    }

    public static PendingResolution Resolve(PendingVerification pending, bool? observed)
    {
        if (observed is null)
        {
            return PendingResolution.StillUnverified;
        }

        return observed.Value == pending.Target
            ? PendingResolution.Confirmed
            : PendingResolution.WriteMismatch(observed.Value);
    }

    public static string UnverifiedMessage(bool target) =>
        target
            ? "Couldn\u2019t confirm keep-awake with the system \u2014 it may not hold when you close the lid."
            : "Couldn\u2019t confirm sleep was restored \u2014 your PC may still stay awake.";

    public static string WriteMismatchMessage(bool actual) =>
        actual
            ? "The change didn\u2019t hold \u2014 the system reports keep-awake is on."
            : "The change didn\u2019t hold \u2014 the system reports keep-awake is off.";
}

/// <summary>Decides which async replies are still worth applying, using monotonic counters.</summary>
public struct StateSync : IEquatable<StateSync>
{
    public readonly struct ReadToken : IEquatable<ReadToken>
    {
        internal ReadToken(ulong read, ulong mutations)
        {
            Read = read;
            Mutations = mutations;
        }

        internal ulong Read { get; }
        internal ulong Mutations { get; }
        public bool Equals(ReadToken other) => Read == other.Read && Mutations == other.Mutations;
        public override bool Equals(object? obj) => obj is ReadToken other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Read, Mutations);
    }

    public readonly struct MutationToken : IEquatable<MutationToken>
    {
        internal MutationToken(ulong mutation) => Mutation = mutation;
        internal ulong Mutation { get; }
        public bool Equals(MutationToken other) => Mutation == other.Mutation;
        public override bool Equals(object? obj) => obj is MutationToken other && Equals(other);
        public override int GetHashCode() => Mutation.GetHashCode();
    }

    private ulong _reads;
    private ulong _mutations;

    public ReadToken BeginRead()
    {
        _reads++;
        return new ReadToken(_reads, _mutations);
    }

    public MutationToken BeginMutation()
    {
        _mutations++;
        return new MutationToken(_mutations);
    }

    public readonly bool ShouldApply(ReadToken token) =>
        token.Read == _reads && token.Mutations == _mutations;

    public readonly bool ShouldApply(MutationToken token) => token.Mutation == _mutations;

    public readonly bool Equals(StateSync other) => _reads == other._reads && _mutations == other._mutations;
    public override readonly bool Equals(object? obj) => obj is StateSync other && Equals(other);
    public override readonly int GetHashCode() => HashCode.Combine(_reads, _mutations);
}
