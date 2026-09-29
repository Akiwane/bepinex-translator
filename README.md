# BepInEx Translator

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
