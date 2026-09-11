using DevDesk.Core.Git;
using Microsoft.Extensions.DependencyInjection;

namespace DevDesk.Infrastructure.Git;

public static class GitServiceExtensions
{
    /// <summary>
    /// Registers DevDesk Git infrastructure, tool locator, process runner, and services.
    /// </summary>
    public static IServiceCollection AddDevDeskGit(this IServiceCollection services)
    {
        services.AddSingleton<IGitToolLocator, GitToolLocator>();
        services.AddSingleton<IGitProcessRunner, GitProcessRunner>();
        services.AddSingleton<IGitService, GitService>();

        return services;
    }
}
