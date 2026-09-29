namespace Injector.Core.Localization;

/// <summary>
/// Chinese user-facing strings for the injector GUI and stub installer messages.
/// </summary>
public static class UiStrings
{
    public const string AppTitle = "BepInEx Translator 注入器";
    public const string SelectGame = "选择游戏";
    public const string BrowseFolder = "浏览文件夹…";
    public const string BrowseExe = "选择 exe…";
    public const string PathPlaceholder = "请选择游戏目录或 .exe 文件";
    public const string ProbeResults = "探测结果";
    public const string UnityVersion = "Unity 版本";
    public const string Runtime = "运行时";
    public const string Evidence = "依据";
    public const string Unknown = "未知";
    public const string NotDetected = "未探测";
    public const string InstallButton = "一键安装";
    public const string OverwriteAndInstall = "覆盖并安装";
    public const string SkipInstall = "跳过";
    public const string Ready = "就绪";
    public const string Probing = "正在探测…";
    public const string Installing = "正在安装…";
    public const string SuccessBanner = "安装完成（当前为 stub：未下载真实 BepInEx 压缩包）";
    public const string StubNote = "当前使用 stub 安装器：仅模拟进度；不会默认下载真实 BepInEx。";

    public const string ErrInvalidPath = "路径无效：请选择存在的游戏目录或 .exe 文件。";
    public const string ErrNotUnity = "无法识别为 Unity 游戏：未找到 *_Data 目录，也没有 IL2CPP 标记。";
    public const string ErrUnknownRuntime = "已识别为 Unity 游戏，但无法判断 Mono / IL2CPP，请手动确认后再试。";
    public const string ErrDownloadFailed = "下载失败：无法获取 BepInEx 或模组包，请检查网络后重试。";
    public const string ErrUnpackWrite = "解压或写入失败：请确认磁盘空间与目标路径可写。";
    public const string ErrPermission = "权限不足：无法写入游戏目录，请以管理员身份运行或检查文件夹权限。";
    public const string ErrAlreadyInstalled = "检测到已安装 BepInEx / 本模组。可选择覆盖安装，或跳过。";
    public const string ErrCancelled = "已取消安装。";
    public const string ErrUnexpected = "发生未预期错误，请查看状态详情。";

    public const string StageDetecting = "探测游戏…";
    public const string StageResolving = "解析匹配包…";
    public const string StageDownloadingBepInEx = "下载 BepInEx…";
    public const string StageDownloadingPlugin = "下载翻译模组…";
    public const string StageExtracting = "解压安装包…";
    public const string StageWritingLayout = "写入目录布局…";
    public const string StageCopyingConfig = "复制配置示例…";
    public const string StageDone = "完成";

    public const string RuntimeMono = "Mono";
    public const string RuntimeIl2Cpp = "IL2CPP";
    public const string RuntimeUnknown = "Unknown";

    public static string RuntimeLabel(Models.RuntimeKind kind) => kind switch
    {
        Models.RuntimeKind.Mono => RuntimeMono,
        Models.RuntimeKind.Il2Cpp => RuntimeIl2Cpp,
        _ => RuntimeUnknown,
    };

    public static string StageLabel(Models.InstallStage stage) => stage switch
    {
        Models.InstallStage.Detecting => StageDetecting,
        Models.InstallStage.Resolving => StageResolving,
        Models.InstallStage.DownloadingBepInEx => StageDownloadingBepInEx,
        Models.InstallStage.DownloadingPlugin => StageDownloadingPlugin,
        Models.InstallStage.Extracting => StageExtracting,
        Models.InstallStage.WritingLayout => StageWritingLayout,
        Models.InstallStage.CopyingConfig => StageCopyingConfig,
        Models.InstallStage.Done => StageDone,
        _ => stage.ToString(),
    };

    public static string ErrorMessage(Models.InstallErrorCode code) => code switch
    {
        Models.InstallErrorCode.InvalidPath => ErrInvalidPath,
        Models.InstallErrorCode.NotUnity => ErrNotUnity,
        Models.InstallErrorCode.UnknownRuntime => ErrUnknownRuntime,
        Models.InstallErrorCode.DownloadFailed => ErrDownloadFailed,
        Models.InstallErrorCode.UnpackOrWriteFailed => ErrUnpackWrite,
        Models.InstallErrorCode.PermissionDenied => ErrPermission,
        Models.InstallErrorCode.AlreadyInstalled => ErrAlreadyInstalled,
        Models.InstallErrorCode.Cancelled => ErrCancelled,
        _ => ErrUnexpected,
    };
}
