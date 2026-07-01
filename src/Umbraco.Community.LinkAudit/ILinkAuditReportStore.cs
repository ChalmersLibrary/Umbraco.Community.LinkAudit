using Microsoft.Extensions.Logging;

namespace Umbraco.Community.LinkAudit;

/// <summary>Holds the most recent <see cref="LinkAuditReport"/> in memory for the dashboard to read.</summary>
public interface ILinkAuditReportStore
{
    /// <summary>The latest completed report, or null if no crawl has run yet this process lifetime.</summary>
    LinkAuditReport? Latest { get; }

    /// <summary>Replaces the stored report.</summary>
    void Set(LinkAuditReport report);
}

/// <inheritdoc />
public sealed class LinkAuditReportStore : ILinkAuditReportStore
{
    private volatile LinkAuditReport? _latest;

    public LinkAuditReport? Latest => _latest;

    public void Set(LinkAuditReport report) => _latest = report;
}

/// <summary>
/// Runs the audit and stores the result, ensuring only one crawl runs at a time. Shared by the scheduled
/// job and the dashboard's manual "Rescan now" action so they can never overlap.
/// </summary>
public interface ILinkAuditRunner
{
    /// <summary>
    /// Runs the audit and stores the report, unless one is already in progress — in which case this
    /// returns <c>null</c> without starting a second crawl.
    /// </summary>
    Task<LinkAuditReport?> RunAsync(CancellationToken cancellationToken);

    /// <summary>
    /// When the next scheduled crawl is expected, set by the recurring job. Null until the job has ticked
    /// at least once. Manual rescans don't change it — the schedule runs on its own cadence.
    /// </summary>
    DateTimeOffset? NextScheduledScan { get; set; }
}

/// <inheritdoc />
public sealed class LinkAuditRunner : ILinkAuditRunner
{
    private readonly ILinkAuditService _service;
    private readonly ILinkAuditReportStore _store;
    private readonly ILogger<LinkAuditRunner> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LinkAuditRunner(ILinkAuditService service, ILinkAuditReportStore store, ILogger<LinkAuditRunner> logger)
    {
        _service = service;
        _store = store;
        _logger = logger;
    }

    public DateTimeOffset? NextScheduledScan { get; set; }

    public async Task<LinkAuditReport?> RunAsync(CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            return null; // A crawl is already running — don't pile on.
        }

        // Logged here (rather than in a caller) so scheduled and manual runs report identically.
        _logger.LogInformation("Link Audit starting.");
        try
        {
            LinkAuditReport report = await _service.RunAuditAsync(cancellationToken);
            _store.Set(report);
            _logger.LogInformation(
                "Link Audit complete: {Pages} pages, {Links} links scanned, {Findings} findings. Next automatic scan {Next}.",
                report.PagesScanned,
                report.LinksScanned,
                report.Findings.Count,
                NextScheduledScan is { } next ? $"around {next.LocalDateTime:g}" : "not yet scheduled");
            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Link Audit failed.");
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }
}
