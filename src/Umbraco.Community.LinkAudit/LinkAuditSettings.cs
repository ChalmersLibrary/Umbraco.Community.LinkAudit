namespace Umbraco.Community.LinkAudit;

/// <summary>
/// Configuration for the link auditor. Bound from the optional "LinkAudit" section in appsettings;
/// every value has a sensible default so the feature works with no configuration at all.
/// </summary>
/// <remarks>
/// Each absolute http(s) link found in content is processed in this order — the first match wins:
/// <list type="number">
///   <item><see cref="IgnoredHosts"/> (exact host) — dropped entirely: never flagged, never probed, never reported.</item>
///   <item><see cref="FlaggedHostPatterns"/> (host, "*." wildcard) — reported as a "Flagged" finding.</item>
///   <item>Otherwise — probed over HTTP (when <see cref="ExternalCheckEnabled"/>), and reported only if the
///   probe fails; <see cref="IgnoredStatusCodes"/> then downgrades chosen response codes back to OK.</item>
/// </list>
/// So the three "suppress" levers act at different stages: <see cref="IgnoredHosts"/> removes a link from the
/// whole audit, <see cref="IgnoredStatusCodes"/> only forgives a specific probe outcome.
/// </remarks>
public sealed class LinkAuditSettings
{
    /// <summary>Master on/off switch for the scheduled crawl.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Document-type alias of the content roots to crawl. When empty (the default) every content root is
    /// crawled; set it to restrict the audit to roots of a single type (e.g. a site's home/start page type).
    /// </summary>
    public string? RootDocumentTypeAlias { get; set; }

    /// <summary>
    /// Host patterns that should never appear in content links. Supports a leading "*." wildcard.
    /// Defaults to <c>["*.umbraco.io"]</c> when unconfigured — applied in <c>LinkAuditComposer</c> via
    /// PostConfigure, NOT as a field initializer here: the configuration binder <em>appends</em> bound
    /// array values to a non-empty default rather than replacing them, so a seed here would leak into
    /// every consumer's configured list.
    /// </summary>
    public string[] FlaggedHostPatterns { get; set; } = [];

    /// <summary>
    /// Hosts to exclude from the audit completely — links to them are neither flagged nor probed nor reported.
    /// Exact host match, case-insensitive, no wildcard (unlike <see cref="FlaggedHostPatterns"/>).
    /// </summary>
    public string[] IgnoredHosts { get; set; } = [];

    /// <summary>Whether to HTTP-probe external links for 404s.</summary>
    public bool ExternalCheckEnabled { get; set; } = true;

    /// <summary>
    /// User-Agent header sent on external probes. When left empty, a default of
    /// <c>LinkAudit/1.0 (+{site root})</c> is used, where the site root is resolved from the
    /// running site's own absolute URL at crawl time (so it reflects the actual host, be that staging or production).
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// HTTP status codes to treat as OK rather than reporting as warnings. Use for links we knowingly can't
    /// verify anonymously — e.g. <c>401</c>/<c>403</c> for pages behind a login. Empty by default.
    /// </summary>
    public int[] IgnoredStatusCodes { get; set; } = [];

    /// <summary>Per-request timeout for external probes, in seconds.</summary>
    public int ExternalTimeoutSeconds { get; set; } = 10;

    /// <summary>Maximum number of external probes in flight at once.</summary>
    public int ExternalConcurrency { get; set; } = 8;

    /// <summary>How often the crawl runs.</summary>
    public double IntervalHours { get; set; } = 24;

    /// <summary>Delay after app startup before the first crawl.</summary>
    public double StartupDelayMinutes { get; set; } = 5;
}
