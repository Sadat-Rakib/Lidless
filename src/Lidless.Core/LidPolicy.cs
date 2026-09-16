using System.Globalization;
using System.Text.RegularExpressions;

namespace Lidless;

/// <summary>Windows lid-close action stored in the current power scheme.</summary>
public enum LidAction
{
    DoNothing = 0,
    Sleep = 1,
    Hibernate = 2,
    ShutDown = 3,
}

/// <summary>AC and DC lid-close actions for one power scheme.</summary>
public readonly struct LidPolicy : IEquatable<LidPolicy>
{
    public LidPolicy(LidAction ac, LidAction dc)
    {
        Ac = ac;
        Dc = dc;
    }

    public LidAction Ac { get; }
    public LidAction Dc { get; }

    public bool IsDoNothing => Ac == LidAction.DoNothing && Dc == LidAction.DoNothing;

    public static string Describe(LidAction action) => action switch
    {
        LidAction.DoNothing => "Do nothing",
        LidAction.Sleep => "Sleep",
        LidAction.Hibernate => "Hibernate",
        LidAction.ShutDown => "Shut down",
        _ => $"Unknown ({(int)action})",
    };

    public bool Equals(LidPolicy other) => Ac == other.Ac && Dc == other.Dc;
    public override bool Equals(object? obj) => obj is LidPolicy other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Ac, Dc);
}

/// <summary>Power scheme identifiers used when calling powercfg.</summary>
public static class PowerCfgGuids
{
    public const string SubButtons = "4f971e89-eebd-4455-a8de-9e59040e7347";
    public const string LidAction = "5ca83367-6e45-459f-a27b-476b1d01c936";
}

/// <summary>Pure parsers for powercfg text output.</summary>
public static class LidPolicyParser
{
    private static readonly Regex SchemeGuid = new(
        @"Power Scheme GUID:\s*([0-9a-fA-F-]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ActiveSchemeGuid = new(
        @"GUID:\s*([0-9a-fA-F-]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AcIndex = new(
        @"Current AC Power Setting Index:\s*(0x[0-9a-fA-F]+|\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DcIndex = new(
        @"Current DC Power Setting Index:\s*(0x[0-9a-fA-F]+|\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string? ParseActiveSchemeGuid(string getActiveSchemeOutput)
    {
        var match = SchemeGuid.Match(getActiveSchemeOutput);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        match = ActiveSchemeGuid.Match(getActiveSchemeOutput);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Reads AC/DC lid-close indices from <c>powercfg /Q</c> output for the lid setting.
    /// Returns null when the output does not actually state both indices.
    /// </summary>
    public static LidPolicy? Parse(string powerCfgQueryOutput)
    {
        var ac = ParseIndex(AcIndex.Match(powerCfgQueryOutput));
        var dc = ParseIndex(DcIndex.Match(powerCfgQueryOutput));
        if (ac is null || dc is null)
        {
            return null;
        }

        return new LidPolicy(ToLidAction(ac.Value), ToLidAction(dc.Value));
    }

    /// <summary>
    /// True when both AC and DC lid actions are Do nothing — the Windows analog of
    /// a sleep-disabled machine. Unknown output must not collapse to false.
    /// </summary>
    public static bool? IsLidDoNothing(string powerCfgQueryOutput)
    {
        var policy = Parse(powerCfgQueryOutput);
        return policy?.IsDoNothing;
    }

    private static int? ParseIndex(Match match)
    {
        if (!match.Success)
        {
            return null;
        }

        var raw = match.Groups[1].Value.Trim();
        if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(raw.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex)
                ? hex
                : null;
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dec) ? dec : null;
    }

    private static LidAction ToLidAction(int index) =>
        Enum.IsDefined(typeof(LidAction), index) ? (LidAction)index : LidAction.Sleep;
}
