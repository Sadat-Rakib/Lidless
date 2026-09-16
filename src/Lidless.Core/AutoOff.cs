namespace Lidless;

/// <summary>
/// Auto-off timer logic (pure, unit-testable).
/// Keep-awake is a convenience, not a safety mechanism, so the countdown lives
/// in the app — if the app dies the watchdog restores sleep anyway.
/// </summary>
public static class AutoOff
{
    /// <summary>Selectable durations (minutes). 0 means "no auto-off".</summary>
    public static readonly int[] PresetMinutes = [15, 30, 60, 120, 240];

    /// <summary>What picking a duration should do.</summary>
    public abstract record Request
    {
        public static Request IgnoredInAutoMode { get; } = new IgnoredInAutoModeRequest();
        public static Request CancelTimer { get; } = new CancelTimerRequest();
        public static Request ArmTimer(int minutes) => new ArmTimerRequest(minutes);
        public static Request EnableThenArmTimer(int minutes) => new EnableThenArmTimerRequest(minutes);

        public sealed record IgnoredInAutoModeRequest : Request;
        public sealed record CancelTimerRequest : Request;
        public sealed record ArmTimerRequest(int Minutes) : Request;
        public sealed record EnableThenArmTimerRequest(int Minutes) : Request;
    }

    public static Request For(int minutes, bool isEnabled, bool autoModeOn)
    {
        if (autoModeOn)
        {
            return Request.IgnoredInAutoMode;
        }

        if (minutes <= 0)
        {
            return Request.CancelTimer;
        }

        return isEnabled ? Request.ArmTimer(minutes) : Request.EnableThenArmTimer(minutes);
    }

    public static string DurationLabel(int minutes) =>
        minutes > 0 ? OptionLabel(minutes) : "No limit";

    public static DateTime Deadline(DateTime start, int minutes) =>
        start.AddMinutes(minutes);

    public static TimeSpan Remaining(DateTime deadline, DateTime now) =>
        deadline > now ? deadline - now : TimeSpan.Zero;

    public static bool IsExpired(DateTime deadline, DateTime now) => now >= deadline;

    public static string FormatCountdown(TimeSpan remaining)
    {
        var total = (int)Math.Round(remaining.TotalSeconds);
        if (total < 0)
        {
            total = 0;
        }

        var h = total / 3600;
        var m = total % 3600 / 60;
        var s = total % 60;
        return h > 0 ? $"{h}:{m:D2}:{s:D2}" : $"{m}:{s:D2}";
    }

    public static string OptionLabel(int minutes)
    {
        if (minutes % 60 != 0)
        {
            return $"{minutes} min";
        }

        var h = minutes / 60;
        return h == 1 ? "1 hour" : $"{h} hours";
    }
}
