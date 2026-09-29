using Injector.Core.Models;

namespace Injector.Core.Abstractions;

/// <summary>
/// Detects Unity version and Mono/IL2CPP layout under a game path.
/// </summary>
public interface IGameProbe
{
    /// <summary>
    /// Probe a directory or .exe path. When <paramref name="path"/> is an .exe, its parent directory is used.
    /// </summary>
    GameProbeResult Detect(string path);
}
