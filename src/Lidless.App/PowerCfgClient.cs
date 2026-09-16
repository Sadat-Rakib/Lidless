using System.Diagnostics;
using System.Text;

namespace Lidless.App;

/// <summary>Runs powercfg.exe and parses lid-close policy for the active scheme.</summary>
internal sealed class PowerCfgClient
{
    public string ActiveSchemeGuid()
    {
        var output = Run("/GETACTIVESCHEME");
        return LidPolicyParser.ParseActiveSchemeGuid(output)
               ?? throw new InvalidOperationException("Could not read the active power scheme.");
    }

    public LidPolicy? ReadLidPolicy()
    {
        var scheme = ActiveSchemeGuid();
        var output = Run("/Q", scheme, PowerCfgGuids.SubButtons, PowerCfgGuids.LidAction);
        return LidPolicyParser.Parse(output);
    }

    public void SetLidPolicy(LidPolicy policy)
    {
        var scheme = ActiveSchemeGuid();
        Run("/SETACVALUEINDEX", scheme, PowerCfgGuids.SubButtons, PowerCfgGuids.LidAction, ((int)policy.Ac).ToString());
        Run("/SETDCVALUEINDEX", scheme, PowerCfgGuids.SubButtons, PowerCfgGuids.LidAction, ((int)policy.Dc).ToString());
        Run("/SETACTIVE", scheme);
    }

    public static string Run(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "powercfg.exe"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)
                            ?? throw new InvalidOperationException("Failed to start powercfg.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(15_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw new TimeoutException("powercfg timed out.");
        }

        if (process.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(detail)
                    ? $"powercfg exited {process.ExitCode}."
                    : detail.Trim());
        }

        return stdout;
    }
}
