namespace Injector.Core.Models;

/// <summary>
/// Progress snapshot for the install pipeline (stage + optional percent + status text).
/// </summary>
public sealed class InstallProgress
{
    public InstallStage Stage { get; init; }

    /// <summary>0–100 when known; null means indeterminate for the current stage.</summary>
    public int? Percent { get; init; }

    public required string Message { get; init; }

    public static InstallProgress Create(InstallStage stage, string message, int? percent = null) =>
        new()
        {
            Stage = stage,
            Percent = percent,
            Message = message,
        };
}
