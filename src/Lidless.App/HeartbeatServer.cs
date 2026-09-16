using System.IO.MemoryMappedFiles;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lidless.App;

internal sealed class SessionState
{
    [JsonPropertyName("ac")] public int Ac { get; set; }
    [JsonPropertyName("dc")] public int Dc { get; set; }
    [JsonPropertyName("restoreArmed")] public bool RestoreArmed { get; set; }
}

/// <summary>
/// Persists the original lid-close policy so a reboot after a crash can still restore it.
/// Windows lid actions survive reboot; macOS SleepDisabled does not.
/// </summary>
internal static class SessionStore
{
    public static string DirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lidless");

    public static string FilePath => Path.Combine(DirectoryPath, "session.json");

    public static void Save(LidPolicy original, bool restoreArmed)
    {
        Directory.CreateDirectory(DirectoryPath);
        var json = JsonSerializer.Serialize(new SessionState
        {
            Ac = (int)original.Ac,
            Dc = (int)original.Dc,
            RestoreArmed = restoreArmed,
        });
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, FilePath, overwrite: true);
    }

    public static SessionState? Load()
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SessionState>(File.ReadAllText(FilePath));
        }
        catch
        {
            return null;
        }
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }
        }
        catch
        {
            // Non-fatal: next launch will try again.
        }
    }
}

internal sealed class HeartbeatServer : IDisposable
{
    public const string MapName = "Local\\Lidless.Heartbeat";

    private readonly MemoryMappedFile _file;
    private readonly MemoryMappedViewAccessor _view;

    private HeartbeatServer(MemoryMappedFile file, MemoryMappedViewAccessor view)
    {
        _file = file;
        _view = view;
    }

    public static HeartbeatServer Create()
    {
        var file = MemoryMappedFile.CreateOrOpen(MapName, HeartbeatRecord.Size);
        return new HeartbeatServer(file, file.CreateViewAccessor());
    }

    public static HeartbeatServer Open()
    {
        var file = MemoryMappedFile.OpenExisting(MapName);
        return new HeartbeatServer(file, file.CreateViewAccessor());
    }

    public void Write(HeartbeatRecord record)
    {
        var bytes = record.ToBytes();
        _view.WriteArray(0, bytes, 0, bytes.Length);
        _view.Flush();
    }

    public bool TryRead(out HeartbeatRecord record)
    {
        var bytes = new byte[HeartbeatRecord.Size];
        _view.ReadArray(0, bytes, 0, bytes.Length);
        return HeartbeatRecord.TryRead(bytes, out record);
    }

    public void Dispose()
    {
        _view.Dispose();
        _file.Dispose();
    }
}
