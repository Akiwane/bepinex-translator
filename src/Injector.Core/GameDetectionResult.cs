namespace BepInExTranslator.Injector.Core;

/// <summary>
/// 对游戏目录 / exe 的探测结果（含证据路径，供 GUI 展示）。
/// </summary>
public sealed class GameDetectionResult
{
    public required string GameRoot { get; init; }

    public string? ExePath { get; init; }

    public required UnityRuntimeKind Runtime { get; init; }

    /// <summary>尽力解析的 Unity 版本字符串（如 2021.3.35f1）；失败时为 null。</summary>
    public string? UnityVersion { get; init; }

    public string? DataDirectory { get; init; }

    /// <summary>是否具备 Unity 游戏根目录的最低特征（*_Data 等）。</summary>
    public bool IsValidUnityGame { get; init; }

    /// <summary>探测所依据的路径证据（Managed / il2cpp_data / GameAssembly.dll 等）。</summary>
    public IReadOnlyList<string> EvidencePaths { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();

    /// <summary>类型化错误；成功探测时为 null。</summary>
    public InjectorError? Error { get; init; }

    public string RuntimeDisplayName => Runtime switch
    {
        UnityRuntimeKind.Mono => "Mono → BepInEx 5.x",
        UnityRuntimeKind.Il2Cpp => "IL2CPP → BepInEx 6.x Unity IL2CPP",
        _ => "未知",
    };
}
