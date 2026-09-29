namespace Injector.Core.Models;

/// <summary>
/// Request to install BepInEx + Translator into a probed game directory.
/// </summary>
public sealed class InstallRequest
{
    public required string GamePath { get; init; }

    public required GameProbeResult Probe { get; init; }

    /// <summary>When true, overwrite an existing BepInEx / plugin layout instead of aborting.</summary>
    public bool Overwrite { get; init; }
}
