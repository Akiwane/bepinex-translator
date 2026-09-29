namespace BepInExTranslator.Injector.Core;

/// <summary>
/// Unity 玩家后端。Mono → BepInEx 5.x；IL2CPP → BepInEx 6.x Unity IL2CPP。不可混用。
/// </summary>
public enum UnityRuntimeKind
{
    Unknown = 0,
    Mono = 1,
    Il2Cpp = 2,
}
