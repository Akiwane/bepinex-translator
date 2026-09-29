namespace Injector.Core.Models;

/// <summary>
/// Unity player backend detected under a game directory.
/// </summary>
public enum RuntimeKind
{
    /// <summary>Could not determine Mono vs IL2CPP.</summary>
    Unknown = 0,

    /// <summary>Classic Mono: *_Data/Managed present, no IL2CPP markers.</summary>
    Mono = 1,

    /// <summary>IL2CPP: il2cpp_data and/or GameAssembly.dll present.</summary>
    Il2Cpp = 2,
}
