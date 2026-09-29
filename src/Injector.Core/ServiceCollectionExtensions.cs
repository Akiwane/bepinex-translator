using Injector.Core.Abstractions;
using Injector.Core.Install;
using Injector.Core.Packages;
using Injector.Core.Probing;
using Microsoft.Extensions.DependencyInjection;

namespace Injector.Core;

/// <summary>
/// DI helpers: real <see cref="GameProbe"/> + stub installer/resolver for the GUI demo.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInjectorCore(this IServiceCollection services)
    {
        services.AddSingleton<IGameProbe, GameProbe>();
        services.AddSingleton<IPackageResolver, StubPackageResolver>();
        services.AddSingleton<IInstaller, StubInstaller>();
        // HttpPackageDownloader is registered for future backend wiring; stub does not use it.
        services.AddSingleton<IPackageDownloader, HttpPackageDownloader>();
        return services;
    }
}
