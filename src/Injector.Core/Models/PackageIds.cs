namespace Injector.Core.Models;

/// <summary>
/// Resolved package identifiers / download URLs for a given <see cref="RuntimeKind"/>.
/// </summary>
public sealed class PackageIds
{
    public required string BepInExPackageId { get; init; }

    public required string PluginPackageId { get; init; }

    /// <summary>
    /// Placeholder URL for the BepInEx zip.
    /// Real downloads are gated behind <see cref="Abstractions.IPackageDownloader"/> (stub skips).
    /// </summary>
    public required string BepInExDownloadUrl { get; init; }

    /// <summary>
    /// Placeholder URL for the Translator plugin package.
    /// </summary>
    public required string PluginDownloadUrl { get; init; }

    public required string Description { get; init; }
}
