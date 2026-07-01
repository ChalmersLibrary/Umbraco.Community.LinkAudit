using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Infrastructure.BackgroundJobs;

namespace Umbraco.Community.LinkAudit;

/// <summary>
/// Recurring background job that runs the link audit on a schedule and stores the result for the dashboard.
/// The framework only fires this once the runtime is in the <c>Run</c> state and on the scheduling server,
/// so the content cache is guaranteed to be available inside <see cref="ILinkAuditService.RunAuditAsync"/>.
/// </summary>
public sealed class LinkAuditJob : RecurringBackgroundJobBase
{
    private readonly ILinkAuditRunner _runner;
    private readonly ILogger<LinkAuditJob> _logger;
    private readonly LinkAuditSettings _settings;

    public LinkAuditJob(
        ILinkAuditRunner runner,
        ILogger<LinkAuditJob> logger,
        IOptions<LinkAuditSettings> options)
        : base(TimeSpan.FromHours(options.Value.IntervalHours))
    {
        _runner = runner;
        _logger = logger;
        _settings = options.Value;
    }

    public override TimeSpan Delay => TimeSpan.FromMinutes(_settings.StartupDelayMinutes);

    public override async Task RunJobAsync(CancellationToken cancellationToken)
    {
        if (!_settings.Enabled)
        {
            return;
        }

        // Record when the following tick is due (this run + one interval) so the completion log can report it.
        _runner.NextScheduledScan = DateTimeOffset.Now.AddHours(_settings.IntervalHours);

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
