namespace BepInExTranslator.Injector.Core;

/// <summary>
/// 安装/探测等操作的类型化错误（API 不以裸异常作为唯一契约）。
/// </summary>
public enum InjectorErrorKind
{
    None = 0,
    InvalidPath = 1,
    NotUnityGame = 2,
    UnknownRuntime = 3,
    PackageNotFound = 4,
    DownloadFailed = 5,
    ExtractFailed = 6,
    PermissionDenied = 7,
    Cancelled = 8,
    Unexpected = 9,
    /// <summary>目标路径上文件与目录类型冲突（非权限问题）。</summary>
    PathConflict = 10,
}

public sealed class InjectorError
{
    public required InjectorErrorKind Kind { get; init; }

    public required string Message { get; init; }

    public string? Detail { get; init; }

    public static InjectorError InvalidPath(string message, string? detail = null) =>
        new() { Kind = InjectorErrorKind.InvalidPath, Message = message, Detail = detail };

    public static InjectorError NotUnityGame(string message, string? detail = null) =>
        new() { Kind = InjectorErrorKind.NotUnityGame, Message = message, Detail = detail };

    public static InjectorError UnknownRuntime(string message, string? detail = null) =>
        new() { Kind = InjectorErrorKind.UnknownRuntime, Message = message, Detail = detail };

    public static InjectorError PackageNotFound(string message, string? detail = null) =>
        new() { Kind = InjectorErrorKind.PackageNotFound, Message = message, Detail = detail };

    public static InjectorError DownloadFailed(string message, string? detail = null) =>
        new() { Kind = InjectorErrorKind.DownloadFailed, Message = message, Detail = detail };

    public static InjectorError ExtractFailed(string message, string? detail = null) =>
        new() { Kind = InjectorErrorKind.ExtractFailed, Message = message, Detail = detail };

    public static InjectorError PermissionDenied(string message, string? detail = null) =>
        new() { Kind = InjectorErrorKind.PermissionDenied, Message = message, Detail = detail };

    public static InjectorError PathConflict(string message, string? detail = null) =>
        new() { Kind = InjectorErrorKind.PathConflict, Message = message, Detail = detail };

    public static InjectorError Cancelled(string message = "操作已取消。") =>
        new() { Kind = InjectorErrorKind.Cancelled, Message = message };

    public static InjectorError Unexpected(string message, string? detail = null) =>
        new() { Kind = InjectorErrorKind.Unexpected, Message = message, Detail = detail };
}

/// <summary>安装进度（供 GUI / CLI 绑定）。</summary>
public sealed class InstallProgress
{
    public required string Message { get; init; }

    /// <summary>0–100；未知时为 null。</summary>
    public int? Percent { get; init; }

    public InstallPhase Phase { get; init; } = InstallPhase.General;
}

public enum InstallPhase
{
    General = 0,
    Detect = 1,
    ResolvePackages = 2,
    Download = 3,
    Extract = 4,
    WritePlugins = 5,
    WriteConfig = 6,
    Done = 7,
}
