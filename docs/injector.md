# Windows 注入器（REQUIREMENTS §7 / v1.1）

独立桌面 GUI：选择游戏目录或 `.exe` → 识别 Unity 版本与 **Mono / IL2CPP** → 自动下载匹配的 **BepInEx** 与本翻译模组包 → 写入标准 BepInEx 布局（doorstop / `winhttp` 等）。

> 平台：**仅 Windows** 运行 GUI。核心库与单元测试可在 Linux CI 上 `dotnet test`。

## 工程

| 项目 | 说明 |
|------|------|
| `src/Injector.Core` | 探测 / 包解析 / 下载 / 落盘（PR #7；可单测） |
| `src/Injector.Gui` | **完整 Avalonia UI**（本 PR #6）：选游戏 → 探测 → 一键安装 / 进度 / 中文错误；依赖 #7 Core |
| `tests/Injector.Core.Tests` | Mono/IL2CPP 探测桩 + 布局规划 / 安装落盘单测（随 #7） |

## Core 公共契约

前端 GUI 应依赖下列入口（实现类：`GameDetector` / `PackageResolver` / `GameInstaller`）：

```csharp
IGameProbe.Detect(path)
  → GameDetectionResult（UnityVersion、Runtime、EvidencePaths、可选 InjectorError）

IPackageResolver.Resolve(runtime, PackageSourceOptions)
  → PackageResolveResult（Packages 或 InjectorError）

IInstaller.InstallAsync(InstallOptions, IProgress<InstallProgress>?, CancellationToken)
  → InstallResult（Success、CopiedFiles、可选 InjectorError）
```

错误用 `InjectorError` / `InjectorErrorKind`（InvalidPath、NotUnityGame、DownloadFailed、PermissionDenied 等），不以裸异常作为唯一 API。

## 构建

```bash
# 建议先构建对应运行时的插件产物（供本地 artifacts 安装）
dotnet build src/BepInExTranslator.Plugin/BepInExTranslator.Plugin.csproj -c Release
dotnet build src/BepInExTranslator.Plugin.Il2Cpp/BepInExTranslator.Plugin.Il2Cpp.csproj -c Release

dotnet build src/Injector.Core/Injector.Core.csproj -c Release
dotnet build src/Injector.Gui/Injector.Gui.csproj -c Release
dotnet test tests/Injector.Core.Tests -c Release
```

GUI（完整安装 UI，绑定 #7 Core）：

```bash
dotnet run --project src/Injector.Gui -c Release
```

> 合并说明：以 PR #7 的 `Injector.Core` 为准；本 PR 提供完整 Gui。合并 #7 时请丢弃其占位 Gui stub。

## 探测规则（与 README 一致）

| 信号 | 判定 | BepInEx |
|------|------|---------|
| 存在 `*_Data/Managed` 且无 `il2cpp_data` | **Mono** | **5.x** |
| 存在 `il2cpp_data` 或 `GameAssembly.dll` | **IL2CPP** | **6.x Unity IL2CPP** |
| 无 `*_Data` | 拒绝安装 | — |

Unity 版本尽力从 `globalgamemanagers` / `data.unity3d` / `*_Data/unity version.txt` 解析，主要用于 UI 展示与日志；**选包以 Mono vs IL2CPP 为准**。

### 探测桩（单测 / 无真机时）

测试会在临时目录伪造：

- Mono：`Game.exe` + `Game_Data/Managed/` + 版本桩文件  
- IL2CPP：`Game.exe` + `Game_Data/il2cpp_data/` 和/或根目录 `GameAssembly.dll`

见 `tests/Injector.Core.Tests/GameDetectorTests.cs`。

## 默认包映射（已 pin）

| 运行时 | BepInEx（注入器默认下载） | 资产 |
|--------|---------------------------|------|
| Mono | **5.4.23.5** | `BepInEx_win_x64_5.4.23.5.zip` |
| IL2CPP | **6.0.0-pre.2** | `BepInEx-Unity.IL2CPP-win-x64-6.0.0-pre.2.zip` |

下载自官方 GitHub Releases：

- `https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip`
- `https://github.com/BepInEx/BepInEx/releases/download/v6.0.0-pre.2/BepInEx-Unity.IL2CPP-win-x64-6.0.0-pre.2.zip`

### IL2CPP：pre.2（注入器）vs BE.733（插件 NuGet）

| 用途 | 版本 | 说明 |
|------|------|------|
| 注入器默认 GitHub zip | **6.0.0-pre.2** | 官方 Releases 可稳定 pin；大多数游戏可用 |
| 插件工程 NuGet | **6.0.0-be.733**（`BepInEx.Unity.IL2CPP`） | 编译插件 API；与运行时 loader 需兼容 |

