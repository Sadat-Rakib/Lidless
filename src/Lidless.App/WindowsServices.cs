using System.IO;
using System.Management;

namespace Lidless.App;

internal sealed class WindowsBatterySource : IBatterySource
{
    public BatteryInfo Read()
    {
        if (!NativeMethods.GetSystemPowerStatus(out var status))
        {
            return new BatteryInfo(100, true);
        }

        var onAc = status.ACLineStatus != 0; // 0 = offline; 1 = online; 255 = unknown (treat as AC)
        var noBattery = (status.BatteryFlag & 128) != 0;
        var percent = status.BatteryLifePercent;
        if (noBattery || percent > 100)
        {
            return new BatteryInfo(100, true);
        }

        return new BatteryInfo(percent, onAc);
    }
}

/// <summary>
/// Reads ACPI thermal zone temperatures via WMI. Missing sensors are treated as
/// "not serious" so we never false-pause keep-awake on desktops without zones.
/// Serious means any zone at or above 90 °C.
/// </summary>
internal sealed class WmiThermalSource : IThermalSource
{
    public const double SeriousCelsius = 90;

    public bool IsThermalSerious()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\wmi",
                "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            foreach (var obj in searcher.Get())
            {
                using (obj)
                {
                    var tenthsKelvin = Convert.ToDouble(obj["CurrentTemperature"]);
                    var celsius = tenthsKelvin / 10.0 - 273.15;
                    if (celsius >= SeriousCelsius)
                    {
                        return true;
                    }
                }
            }
        }
        catch (ManagementException)
        {
            // Zone not present, access denied, or driver stub — not serious.
        }
        catch (Exception)
        {
            // WMI unavailable.
        }

        return false;
    }
}

internal sealed class LoginItemService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Lidless";

    public bool IsEnabled
    {
        get
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string;
        }
    }

    public string? SetEnabled(bool enabled)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                var path = Environment.ProcessPath
                           ?? throw new InvalidOperationException("Cannot locate Lidless.exe.");
                key.SetValue(ValueName, $"\"{path}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}

internal sealed class JsonFileKeyValueStore : IKeyValueStore
{
    private readonly string _path;
    private readonly Dictionary<string, object?> _values;
    private readonly object _gate = new();

    public JsonFileKeyValueStore(string path)
    {
        _path = path;
        _values = Load(path);
    }

    public bool GetBoolean(string key, bool defaultValue = false)
    {
        lock (_gate)
        {
            return _values.TryGetValue(key, out var value) ? ToBool(value, defaultValue) : defaultValue;
        }
    }

    public int GetInt32(string key, int defaultValue = 0)
    {
        lock (_gate)
        {
            return _values.TryGetValue(key, out var value) ? ToInt(value, defaultValue) : defaultValue;
        }
    }

    public string GetString(string key, string defaultValue = "")
    {
        lock (_gate)
        {
            return _values.TryGetValue(key, out var value) && value is string text ? text : defaultValue;
        }
    }

    public void SetBoolean(string key, bool value) => Set(key, value);
    public void SetInt32(string key, int value) => Set(key, value);
    public void SetString(string key, string value) => Set(key, value);

    private void Set(string key, object value)
    {
        lock (_gate)
        {
            _values[key] = value;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var json = System.Text.Json.JsonSerializer.Serialize(_values);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _path, overwrite: true);
        }
    }

    private static Dictionary<string, object?> Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new Dictionary<string, object?>(StringComparer.Ordinal);
            }

            var raw = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(
                File.ReadAllText(path));
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (raw is null)
            {
                return result;
            }

            foreach (var (key, element) in raw)
            {
                result[key] = element.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.True => true,
                    System.Text.Json.JsonValueKind.False => false,
                    System.Text.Json.JsonValueKind.Number => element.TryGetInt32(out var n) ? n : element.GetDouble(),
                    System.Text.Json.JsonValueKind.String => element.GetString(),
                    _ => null,
                };
            }

            return result;
        }
        catch
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }
    }

    private static bool ToBool(object? value, bool fallback) => value switch
    {
        bool flag => flag,
        _ => fallback,
    };

    private static int ToInt(object? value, int fallback) => value switch
    {
        int n => n,
        long n => (int)n,
        double d => (int)d,
        _ => fallback,
    };
}
