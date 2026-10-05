using System.Diagnostics;
using Zuil.Core.Abstractions;

namespace Zuil.Host;

/// <summary>
/// Wall clock, plus a check on whether it can be trusted.
///
/// A Raspberry Pi has no battery-backed RTC. If it boots without a network it
/// comes up believing it is whenever it last shut down, and an application whose
/// entire job is "which meeting is happening now" will then confidently point
/// visitors at the wrong room. So the UI is gated on NTP having synchronised.
/// </summary>
public sealed class SystemClock : IClock
{
    private readonly ILogger<SystemClock> _logger;
    private bool _synchronized;
    private DateTimeOffset _lastChecked = DateTimeOffset.MinValue;

    public SystemClock(ILogger<SystemClock> logger) => _logger = logger;

    public DateTimeOffset Now => DateTimeOffset.Now;

    public bool IsSynchronized
    {
        get
        {
            // Re-check occasionally until it goes true, then stop asking.
            if (_synchronized || DateTimeOffset.UtcNow - _lastChecked < TimeSpan.FromSeconds(15))
            {
                return _synchronized;
            }

            _lastChecked = DateTimeOffset.UtcNow;
            _synchronized = QueryTimedatectl();
            return _synchronized;
        }
    }

    private bool QueryTimedatectl()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "timedatectl",
                Arguments = "show --property=NTPSynchronized --value",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            if (process is null)
            {
                return true; // not systemd; assume the host manages its own clock
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(2000);

            return string.Equals(output, "yes", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not determine NTP sync state; assuming synchronized");
            return true;
        }
    }
}