若某款新游戏需要更新的 Bleeding Edge loader：

1. 从 [builds.bepinex.dev](https://builds.bepinex.dev/projects/bepinex_be) 下载 `BepInEx-Unity.IL2CPP-win-x64-…` zip  
2. 通过 `PackageSourceOptions.BepInExLocalZipPath` 指向该 zip，**或**  
3. 通过 `BepInExIl2CppUrl` 指向 HTTPS URL（主机须在允许列表，含 `builds.bepinex.dev`）

Gui 当前未暴露本地 BE 路径输入框；高级用户可在代码/DI 中设置 `PackageSourceOptions`，或开发机用本地 zip 覆盖。常量见 `PackageCatalog.cs`。

可通过 `PackageSourceOptions.BepInExLocalZipPath` / `BepInExMonoUrl` / `BepInExIl2CppUrl` 覆盖。

## 翻译模组包来源（优先级）

1. `PackageSourceOptions.TranslatorLocalZipPath`  
2. `TranslatorLocalArtifactsDirectory`，或仓库相对默认：`artifacts/mono` / `artifacts/il2cpp`（需先 `dotnet build` 对应插件）  
3. `TranslatorDownloadUrl`（须 HTTPS + 允许主机）  
4. **默认远程**：`TranslatorReleaseTag`（空则 **`latest`**）→  
   - tag=`latest` → `https://github.com/Akiwane/bepinex-translator/releases/latest/download/BepInExTranslator-{mono\|il2cpp}-win.zip`  
   - 其它 tag → `…/releases/download/{tag}/BepInExTranslator-{mono\|il2cpp}-win.zip`

**开发机推荐**：先构建插件，Core 从 `artifacts/` 复制（优先于远程）。  
**独立用户**：无本地 artifacts 时自动走上述 GitHub Release。若 Release 资产尚未发布，下载失败信息会提示构建 `artifacts/` 或设置本地 zip。不要把下载的 zip / DLL 提交进 git。

## 下载 URL 信任

`PackageDownloader`（及解析阶段的远程 URL）强制：

- **仅 HTTPS**
- 主机允许列表：`github.com`、`objects.githubusercontent.com`、`release-assets.githubusercontent.com`、`builds.bepinex.dev`

其它主机返回类型化 `InjectorError`（`DownloadFailed`）。

## 解压安全（zip-slip）

`ExtractZipToGameRoot` / `ExtractTranslatorZip` / `NormalizeZipEntry`：

- 拒绝条目中的 `..`、绝对路径、盘符路径  
- 解压前 `Path.GetFullPath`，断言落盘路径前缀严格位于目标根（游戏根 / plugins）之下（带尾部分隔符）  
- 违例返回 `InjectorError`（`InvalidPath`），不写出任何逃逸文件

## 安装落盘布局

解压 BepInEx 到游戏根后，期望（相对游戏根）：

```text
winhttp.dll                 # 少数包可能为 version.dll
doorstop_config.ini
BepInEx/core/…
dotnet/                     # 仅 IL2CPP 包
BepInEx/plugins/Translator/
  BepInExTranslator.dll
  BepInExTranslator.Core.dll
BepInEx/config/Translator.cfg.example
```

覆盖策略：默认**备份后覆盖**（写入游戏根 `.injector-backup/<时间戳>/`）；也可跳过已存在或直接覆盖。下载缓存默认在游戏根 `.bepinex-translator-cache/`（勿提交）。

## 如何验证（验收）

1. **探测**：对至少一款 Mono、一款 IL2CPP 真机，或跑 `dotnet test` 中的探测桩，运行时判定正确。  
2. **BepInEx**：安装后启动游戏一次，确认出现 `BepInEx/LogOutput.log`（及 `BepInEx/config` 生成）。若无日志，检查 doorstop / `winhttp.dll` 是否在游戏根、杀毒是否拦截。  
3. **本模组**：确认 `BepInEx/plugins/Translator/` 下有 DLL；日志中应有插件加载相关输出。将 `Translator.cfg.example` 对照填写后重启游戏。

## 安全注意

- 路径必须通过 Unity `*_Data` 探测，否则拒绝写入。  
- zip 解压有 zip-slip 防护；下载仅 HTTPS + 主机允许列表。  
- 不提交 API 密钥；包 URL 为公开 Releases，无 secrets。  
- 遵守游戏 EULA / ToS 与 BepInEx 许可。
