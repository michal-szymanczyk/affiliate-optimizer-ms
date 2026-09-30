using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Common.Domain.Time;

/// <summary>Every timestamp comes from here (ArchitectureSimple.md §10). Always UTC. Replaceable in tests.</summary>
public interface ISystemTime
{
    DateTime Now();
}

/// <summary>Wraps <see cref="TimeProvider"/> so one FakeTimeProvider drives both the clock and awaited delays.</summary>
public sealed class SystemTime(TimeProvider timeProvider) : ISystemTime
{
    public DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;
}

public static class SystemTimeServiceExtensions
{
    public static IServiceCollection AddSystemTime(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISystemTime, SystemTime>();
        return services;
    }
}
