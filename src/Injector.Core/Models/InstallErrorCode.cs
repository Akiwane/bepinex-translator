namespace Injector.Core.Models;

/// <summary>
/// Stable error codes for probe/install failures (GUI maps these to Chinese banners).
/// </summary>
public enum InstallErrorCode
{
    None = 0,
    InvalidPath = 1,
    NotUnity = 2,
    UnknownRuntime = 3,
    DownloadFailed = 4,
    UnpackOrWriteFailed = 5,
    PermissionDenied = 6,
    AlreadyInstalled = 7,
    Cancelled = 8,
    Unexpected = 99,
}
