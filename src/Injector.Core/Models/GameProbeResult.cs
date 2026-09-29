namespace Injector.Core.Models;

/// <summary>
/// Result of probing a candidate game path (directory or .exe).
/// </summary>
public sealed class GameProbeResult
{
    public required string GamePath { get; init; }

    /// <summary>Best-effort Unity editor/player version string, if found.</summary>
    public string? UnityVersion { get; init; }

    public RuntimeKind Runtime { get; init; }

    /// <summary>Short Chinese/English evidence line shown in the UI (e.g. Managed vs il2cpp_data).</summary>
    public required string Evidence { get; init; }

    /// <summary>True when the path looks like a Unity game with a known or unknown runtime.</summary>
    public bool IsValid { get; init; }

    /// <summary>Optional machine-readable failure reason for invalid probes.</summary>
    public InstallErrorCode? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public static GameProbeResult Invalid(string gamePath, InstallErrorCode code, string message) =>
        new()
        {
            GamePath = gamePath,
            UnityVersion = null,
            Runtime = RuntimeKind.Unknown,
            Evidence = string.Empty,
            IsValid = false,
            ErrorCode = code,
            ErrorMessage = message,
        };
}
