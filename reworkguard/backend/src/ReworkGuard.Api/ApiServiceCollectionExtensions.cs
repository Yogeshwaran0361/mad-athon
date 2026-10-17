using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.OpenApi.Models;
using ReworkGuard.Api.Infrastructure;
using ReworkGuard.Api.Realtime;
using ReworkGuard.Application.Abstractions;

namespace ReworkGuard.Api;

public static class ApiServiceCollectionExtensions
{
    public const string CorsPolicyName = "frontend";
    public const string LoginRateLimitPolicy = "login";
    public const string ReadyTag = "ready";

    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddControllers()
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        // Behind nginx or a load balancer the client address and scheme arrive in X-Forwarded-* headers. Only proxies in
        // ForwardedHeaders:KnownNetworks (CIDR) are trusted, so a client cannot spoof its address to dodge rate limits.
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            foreach (var cidr in configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
            {
                options.KnownNetworks.Add(Microsoft.AspNetCore.HttpOverrides.IPNetwork.Parse(cidr));
            }
        });

        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.TraceIdentifier);
        services.AddExceptionHandler<ApiExceptionHandler>();

        // Every endpoint requires an authenticated user unless it explicitly opts out with [AllowAnonymous].
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options => options.AddPolicy(CorsPolicyName, policy =>
            policy.WithOrigins(origins).AllowAnyHeader().WithMethods("GET", "POST", "PUT", "DELETE").AllowCredentials()
            .WithExposedHeaders(ServerTimeMiddleware.HeaderName)));

        // Slows down password guessing: 10 sign-in or refresh attempts per minute per client address (configurable).
        var permitLimit = configuration.GetValue("RateLimiting:LoginPermitLimit", 10);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(LoginRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = TimeSpan.FromMinutes(1) }));
        });

        services.AddSignalR().AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddSingleton<IRealtimeNotifier, SignalRRealtimeNotifier>();

        var checks = services.AddHealthChecks()
            .AddCheck<DatabaseReadinessCheck>("database", tags: [ReadyTag]);
        if (configuration.GetValue("Jobs:Enabled", true))
        {
            checks.AddCheck<JobServerReadinessCheck>("jobs", tags: [ReadyTag]);
        }

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = "ReworkGuard API", Version = "v1" });
            var scheme = new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
            };
            options.AddSecurityDefinition("Bearer", scheme);
            options.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = [] });
        });

        return services;
    }
}
