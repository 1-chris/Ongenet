using System;
using Microsoft.Extensions.DependencyInjection;

namespace Ongenet.Core.DependencyInjection;

/// <summary>
/// Convenience factory for building a service provider with Ongenet.Core services registered.
/// </summary>
public static class OngenetServiceProvider
{
    /// <summary>
    /// Creates a service provider with core services registered.
    /// </summary>
    /// <param name="configureServices">Optional callback to register additional services.</param>
    /// <returns>A configured service provider.</returns>
    public static IServiceProvider Create(Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        services.AddOngenetCore();
        configureServices?.Invoke(services);
        return services.BuildServiceProvider();
    }
}
