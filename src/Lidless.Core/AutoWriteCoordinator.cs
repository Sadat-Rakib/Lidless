namespace Lidless;

/// <summary>
/// Tracks the one system write auto mode is allowed to have outstanding.
/// The claim spans the whole write — issued until resolved — so a reply that
/// reads back wrong cannot immediately provoke another write.
/// </summary>
public struct AutoWriteCoordinator : IEquatable<AutoWriteCoordinator>
{
    public abstract record State
    {
        public static State Idle { get; } = new IdleState();
        public static State InFlight(bool target) => new InFlightState(target);
        public static State Deferred { get; } = new DeferredState();

        public sealed record IdleState : State;
        public sealed record InFlightState(bool Target) : State;
        public sealed record DeferredState : State;
    }

    public AutoWriteCoordinator() => Current = State.Idle;

    public AutoWriteCoordinator(State state) => Current = state;

    public State Current { get; private set; }

    public readonly bool MayWrite => Current is State.IdleState;

    public readonly bool? InFlightTarget => Current is State.InFlightState flight ? flight.Target : null;

    public void Issued(bool target) => Current = State.InFlight(target);

    public void Resolved()
    {
        if (Current is State.InFlightState)
        {
            Current = State.Deferred;
        }
    }

    public void AdvanceTick()
    {
        Current = Current switch
        {
            State.IdleState => Current,
            State.InFlightState => State.Deferred,
            State.DeferredState => State.Idle,
            _ => Current,
        };
    }

    public void Clear() => Current = State.Idle;

    public readonly bool Equals(AutoWriteCoordinator other) => Equals(Current, other.Current);
    public override readonly bool Equals(object? obj) => obj is AutoWriteCoordinator other && Equals(other);
    public override readonly int GetHashCode() => Current.GetHashCode();
}
