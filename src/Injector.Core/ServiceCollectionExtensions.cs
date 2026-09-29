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
        // Do not register HttpPackageDownloader here — backend wires real download/install.
        return services;
    }
}
