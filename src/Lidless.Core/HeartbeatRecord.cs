using System.Buffers.Binary;

namespace Lidless;

/// <summary>
/// Shared heartbeat payload between the tray app and the watchdog process.
/// Stored in a named memory-mapped file so a crash of the UI still leaves
/// enough information to restore the previous lid-close policy.
/// </summary>
public struct HeartbeatRecord : IEquatable<HeartbeatRecord>
{
    public const uint MagicValue = 0x4C49444C; // "LIDL"
    public const int Size = 40;

    public uint Magic;
    public int ParentProcessId;
    public long LastHeartbeatUnixMs;
    public int AcLidAction;
    public int DcLidAction;
    public int RestoreArmed;
    public int Active;
    public int ShutdownRequested;

    public LidPolicy OriginalLidPolicy =>
        new((LidAction)AcLidAction, (LidAction)DcLidAction);

    public DateTime LastHeartbeatUtc =>
        DateTimeOffset.FromUnixTimeMilliseconds(LastHeartbeatUnixMs).UtcDateTime;

    public byte[] ToBytes()
    {
        var buffer = new byte[Size];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, 4), Magic);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4, 4), ParentProcessId);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(8, 8), LastHeartbeatUnixMs);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(16, 4), AcLidAction);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(20, 4), DcLidAction);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(24, 4), RestoreArmed);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(28, 4), Active);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(32, 4), ShutdownRequested);
        return buffer;
    }

    public static bool TryRead(ReadOnlySpan<byte> data, out HeartbeatRecord record)
    {
        record = default;
        if (data.Length < Size)
        {
            return false;
        }

        record = new HeartbeatRecord
        {
            Magic = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]),
            ParentProcessId = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(4, 4)),
            LastHeartbeatUnixMs = BinaryPrimitives.ReadInt64LittleEndian(data.Slice(8, 8)),
            AcLidAction = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(16, 4)),
            DcLidAction = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(20, 4)),
            RestoreArmed = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(24, 4)),
            Active = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(28, 4)),
            ShutdownRequested = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(32, 4)),
        };
        return record.Magic == MagicValue;
    }

    /// <summary>
    /// Watchdog decision: restore the previous lid policy when keep-awake is
    /// armed and either the parent is gone or the heartbeat has gone quiet.
    /// A clean shutdown flag means the app already restored (or is restoring).
    /// </summary>
    public static bool ShouldRestore(
        HeartbeatRecord record,
        bool parentAlive,
        DateTime utcNow,
        TimeSpan timeout)
    {
        if (record.Magic != MagicValue || record.ShutdownRequested != 0 || record.RestoreArmed == 0)
        {
            return false;
        }

        if (!parentAlive)
        {
            return true;
        }

        return Watchdog.ShouldAutoRestore(record.LastHeartbeatUtc, utcNow, timeout);
    }

    public readonly bool Equals(HeartbeatRecord other) =>
        Magic == other.Magic
        && ParentProcessId == other.ParentProcessId
        && LastHeartbeatUnixMs == other.LastHeartbeatUnixMs
        && AcLidAction == other.AcLidAction
        && DcLidAction == other.DcLidAction
        && RestoreArmed == other.RestoreArmed
        && Active == other.Active
        && ShutdownRequested == other.ShutdownRequested;

    public override readonly bool Equals(object? obj) => obj is HeartbeatRecord other && Equals(other);
    public override readonly int GetHashCode() =>
        HashCode.Combine(Magic, ParentProcessId, LastHeartbeatUnixMs, AcLidAction, DcLidAction, RestoreArmed, Active, ShutdownRequested);
}
