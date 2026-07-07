using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;

namespace LinkAudit.TestSite.Seeding;

/// <summary>
/// Seeds a handful of published pages the first time the test site boots, so the Link Audit dashboard
/// has something real to report. Idempotent: it only runs when the seed document type is absent, so it
/// fires once on a fresh database and is a no-op on every subsequent start.
/// </summary>
/// <remarks>
/// The auditor scans the RAW source value of each property for absolute URLs, and its regex only matches
/// a URL when it is immediately preceded by a quote (it targets <c>href="…"</c> and block-editor
/// <c>"…</c> JSON). So the seed bodies below deliberately wrap every link in HTML markup — a bare
/// plain-text URL would never be picked up.
///
/// The pages cover both storage shapes the regex is meant to handle: plain <c>Textarea</c> HTML, and a
/// real <c>Rich Text Editor</c> value in Umbraco's v14+ <c>{ "markup", "blocks" }</c> format — the latter
/// carries a link both in escaped-quote markup (<c>\"https…\"</c>) and in plain-quote block JSON
/// (<c>"url":"https…"</c>), so a fresh boot on any supported Umbraco proves the scan finds links inside
/// these special elements, not just simple anchors.
/// </remarks>
public sealed class LinkAuditTestDataSeeder : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    // Built-in "Textarea" data type — stores the body markup verbatim (no rich-text transformation).
    private static readonly Guid TextareaDataType = new("c6bac0dd-4ab9-45b1-8e30-e4b619ee5da3");
    // Built-in "Richtext editor" data type — a genuine special element whose stored value is JSON.
    private static readonly Guid RichTextDataType = new("ca90c950-0aff-4e72-b976-a30b1ac57dad");
    private const string SeedDocTypeAlias = "linkAuditTestPage";
    private const string BodyAlias = "bodyText";
    private const string RichTextAlias = "bodyRichText";

    private readonly IContentTypeService _contentTypeService;
    private readonly IContentService _contentService;
    private readonly IDataTypeService _dataTypeService;
    private readonly IShortStringHelper _shortStringHelper;
    private readonly ILogger<LinkAuditTestDataSeeder> _logger;

    public LinkAuditTestDataSeeder(
        IContentTypeService contentTypeService,
        IContentService contentService,
        IDataTypeService dataTypeService,
        IShortStringHelper shortStringHelper,
        ILogger<LinkAuditTestDataSeeder> logger)
    {
        _contentTypeService = contentTypeService;
        _contentService = contentService;
        _dataTypeService = dataTypeService;
        _shortStringHelper = shortStringHelper;
        _logger = logger;
    }

    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        if (_contentTypeService.Get(SeedDocTypeAlias) is not null)
        {
            return; // Already seeded on a previous boot.
        }

        IDataType? textarea = await _dataTypeService.GetAsync(TextareaDataType);
        IDataType? richText = await _dataTypeService.GetAsync(RichTextDataType);
        if (textarea is null || richText is null)
        {
            _logger.LogWarning("LinkAudit test seed: a built-in data type (Textarea/Richtext) was not found; skipping.");
            return;
        }

        _logger.LogInformation("LinkAudit test seed: creating document type and example pages.");

        var contentType = new ContentType(_shortStringHelper, Constants.System.Root)
        {
            Alias = SeedDocTypeAlias,
            Name = "Link Audit Test Page",
            Icon = "icon-link color-blue",
            AllowedAsRoot = true,
        };
        contentType.AddPropertyType(new PropertyType(_shortStringHelper, textarea)
        {
            Alias = BodyAlias,
            Name = "Body Text",
        });
        contentType.AddPropertyType(new PropertyType(_shortStringHelper, richText)
        {
            Alias = RichTextAlias,
            Name = "Body (Rich Text)",
        });
        // IContentTypeService.Save was removed in Umbraco 18; CreateAsync is the cross-version API (present
        // in 17.5 and 18). It persists the new type — we reference it by alias below, so the return is unused.
        await _contentTypeService.CreateAsync(contentType, Constants.Security.SuperUserKey);

        // Each page mixes link kinds so the dashboard shows a representative spread. The umbraco.io link is
        // flagged by the default FlaggedHostPatterns (*.umbraco.io) with no network call; the httpstat.us
        // links exercise the external HTTP probe (404 → broken, 500 → warning) and need outbound internet.
        SeedPage("Company Home",
            """
            <p>Welcome — read <a href="https://example.com">our overview</a>.</p>
            <p>Our legacy portal still lives at <a href="https://demo.umbraco.io/features">demo.umbraco.io</a>.</p>
            """);

        SeedPage("Resources",
            """
            <p>A useful <a href="https://example.org">reference</a>, and a
            <a href="https://httpstat.us/404">document that has moved</a> (should report as broken).</p>
            """);

        SeedPage("Old Microsite",
            """
            <p>Hosted on <a href="https://oldsite.umbraco.io">oldsite.umbraco.io</a> (flagged host).</p>
            <p>Its status endpoint <a href="https://httpstat.us/500">sometimes errors</a> (should report as a warning).</p>
            """);

        SeedPage("Clean Page",
            """
            <p>This page only links to <a href="https://example.com">a healthy site</a> — it should produce no findings.</p>
            """);

        // A real Rich Text Editor value in Umbraco's v14+ storage format. The backslash-escaped quotes are
        // literal characters in the stored JSON (that is how the RTE persists inline markup), so this string
        // matches exactly what GetSourceValue returns for an RTE property. It carries two links the regex must
        // find via DIFFERENT branches:
        //   • markup anchor  -> \"https://legacy.umbraco.io/docs\"  (flagged host; escaped-quote branch)
        //   • block JSON     -> "url":"https://httpstat.us/404"      (broken; plain-quote branch)
        SeedRichTextPage("Rich Text Page",
            """
            {"markup":"<p>Legacy docs live at <a href=\"https://legacy.umbraco.io/docs\">legacy.umbraco.io</a>.</p><umb-rte-block data-content-key=\"b1a9f7e2-0000-0000-0000-000000000001\"><!--Umbraco-Block--></umb-rte-block>","blocks":{"contentData":[{"key":"b1a9f7e2-0000-0000-0000-000000000001","contentTypeKey":"b1a9f7e2-0000-0000-0000-0000000000c7","values":[{"alias":"externalLink","value":[{"url":"https://httpstat.us/404","name":"Moved document"}]}]}],"settingsData":[],"expose":[]}}
            """);

        _logger.LogInformation("LinkAudit test seed: complete.");
    }

    private void SeedPage(string name, string body) => Seed(name, BodyAlias, body);

    private void SeedRichTextPage(string name, string rteJson) => Seed(name, RichTextAlias, rteJson);

    private void Seed(string name, string alias, string value)
    {
        IContent page = _contentService.Create(name, Constants.System.Root, SeedDocTypeAlias);
        page.SetValue(alias, value);
        _contentService.Save(page);
        _contentService.Publish(page, ["*"]);
    }
}
