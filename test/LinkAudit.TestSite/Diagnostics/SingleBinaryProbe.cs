using System.Reflection;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Cms.Core.Web;
using Umbraco.Community.LinkAudit;

namespace LinkAudit.TestSite.Diagnostics;

/// <summary>
/// Proves that ONE LinkAudit binary — compiled against the Umbraco 17.5.0 floor — actually runs a full
/// audit on whichever Umbraco major this site resolved at restore. Lives only in the test site.
/// </summary>
/// <remarks>
/// Drives the crawl through <see cref="ILinkAuditRunner"/> rather than waiting for the scheduled job: a
/// recurring job only fires once the server role is established, and with a 24-hour period a skipped first
/// tick means a 24-hour wait. The runner is the same code path the job and the dashboard's "Rescan now"
/// both use, so nothing about the crawl is bypassed.
///
/// Set LINKAUDIT_PROBE_EXIT=1 to shut the site down once the verdict is printed, so a CI job can boot each
/// major, capture stdout and assert on it.
/// </remarks>
public sealed class SingleBinaryProbe : IHostedService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ContentTimeout = TimeSpan.FromMinutes(3);

    private readonly ILinkAuditRunner _runner;
    private readonly IUmbracoContextFactory _contextFactory;
    private readonly IDocumentNavigationQueryService _navigation;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<SingleBinaryProbe> _logger;
    private CancellationTokenSource? _cts;
    private Task? _worker;

    public SingleBinaryProbe(
        ILinkAuditRunner runner,
        IUmbracoContextFactory contextFactory,
        IDocumentNavigationQueryService navigation,
        IHostApplicationLifetime lifetime,
        ILogger<SingleBinaryProbe> logger)
    {
        _runner = runner;
        _contextFactory = contextFactory;
        _navigation = navigation;
        _lifetime = lifetime;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _worker = Task.Run(() => RunAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        if (_worker is not null)
        {
            try
            {
                await _worker;
            }
            catch (OperationCanceledException)
            {
                // Shutting down before the probe finished — nothing to report.
            }
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        bool pass = false;
        try
        {
            ReportInterfaceBinding();

            IPublishedContent? sample = await WaitForContentAsync(cancellationToken);
            if (sample is null)
            {
                _logger.LogError("PROBE: no published content appeared within {Timeout:g}.", ContentTimeout);
            }
            else
            {
                bool bindingOk = ReportConcreteBinding(sample);
                LinkAuditReport? report = await _runner.RunAsync(cancellationToken);
                pass = report is not null && ReportFindings(report, bindingOk);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A binary incompatibility surfaces here as MissingMethodException / TypeLoadException.
            _logger.LogError(ex, "PROBE: threw {Type}.", ex.GetType().Name);
        }

        _logger.LogInformation("PROBE: {Verdict}", pass ? "PASS" : "FAILED");

        if (Environment.GetEnvironmentVariable("LINKAUDIT_PROBE_EXIT") == "1")
        {
            _lifetime.StopApplication();
        }
    }

    /// <summary>
    /// Logs which interface declares Name/Cultures on the Umbraco actually loaded. Type.GetProperty does not
    /// search base interfaces, so exactly one of the two columns finds each member — which is precisely the
    /// binary break the compat shim exists to absorb.
    /// </summary>
    private void ReportInterfaceBinding()
    {
        _logger.LogInformation("PROBE: running against Umbraco.Core {Version}", typeof(IPublishedContent).Assembly.GetName().Version);

        foreach (string member in new[] { nameof(IPublishedContent.Name), nameof(IPublishedContent.Cultures) })
        {
            bool onContent = typeof(IPublishedContent).GetProperty(member, BindingFlags.Public | BindingFlags.Instance) is not null;
            bool onElement = typeof(IPublishedElement).GetProperty(member, BindingFlags.Public | BindingFlags.Instance) is not null;
            _logger.LogInformation(
                "PROBE: {Member} declared on IPublishedContent={OnContent}, IPublishedElement={OnElement}",
                member,
                onContent,
                onElement);
        }
    }

    /// <summary>Polls until the seeder has published content and the cache can serve it.</summary>
    private async Task<IPublishedContent?> WaitForContentAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(ContentTimeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using UmbracoContextReference contextRef = _contextFactory.EnsureUmbracoContext();
                var cache = contextRef.UmbracoContext.Content;
                if (cache is not null && _navigation.TryGetRootKeys(out IEnumerable<Guid> rootKeys))
                {
                    foreach (Guid key in rootKeys)
                    {
                        if (await cache.GetByIdAsync(key) is { } page)
                        {
                            return page;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("PROBE: content not ready yet ({Type}).", ex.GetType().Name);
            }

            await Task.Delay(PollInterval, cancellationToken);
        }

        return null;
    }

    /// <summary>
    /// Resolves Name/Cultures on a real page's CONCRETE type — the lookup PublishedContentCompat actually
    /// performs. This is the check the findings alone cannot make: the audit treats "no cultures" as
    /// "invariant, scan once", so on invariant test content a shim that silently resolved nothing would
    /// still produce a passing report. Here an empty culture list fails.
    /// </summary>
    private bool ReportConcreteBinding(IPublishedContent page)
    {
        Type concrete = page.GetType();
        PropertyInfo? name = FindProperty(concrete, nameof(IPublishedContent.Name));
        PropertyInfo? cultures = FindProperty(concrete, nameof(IPublishedContent.Cultures));
        var keys = (cultures?.GetValue(page) as IReadOnlyDictionary<string, PublishedCultureInfo>)?.Keys.ToList() ?? [];

        _logger.LogInformation(
            "PROBE: concrete type {Concrete} — Name declaredBy={NameOwner} value='{NameValue}'; Cultures declaredBy={CultureOwner} keys=[{Keys}] count={Count}",
            concrete.Name,
            name?.DeclaringType?.Name ?? "NOT FOUND",
            name?.GetValue(page) as string ?? "<null>",
            cultures?.DeclaringType?.Name ?? "NOT FOUND",
            string.Join(",", keys.Select(k => k.Length == 0 ? "<invariant>" : k)),
            keys.Count);

        bool ok = name is not null && cultures is not null && keys.Count > 0;
        if (!ok)
        {
            _logger.LogError("PROBE: the compat lookup did not resolve on the concrete type.");
        }

        return ok;
    }

    private static PropertyInfo? FindProperty(Type type, string name)
    {
        PropertyInfo? property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (property is not null)
        {
            return property;
        }

        foreach (Type contract in type.GetInterfaces())
        {
            property = contract.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property is not null)
            {
                return property;
            }
        }

        return null;
    }

    private bool ReportFindings(LinkAuditReport report, bool bindingOk)
    {
        _logger.LogInformation(
            "PROBE: report — {Pages} pages, {Links} links, {Findings} findings",
            report.PagesScanned,
            report.LinksScanned,
            report.Findings.Count);

        foreach (LinkAuditFinding f in report.Findings)
        {
            _logger.LogInformation(
                "PROBE: finding {Kind} page='{PageName}' culture='{Culture}' property='{Alias}' url={Url} status={Status}",
                f.Kind,
                f.PageName,
                f.Culture,
                f.PropertyAlias,
                f.Url,
                f.HttpStatus?.ToString() ?? f.Detail ?? "-");
        }

        // Flagged-host findings need no network, so they must appear even on an offline runner; external
        // probes may legitimately degrade to Warning/timeout without a route to the internet.
        int flagged = report.Findings.Count(f => f.Kind == LinkFindingKind.FlaggedHost);
        bool anyUnnamed = report.Findings.Any(f => f.PageName == "(unnamed)");

        _logger.LogInformation(
            "PROBE: checks — compatBinding={Binding} pagesScanned={Pages} flaggedFindings={Flagged} unnamedPages={Unnamed}",
            bindingOk,
            report.PagesScanned,
            flagged,
            anyUnnamed);

        return bindingOk && report.PagesScanned > 0 && flagged > 0 && !anyUnnamed;
    }
}
