namespace BepInExTranslator.Injector.Core;

/// <summary>
/// 按运行时解析 BepInEx 与翻译模组包来源（本地优先，其次配置 URL，再次默认 pin / latest Release）。
/// </summary>
public sealed class PackageResolver : IPackageResolver
{
    public PackageResolveResult Resolve(UnityRuntimeKind runtime, PackageSourceOptions options)
    {
        if (runtime is not (UnityRuntimeKind.Mono or UnityRuntimeKind.Il2Cpp))
        {
            return PackageResolveResult.Fail(
                InjectorError.UnknownRuntime("无法为未知运行时解析安装包。"));
        }

        try
        {
            var bepinex = ResolveBepInEx(runtime, options);
            var translator = ResolveTranslator(runtime, options);

            // —— 远程 URL 信任（解析阶段尽早失败）——
            foreach (var pkg in new[] { bepinex, translator })
            {
                if (pkg.SourceKind != PackageSourceKind.RemoteUrl)
                {
                    continue;
                }

                var trust = PackageDownloader.ValidateDownloadUrl(pkg.DownloadUrl);
                if (trust != null)
                {
                    return PackageResolveResult.Fail(trust);
                }
            }

            return PackageResolveResult.Ok(new[] { bepinex, translator });
        }
        catch (FileNotFoundException ex)
        {
            return PackageResolveResult.Fail(
                InjectorError.PackageNotFound(ex.Message, ex.FileName));
        }
        catch (InvalidOperationException ex)
        {
            return PackageResolveResult.Fail(
                InjectorError.PackageNotFound(ex.Message));
        }
    }

    public ResolvedPackage ResolveBepInEx(UnityRuntimeKind runtime, PackageSourceOptions options)
    {
        // —— 本地 zip 优先（含 BE.733 等覆盖默认 pre.2）——
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
            version = string.IsNullOrWhiteSpace(options.BepInExIl2CppUrl)
                ? PackageCatalog.BepInEx6Version
                : "url-override";
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

        // —— 默认远程：TranslatorReleaseTag 或 Catalog 默认 latest ——
        var tag = string.IsNullOrWhiteSpace(options.TranslatorReleaseTag)
            ? PackageCatalog.DefaultTranslatorReleaseTag
            : options.TranslatorReleaseTag.Trim();
        var url = PackageCatalog.BuildTranslatorReleaseDownloadUrl(
            runtime,
            tag,
            options.TranslatorGitHubOwner,
            options.TranslatorGitHubRepo);
        var runtimeKey = runtime == UnityRuntimeKind.Mono ? "mono" : "il2cpp";
        var asset = PackageCatalog.DefaultTranslatorReleaseAssetPattern
            .Replace("{runtime}", runtimeKey, StringComparison.Ordinal);

        return new ResolvedPackage
        {
            Kind = PackageKind.TranslatorMod,
            SourceKind = PackageSourceKind.RemoteUrl,
            Runtime = runtime,
            VersionLabel = tag,
            DownloadUrl = url,
            DisplayName = $"Translator（GitHub {tag}/{asset}）",
        };
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
