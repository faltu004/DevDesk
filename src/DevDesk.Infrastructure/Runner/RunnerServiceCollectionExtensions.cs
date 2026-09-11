using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DevDesk.Core.Runner;
using DevDesk.Core.Services;

namespace DevDesk.Infrastructure.Runner;

/// <summary>
/// Extension methods for registering DevDesk project runner services into DI.
/// </summary>
public static class RunnerServiceCollectionExtensions
{
    public static IServiceCollection AddDevDeskRunner(this IServiceCollection services)
    {
        services.AddSingleton<IRunnerToolLocator, WindowsRunnerToolLocator>();
        services.AddSingleton<IProcessLauncher, SystemProcessLauncher>();
        services.AddSingleton<IProjectRunnerService>(sp => new ProjectRunnerService(
            sp.GetRequiredService<IProjectService>(),
            sp.GetRequiredService<IProcessLauncher>(),
            sp.GetRequiredService<IRunnerToolLocator>(),
            sp.GetRequiredService<ILogger<ProjectRunnerService>>()));

        return services;
    }
}
