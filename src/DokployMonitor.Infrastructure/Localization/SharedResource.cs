namespace DokployMonitor.Infrastructure.Localization;

/// <summary>
/// Marker type for the shared translation resource.
///
/// Lives in Infrastructure so that validators in both layers can inject
/// <c>IStringLocalizer&lt;SharedResource&gt;</c>. The database-backed localizer ignores the
/// resource type; the type only exists to satisfy the generic localizer contract.
/// </summary>
public sealed class SharedResource
{
}
