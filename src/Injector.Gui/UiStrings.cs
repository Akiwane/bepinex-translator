using BepInExTranslator.Injector.Core;

namespace Injector.Gui;

/// <summary>
/// Chinese UI copy for the injector window (Gui-owned; Core errors already carry Message).
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
    public const string Notes = "说明";
    public const string NotDetected = "未探测";
    public const string Unknown = "未知";
    public const string InstallButton = "一键安装";
    public const string OverwritePolicyLabel = "覆盖策略";
    public const string PolicyBackup = "备份后覆盖（推荐）";
    public const string PolicySkip = "跳过已存在";
    public const string PolicyOverwrite = "直接覆盖";
    public const string Ready = "就绪";
    public const string Probing = "正在探测…";
    public const string Installing = "正在安装…";
    public const string ProbeHint =
        "探测通过后可安装。BepInEx 默认 pin：Mono 5.4.23.5 / IL2CPP 6.0.0-pre.2；模组优先 artifacts/。";

    public static string FormatError(InjectorError? error)
    {
        if (error is null)
        {
            return "发生未知错误。";
        }

        if (string.IsNullOrWhiteSpace(error.Detail))
        {
            return error.Message;
        }

        return $"{error.Message}\n{error.Detail}";
    }

    public static string KindHint(InjectorErrorKind kind) => kind switch
    {
        InjectorErrorKind.InvalidPath => "请重新选择有效的游戏目录或 .exe。",
        InjectorErrorKind.NotUnityGame => "路径不像 Unity 游戏（缺少 *_Data）。",
        InjectorErrorKind.UnknownRuntime => "无法判定 Mono / IL2CPP，请人工确认游戏后端。",
        InjectorErrorKind.PackageNotFound => "未找到安装包：请先构建 artifacts/mono 或 artifacts/il2cpp，或配置下载 URL。",
        InjectorErrorKind.DownloadFailed => "下载失败：请检查网络，或改用本地 zip / artifacts。",
        InjectorErrorKind.ExtractFailed => "解压或写入失败：请检查磁盘空间与路径。",
        InjectorErrorKind.PermissionDenied => "权限不足：请检查文件夹权限或以管理员运行。",
        InjectorErrorKind.Cancelled => "已取消安装。",
        _ => string.Empty,
    };
}
