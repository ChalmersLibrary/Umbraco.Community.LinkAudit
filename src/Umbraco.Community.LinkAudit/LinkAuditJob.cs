using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Infrastructure.BackgroundJobs;

namespace Umbraco.Community.LinkAudit;

/// <summary>
/// Recurring background job that runs the link audit on a schedule and stores the result for the dashboard.
/// The framework only fires this once the runtime is in the <c>Run</c> state and on the scheduling server,
/// so the content cache is guaranteed to be available inside <see cref="ILinkAuditRunner.RunAsync"/>.
/// </summary>
/// <remarks>
/// The <see cref="RecurringBackgroundJobBase.Period"/> is captured from configuration once, when the job is
/// constructed at startup. Changing <see cref="LinkAuditSettings.IntervalHours"/> or
/// <see cref="LinkAuditSettings.StartupDelayMinutes"/> in appsettings therefore only takes effect after an
/// application restart — the recurring schedule does not honour hot config reloads.
/// </remarks>
public sealed class LinkAuditJob : RecurringBackgroundJobBase
{
    /// <summary>
    /// Lower bound on the crawl interval. A link audit is a heavyweight, site-wide operation, so we never let
    /// it run more often than hourly — a misconfigured tiny interval would hammer the site (and, at zero or
    /// negative, make the background runner re-fire with no delay or throw inside its timer).
    /// </summary>
    private static readonly TimeSpan MinimumPeriod = TimeSpan.FromHours(1);

    /// <summary>
    /// Cadence used when <c>IntervalHours</c> is non-positive (missing/invalid config). Mirrors the default
    /// declared on <see cref="LinkAuditSettings.IntervalHours"/>, so a bad value falls back to normal behaviour
    /// rather than the bare minimum.
    /// </summary>
    private static readonly TimeSpan DefaultPeriod = TimeSpan.FromHours(24);

    private readonly ILinkAuditRunner _runner;
    private readonly ILogger<LinkAuditJob> _logger;
    private readonly LinkAuditSettings _settings;
    private bool _warnedIntervalOverridden;

    public LinkAuditJob(
        ILinkAuditRunner runner,
        ILogger<LinkAuditJob> logger,
        IOptions<LinkAuditSettings> options)
        : base(ResolvePeriod(options.Value))
    {
        _runner = runner;
        _logger = logger;
        _settings = options.Value;
    }

    // Never negative: TimeSpan.FromMinutes throws on a negative StartupDelayMinutes, and the base class
    // treats a non-positive delay as "run immediately", which is the safe fallback for a bad config value.
    public override TimeSpan Delay =>
        _settings.StartupDelayMinutes > 0 ? TimeSpan.FromMinutes(_settings.StartupDelayMinutes) : TimeSpan.Zero;

    private static TimeSpan ResolvePeriod(LinkAuditSettings settings)
    {
        // Non-positive → treat as unconfigured and use the default cadence; otherwise honour the value but
        // never allow a crawl more frequent than the hourly floor.
        TimeSpan configured = settings.IntervalHours > 0 ? TimeSpan.FromHours(settings.IntervalHours) : DefaultPeriod;
        return configured < MinimumPeriod ? MinimumPeriod : configured;
    }

    public override async Task RunJobAsync(CancellationToken cancellationToken)
    {
        if (!_settings.Enabled)
        {
            return;
        }

        // Surface once (not every tick) when the configured interval was ignored, so the effective cadence
        // isn't a silent surprise.
        if (!_warnedIntervalOverridden && TimeSpan.FromHours(_settings.IntervalHours) != Period)
        {
            _warnedIntervalOverridden = true;
            _logger.LogWarning(
                "Configured LinkAudit:IntervalHours ({Configured}) is invalid or below the {Minimum:g} minimum; " +
                "using an effective interval of {Effective:g}.",
                _settings.IntervalHours,
                MinimumPeriod,
                Period);
        }

        // Record when the following tick is due (this run + one interval) so the completion log can report it.
        // Use the resolved Period rather than the raw setting so a clamped interval is reported accurately.
        _runner.NextScheduledScan = DateTimeOffset.Now.Add(Period);

        // The runner logs start/complete/error; it also swallows nothing, so guard the scheduled tick here.
        try
        {
            LinkAuditReport? report = await _runner.RunAsync(cancellationToken);
            if (report is null)
            {
                _logger.LogInformation("Link Audit skipped: a crawl is already running.");
            }
        }
        catch (Exception)
        {
            // Already logged by the runner; swallow so the recurring schedule keeps ticking.
        }
    }
}
