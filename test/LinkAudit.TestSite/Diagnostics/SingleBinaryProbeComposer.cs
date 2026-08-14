using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace LinkAudit.TestSite.Diagnostics;

/// <summary>
/// Registers the cross-major probe. Test site only — never shipped in the package.
/// </summary>
public class SingleBinaryProbeComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
        => builder.Services.AddHostedService<SingleBinaryProbe>();
}
