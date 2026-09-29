using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using BepInExTranslator.Services;
using HarmonyLib;

namespace BepInExTranslator.Hooks
{
    public static class TextHookInstaller
    {
        public static void Apply(Harmony harmony, ManualLogSource log)
        {
            var settings = TranslatorPlugin.Settings;

            if (settings.EnableUgui.Value)
            {
                TryPatchSetter(harmony, log, "UnityEngine.UI.Text", "UnityEngine.UI", "text");
            }

            if (settings.EnableTextMesh.Value)
            {
                TryPatchSetter(harmony, log, "UnityEngine.TextMesh", null, "text");
            }

            if (settings.EnableTextMeshPro.Value)
            {
                if (!TryPatchSetter(harmony, log, "TMPro.TMP_Text", "Unity.TextMeshPro", "text"))
                {
                    TryPatchSetter(harmony, log, "TMPro.TMP_Text", "TextMeshPro", "text");
                }

                TryPatchMethod(harmony, log, "TMPro.TMP_Text", new[] { "Unity.TextMeshPro", "TextMeshPro" },
                    "SetText", new[] { typeof(string) });
            }
        }

        private static bool TryPatchSetter(
            Harmony harmony,
            ManualLogSource log,
            string typeName,
            string? preferredAssembly,
            string propertyName)
        {
            try
            {
                var type = FindType(typeName, preferredAssembly);
                if (type == null)
                {
                    log.LogInfo($"Type not found, skip hook: {typeName}");
                    return false;
                }

                var prop = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
                var setter = prop?.GetSetMethod();
                if (setter == null)
                {
                    log.LogWarning($"No public setter for {typeName}.{propertyName}");
                    return false;
                }

                var prefix = typeof(TextHooks).GetMethod(nameof(TextHooks.PrefixSetText), BindingFlags.Static | BindingFlags.Public);
                harmony.Patch(setter, prefix: new HarmonyMethod(prefix));
                log.LogInfo($"Hooked {typeName}.{propertyName} setter");
                return true;
            }
            catch (Exception ex)
            {
                log.LogWarning($"Failed to hook {typeName}: {ex.Message}");
                return false;
            }
        }

        private static bool TryPatchMethod(
            Harmony harmony,
            ManualLogSource log,
            string typeName,
            IEnumerable<string> assemblies,
            string methodName,
            Type[] parameters)
        {
            try
            {
                Type? type = null;
                foreach (var asm in assemblies)
                {
                    type = FindType(typeName, asm);
                    if (type != null)
                    {
                        break;
                    }
                }

                type ??= FindType(typeName, null);
                if (type == null)
                {
                    return false;
                }

                var method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public, null, parameters, null);
                if (method == null)
                {
                    return false;
                }

                var prefix = typeof(TextHooks).GetMethod(nameof(TextHooks.PrefixSetTextMethod), BindingFlags.Static | BindingFlags.Public);
                harmony.Patch(method, prefix: new HarmonyMethod(prefix));
                log.LogInfo($"Hooked {typeName}.{methodName}(string)");
                return true;
            }
            catch (Exception ex)
            {
                log.LogWarning($"Failed to hook {typeName}.{methodName}: {ex.Message}");
                return false;
            }
        }

        private static Type? FindType(string typeName, string? preferredAssembly)
        {
            if (!string.IsNullOrEmpty(preferredAssembly))
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, preferredAssembly, StringComparison.OrdinalIgnoreCase));
                var t = asm?.GetType(typeName, throwOnError: false);
                if (t != null)
                {
                    return t;
                }
            }

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type? t = null;
                try
                {
                    t = asm.GetType(typeName, throwOnError: false);
                }
                catch
                {
                    // ignore
                }

                if (t != null)
                {
                    return t;
                }
            }

            return null;
        }
    }

    public static class TextHooks
    {
        public static void PrefixSetText(object __instance, ref string value) => Handle(__instance, ref value);

        public static void PrefixSetTextMethod(object __instance, ref string text) => Handle(__instance, ref text);

        private static void Handle(object instance, ref string text)
        {
            var runtime = TranslatorPlugin.Runtime;
            if (runtime == null || instance == null || runtime.IsMutating(instance))
            {
                return;
            }

            var original = text;
            text = runtime.ProcessTextChange(instance, original, translated => TryWriteText(instance, translated));
        }

        private static void TryWriteText(object instance, string translated)
        {
            try
            {
                var type = instance.GetType();
                var prop = type.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(instance, translated);
                    return;
                }

                var setText = type.GetMethod("SetText", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(string) }, null);
                setText?.Invoke(instance, new object[] { translated });
            }
            catch
            {
                // ignore
            }
        }
    }
}
