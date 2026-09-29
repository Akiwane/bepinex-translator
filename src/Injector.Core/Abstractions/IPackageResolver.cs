using Injector.Core.Models;

namespace Injector.Core.Abstractions;

/// <summary>
/// Maps <see cref="RuntimeKind"/> to BepInEx / plugin package ids and download URLs.
/// </summary>
public interface IPackageResolver
{
    /// <summary>
    /// Resolve package metadata. Returns null when runtime is <see cref="RuntimeKind.Unknown"/>.
    /// </summary>
    PackageIds? Resolve(RuntimeKind runtime);
}
