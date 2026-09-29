namespace BepInExTranslator.Injector.Core;

public enum PackageKind
{
    BepInEx = 0,
    TranslatorMod = 1,
}

public enum PackageSourceKind
{
    /// <summary>官方 / 配置的远程 URL（GitHub Releases 等）。</summary>
    RemoteUrl = 0,

    /// <summary>本地 zip 文件路径。</summary>
    LocalZip = 1,

    /// <summary>本地构建产物目录（如 artifacts/mono）。</summary>
    LocalDirectory = 2,
}

/// <summary>
/// 解析得到的待安装包描述（尚未下载/复制）。
/// </summary>
public sealed class ResolvedPackage
{
    public required PackageKind Kind { get; init; }

    public required PackageSourceKind SourceKind { get; init; }

    public required UnityRuntimeKind Runtime { get; init; }

    /// <summary>展示用版本或标签（如 5.4.23.5 / local-artifacts）。</summary>
    public required string VersionLabel { get; init; }

    /// <summary>远程下载 URL；仅 RemoteUrl 时有效。</summary>
    public string? DownloadUrl { get; init; }

    /// <summary>本地 zip 或目录路径。</summary>
    public string? LocalPath { get; init; }

    public required string DisplayName { get; init; }
}
