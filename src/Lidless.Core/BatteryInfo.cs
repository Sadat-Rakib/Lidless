namespace Lidless;

/// <summary>One sample of battery charge and whether the machine is on AC power.</summary>
public readonly struct BatteryInfo : IEquatable<BatteryInfo>
{
    public BatteryInfo(int percent, bool onAc)
    {
        Percent = percent;
        OnAc = onAc;
    }

    public int Percent { get; }
    public bool OnAc { get; }
    public string Source => OnAc ? "AC" : "Battery";

    public bool Equals(BatteryInfo other) => Percent == other.Percent && OnAc == other.OnAc;
    public override bool Equals(object? obj) => obj is BatteryInfo other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Percent, OnAc);
    public static bool operator ==(BatteryInfo left, BatteryInfo right) => left.Equals(right);
    public static bool operator !=(BatteryInfo left, BatteryInfo right) => !left.Equals(right);
}
