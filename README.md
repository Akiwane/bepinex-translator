# BepInEx Translator

基于 [BepInEx](https://docs.bepinex.dev/) 的 Unity 游戏**通用按需翻译**插件：运行时 Hook `UGUI Text` / `TextMeshPro` / 旧版 `TextMesh`，把未本地化文本替换为目标语言（默认 **zh-CN**）。

当前产品版本：**1.0.0-beta**。v1.0 **无**游戏内/桌面配置 UI，仅使用 BepInEx ConfigFile + JSON 翻译产物。

构建步骤见 [`BUILD.md`](BUILD.md)。

> **简要说明**：本项目为非官方第三方个人/学习用途工具，与 Unity、BepInEx 及任何游戏发行商无关；使用风险自负，详见下方[免责声明](#免责声明--disclaimer)。本项目为**纯 AI 编程**产出，详见 [AI 声明](#ai-声明--ai-disclosure)。以 [MIT License](./LICENSE) 发布；**切勿**提交 API 密钥。

## 配置与样例（字段契约）

下列文件中的**字段名是插件后端的实现契约**。后端按这些名字接线；**切勿**把真实 API 密钥提交进仓库。

| 产物 | 路径 | 说明 |
|------|------|------|
| 配置模板 | [`config/Translator.cfg.example`](./config/Translator.cfg.example) | `TargetLanguage`、`BackendType`、`HttpTemplate` / `LlmOpenAiCompatible`、超时、产物路径、自动缩字号、字体等 |
| 字体路径说明 | [`docs/fonts.md`](./docs/fonts.md) | Unity / 系统字体 / 自定义文件路径各一例 |
| 翻译产物样例 | [`docs/translations.example.json`](./docs/translations.example.json) | JSON **数组**；条目：`hash`、`source`、`translation`（空字符串 = 未译） |

复制示例到游戏的 `BepInEx/config/` 后，将插件生成的 cfg 或本示例字段对齐填写。

## 功能一览

| 能力 | 说明 |
|------|------|
| 文本源 | `UnityEngine.UI.Text`、`TMPro.TMP_Text`、`UnityEngine.TextMesh`（反射软依赖，缺程序集则跳过） |
| 按需翻译 | 仅当产物 JSON 中 `translation` **非空**时跳过 API |
| 产物 | 可读 JSON 数组：`hash` + `source` + `translation` |
| 后端 | `HttpTemplate`（通用 HTTP JSON） / `LlmOpenAiCompatible`（Chat Completions） |
| 布局 | `AutoShrinkFontSize`：长译文自动缩字号 + 尝试开启换行 |
| 字体 | `FontSourceType`：`Unity` / `System` / `CustomFile` + `FontPath`（详见 [`docs/fonts.md`](./docs/fonts.md)） |
| 运行时 | **Mono → BepInEx 5**；**IL2CPP → BepInEx 6**（双目标工程） |

## 安装到游戏

### 推荐：Windows 注入器

`Injector.Gui`（Avalonia）选择游戏目录后，会自动：

1. **探测** Mono（`*_Data/Managed`）或 IL2CPP（`il2cpp_data` / `GameAssembly.dll`）
2. **下载**匹配的 BepInEx（Mono → 5.x；IL2CPP → 6.x Unity IL2CPP）
3. **放入插件**到 `BepInEx/plugins/Translator/`

```bash
dotnet build src/Injector.Core/Injector.Core.csproj -c Release
dotnet build src/Injector.Gui/Injector.Gui.csproj -c Release
dotnet run --project src/Injector.Gui
```

开发机可先构建对应运行时的插件，注入器会优先从 `artifacts/mono` / `artifacts/il2cpp` 复制；无本地产物时再尝试 GitHub Release。

### 1. 确认游戏后端（手动安装时）

- 存在 `*_Data/Managed` 且无 `il2cpp_data` → **Mono** → **BepInEx 5.x**
- 存在 `il2cpp_data` / `GameAssembly.dll` → **IL2CPP** → **BepInEx 6.x Unity IL2CPP**

> **未知/不保证**：不声称兼容全部 Unity 版本与全部自定义 UI 框架。自研 SDF / NGUI / FairyGUI 等不在 v1 范围。

### 2. 安装 BepInEx 并放入插件

按 [BepInEx 安装文档](https://docs.bepinex.dev/articles/user_guide/installation/index.html) 安装后，将构建产物复制到：

```text
<Game>/BepInEx/plugins/Translator/
  BepInExTranslator.dll
  BepInExTranslator.Core.dll
  translations.json          # 可选；也可按 ProductJsonPath 配置
```

- Mono 输出：`artifacts/mono/`
- IL2CPP 输出：`artifacts/il2cpp/`

**不要混用** Mono / IL2CPP 产物与错误的 BepInEx 大版本。

### 3. 配置

启动一次后编辑 `BepInEx/config/` 下由插件写出的 cfg，或对照 [`config/Translator.cfg.example`](./config/Translator.cfg.example)。

默认产物路径（可改 `ProductJsonPath`）：

```text
BepInEx/plugins/Translator/translations.json
```

## 翻译产物 JSON

格式（人工可精翻）：

```json
[
  {
    "hash": "…",
    "source": "Start Game",
    "translation": "开始游戏"
  }
]
```

- 程序以 `hash`（原文 UTF-8 SHA-256 前 32 hex，换行已归一化）为键。
- `translation` **非空** → 直接替换，**不调 API**；空字符串 = 未译，仍会按需请求。
- 人工改 JSON 后**重启游戏**生效（v1 无热重载）。
- 断网时只要产物里有非空译文，仍可替换。

## 配置后端

### A. 通用 HTTP JSON 模板（`BackendType = HttpTemplate`）

```ini
[General]
BackendType = HttpTemplate
ProductJsonPath = BepInEx/plugins/Translator/translations.json

[HttpTemplate]
EndpointUrl = https://example.com/api/translate
HeadersJson = {"Authorization":"Bearer YOUR_API_KEY","Content-Type":"application/json"}
BodyTemplate = {"q":"{source}","target":"{targetLanguage}","id":"{hash}"}
ResponseTranslationPath = data.translation
```

占位符（契约）：`{source}`、`{targetLanguage}`、`{hash}`。写入 JSON body 时会对 `source` 做转义。

`ResponseTranslationPath` 支持点分路径与简单下标，例如 `data.translation`、`choices.0.message.content`。

### B. OpenAI 兼容 Chat Completions（`BackendType = LlmOpenAiCompatible`）

```ini
[General]
BackendType = LlmOpenAiCompatible

[LlmOpenAiCompatible]
BaseUrl = https://api.openai.com/v1
Model = gpt-4o-mini
ApiKey = YOUR_API_KEY
SystemPrompt = You are a game UI translator. Translate faithfully; keep placeholders and markup intact.
ChatCompletionsPath = /chat/completions
```

本地 LLM / 第三方兼容网关：改 `BaseUrl`（及必要时 `ChatCompletionsPath`），本地填写 `ApiKey`。

## 字体与布局

```ini
[Layout]
AutoShrinkFontSize = true

[Font]
FontSourceType = System
FontPath = Microsoft YaHei
```

详见 [`docs/fonts.md`](./docs/fonts.md)。**已知限制**：TMP 需 `TMP_FontAsset`；磁盘直接加载 TTF/OTF 在不同 Unity/IL2CPP 版本上无统一公开 API，插件会尽量回退为 OS 动态字体。

## Mono vs IL2CPP

| | Mono | IL2CPP |
|--|------|--------|
| BepInEx | 5.x | 6.x Unity IL2CPP |
| 工程 | `src/BepInExTranslator.Plugin` | `src/BepInExTranslator.Plugin.Il2Cpp` |
| TFM | `net472` | `net6.0` |
| 输出 | `artifacts/mono/` | `artifacts/il2cpp/` |
| 文本 Hook | 反射挂 `UnityEngine.UI` / TMP / TextMesh | 强制加载 `BepInEx/interop` 后挂同一批类型；延迟重试 |
| 主线程泵 | 插件 `Update` | Harmony 挂 Canvas 帧回调（无游戏特化 MonoBehaviour） |

共享逻辑在 `BepInExTranslator.Core`。细节见 [`BUILD.md`](BUILD.md)。

## 开发

```bash
dotnet test BepInExTranslator.sln
dotnet build src/BepInExTranslator.Plugin/BepInExTranslator.Plugin.csproj -c Release
dotnet build src/BepInExTranslator.Plugin.Il2Cpp/BepInExTranslator.Plugin.Il2Cpp.csproj -c Release
dotnet build src/Injector.Gui/Injector.Gui.csproj -c Release
```

## 免责声明 / Disclaimer

本项目为**非官方第三方工具**，仅供个人学习与研究使用。

- **非关联**：与 Unity、BepInEx、任何游戏发行商或其关联方**无任何隶属、授权或背书关系**。
- **风险自负**：修改游戏运行时（含注入 BepInEx / 本插件）可能导致崩溃、存档损坏、账号处罚或违反服务条款；**使用者自行承担全部风险**。
- **无担保**：软件按「现状」提供，不提供任何明示或暗示担保（含适销性、特定用途适用性等）。
- **合法使用**：请勿用于盗版，或在禁止 circumvention 的情况下绕过付费本地化 / DRM；请遵守所玩游戏的 EULA，以及所用翻译 API 的服务条款。

## AI 声明 / AI disclosure

本项目为**纯 AI 编程**（pure AI programming）产出：需求、代码、文档等均由 AI 完成，而非「AI 辅助人工编写」。

- 合并或发布前仍需**人工审阅**。
- **不保证**正确性、完整性或安全性；请自行验证后再用于生产或对外分发。

## 许可证与安全

本项目以 [**MIT License**](./LICENSE) 发布。完整条款见仓库根目录 [`LICENSE`](./LICENSE)。

- 仓库内**不得**提交 API Key 或带密钥的真实 endpoint。
- 使用第三方翻译 API 时请遵守其服务条款与游戏 EULA / ToS。
