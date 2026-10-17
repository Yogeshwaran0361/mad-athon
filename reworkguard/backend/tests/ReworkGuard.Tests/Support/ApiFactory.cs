using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Application.Auth;
using ReworkGuard.Domain.Entities;

namespace ReworkGuard.Tests.Support;

/// <summary>Hosts the real API against a test database, with a controllable clock.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly string _connectionString;

    public ApiFactory(string connectionString, TestClock clock)
    {
        _connectionString = connectionString;
        Clock = clock;
    }

    public TestClock Clock { get; }

    public RecordingEmailSender Emails { get; } = new();

    /// <summary>Most tests call job services directly; set before first use to run the real Hangfire server.</summary>
    public bool JobsEnabled { get; init; }

    /// <summary>Hosting environment; "Testing" unless a test needs Development-only behaviour.</summary>
    public string EnvironmentName { get; init; } = "Testing";

    /// <summary>Extra configuration values applied after the defaults below.</summary>
    public IDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>();

    /// <summary>Runs a service from the API's container in its own scope, as a background job would (no signed-in user).</summary>
    public async Task<T> RunScopedAsync<TService, T>(Func<TService, Task<T>> action)
        where TService : notnull
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    public async Task<HttpClient> ClientForAsync(Employee employee)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(employee.Email, TestData.Password), Json);
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
        builder.UseSetting("Jwt:SigningKey", "integration-tests-only-signing-key-0123456789abcdef");
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Database:SeedOnStartup", "false");
        builder.UseSetting("Auth:SecureCookie", "false");
        builder.UseSetting("RateLimiting:LoginPermitLimit", "1000");
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
        builder.UseSetting("Jobs:Enabled", JobsEnabled ? "true" : "false");
        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IClock>(Clock));
            services.Replace(ServiceDescriptor.Singleton<IEmailSender>(Emails));
        });
    }
}
