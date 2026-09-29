using Injector.Core.Models;

namespace Injector.Core.Abstractions;

/// <summary>
/// Installs matching BepInEx + Translator into a game directory.
/// </summary>
public interface IInstaller
{
    Task<InstallResult> InstallAsync(
        InstallRequest request,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
