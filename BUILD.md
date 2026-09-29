# BUILD.md — 构建与打包

## 环境

- [.NET SDK 8+](https://dotnet.microsoft.com/download)（用于还原/测试/构建）
- 可选：目标游戏的 `Managed` 或 IL2CPP 互操作程序集（做游戏特化强化时需要）

本仓库已配置 `nuget.config`，同时使用 nuget.org 与 [BepInEx NuGet](https://nuget.bepinex.dev/)。

## 仓库结构

```text
src/
  BepInExTranslator.Core/           # 纯逻辑（netstandard2.0）
  BepInExTranslator.Plugin/         # BepInEx 5 Mono 插件（net472）
  BepInExTranslator.Plugin.Il2Cpp/  # BepInEx 6 IL2CPP 插件（net6.0）
tests/
  BepInExTranslator.Core.Tests/     # 无 Unity 的单元测试
samples/                            # 示例 cfg / JSON
artifacts/mono|il2cpp/              # 构建输出（gitignore）
```

## 单元测试（不需要 Unity）

```bash
dotnet test BepInExTranslator.sln -c Release
```

覆盖：

- `TextHasher` 稳定性与换行归一化
- `TemplateFiller` / JSON 转义
- `TranslationCache` 读写与「有译文跳过 API」
- `FontSizeAdjuster`
- `JsonPathExtractor`（含 OpenAI `choices[0].message.content` 形态）

## 构建 Mono 插件（BepInEx 5）

```bash
dotnet build src/BepInExTranslator.Plugin/BepInExTranslator.Plugin.csproj -c Release
```

输出目录：`artifacts/mono/`

部署到游戏：

```text
<Game>/BepInEx/plugins/BepInExTranslator/
  BepInExTranslator.dll
  BepInExTranslator.Core.dll
```

依赖说明：

- 编译期使用 `UnityEngine.Modules` **stub**（NuGet），便于无游戏程序集时构建通用插件。
- 运行时挂接通过**反射**查找 `UnityEngine.UI.Text` / `TMPro.TMP_Text` / `UnityEngine.TextMesh`，因此：
  - 游戏缺少某程序集 → 对应 Hook 跳过，插件仍加载。
  - **未知**：特定 Unity / TMP 小版本若改掉属性/方法签名，Hook 可能失败（日志会有 Warning）。

若要对某一款游戏做强化（直接引用其 `UnityEngine.UI.dll`、`Unity.TextMeshPro.dll`），在 csproj 中增加 `Reference` HintPath 指向该游戏 `*_Data/Managed`，并可用更具体的 Harmony 补丁替换反射挂接。

## 构建 IL2CPP 插件（BepInEx 6）

```bash
dotnet build src/BepInExTranslator.Plugin.Il2Cpp/BepInExTranslator.Plugin.Il2Cpp.csproj -c Release
```

输出目录：`artifacts/il2cpp/`

部署到 **BepInEx 6 Unity IL2CPP** 游戏：

```text
<Game>/BepInEx/plugins/BepInExTranslator/
  BepInExTranslator.dll
  BepInExTranslator.Core.dll
```

### IL2CPP 注意事项（请当限制而非保证）

1. 必须使用与游戏匹配的 **BepInEx 6 IL2CPP** 构建；Mono 版 DLL 无法在 IL2CPP 游戏中加载。
2. NuGet 包 `BepInEx.Unity.IL2CPP` 提供插件宿主 API；真实游戏类型由 BepInEx 启动时的 Il2CppInterop 生成。反射 Hook 在多数情况下可用，但：
   - 部分 IL2CPP 游戏对 `string` 属性 setter 的互操作签名不同；
   - TextMeshPro 版本差异可能导致 `SetText` 重载找不到（已容忍失败）。
3. 通用 IL2CPP 构建为降低对游戏 interop 的编译依赖，**不内置** Il2Cpp `MonoBehaviour` 泵。缓存命中仍在 `set_text` 前缀中同步替换；异步新译文会尝试直接回写，失败则写入 JSON，下次启动生效。游戏特化构建可注册自定义 Il2Cpp MonoBehaviour，每帧调用 `UnityMainThread.Pump()`。
4. 字体：`CreateDynamicFontFromOSFont` / BuiltIn Arial 在 IL2CPP 上的可用性因引擎裁剪而异；CJK 显示不出来时优先改系统字体名或使用游戏自带 TMP 字体。

### 推荐的 IL2CPP「游戏特化」流程

当通用包在某游戏上 Hook 失败时：

1. 用 [Cpp2IL](https://github.com/SamboyCoding/Cpp2IL) / BepInEx 生成的 interop 程序集确认 `TMP_Text` / `Text` 真实类型名。
2. 在本机将 HintPath 指到该游戏 interop DLL，增加显式 `HarmonyPatch`。
3. 仅分发特化构建给该游戏，避免把游戏程序集提交进本仓库。

## 配置生成位置

| 文件 | 路径 |
|------|------|
| BepInEx 配置 | `BepInEx/config/com.akiwane.bepinextranslator.cfg` |
| 翻译产物（默认） | `BepInEx/plugins/BepInExTranslator/translations.json` |

首次运行后由 BepInEx 自动写出 cfg；可参考 `samples/`。

## 验收对照（REQUIREMENTS §5）

1. 未缓存文本走接口；有产物断网可替换 → `OnDemandTranslator` + `TranslationCache`
2. 人工改 JSON 重启生效且不请求 → 单测 `ManualEdit_NonEmptyTranslation_IsUsedWithoutApi`
3. UGUI / TMP / TextMesh 替换路径 → 反射 Hook（运行时依赖游戏程序集）
4. Mono / IL2CPP 双工程产物 → 本文件两套 build
5. HTTP 模板与 OpenAI 兼容 → `BackendType` 切换
6. 长译文缩字号 / 三字体源 → `Layout` + `Font` 配置

## 故障排查

| 现象 | 检查 |
|------|------|
| 插件未加载 | BepInEx 版本是否与 Mono/IL2CPP 匹配；看 `BepInEx/LogOutput.log` |
| 无翻译请求 | 产物里是否已有非空译文；`BackendType`/URL/Key 是否有效 |
| 有译文但不显示 | 是否自定义 UI；对应 Hook 是否在日志中 “Hooked …” |
| 中文乱码/方块 | 字体不含字形；改 `Font.Source=System` 与 `SystemFontName` |
| IL2CPP 崩溃 | 先关 `EnableTextMeshPro` 二分；对照 TMP/Unity 版本差异 |
