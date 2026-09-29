# BepInEx Translator

<<<<<<< HEAD
基于 BepInEx 的 Unity 通用翻译模组。v1.0 **无**游戏内/桌面配置 UI，仅使用 BepInEx 配置文件 + JSON 翻译产物。

需求与决策见 [REQUIREMENTS.md](./REQUIREMENTS.md)、[DECISIONS.md](./DECISIONS.md)。

## 配置与样例（字段契约）

下列文件中的**字段名是插件后端的实现契约**（frontend-readable contract）。后端按这些名字接线；前端/文档侧只维护示例，不提交真实密钥。

| 产物 | 路径 | 说明 |
|------|------|------|
| 配置模板 | [`config/Translator.cfg.example`](./config/Translator.cfg.example) | `TargetLanguage`、`BackendType`、`HttpTemplate` / `LlmOpenAiCompatible`、超时、产物路径、自动缩字号、字体等 |
| 字体路径说明 | [`docs/fonts.md`](./docs/fonts.md) | Unity / 系统字体 / 自定义文件路径各一例 |
| 翻译产物样例 | [`docs/translations.example.json`](./docs/translations.example.json) | JSON **数组**；条目字段：`hash`、`source`、`translation`（空字符串 = 未译） |

复制示例到本地后替换 `YOUR_API_KEY` 等占位符；**切勿**把真实 API 密钥提交进仓库。
=======
基于 [BepInEx](https://docs.bepinex.dev/) 的 Unity 游戏**通用按需翻译**插件：运行时 Hook `UGUI Text` / `TextMeshPro` / 旧版 `TextMesh`，把未本地化文本替换为目标语言（默认 **zh-CN**）。

权威需求见 [`REQUIREMENTS.md`](REQUIREMENTS.md) / [`DECISIONS.md`](DECISIONS.md)。构建步骤见 [`BUILD.md`](BUILD.md)。

## 功能一览

| 能力 | 说明 |
|------|------|
| 文本源 | `UnityEngine.UI.Text`、`TMPro.TMP_Text`、`UnityEngine.TextMesh`（反射软依赖，缺程序集则跳过） |
| 按需翻译 | 仅当产物 JSON 中无**非空**译文时才请求 API |
| 产物 | 可读 JSON 数组：`hash` + `source` + `translation` |
| 后端 | `HttpJsonTemplate`（通用 HTTP JSON） / `OpenAiCompatible`（Chat Completions） |
| 布局 | 长译文自动缩字号 + 尝试开启换行 |
| 字体 | `Default` / `BuiltIn` / `System` / `CustomFile`（跨 Unity 版本能力见下文） |
| 运行时 | **Mono → BepInEx 5**；**IL2CPP → BepInEx 6**（双目标工程） |

## 安装到游戏

### 1. 确认游戏后端

- 游戏目录存在 `GameName_Data/Managed/Assembly-CSharp.dll`（或类似）且无 `il2cpp_data` → **Mono** → 使用 **BepInEx 5.x (Mono)**。
- 存在 `il2cpp_data` / `GameAssembly.dll` → **IL2CPP** → 使用 **BepInEx 6.x (Unity IL2CPP)**。

> **未知/不保证**：本插件不声称兼容全部 Unity 版本与全部自定义 UI 框架。若游戏使用非标准文本组件（如自研 SDF、NGUI、FairyGUI），v1 不会自动翻译。

### 2. 安装 BepInEx

按 [BepInEx 安装文档](https://docs.bepinex.dev/articles/user_guide/installation/index.html) 将对应版本解压到游戏根目录，先启动一次游戏以生成 `BepInEx/config` 等目录。

### 3. 放入插件

将构建产物复制到：

```text
<Game>/BepInEx/plugins/BepInExTranslator/
  BepInExTranslator.dll          # 插件
  BepInExTranslator.Core.dll     # 核心逻辑（若未合并打包）
  translations.json              # 可选；启动后自动创建
```

- Mono 构建输出：`artifacts/mono/`
- IL2CPP 构建输出：`artifacts/il2cpp/`

**不要混用**：BepInEx 5 只能加载 Mono 产物；BepInEx 6 IL2CPP 只能加载 IL2CPP 产物。

### 4. 配置

启动一次后编辑：

```text
BepInEx/config/com.akiwane.bepinextranslator.cfg
```

示例见 [`samples/com.akiwane.bepinextranslator.cfg.example`](samples/com.akiwane.bepinextranslator.cfg.example)。

**切勿**把含真实 API Key / 私密 endpoint 的 cfg 提交到 git。

## 翻译产物 JSON

默认路径：

```text
BepInEx/plugins/BepInExTranslator/translations.json
```

也可在配置 `CacheFilePath` 改为相对 `BepInEx` 根目录或绝对路径（例如 `config/BepInExTranslator/translations.json`）。

格式（人工可精翻）：

```json
[
  {
    "hash": "…",
    "source": "Save Game",
    "translation": "保存游戏"
  }
]
```

- 程序以 `hash`（原文 UTF-8 SHA-256 前 32 hex，换行已归一化）为键。
- `translation` **非空** → 直接替换，**不调 API**。
- 人工改 JSON 后**重启游戏**生效（v1 无热重载）。
- 断网时只要产物里有译文，仍可替换。

样例：[`samples/translations.sample.json`](samples/translations.sample.json)。

## 配置后端

### A. 通用 HTTP JSON 模板（默认）

```ini
[General]
BackendType = HttpJsonTemplate

[HttpJsonTemplate]
Url = https://your.api/translate
Method = POST
BodyTemplate = {"text":"{{text}}","target":"{{target_lang}}"}
ResponseJsonPath = translation
Headers = Authorization: Bearer YOUR_TOKEN_HERE
```

占位符：`{{text}}` / `{{source}}` / `{{target_lang}}` / `{{lang}}` 等。写入 JSON body 时 `text`/`source` 会自动转义。

`ResponseJsonPath` 支持点分路径与简单下标，例如：

- `translation`
- `data.translatedText`
- `choices[0].message.content`

### B. OpenAI 兼容 Chat Completions

```ini
[General]
BackendType = OpenAiCompatible

[OpenAiCompatible]
Endpoint = https://api.openai.com/v1/chat/completions
ApiKey = sk-...
Model = gpt-4o-mini
SystemPrompt = You are a translator. Translate the user message into {{target_lang}}. Reply with only the translation, no quotes or explanation.
```

本地 LLM / 第三方兼容网关：把 `Endpoint` 改成其 Chat Completions URL，按需填 `ApiKey`。

## 字体与布局

```ini
[Layout]
AutoResizeFont = true
MinFontScale = 0.5
MinFontSize = 8

[Font]
Source = System
SystemFontName = Microsoft YaHei
CustomFontPath =
```

| Source | 行为 |
|--------|------|
| `Default` | 不改字体 |
| `BuiltIn` | `Resources.GetBuiltinResource("Arial.ttf")` |
| `System` | `Font.CreateDynamicFontFromOSFont` |
| `CustomFile` | 尝试按路径文件名或字体名走 OS 字体；**不能保证**所有 Unity 版本能直接加载任意 TTF |

**已知限制（请勿当作保证）：**

- TextMeshPro 需要 `TMP_FontAsset`，普通 `Font` 无法可靠注入；TMP 场景请自备含目标字形的 TMP 字体资源，或依赖游戏已有 CJK 字体。
- 从磁盘直接加载 `.ttf/.otf` 在不同 Unity / IL2CPP 版本上**没有统一公开 API**，本插件会回退为按字体名创建动态 OS 字体。
- 自动缩字号是长度启发，不是完整 TextGenerator 排版。

## Mono vs IL2CPP 打包

| | Mono | IL2CPP |
|--|------|--------|
| BepInEx | 5.x | 6.x Unity IL2CPP |
| 工程 | `src/BepInExTranslator.Plugin` | `src/BepInExTranslator.Plugin.Il2Cpp` |
| TFM | `net472` | `net6.0` |
| 输出 | `artifacts/mono/` | `artifacts/il2cpp/` |

共享逻辑在 `BepInExTranslator.Core`（可单测、无 Unity 依赖）。细节与排错见 [`BUILD.md`](BUILD.md)。

## 开发

```bash
dotnet test BepInExTranslator.sln
dotnet build src/BepInExTranslator.Plugin/BepInExTranslator.Plugin.csproj -c Release
```

纯逻辑单测覆盖：JSON 缓存、模板填充、hash、字号调整、JSON 路径提取。

## 许可证与安全

- 仓库内**不得**提交 API Key 或带密钥的真实 endpoint。
- 使用第三方翻译 API 时请遵守其服务条款与游戏 EULA / ToS。
>>>>>>> dac8ad8 (Implement BepInEx universal on-demand translator (Mono + IL2CPP))
