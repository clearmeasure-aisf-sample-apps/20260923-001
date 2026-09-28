using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ClearMeasure.Bootcamp.UnitTests.Api;

/// <summary>
/// Removes the NServiceBus endpoint from an in-process UI.Server test host. HTTP middleware tests never send a message,
/// and disposing the host less than a second after start raced the endpoint's stop (ObjectDisposedException from
/// <c>RunningEndpointInstance.Stop</c> in <c>WebApplicationFactory.DisposeAsync</c>).
/// </summary>
public static class NServiceBusTestHost
{
    /// <summary>Removes every hosted service that NServiceBus registered.</summary>
    /// <param name="services">The test host's services.</param>
    public static void RemoveNServiceBusHostedService(IServiceCollection services)
    {
        var endpoints = services
            .Where(descriptor => descriptor.ServiceType == typeof(IHostedService)
                && (descriptor.ImplementationType?.FullName ?? descriptor.ImplementationFactory?.Method.DeclaringType?.FullName ?? string.Empty)
                    .StartsWith("NServiceBus", StringComparison.Ordinal))
            .ToList();
        foreach (var endpoint in endpoints)
        {
            services.Remove(endpoint);
        }
    }
}
