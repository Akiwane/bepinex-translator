namespace Injector.Core.Models;

/// <summary>
/// Ordered install pipeline stages reported via <see cref="InstallProgress"/>.
/// </summary>
public enum InstallStage
{
    Detecting = 0,
    Resolving = 1,
    DownloadingBepInEx = 2,
    DownloadingPlugin = 3,
    Extracting = 4,
    WritingLayout = 5,
    CopyingConfig = 6,
    Done = 7,
}
