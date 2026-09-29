# BUILD.md — 构建与打包

## 环境

- [.NET SDK 8+](https://dotnet.microsoft.com/download)（用于还原/测试/构建）
- 可选：目标游戏的 `Managed` 或 IL2CPP 互操作程序集（做游戏特化强化时需要）

本仓库已配置 `nuget.config`，同时使用 nuget.org 与 [BepInEx NuGet](https://nuget.bepinex.dev/)。

## 仓库结构

```text
config/Translator.cfg.example       # 配置字段契约（权威）
docs/fonts.md                       # 三字体源路径说明
docs/translations.example.json      # 产物 JSON 样例
docs/injector.md                    # Windows 注入器 GUI（§7）
src/
  BepInExTranslator.Core/           # 纯逻辑（netstandard2.0）
  BepInExTranslator.Plugin/         # BepInEx 5 Mono 插件（net472）
  BepInExTranslator.Plugin.Il2Cpp/  # BepInEx 6 IL2CPP 插件（net6.0）
  Injector.Core/                    # 注入器核心（探测/下载/布局）
  Injector.Gui/                     # Avalonia GUI stub（完整 UI 归前端）
tests/
  BepInExTranslator.Core.Tests/     # 无 Unity 的单元测试
  Injector.Core.Tests/
artifacts/mono|il2cpp/              # 构建输出（gitignore）
```

## 单元测试（不需要 Unity）

```bash
dotnet test BepInExTranslator.sln -c Release
```

覆盖：`TextHasher`、`TemplateFiller`（含 `{source}`/`{targetLanguage}`/`{hash}`）、`TranslationCache`、`FontSizeAdjuster`、`JsonPathExtractor`（含 `choices.0.message.content`）、按需翻译跳过 API；以及注入器探测桩（Mono/IL2CPP）、布局规划与本地 zip 落盘。

## Windows 注入器

详见 [`docs/injector.md`](docs/injector.md)。**Core** 为可测安装逻辑；**Gui** 为 Avalonia stub（完整 UI 归前端）。

```bash
dotnet build src/Injector.Core/BepInExTranslator.Injector.Core.csproj -c Release
dotnet build src/Injector.Gui/BepInExTranslator.Injector.Gui.csproj -c Release
```

默认 BepInEx pin：Mono → `5.4.23.5` win-x64；IL2CPP → `6.0.0-pre.2` Unity.IL2CPP win-x64。模组默认取自 `artifacts/mono|il2cpp`。

公共契约：`IGameProbe` / `IPackageResolver` / `IInstaller` + `InjectorError` / `InstallProgress`。

## 构建 Mono 插件（BepInEx 5）

```bash
dotnet build src/BepInExTranslator.Plugin/BepInExTranslator.Plugin.csproj -c Release
```

输出：`artifacts/mono/`

部署：

```text
<Game>/BepInEx/plugins/Translator/
  BepInExTranslator.dll
  BepInExTranslator.Core.dll
```

编译期使用 `UnityEngine.Modules` stub；运行时反射挂接 `UnityEngine.UI.Text` / `TMPro.TMP_Text` / `UnityEngine.TextMesh`。缺程序集则跳过对应 Hook。

## 构建 IL2CPP 插件（BepInEx 6）

```bash
dotnet build src/BepInExTranslator.Plugin.Il2Cpp/BepInExTranslator.Plugin.Il2Cpp.csproj -c Release
```

输出：`artifacts/il2cpp/` → 同上目录结构，但必须搭配 **BepInEx 6 Unity IL2CPP**。

### IL2CPP 限制（非保证）

1. Mono 版 DLL 无法在 IL2CPP 游戏中加载。
2. 通用构建不内置 Il2Cpp `MonoBehaviour` 泵：缓存命中仍同步替换；异步新译文会尝试直接回写，失败则写入 JSON，下次启动生效。游戏特化可注册自定义组件每帧调用 `UnityMainThread.Pump()`。
3. TMP / 字体 API 因引擎版本与裁剪而异，见 [`docs/fonts.md`](docs/fonts.md)。

## 配置与产物位置

| 文件 | 路径 |
|------|------|
| 配置契约示例 | 仓库 `config/Translator.cfg.example` |
| 运行时配置 | `BepInEx/config/com.akiwane.bepinextranslator.cfg`（BepInEx 自动生成；字段名与契约对齐） |
| 翻译产物（默认） | `BepInEx/plugins/Translator/translations.json`（`General.ProductJsonPath`） |

`BackendType`：`HttpTemplate` | `LlmOpenAiCompatible`  
占位符：`{source}` `{targetLanguage}` `{hash}`  
字体：`FontSourceType` = `Unity` | `System` | `CustomFile`，路径见 `FontPath`。

## 验收对照（REQUIREMENTS §5 / TEST-CASES）

见 [`TEST-CASES.md`](TEST-CASES.md)。本地可先跑 `dotnet test` 覆盖纯逻辑 AC 子集。

## 故障排查

| 现象 | 检查 |
|------|------|
| 插件未加载 | BepInEx 版本是否与 Mono/IL2CPP 匹配；`BepInEx/LogOutput.log` |
| 无翻译请求 | 产物非空译文？`BackendType` / `EndpointUrl` / `ApiKey` |
| 有译文但不显示 | 自定义 UI？日志是否有 `Hooked …` |
| 中文方块 | `FontSourceType` / `FontPath`（docs/fonts.md） |
| IL2CPP 崩溃 | 先关 `EnableTextMeshPro` 二分 |
