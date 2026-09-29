using System;
using System.IO;
using System.Reflection;
using BepInEx.Logging;

namespace BepInExTranslator.Services
{
    /// <summary>字体解析（反射调用 Unity Font API，无编译期 Unity 引用）。</summary>
    public sealed class FontProvider
    {
        private readonly PluginSettings _settings;
        private readonly ManualLogSource _log;
        private object? _cached;
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
            if (fontProp != null && fontProp.CanWrite)
            {
                try
                {
                    fontProp.SetValue(component, font);
                }
                catch (Exception ex)
                {
                    _log.LogDebug("Font assign skipped: " + ex.Message);
                }
            }
        }

        private object? Resolve()
        {
            if (_resolved)
            {
                return _cached;
            }

            _resolved = true;
            var source = (_settings.FontSource.Value ?? "Default").Trim();
            try
            {
                if (source.Equals("Default", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                if (source.Equals("BuiltIn", StringComparison.OrdinalIgnoreCase))
                {
                    var fontType = FindType("UnityEngine.Font");
                    if (fontType != null)
                    {
                        _cached = InvokeStatic(
                            "UnityEngine.Resources",
                            "GetBuiltinResource",
                            fontType,
                            "Arial.ttf");
                    }
                }
                else if (source.Equals("System", StringComparison.OrdinalIgnoreCase))
                {
                    var name = string.IsNullOrWhiteSpace(_settings.SystemFontName.Value)
                        ? "Arial"
                        : _settings.SystemFontName.Value;
                    _cached = CallCreateDynamicFont(name);
                }
                else if (source.Equals("CustomFile", StringComparison.OrdinalIgnoreCase))
                {
                    var path = _settings.CustomFontPath.Value;
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        var name = File.Exists(path) ? Path.GetFileNameWithoutExtension(path) : path;
                        _log.LogWarning(
                            "IL2CPP custom font file loading is Unity-version dependent; trying OS font name: " + name);
                        _cached = CallCreateDynamicFont(name);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning($"Font resolve failed ({source}): {ex.Message}");
            }

            return _cached;
        }

        private static object? CallCreateDynamicFont(string name)
        {
            var fontType = FindType("UnityEngine.Font");
            if (fontType == null)
            {
                return null;
            }

            var method = fontType.GetMethod(
                "CreateDynamicFontFromOSFont",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string), typeof(int) },
                null);
            return method?.Invoke(null, new object[] { name, 16 });
        }

        private static object? InvokeStatic(string typeName, string methodName, Type resourceType, string resourceName)
        {
            var resources = FindType(typeName);
            if (resources == null)
            {
                return null;
            }

            var method = resources.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(Type), typeof(string) },
                null);
            return method?.Invoke(null, new object[] { resourceType, resourceName });
        }

        private static Type? FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(fullName, throwOnError: false);
                    if (t != null)
                    {
                        return t;
                    }
                }
                catch
                {
                    // ignore
                }
            }

            return null;
        }
    }
}
