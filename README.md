# BepInEx Translator

基于 [BepInEx](https://docs.bepinex.dev/) 的 Unity 游戏**通用按需翻译**插件：运行时 Hook `UGUI Text` / `TextMeshPro` / 旧版 `TextMesh`，把未本地化文本替换为目标语言（默认 **zh-CN**）。

v1.0 **无**游戏内/桌面配置 UI，仅使用 BepInEx ConfigFile + JSON 翻译产物。

权威需求见 [`REQUIREMENTS.md`](REQUIREMENTS.md)、[`DECISIONS.md`](DECISIONS.md)、[`TEST-CASES.md`](TEST-CASES.md)。构建步骤见 [`BUILD.md`](BUILD.md)。

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

### 推荐：Windows 注入器（v1.1 / REQUIREMENTS §7）

`Injector.Core` 可自动识别 Mono/IL2CPP、下载匹配的 BepInEx，并将本模组放入 `BepInEx/plugins`。`Injector.Gui` 目前为 **Avalonia stub**（完整 UI 由前端负责）。契约、默认包 pin、验收步骤见 **[`docs/injector.md`](./docs/injector.md)**。

```bash
dotnet build src/Injector.Core/Injector.Core.csproj -c Release
dotnet build src/Injector.Gui/Injector.Gui.csproj -c Release
```

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

共享逻辑在 `BepInExTranslator.Core`。细节见 [`BUILD.md`](BUILD.md)。

## 开发

```bash
dotnet test BepInExTranslator.sln
dotnet build src/BepInExTranslator.Plugin/BepInExTranslator.Plugin.csproj -c Release
dotnet build src/BepInExTranslator.Plugin.Il2Cpp/BepInExTranslator.Plugin.Il2Cpp.csproj -c Release
dotnet build src/Injector.Gui/Injector.Gui.csproj -c Release
```

注入器说明：[`docs/injector.md`](./docs/injector.md)。

## 许可证与安全

- 仓库内**不得**提交 API Key 或带密钥的真实 endpoint。
- 使用第三方翻译 API 时请遵守其服务条款与游戏 EULA / ToS。
