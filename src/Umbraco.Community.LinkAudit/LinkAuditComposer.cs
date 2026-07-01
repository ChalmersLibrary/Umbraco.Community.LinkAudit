using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Extensions;

namespace Umbraco.Community.LinkAudit;

/// <summary>
/// Wires up the link auditor: binds settings, registers the crawler + in-memory store, and schedules
/// the recurring crawl. Auto-discovered and run by Umbraco's composer scanning at startup.
/// </summary>
public class LinkAuditComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.Configure<LinkAuditSettings>(builder.Config.GetSection("LinkAudit"));
        builder.Services.AddSingleton<ILinkAuditReportStore, LinkAuditReportStore>();
        builder.Services.AddSingleton<ILinkAuditService, LinkAuditService>();
        builder.Services.AddSingleton<ILinkAuditRunner, LinkAuditRunner>();
        builder.Services.AddHttpClient("LinkAudit");
        builder.Services.AddRecurringBackgroundJob<LinkAuditJob>();
    }
}
