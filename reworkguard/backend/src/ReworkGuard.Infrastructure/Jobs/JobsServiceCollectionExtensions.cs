using Hangfire;
using Hangfire.MySql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using ReworkGuard.Application.Jobs;

namespace ReworkGuard.Infrastructure.Jobs;

public static class JobsServiceCollectionExtensions
{
    public const string TablesPrefix = "Hangfire_";

    internal static void AddJobs(this IServiceCollection services, IConfiguration configuration, string connectionString)
    {
        var section = configuration.GetSection(JobsOptions.SectionName);
        services.AddOptions<JobsOptions>().Bind(section).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<DashboardSession>();
        var options = section.Get<JobsOptions>() ?? new JobsOptions();
        if (!options.Enabled)
        {
            services.AddSingleton<IJobMonitor, DisabledJobMonitor>();
            return;
        }

        // Hangfire's MySQL storage uses session variables in its queries.
        var hangfireConnection = new MySqlConnectionStringBuilder(connectionString) { AllowUserVariables = true }.ConnectionString;
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseStorage(new MySqlStorage(hangfireConnection, new MySqlStorageOptions
            {
                TablesPrefix = TablesPrefix,
                PrepareSchemaIfNecessary = true,
                QueuePollInterval = TimeSpan.FromSeconds(5),
                JobExpirationCheckInterval = TimeSpan.FromHours(1),
            })));
        services.AddSingleton<IJobMonitor, HangfireJobMonitor>();
        services.AddScoped<JobRunner>();
        if (options.RunServer)
        {
            services.AddHangfireServer(server =>
            {
                server.WorkerCount = options.WorkerCount;
                server.SchedulePollingInterval = TimeSpan.FromSeconds(5);
            });
        }
    }
}
