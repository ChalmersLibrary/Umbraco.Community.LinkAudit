using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;
using Umbraco.Cms.Web.Common.Authorization;

namespace Umbraco.Community.LinkAudit;

/// <summary>
/// Read-only Management API endpoint that serves the latest link-audit report to the backoffice dashboard.
/// Secured to users with access to the Content section. There is no write endpoint — the crawl is scheduled.
/// </summary>
[ApiController]
[VersionedApiBackOfficeRoute("link-audit")]
[ApiExplorerSettings(GroupName = "Link Audit")]
[Authorize(Policy = AuthorizationPolicies.SectionAccessContent)]
public class LinkAuditApiController : ManagementApiControllerBase
{
    private readonly ILinkAuditReportStore _store;
    private readonly ILinkAuditRunner _runner;

    public LinkAuditApiController(ILinkAuditReportStore store, ILinkAuditRunner runner)
    {
        _store = store;
        _runner = runner;
    }

    [HttpGet("report")]
    [ProducesResponseType(typeof(LinkAuditReport), StatusCodes.Status200OK)]
    public IActionResult Report()
    {
        LinkAuditReport report = _store.Latest
            ?? new LinkAuditReport(DateTimeOffset.MinValue, 0, 0, []);

        return Ok(report with { NextScheduledScan = _runner.NextScheduledScan });
    }

    /// <summary>Runs a fresh crawl on demand and returns the new report. Returns 409 if a crawl is already running.</summary>
    [HttpPost("rescan")]
    [ProducesResponseType(typeof(LinkAuditReport), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Rescan(CancellationToken cancellationToken)
    {
        LinkAuditReport? report = await _runner.RunAsync(cancellationToken);
        return report is null
            ? Conflict()
            : Ok(report with { NextScheduledScan = _runner.NextScheduledScan });
    }
}
