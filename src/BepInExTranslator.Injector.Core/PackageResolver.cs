namespace BepInExTranslator.Injector.Core;

/// <summary>
/// 按运行时解析 BepInEx 与翻译模组包来源（本地优先，其次配置 URL，再次默认 pin）。
/// </summary>
public sealed class PackageResolver
{
    public IReadOnlyList<ResolvedPackage> Resolve(UnityRuntimeKind runtime, PackageSourceOptions options)
    {
        if (runtime is not (UnityRuntimeKind.Mono or UnityRuntimeKind.Il2Cpp))
        {
            throw new InvalidOperationException("无法为未知运行时解析安装包。");
        }

        return new[]
        {
            ResolveBepInEx(runtime, options),
            ResolveTranslator(runtime, options),
        };
    }

    public ResolvedPackage ResolveBepInEx(UnityRuntimeKind runtime, PackageSourceOptions options)
    {
        // —— 本地 zip 优先 ——
        if (!string.IsNullOrWhiteSpace(options.BepInExLocalZipPath))
        {
            var local = Path.GetFullPath(options.BepInExLocalZipPath);
            if (!File.Exists(local))
            {
                throw new FileNotFoundException("指定的 BepInEx 本地 zip 不存在。", local);
            }

            return new ResolvedPackage
            {
                Kind = PackageKind.BepInEx,
                SourceKind = PackageSourceKind.LocalZip,
                Runtime = runtime,
                VersionLabel = "local-zip",
                LocalPath = local,
                DisplayName = $"BepInEx（本地）{Path.GetFileName(local)}",
            };
        }

        // —— URL：用户覆盖或目录 pin ——
        string url;
        string version;
        if (runtime == UnityRuntimeKind.Mono)
        {
            url = string.IsNullOrWhiteSpace(options.BepInExMonoUrl)
                ? PackageCatalog.BepInEx5DownloadUrl
                : options.BepInExMonoUrl.Trim();
            version = PackageCatalog.BepInEx5Version;
        }
        else
        {
            url = string.IsNullOrWhiteSpace(options.BepInExIl2CppUrl)
                ? PackageCatalog.BepInEx6DownloadUrl
                : options.BepInExIl2CppUrl.Trim();
            version = PackageCatalog.BepInEx6Version;
        }

        return new ResolvedPackage
        {
            Kind = PackageKind.BepInEx,
            SourceKind = PackageSourceKind.RemoteUrl,
            Runtime = runtime,
            VersionLabel = version,
            DownloadUrl = url,
            DisplayName = $"BepInEx {version} ({(runtime == UnityRuntimeKind.Mono ? "Mono/win-x64" : "Unity.IL2CPP/win-x64")})",
        };
    }

    public ResolvedPackage ResolveTranslator(UnityRuntimeKind runtime, PackageSourceOptions options)
    {
        // —— 1) 本地 zip ——
        if (!string.IsNullOrWhiteSpace(options.TranslatorLocalZipPath))
        {
            var zip = Path.GetFullPath(options.TranslatorLocalZipPath);
            if (!File.Exists(zip))
            {
                throw new FileNotFoundException("指定的翻译模组本地 zip 不存在。", zip);
            }

            return new ResolvedPackage
            {
                Kind = PackageKind.TranslatorMod,
                SourceKind = PackageSourceKind.LocalZip,
                Runtime = runtime,
                VersionLabel = "local-zip",
                LocalPath = zip,
                DisplayName = $"Translator（本地 zip）{Path.GetFileName(zip)}",
            };
        }

        // —— 2) 本地 artifacts 目录 ——
        var artifactsDir = ResolveArtifactsDirectory(runtime, options);
        if (artifactsDir != null && Directory.Exists(artifactsDir) && HasTranslatorDlls(artifactsDir))
        {
            return new ResolvedPackage
            {
                Kind = PackageKind.TranslatorMod,
                SourceKind = PackageSourceKind.LocalDirectory,
                Runtime = runtime,
                VersionLabel = "local-artifacts",
                LocalPath = artifactsDir,
                DisplayName = $"Translator（本地 artifacts）{artifactsDir}",
            };
        }

        // —— 3) 显式远程 URL ——
        if (!string.IsNullOrWhiteSpace(options.TranslatorDownloadUrl))
        {
            return new ResolvedPackage
            {
                Kind = PackageKind.TranslatorMod,
                SourceKind = PackageSourceKind.RemoteUrl,
                Runtime = runtime,
                VersionLabel = options.TranslatorReleaseTag ?? "configured-url",
                DownloadUrl = options.TranslatorDownloadUrl.Trim(),
                DisplayName = "Translator（远程 URL）",
            };
        }

        // —— 4) 按约定拼 GitHub Release URL（若 tag 已配置）——
        if (!string.IsNullOrWhiteSpace(options.TranslatorReleaseTag))
        {
            var runtimeKey = runtime == UnityRuntimeKind.Mono ? "mono" : "il2cpp";
            var asset = PackageCatalog.DefaultTranslatorReleaseAssetPattern
                .Replace("{runtime}", runtimeKey, StringComparison.Ordinal);
            var url =
                $"https://github.com/{options.TranslatorGitHubOwner}/{options.TranslatorGitHubRepo}/releases/download/{options.TranslatorReleaseTag}/{asset}";

            return new ResolvedPackage
            {
                Kind = PackageKind.TranslatorMod,
                SourceKind = PackageSourceKind.RemoteUrl,
                Runtime = runtime,
                VersionLabel = options.TranslatorReleaseTag!,
                DownloadUrl = url,
                DisplayName = $"Translator（GitHub {options.TranslatorReleaseTag}/{asset}）",
            };
        }

        throw new InvalidOperationException(
            "未找到翻译模组包：请先构建 artifacts/mono 或 artifacts/il2cpp，" +
            "或在 UI 中指定本地 zip / 下载 URL / Release tag。详见 docs/injector.md。");
    }

    internal static string? ResolveArtifactsDirectory(UnityRuntimeKind runtime, PackageSourceOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.TranslatorLocalArtifactsDirectory))
        {
            return Path.GetFullPath(options.TranslatorLocalArtifactsDirectory);
        }

        if (string.IsNullOrWhiteSpace(options.RepositoryRoot))
        {
            return null;
        }

        var leaf = runtime == UnityRuntimeKind.Mono ? "mono" : "il2cpp";
        return Path.GetFullPath(Path.Combine(options.RepositoryRoot, "artifacts", leaf));
    }

    internal static bool HasTranslatorDlls(string directory)
    {
        var plugin = Path.Combine(directory, "BepInExTranslator.dll");
        var core = Path.Combine(directory, "BepInExTranslator.Core.dll");
        return File.Exists(plugin) && File.Exists(core);
    }
}
