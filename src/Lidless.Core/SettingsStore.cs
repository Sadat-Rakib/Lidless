namespace Lidless;

/// <summary>Minimal key/value persistence used by <see cref="SettingsStore"/>.</summary>
public interface IKeyValueStore
{
    bool GetBoolean(string key, bool defaultValue = false);
    int GetInt32(string key, int defaultValue = 0);
    string GetString(string key, string defaultValue = "");
    void SetBoolean(string key, bool value);
    void SetInt32(string key, int value);
    void SetString(string key, string value);
}

/// <summary>In-memory store for tests and first-run defaults.</summary>
public sealed class MemoryKeyValueStore : IKeyValueStore
{
    private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);

    public bool GetBoolean(string key, bool defaultValue = false) =>
        _values.TryGetValue(key, out var value) && value is bool flag ? flag : defaultValue;

    public int GetInt32(string key, int defaultValue = 0) =>
        _values.TryGetValue(key, out var value) && value is int number ? number : defaultValue;

    public string GetString(string key, string defaultValue = "") =>
        _values.TryGetValue(key, out var value) && value is string text ? text : defaultValue;

    public void SetBoolean(string key, bool value) => _values[key] = value;
    public void SetInt32(string key, int value) => _values[key] = value;
    public void SetString(string key, string value) => _values[key] = value;
}

/// <summary>
/// Persists safety settings. Returns <see cref="SafetySettings.Default"/> until
/// the user has saved at least once (so first launch uses sane defaults, not zeros).
/// </summary>
public sealed class SettingsStore
{
    public static class Key
    {
        public const string LowBattery = "lowBatteryThreshold";
        public const string OnlyCharging = "onlyWhileCharging";
        public const string PauseThermal = "pauseOnHighThermal";
        public const string AutoEnable = "autoEnableWhenCharging";
        public const string Armed = "keepAwakeArmed";
        public const string Seeded = "settingsSeeded";
        public const string AutoOff = "autoOffMinutes";
        public const string Onboarded = "onboardingComplete";
    }

    private readonly IKeyValueStore _store;

    public SettingsStore(IKeyValueStore store) => _store = store;

    public SafetySettings Load()
    {
        if (!_store.GetBoolean(Key.Seeded))
        {
            return SafetySettings.Default.Clone();
        }

        return new SafetySettings
        {
            LowBatteryThreshold = _store.GetInt32(Key.LowBattery, SafetySettings.Default.LowBatteryThreshold),
            OnlyWhileCharging = _store.GetBoolean(Key.OnlyCharging),
            PauseOnHighThermal = _store.GetBoolean(Key.PauseThermal, true),
            AutoEnableWhenCharging = _store.GetBoolean(Key.AutoEnable),
        };
    }

    public void Save(SafetySettings settings)
    {
        _store.SetInt32(Key.LowBattery, settings.LowBatteryThreshold);
        _store.SetBoolean(Key.OnlyCharging, settings.OnlyWhileCharging);
        _store.SetBoolean(Key.PauseThermal, settings.PauseOnHighThermal);
        _store.SetBoolean(Key.AutoEnable, settings.AutoEnableWhenCharging);
        _store.SetBoolean(Key.Seeded, true);
    }

    public bool LoadArmed() => _store.GetBoolean(Key.Armed);
    public void SaveArmed(bool armed) => _store.SetBoolean(Key.Armed, armed);

    public int LoadAutoOffMinutes() => _store.GetInt32(Key.AutoOff);
    public void SaveAutoOffMinutes(int minutes) => _store.SetInt32(Key.AutoOff, minutes);

    public bool LoadOnboardingComplete() => _store.GetBoolean(Key.Onboarded);
    public void SaveOnboardingComplete(bool complete) => _store.SetBoolean(Key.Onboarded, complete);
}
