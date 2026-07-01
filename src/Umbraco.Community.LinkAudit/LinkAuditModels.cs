namespace Umbraco.Community.LinkAudit;

/// <summary>
/// Why a flagged link matters. Ordered loosely by how actionable it is for an editor.
/// </summary>
public enum LinkFindingKind
{
    /// <summary>Absolute link to a flagged host (e.g. *.umbraco.io or the site's own domain) that should be an internal link.</summary>
    FlaggedHost,

    /// <summary>External link that returned 404/410.</summary>
    BrokenExternal,

    /// <summary>Internal link picker that no longer resolves to a published node.</summary>
    BrokenInternal,

    /// <summary>External link that could not be verified (timeout, DNS, 401/403/405/429, 5xx). Not necessarily broken.</summary>
    Warning,
}

/// <summary>A single problematic link discovered on a page.</summary>
public sealed record LinkAuditFinding(
    string PageName,
    Guid PageKey,
    string? PageUrl,
    string Culture,
    string PropertyAlias,
    string Url,
    LinkFindingKind Kind,
    int? HttpStatus,
    string? Detail);

/// <summary>The result of one crawl over all published content.</summary>
public sealed record LinkAuditReport(
    DateTimeOffset GeneratedAt,
    int PagesScanned,
    int LinksScanned,
    IReadOnlyList<LinkAuditFinding> Findings,
    DateTimeOffset? NextScheduledScan = null);
