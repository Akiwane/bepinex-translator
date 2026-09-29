using System;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace BepInExTranslator.Services
{
    /// <summary>
    /// 字体解析：FontSourceType = Unity | System | CustomFile，路径见 FontPath / docs/fonts.md。
    /// </summary>
    public sealed class FontProvider
    {
        private readonly PluginSettings _settings;
        private readonly ManualLogSource _log;
        private Font? _cached;
        private bool _resolved;

        public FontProvider(PluginSettings settings, ManualLogSource log)
        {
            _settings = settings;
            _log = log;
        }

        public void TryApplyFont(object component)
        {
            var font = Resolve();
            if (font == null)
            {
                return;
            }

            var type = component.GetType();
            var fontProp = type.GetProperty("font", BindingFlags.Instance | BindingFlags.Public);
            if (fontProp != null && fontProp.CanWrite && typeof(Font).IsAssignableFrom(fontProp.PropertyType))
            {
                fontProp.SetValue(component, font, null);
                return;
            }

            _log.LogDebug("TMP font override requires a TMP_FontAsset; skipping automatic Font assignment.");
        }

        private Font? Resolve()
        {
            if (_resolved)
            {
                return _cached;
            }

            _resolved = true;
            var sourceType = (_settings.FontSourceType.Value ?? "System").Trim();
            var path = _settings.FontPath.Value ?? string.Empty;

            try
            {
                if (sourceType.Equals("Unity", StringComparison.OrdinalIgnoreCase)
                    || sourceType.Equals("BuiltIn", StringComparison.OrdinalIgnoreCase))
                {
                    // Unity：内置/资源字体名，如 Arial.ttf
                    var name = string.IsNullOrWhiteSpace(path) ? "Arial.ttf" : path;
                    _cached = Resources.GetBuiltinResource<Font>(Path.GetFileName(name));
                    if (_cached == null)
                    {
                        _cached = Resources.GetBuiltinResource<Font>("Arial.ttf");
                    }
                }
                else if (sourceType.Equals("System", StringComparison.OrdinalIgnoreCase))
                {
                    var name = string.IsNullOrWhiteSpace(path) ? "Arial" : path;
                    _cached = Font.CreateDynamicFontFromOSFont(name, 16);
                }
                else if (sourceType.Equals("CustomFile", StringComparison.OrdinalIgnoreCase))
                {
                    _cached = LoadCustomFile(path);
                }
                else
                {
                    _log.LogWarning("Unknown FontSourceType: " + sourceType);
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning($"Font resolve failed ({sourceType}): {ex.Message}");
                _cached = null;
            }

            return _cached;
        }

        private Font? LoadCustomFile(string? pathOrName)
        {
            if (string.IsNullOrWhiteSpace(pathOrName))
            {
                _log.LogWarning("FontSourceType=CustomFile but FontPath is empty.");
                return null;
            }

            // 相对路径：相对游戏根 / BepInEx 根
            var resolved = pathOrName;
            if (!Path.IsPathRooted(pathOrName))
            {
                var fromGame = Path.GetFullPath(Path.Combine(BepInEx.Paths.GameRootPath, pathOrName));
                var fromBep = Path.GetFullPath(Path.Combine(BepInEx.Paths.BepInExRootPath, pathOrName));
                if (File.Exists(fromGame))
                {
                    resolved = fromGame;
                }
                else if (File.Exists(fromBep))
                {
                    resolved = fromBep;
                }
            }

            if (File.Exists(resolved))
            {
                var name = Path.GetFileNameWithoutExtension(resolved);
                _log.LogWarning(
                    "Custom TTF/OTF file loading is Unity-version dependent and not guaranteed. " +
                    "Attempting OS font by file name: " + name + ". See docs/fonts.md.");
                return Font.CreateDynamicFontFromOSFont(name, 16);
            }

            // 当作已安装字体名再试一次
            try
            {
                return Font.CreateDynamicFontFromOSFont(pathOrName, 16);
            }
            catch
            {
                _log.LogWarning("Custom font path not found: " + pathOrName);
                return null;
            }
        }
    }
}
