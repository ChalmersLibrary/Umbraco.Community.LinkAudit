using System.Collections.Concurrent;
using System.Reflection;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Umbraco.Community.LinkAudit;

/// <summary>
/// Reads the two published-content members whose <em>declaring interface</em> changed between Umbraco
/// majors, so a single LinkAudit binary can run on every supported major.
/// </summary>
/// <remarks>
/// Umbraco 18 pushed most of <see cref="IPublishedContent"/> down into its base
/// <see cref="IPublishedElement"/> — <c>Name</c> and <c>Cultures</c> among them. A compiled call to
/// <c>IPublishedContent.Name</c> records the declaring interface in the IL member reference, and member-ref
/// resolution does <em>not</em> search base interfaces, so a 17-built call site throws
/// <see cref="MissingMethodException"/> on 18 (and vice versa). These are the only two members in the whole
/// package with that problem — everything else LinkAudit touches (<c>RecurringBackgroundJobBase</c>,
/// <c>IComposer</c>, <c>ManagementApiControllerBase</c>, <c>IPublishedProperty</c>, the routing and
/// authorization attributes) is declaration-identical across majors.
///
/// Resolving them by name at runtime sidesteps the move entirely. The lookup runs once per concrete
/// content type and is then cached, and both members are read at most once per page (or per finding), so
/// the reflection cost is irrelevant next to the HTTP probes that dominate a crawl.
/// </remarks>
internal static class PublishedContentCompat
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> NameProperties = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> CultureProperties = new();

    /// <summary>The content item's name, or null when it cannot be resolved on this Umbraco version.</summary>
    internal static string? Name(IPublishedContent page)
    {
        PropertyInfo? property = NameProperties.GetOrAdd(page.GetType(), static t => FindProperty(t, nameof(IPublishedContent.Name)));
        return property?.GetValue(page) as string;
    }

    /// <summary>
    /// The cultures this item is published in. Empty when the item is invariant or the member cannot be
    /// resolved — callers already treat an empty result as "invariant, scan once".
    /// </summary>
    internal static IReadOnlyList<string> Cultures(IPublishedContent page)
    {
        PropertyInfo? property = CultureProperties.GetOrAdd(page.GetType(), static t => FindProperty(t, nameof(IPublishedContent.Cultures)));

        // PublishedCultureInfo keeps its name and assembly across majors, so naming it in the cast is safe
        // even though the property that returns it moved.
        return property?.GetValue(page) is IReadOnlyDictionary<string, PublishedCultureInfo> cultures
            ? cultures.Keys.ToList()
            : [];
    }

    /// <summary>
    /// Finds a public instance property by name on the concrete type, falling back to the interfaces it
    /// implements so that explicit interface implementations (where the concrete type exposes nothing
    /// public) still resolve.
    /// </summary>
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
}
