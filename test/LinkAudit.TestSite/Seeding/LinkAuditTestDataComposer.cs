using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Extensions;

namespace LinkAudit.TestSite.Seeding;

/// <summary>
/// Registers the one-time test-data seeder. Lives only in the test site — never shipped in the package.
/// </summary>
public class LinkAuditTestDataComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
        => builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, LinkAuditTestDataSeeder>();
}
