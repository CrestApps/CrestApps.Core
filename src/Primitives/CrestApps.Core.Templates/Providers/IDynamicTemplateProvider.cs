namespace CrestApps.Core.Templates.Providers;

/// <summary>
/// Marks an <see cref="ITemplateProvider"/> whose templates can change while the application runs,
/// such as templates stored in a database and edited by users.
/// </summary>
/// <remarks>
/// A template service may cache templates from other providers for the lifetime of the application,
/// but reads a dynamic provider again in every scope, so an added, edited, or deleted template applies
/// to the next request. When a dynamic template has the same identifier as a template from another
/// provider, the other provider's template is used, so a stored template cannot replace a template
/// the application ships with.
/// </remarks>
public interface IDynamicTemplateProvider : ITemplateProvider
{
}
