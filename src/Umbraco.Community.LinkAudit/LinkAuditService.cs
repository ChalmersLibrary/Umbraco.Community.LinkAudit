using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;

namespace Umbraco.Community.LinkAudit;

public interface ILinkAuditService
{
    /// <summary>Crawls all published content and returns the links that need attention.</summary>
    Task<LinkAuditReport> RunAuditAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Walks every published page, scans each property's raw source value for absolute URLs, and flags the
/// ones that point at a forbidden host or fail an external check. Scanning the source value (rather than
/// the rendered property value) keeps results deterministic on a background thread, where rich-text
/// rendering throws.
/// </summary>
public sealed partial class LinkAuditService : ILinkAuditService
{
    private readonly IUmbracoContextFactory _umbracoContextFactory;
    private readonly IDocumentNavigationQueryService _navigation;
    private readonly IVariationContextAccessor _variationContextAccessor;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<LinkAuditSettings> _options;
    private readonly ILogger<LinkAuditService> _logger;

    public LinkAuditService(
        IUmbracoContextFactory umbracoContextFactory,
        IDocumentNavigationQueryService navigation,
        IVariationContextAccessor variationContextAccessor,
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<LinkAuditSettings> options,
        ILogger<LinkAuditService> logger)
    {
        _umbracoContextFactory = umbracoContextFactory;
        _navigation = navigation;
        _variationContextAccessor = variationContextAccessor;
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    [GeneratedRegex("https?://[^\\s\"'<>\\\\)}]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    public async Task<LinkAuditReport> RunAuditAsync(CancellationToken cancellationToken)
    {
        LinkAuditSettings settings = _options.CurrentValue;
        var findings = new List<LinkAuditFinding>();
        var externalRefs = new List<ExternalRef>();
        var seen = new HashSet<string>();
        int pagesScanned = 0;
        int linksScanned = 0;
        string? siteRoot = null;

        using (UmbracoContextReference contextRef = _umbracoContextFactory.EnsureUmbracoContext())
        {
            var cache = contextRef.UmbracoContext.Content;
            if (cache is null)
            {
                _logger.LogWarning("Link Audit: content cache unavailable.");
                return new LinkAuditReport(DateTimeOffset.UtcNow, 0, 0, findings);
            }

            // By default crawl every content root; when a root document-type alias is configured, restrict the
            // crawl to roots of that type (e.g. a site that only wants its main site tree audited).
            IEnumerable<Guid> rootKeys = [];
            bool haveRoots = string.IsNullOrWhiteSpace(settings.RootDocumentTypeAlias)
                ? _navigation.TryGetRootKeys(out rootKeys)
                : _navigation.TryGetRootKeysOfType(settings.RootDocumentTypeAlias!, out rootKeys);

            if (haveRoots)
            {
                foreach (var rootKey in rootKeys)
                {
                    if (!_navigation.TryGetDescendantsKeysOrSelfKeys(rootKey, out IEnumerable<Guid> pageKeys))
                    {
                        continue;
                    }

                    foreach (var key in pageKeys)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var page = await cache.GetByIdAsync(key);
                        if (page is null)
                        {
                            continue;
                        }

                        pagesScanned++;

                        var cultures = page.Cultures.Keys.ToList();
                        if (cultures.Count == 0)
                        {
                            cultures.Add(string.Empty);
                        }

                        foreach (var culture in cultures)
                        {
                            _variationContextAccessor.VariationContext = new VariationContext(culture);

                            string? pageUrl = SafeUrl(page, culture);
                            siteRoot ??= SafeAbsoluteRoot(page, culture);

                            foreach (IPublishedProperty prop in page.Properties)
                            {
                                // Scan the RAW stored source value, not the rendered object value. The source
                                // (rich-text markup, block JSON, link-picker JSON) contains every literal URL an
                                // editor entered, regardless of thread/context — whereas rendering the object value
                                // on a background thread throws/returns partial markup for rich-text properties.
                                if (SafeGetSourceValue(prop, culture) is not { Length: > 0 } source)
                                {
                                    continue;
                                }

                                foreach (Match m in UrlRegex().Matches(source))
                                {
                                    linksScanned++;
                                    Classify(m.Value, page, key, pageUrl, culture, prop.Alias, settings, seen, findings, externalRefs);
                                }
                            }
                        }
                    }
                }
            }
        }

        if (settings.ExternalCheckEnabled && externalRefs.Count > 0)
        {
            await ProbeExternalAsync(externalRefs, findings, settings, ResolveUserAgent(settings, siteRoot), cancellationToken);
        }

        return new LinkAuditReport(DateTimeOffset.UtcNow, pagesScanned, linksScanned, findings);
    }

    private void Classify(
        string rawUrl,
        IPublishedContent page,
        Guid key,
        string? pageUrl,
        string culture,
        string alias,
        LinkAuditSettings settings,
        HashSet<string> seen,
        List<LinkAuditFinding> findings,
        List<ExternalRef> externalRefs)
    {
        string url = rawUrl.Trim().TrimEnd('.', ',', ';', ':', '!', '?');
        if (url.Length == 0)
        {
            return;
        }

        // Report each unique link once per page + property, regardless of how many cultures share it.
        if (!seen.Add($"{key}|{alias}|{url}"))
        {
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            // Relative/internal links, mailto:, tel:, fragment-only — nothing to flag.
            return;
        }

        string host = uri.Host;
        string pageName = page.Name ?? "(unnamed)";

        if (settings.IgnoredHosts.Any(h => host.Equals(h, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        if (settings.FlaggedHostPatterns.Any(pattern => HostMatches(host, pattern)))
        {
            findings.Add(new LinkAuditFinding(pageName, key, pageUrl, culture, alias, url, LinkFindingKind.FlaggedHost, null, $"Points at {host}"));
            return;
        }

        externalRefs.Add(new ExternalRef(url, pageName, key, pageUrl, culture, alias));
    }

    private async Task ProbeExternalAsync(
        List<ExternalRef> refs,
        List<LinkAuditFinding> findings,
        LinkAuditSettings settings,
        string userAgent,
        CancellationToken cancellationToken)
    {
        List<string> unique = refs.Select(r => r.Url).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var results = new ConcurrentDictionary<string, ProbeResult>(StringComparer.OrdinalIgnoreCase);
        using var gate = new SemaphoreSlim(Math.Max(1, settings.ExternalConcurrency));
        HttpClient client = _httpClientFactory.CreateClient("LinkAudit");

        IEnumerable<Task> tasks = unique.Select(async url =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                results[url] = await ProbeOneAsync(client, url, settings, userAgent, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);

        foreach (ExternalRef reference in refs)
        {
            if (!results.TryGetValue(reference.Url, out ProbeResult result) || result.Kind is null)
            {
                continue; // OK or unknown — nothing to report.
            }

            findings.Add(new LinkAuditFinding(
                reference.PageName,
                reference.PageKey,
                reference.PageUrl,
                reference.Culture,
                reference.Alias,
                reference.Url,
                result.Kind.Value,
                result.Status,
                result.Detail));
        }
    }

    private static async Task<ProbeResult> ProbeOneAsync(HttpClient client, string url, LinkAuditSettings settings, string userAgent, CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, settings.ExternalTimeoutSeconds)));

            int status = await SendAsync(client, HttpMethod.Head, url, userAgent, cts.Token);

            // Many servers reject HEAD, block bots, or return a misleading 404 to HEAD; retry once with GET
            // before drawing a conclusion (especially before reporting a link as broken).
            if (status is 405 or 501 or 403 or 401 or 404 or 410)
            {
                status = await SendAsync(client, HttpMethod.Get, url, userAgent, cts.Token);
            }

            return MapStatus(status, settings);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProbeResult(LinkFindingKind.Warning, null, "Timed out");
        }
        catch (Exception ex)
        {
            // DNS failures, TLS errors, etc. — treat as unverifiable rather than definitively broken.
            return new ProbeResult(LinkFindingKind.Warning, null, ex.GetType().Name);
        }
    }

    private static async Task<int> SendAsync(HttpClient client, HttpMethod method, string url, string userAgent, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return (int)response.StatusCode;
    }

    private static ProbeResult MapStatus(int code, LinkAuditSettings settings)
    {
        if (settings.IgnoredStatusCodes.Contains(code))
        {
            return new ProbeResult(null, code, null); // Known & accepted (e.g. login-gated) — not reported.
        }

        if (code is 404 or 410)
        {
            return new ProbeResult(LinkFindingKind.BrokenExternal, code, "Not found");
        }

        if (code is >= 200 and < 400)
        {
            return new ProbeResult(null, code, null); // OK
        }

        return new ProbeResult(LinkFindingKind.Warning, code, "Unverifiable response");
    }

    private static bool HostMatches(string host, string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return false;
        }

        if (pattern.StartsWith("*.", StringComparison.Ordinal))
        {
            string suffix = pattern[1..]; // ".umbraco.io"
            return host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
                   host.Equals(suffix[1..], StringComparison.OrdinalIgnoreCase);
        }

        return host.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static string? SafeUrl(IPublishedContent page, string culture)
    {
        try
        {
            return page.Url(culture, UrlMode.Relative);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Scheme + host (e.g. <c>https://example.com</c>) of the running site, from a page's absolute URL.</summary>
    private static string? SafeAbsoluteRoot(IPublishedContent page, string culture)
    {
        try
        {
            string? absolute = page.Url(culture, UrlMode.Absolute);
            return Uri.TryCreate(absolute, UriKind.Absolute, out Uri? uri)
                ? uri.GetLeftPart(UriPartial.Authority)
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Configured User-Agent verbatim, or a default that references the running site's own host.</summary>
    private static string ResolveUserAgent(LinkAuditSettings settings, string? siteRoot)
    {
        if (!string.IsNullOrWhiteSpace(settings.UserAgent))
        {
            return settings.UserAgent.Trim();
        }

        return string.IsNullOrEmpty(siteRoot)
            ? "LinkAudit/1.0"
            : $"LinkAudit/1.0 (+{siteRoot})";
    }

    private static string? SafeGetSourceValue(IPublishedProperty prop, string culture)
    {
        try
        {
            return prop.GetSourceValue(culture)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private sealed record ExternalRef(string Url, string PageName, Guid PageKey, string? PageUrl, string Culture, string Alias);

    private readonly record struct ProbeResult(LinkFindingKind? Kind, int? Status, string? Detail);
}
