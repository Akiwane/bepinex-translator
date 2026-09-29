namespace Injector.Core.Models;

/// <summary>
/// Outcome of <see cref="Abstractions.IInstaller.InstallAsync"/>.
/// </summary>
public sealed class InstallResult
{
    public bool Success { get; init; }

    public required string Message { get; init; }

    public InstallErrorCode ErrorCode { get; init; }

    /// <summary>True when the target already has BepInEx and Overwrite was false.</summary>
    public bool AlreadyInstalled => ErrorCode == InstallErrorCode.AlreadyInstalled;

    public static InstallResult Ok(string message) =>
        new()
        {
            Success = true,
            Message = message,
            ErrorCode = InstallErrorCode.None,
        };

    public static InstallResult Fail(InstallErrorCode code, string message) =>
        new()
        {
            Success = false,
            Message = message,
            ErrorCode = code,
        };
}
